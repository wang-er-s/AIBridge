using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AIBridge.Runtime
{
    public sealed class AIBridgeRuntimeBridge : MonoBehaviour
    {
        private const int MinTimeoutMs = 100;
        private const int MaxTimeoutMs = 300000;
        private const int MaxResultDepth = 8;
        private const int MaxCollectionItems = 512;
        private const int MaxPendingCommands = 8;

        private readonly Queue<PendingCommand> _commands = new Queue<PendingCommand>();
        private HttpServer _server;
        private bool _initialized;
        private int _pendingCommandCount;

        public AIBridgeRuntimeSettings settings = new AIBridgeRuntimeSettings();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (Application.isEditor)
            {
                Debug.Log("[AIBridgeSelfRuntime] Remote code execution is disabled in Editor.");
                return;
            }

            if (!IsEnabledForBuild())
            {
                return;
            }

            try
            {
                _server = new HttpServer(this, settings);
                _server.Start();
                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[AIBridgeSelfRuntime] Failed to start Runtime Bridge: " + ex);
            }
        }

        private void OnDisable()
        {
            _initialized = false;
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }

            lock (_commands)
            {
                while (_commands.Count > 0)
                {
                    _commands.Dequeue().Complete(Failure(null, "bridge_stopped", "Runtime Bridge stopped."));
                }
            }
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            PendingCommand command = null;
            lock (_commands)
            {
                if (_commands.Count > 0)
                {
                    command = _commands.Dequeue();
                }
            }

            if (command != null && !command.IsClosed)
            {
                ExecuteOnMainThread(command);
            }
        }

        internal bool Enqueue(AIBridgeSelfCodeExecuteRequest request, out PendingCommand pending, out string error)
        {
            pending = null;
            error = null;

            if (!_initialized)
            {
                error = "Runtime Bridge is not ready.";
                return false;
            }

            if (settings == null || !settings.enableRuntimeCodeExecution)
            {
                error = "Runtime code execution is disabled.";
                return false;
            }

            if (Interlocked.Increment(ref _pendingCommandCount) > MaxPendingCommands)
            {
                Interlocked.Decrement(ref _pendingCommandCount);
                error = "Runtime command queue is full.";
                return false;
            }

            pending = new PendingCommand(request, () => Interlocked.Decrement(ref _pendingCommandCount));
            lock (_commands)
            {
                _commands.Enqueue(pending);
            }

            return true;
        }

        private bool IsEnabledForBuild()
        {
            if (settings == null || !settings.enableRuntimeBridge)
            {
                return false;
            }

#if DEVELOPMENT_BUILD
            return true;
#else
            return settings.allowInReleaseBuild;
#endif
        }

        private void ExecuteOnMainThread(PendingCommand pending)
        {
            var request = pending.Request;
            AIBridgeSelfRuntimeResult validationFailure;
            byte[] assemblyBytes;
            if (!TryValidateRequest(request, out assemblyBytes, out validationFailure))
            {
                pending.Complete(validationFailure);
                return;
            }

            try
            {
                var assembly = Assembly.Load(assemblyBytes);
                var entryType = assembly.GetType(request.entryType, false);
                if (entryType == null)
                {
                    pending.Complete(Failure(request, "entry_type_not_found", "Runtime code entry type was not found."));
                    return;
                }

                var method = entryType.GetMethod(
                    request.methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null)
                {
                    pending.Complete(Failure(request, "entry_method_not_found", "Runtime code entry method was not found."));
                    return;
                }

                object value;
                try
                {
                    value = method.Invoke(null, null);
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    pending.Complete(Failure(request, "invocation_failed", inner.GetType().Name + ": " + inner.Message));
                    return;
                }

                var task = value as Task;
                if (task != null)
                {
                    StartCoroutine(CompleteTask(pending, task));
                    return;
                }

                pending.Complete(Success(request, NormalizeValue(value, 0)));
            }
            catch (Exception ex)
            {
                pending.Complete(Failure(request, "execution_failed", ex.GetType().Name + ": " + ex.Message));
            }
        }

        private IEnumerator CompleteTask(PendingCommand pending, Task task)
        {
            var timeoutMs = ClampTimeout(pending.Request == null
                ? AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs
                : pending.Request.timeoutMs);
            var startedAt = Time.realtimeSinceStartup;
            while (!task.IsCompleted)
            {
                if ((Time.realtimeSinceStartup - startedAt) * 1000f >= timeoutMs)
                {
                    pending.Complete(Failure(pending.Request, "execution_timeout", "Runtime code execution timed out."));
                    yield break;
                }

                yield return null;
            }

            if (task.IsCanceled)
            {
                pending.Complete(Failure(pending.Request, "task_canceled", "Runtime code task was canceled."));
                yield break;
            }

            if (task.IsFaulted)
            {
                var exception = task.Exception == null ? null : task.Exception.GetBaseException();
                pending.Complete(Failure(
                    pending.Request,
                    "task_failed",
                    exception == null ? "Runtime code task failed." : exception.GetType().Name + ": " + exception.Message));
                yield break;
            }

            object result = null;
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
            if (resultProperty != null)
            {
                result = resultProperty.GetValue(task, null);
            }

            pending.Complete(Success(pending.Request, NormalizeValue(result, 0)));
        }

        private bool TryValidateRequest(
            AIBridgeSelfCodeExecuteRequest request,
            out byte[] assemblyBytes,
            out AIBridgeSelfRuntimeResult failure)
        {
            assemblyBytes = null;
            failure = null;
            if (request == null)
            {
                failure = Failure(null, "invalid_request", "Request body is required.");
                return false;
            }

            if (!string.Equals(request.action, AIBridgeRuntimeProtocol.CodeExecuteAction, StringComparison.Ordinal))
            {
                failure = Failure(request, "invalid_action", "Unsupported Runtime action.");
                return false;
            }

            if (!request.riskAccepted)
            {
                failure = Failure(request, "risk_not_accepted", "Runtime code execution requires riskAccepted=true.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.assemblyBase64)
                || string.IsNullOrWhiteSpace(request.entryType)
                || string.IsNullOrWhiteSpace(request.methodName))
            {
                failure = Failure(request, "missing_parameter", "Assembly and entry method parameters are required.");
                return false;
            }

            try
            {
                assemblyBytes = Convert.FromBase64String(request.assemblyBase64);
            }
            catch (Exception ex)
            {
                failure = Failure(request, "invalid_assembly", "Invalid assemblyBase64: " + ex.Message);
                return false;
            }

            var maxAssemblyBytes = settings == null
                ? AIBridgeRuntimeProtocol.MaxAssemblyBytes
                : Math.Max(1, settings.maxAssemblyBytes);
            if (assemblyBytes.Length == 0
                || assemblyBytes.Length > maxAssemblyBytes
                || assemblyBytes.Length > AIBridgeRuntimeProtocol.MaxAssemblyBytes)
            {
                failure = Failure(request, "assembly_too_large", "Runtime assembly size is invalid.");
                return false;
            }

            var actualSha256 = AIBridgeRuntimeProtocol.ComputeSha256(assemblyBytes);
            if (string.IsNullOrWhiteSpace(request.sha256)
                || !string.Equals(request.sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                failure = Failure(request, "sha256_mismatch", "Runtime assembly SHA-256 does not match.");
                return false;
            }

            return true;
        }

        private static object NormalizeValue(object value, int depth)
        {
            if (value == null)
            {
                return null;
            }

            if (depth >= MaxResultDepth)
            {
                return "<max-depth>";
            }

            var type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal)
            {
                return value;
            }

            if (value is Enum)
            {
                return value.ToString();
            }

            var unityObject = value as UnityEngine.Object;
            if (unityObject != null)
            {
                return unityObject.name;
            }

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var result = new Dictionary<string, object>();
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= MaxCollectionItems)
                    {
                        result["__truncated"] = true;
                        break;
                    }

                    result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] =
                        NormalizeValue(entry.Value, depth + 1);
                }

                return result;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var result = new List<object>();
                foreach (var item in enumerable)
                {
                    if (result.Count >= MaxCollectionItems)
                    {
                        result.Add("<truncated>");
                        break;
                    }

                    result.Add(NormalizeValue(item, depth + 1));
                }

                return result;
            }

            return value.ToString();
        }

        private static AIBridgeSelfRuntimeResult Success(AIBridgeSelfCodeExecuteRequest request, object result)
        {
            return new AIBridgeSelfRuntimeResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeRuntimeProtocol.CodeExecuteAction,
                success = true,
                result = result
            };
        }

        private static AIBridgeSelfRuntimeResult Failure(
            AIBridgeSelfCodeExecuteRequest request,
            string errorCode,
            string errorMessage)
        {
            return new AIBridgeSelfRuntimeResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeRuntimeProtocol.CodeExecuteAction,
                success = false,
                errorCode = errorCode,
                errorMessage = errorMessage
            };
        }

        private static int ClampTimeout(int timeoutMs)
        {
            return Math.Max(
                MinTimeoutMs,
                Math.Min(
                    MaxTimeoutMs,
                    timeoutMs <= 0 ? AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs : timeoutMs));
        }

        internal sealed class PendingCommand
        {
            private readonly ManualResetEventSlim _completed = new ManualResetEventSlim(false);
            private readonly Action _onClosed;
            private int _closed;

            public PendingCommand(AIBridgeSelfCodeExecuteRequest request, Action onClosed)
            {
                Request = request;
                _onClosed = onClosed;
            }

            public AIBridgeSelfCodeExecuteRequest Request { get; private set; }
            public AIBridgeSelfRuntimeResult Result { get; private set; }
            public bool IsClosed { get { return Volatile.Read(ref _closed) != 0; } }

            public bool Wait(int timeoutMs)
            {
                return _completed.Wait(ClampTimeout(timeoutMs));
            }

            public void Complete(AIBridgeSelfRuntimeResult result)
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0)
                {
                    return;
                }

                Result = result;
                _completed.Set();
                if (_onClosed != null)
                {
                    _onClosed();
                }
            }
        }

        private sealed class HttpServer : IDisposable
        {
            private const int MaxHeaderBytes = 64 * 1024;
            private readonly AIBridgeRuntimeBridge _bridge;
            private readonly AIBridgeRuntimeSettings _settings;
            private TcpListener _listener;
            private Thread _thread;
            private volatile bool _running;

            public HttpServer(AIBridgeRuntimeBridge bridge, AIBridgeRuntimeSettings settings)
            {
                _bridge = bridge;
                _settings = settings ?? new AIBridgeRuntimeSettings();
            }

            public void Start()
            {
                IPAddress address;
                if (!IPAddress.TryParse(_settings.httpBindAddress, out address))
                {
                    address = IPAddress.Any;
                }

                _listener = new TcpListener(address, Math.Max(1, Math.Min(65535, _settings.httpPort)));
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "AIBridgeSelfRuntimeHttp"
                };
                _thread.Start();
                Debug.Log("[AIBridgeSelfRuntime] Listening on http://"
                    + _settings.httpBindAddress + ":" + _settings.httpPort + ".");
            }

            public void Dispose()
            {
                _running = false;
                try
                {
                    if (_listener != null)
                    {
                        _listener.Stop();
                    }
                }
                catch
                {
                }

                _listener = null;
            }

            private void ListenLoop()
            {
                while (_running)
                {
                    try
                    {
                        var client = _listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                    }
                    catch
                    {
                        if (_running)
                        {
                            Thread.Sleep(50);
                        }
                    }
                }
            }

            private void HandleClient(TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                        if (remoteEndPoint == null || !IsLanAddress(remoteEndPoint.Address))
                        {
                            WriteJson(client.GetStream(), 403, Failure(
                                null,
                                "lan_only",
                                "Runtime Bridge accepts private LAN connections only."), _settings.maxResultBytes);
                            return;
                        }

                        client.ReceiveTimeout = 5000;
                        client.SendTimeout = 5000;
                        var request = ReadHttpRequest(client.GetStream(), _settings.maxRequestBytes);
                        if (request == null)
                        {
                            return;
                        }

                        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(request.Path, AIBridgeRuntimeProtocol.HealthPath, StringComparison.Ordinal))
                        {
                            WriteJson(client.GetStream(), 200, new AIBridgeSelfRuntimeHealth
                            {
                                service = "aibridge-self-runtime",
                                ready = true,
                                endpoint = AIBridgeRuntimeProtocol.CodeExecutePath
                            }, _settings.maxResultBytes);
                            return;
                        }

                        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(request.Path, AIBridgeRuntimeProtocol.CodeExecutePath, StringComparison.Ordinal))
                        {
                            WriteJson(client.GetStream(), 404, Failure(null, "not_found", "Runtime endpoint not found."), _settings.maxResultBytes);
                            return;
                        }

                        AIBridgeSelfCodeExecuteRequest command;
                        try
                        {
                            command = JsonConvert.DeserializeObject<AIBridgeSelfCodeExecuteRequest>(request.Body);
                        }
                        catch (Exception ex)
                        {
                            WriteJson(client.GetStream(), 400, Failure(null, "invalid_json", ex.Message), _settings.maxResultBytes);
                            return;
                        }

                        PendingCommand pending;
                        string error;
                        if (!_bridge.Enqueue(command, out pending, out error))
                        {
                            WriteJson(client.GetStream(), 503, Failure(command, "bridge_unavailable", error), _settings.maxResultBytes);
                            return;
                        }

                        if (!pending.Wait(command == null ? 0 : command.timeoutMs))
                        {
                            pending.Complete(Failure(command, "request_timeout", "Timed out waiting for Runtime execution."));
                        }

                        WriteJson(client.GetStream(), 200, pending.Result, _settings.maxResultBytes);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            WriteJson(client.GetStream(), 500, Failure(null, "server_error", ex.Message), _settings.maxResultBytes);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            private static bool IsLanAddress(IPAddress address)
            {
                if (address == null)
                {
                    return false;
                }

                if (IPAddress.IsLoopback(address))
                {
                    return true;
                }

                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = address.GetAddressBytes();
                    return bytes[0] == 10
                        || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        || (bytes[0] == 192 && bytes[1] == 168)
                        || (bytes[0] == 169 && bytes[1] == 254);
                }

                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    var bytes = address.GetAddressBytes();
                    return address.IsIPv6LinkLocal
                        || address.IsIPv6SiteLocal
                        || (bytes[0] & 0xfe) == 0xfc;
                }

                return false;
            }

            private static HttpRequest ReadHttpRequest(NetworkStream stream, int maxRequestBytes)
            {
                var headerBytes = new List<byte>();
                var matched = 0;
                var terminator = new byte[] { 13, 10, 13, 10 };
                while (headerBytes.Count < MaxHeaderBytes)
                {
                    var next = stream.ReadByte();
                    if (next < 0)
                    {
                        return null;
                    }

                    headerBytes.Add((byte)next);
                    matched = next == terminator[matched] ? matched + 1 : (next == terminator[0] ? 1 : 0);
                    if (matched == terminator.Length)
                    {
                        break;
                    }
                }

                if (matched != terminator.Length)
                {
                    throw new InvalidOperationException("HTTP request headers are too large.");
                }

                var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
                var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var requestLine = lines[0].Split(' ');
                if (requestLine.Length < 2)
                {
                    throw new InvalidOperationException("Invalid HTTP request line.");
                }

                var contentLength = 0;
                for (var i = 1; i < lines.Length; i++)
                {
                    const string ContentLengthHeader = "Content-Length:";
                    if (lines[i].StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(lines[i].Substring(ContentLengthHeader.Length).Trim(), out contentLength);
                    }
                }

                var requestLimit = Math.Max(1024, maxRequestBytes);
                if (contentLength < 0 || contentLength > requestLimit)
                {
                    throw new InvalidOperationException("HTTP request body is too large.");
                }

                var body = new byte[contentLength];
                var offset = 0;
                while (offset < body.Length)
                {
                    var read = stream.Read(body, offset, body.Length - offset);
                    if (read <= 0)
                    {
                        throw new InvalidOperationException("Unexpected end of HTTP request body.");
                    }

                    offset += read;
                }

                return new HttpRequest
                {
                    Method = requestLine[0],
                    Path = requestLine[1].Split('?')[0],
                    Body = Encoding.UTF8.GetString(body)
                };
            }

            private static void WriteJson(NetworkStream stream, int statusCode, object value, int maxResultBytes)
            {
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
                if (body.Length > Math.Max(1024, maxResultBytes))
                {
                    body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                        Failure(null, "result_too_large", "Runtime result exceeds the configured size limit.")));
                    statusCode = 500;
                }

                var reason = statusCode == 200 ? "OK" : "Error";
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 " + statusCode + " " + reason + "\r\n"
                    + "Content-Type: application/json; charset=utf-8\r\n"
                    + "Content-Length: " + body.Length + "\r\n"
                    + "Connection: close\r\n\r\n");
                stream.Write(headers, 0, headers.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }

            private sealed class HttpRequest
            {
                public string Method;
                public string Path;
                public string Body;
            }
        }
    }
}
