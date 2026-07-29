using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed class AIBridgeBridge : MonoBehaviour
    {
        private const int MaxResultDepth = 8;
        private const int MaxCollectionItems = 512;
        private const int MaxPendingCommands = 8;

        private readonly Queue<PendingCommand> _commands = new Queue<PendingCommand>();
        private readonly HashSet<PendingCommand> _activeCommands = new HashSet<PendingCommand>();
        private HttpServer _server;
        private bool _initialized;
        private int _pendingCommandCount;

        public AIBridgeSettings settings = new AIBridgeSettings();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            AIBridgeCommandRegistry.EnsureBuiltInsRegistered();
            AIBridgeScreenshotCommands.Initialize();
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
                lock (_commands)
                {
                    _initialized = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AIBridgeSelfRuntime] Failed to start Runtime Bridge: " + ex);
            }
        }

        private void OnDisable()
        {
            lock (_commands)
            {
                _initialized = false;
                var activeCommands = new List<PendingCommand>(_activeCommands);
                for (var i = 0; i < activeCommands.Count; i++)
                {
                    var active = activeCommands[i];
                    active.Complete(active.CreateFailure("bridge_stopped", "Runtime Bridge stopped."));
                }

                while (_commands.Count > 0)
                {
                    var pending = _commands.Dequeue();
                    pending.Complete(pending.CreateFailure("bridge_stopped", "Runtime Bridge stopped."));
                }
            }

            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }

            AIBridgeLogCommands.StopAndReset();
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
                    if (!command.IsClosed)
                    {
                        _activeCommands.Add(command);
                    }
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

            lock (_commands)
            {
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

                if (_pendingCommandCount >= MaxPendingCommands)
                {
                    error = "Runtime command queue is full.";
                    return false;
                }

                PendingCommand created = null;
                created = new PendingCommand(request, () => HandlePendingClosed(created));
                pending = created;
                _pendingCommandCount++;
                _commands.Enqueue(pending);
                return true;
            }
        }

        internal bool Enqueue(AIBridgeSelfCommandExecuteRequest request, out PendingCommand pending, out string error)
        {
            pending = null;
            error = null;

            lock (_commands)
            {
                if (!_initialized)
                {
                    error = "Runtime Bridge is not ready.";
                    return false;
                }

                if (_pendingCommandCount >= MaxPendingCommands)
                {
                    error = "Runtime command queue is full.";
                    return false;
                }

                PendingCommand created = null;
                created = new PendingCommand(request, () => HandlePendingClosed(created));
                pending = created;
                _pendingCommandCount++;
                _commands.Enqueue(pending);
                return true;
            }
        }

        private void HandlePendingClosed(PendingCommand pending)
        {
            lock (_commands)
            {
                _pendingCommandCount--;
                _activeCommands.Remove(pending);
            }
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
            if (pending.IsRuntimeCommand)
            {
                ExecuteRuntimeCommand(pending);
                return;
            }

            ExecuteCodeOnMainThread(pending);
        }

        private void ExecuteCodeOnMainThread(PendingCommand pending)
        {
            var request = pending.Request;
            AIBridgeSelfResult validationFailure;
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

        private void ExecuteRuntimeCommand(PendingCommand pending)
        {
            var request = pending.CommandRequest;
            if (request == null)
            {
                pending.Complete(CommandFailure(null, "invalid_request", "Request body is required."));
                return;
            }

            if (!string.Equals(request.action, AIBridgeProtocol.CommandExecuteAction, StringComparison.Ordinal))
            {
                pending.Complete(CommandFailure(request, "invalid_action", "Unsupported Runtime action."));
                return;
            }

            if (string.IsNullOrWhiteSpace(request.type))
            {
                pending.Complete(CommandFailure(request, "missing_parameter", "Runtime command type is required."));
                return;
            }

            AIBridgeCommandDelegate command;
            if (!AIBridgeCommandRegistry.TryGet(request.type, out command))
            {
                pending.Complete(CommandFailure(
                    request,
                    "unknown_command",
                    "Unknown Runtime command: " + request.type));
                return;
            }

            var context = new AIBridgeCommandContext(
                request,
                () => pending.IsClosed,
                AIBridgeCommandHost.Player);
            IEnumerator routine;
            try
            {
                routine = command(context);
            }
            catch (AIBridgeBindingException ex)
            {
                pending.Complete(CommandFailure(request, "binding_failed", ex.Message));
                return;
            }
            catch (Exception ex)
            {
                pending.Complete(CommandFailure(
                    request,
                    "execution_failed",
                    ex.GetType().Name + ": " + ex.Message));
                return;
            }

            StartCoroutine(AIBridgeCoroutineExecutor.Execute(
                routine,
                context,
                AIBridgeProtocol.NormalizeTimeoutMs(request.timeoutMs),
                outcome =>
                {
                    if (outcome.Success)
                    {
                        pending.Complete(CommandSuccess(request, NormalizeValue(outcome.Result, 0)));
                    }
                    else
                    {
                        pending.Complete(CommandFailure(
                            request,
                            string.IsNullOrEmpty(outcome.ErrorCode) ? "command_failed" : outcome.ErrorCode,
                            outcome.ErrorMessage));
                    }
                }));
        }

        private IEnumerator CompleteTask(PendingCommand pending, Task task)
        {
            var timeoutMs = AIBridgeProtocol.NormalizeTimeoutMs(pending.Request == null
                ? AIBridgeProtocol.DefaultExecutionTimeoutMs
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
            out AIBridgeSelfResult failure)
        {
            assemblyBytes = null;
            failure = null;
            if (request == null)
            {
                failure = Failure(null, "invalid_request", "Request body is required.");
                return false;
            }

            if (!string.Equals(request.action, AIBridgeProtocol.CodeExecuteAction, StringComparison.Ordinal))
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
                ? AIBridgeProtocol.MaxAssemblyBytes
                : Math.Max(1, settings.maxAssemblyBytes);
            if (assemblyBytes.Length == 0
                || assemblyBytes.Length > maxAssemblyBytes
                || assemblyBytes.Length > AIBridgeProtocol.MaxAssemblyBytes)
            {
                failure = Failure(request, "assembly_too_large", "Runtime assembly size is invalid.");
                return false;
            }

            var actualSha256 = AIBridgeProtocol.ComputeSha256(assemblyBytes);
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

        private static AIBridgeSelfResult Success(AIBridgeSelfCodeExecuteRequest request, object result)
        {
            return new AIBridgeSelfResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeProtocol.CodeExecuteAction,
                success = true,
                result = result
            };
        }

        private static AIBridgeSelfResult Failure(
            AIBridgeSelfCodeExecuteRequest request,
            string errorCode,
            string errorMessage)
        {
            return new AIBridgeSelfResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeProtocol.CodeExecuteAction,
                success = false,
                errorCode = errorCode,
                errorMessage = errorMessage
            };
        }

        private static AIBridgeSelfResult CommandSuccess(
            AIBridgeSelfCommandExecuteRequest request,
            object result)
        {
            return new AIBridgeSelfResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeProtocol.CommandExecuteAction,
                success = true,
                result = result
            };
        }

        private static AIBridgeSelfResult CommandFailure(
            AIBridgeSelfCommandExecuteRequest request,
            string errorCode,
            string errorMessage)
        {
            return new AIBridgeSelfResult
            {
                id = request == null ? null : request.id,
                action = AIBridgeProtocol.CommandExecuteAction,
                success = false,
                errorCode = errorCode,
                errorMessage = errorMessage
            };
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

            public PendingCommand(AIBridgeSelfCommandExecuteRequest request, Action onClosed)
            {
                CommandRequest = request;
                IsRuntimeCommand = true;
                _onClosed = onClosed;
            }

            public AIBridgeSelfCodeExecuteRequest Request { get; private set; }
            public AIBridgeSelfCommandExecuteRequest CommandRequest { get; private set; }
            public bool IsRuntimeCommand { get; private set; }
            public AIBridgeSelfResult Result { get; private set; }
            public bool IsClosed { get { return Volatile.Read(ref _closed) != 0; } }

            public bool Wait(int timeoutMs)
            {
                return _completed.Wait(AIBridgeProtocol.NormalizeTimeoutMs(timeoutMs));
            }

            public void Complete(AIBridgeSelfResult result)
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

            public AIBridgeSelfResult CreateFailure(string errorCode, string errorMessage)
            {
                return IsRuntimeCommand
                    ? CommandFailure(CommandRequest, errorCode, errorMessage)
                    : Failure(Request, errorCode, errorMessage);
            }
        }

        private sealed class HttpServer : IDisposable
        {
            private const int MaxHeaderBytes = 64 * 1024;
            private const int ArtifactSendTimeoutMs = 120000;
            private readonly AIBridgeBridge _bridge;
            private readonly AIBridgeSettings _settings;
            private TcpListener _listener;
            private Thread _thread;
            private volatile bool _running;

            public HttpServer(AIBridgeBridge bridge, AIBridgeSettings settings)
            {
                _bridge = bridge;
                _settings = settings ?? new AIBridgeSettings();
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
                            && string.Equals(request.Path, AIBridgeProtocol.HealthPath, StringComparison.Ordinal))
                        {
                            WriteJson(client.GetStream(), 200, new AIBridgeSelfHealth
                            {
                                service = "aibridge-self-runtime",
                                ready = true,
                                endpoint = AIBridgeProtocol.CodeExecutePath,
                                commandEndpoint = AIBridgeProtocol.CommandExecutePath,
                                artifactEndpoint = AIBridgeProtocol.ArtifactPathPrefix
                            }, _settings.maxResultBytes);
                            return;
                        }

                        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                            && request.Path.StartsWith(
                                AIBridgeProtocol.ArtifactPathPrefix,
                                StringComparison.Ordinal))
                        {
                            HandleArtifact(client.GetStream(), request.Path);
                            return;
                        }

                        var isCodeEndpoint = string.Equals(
                            request.Path,
                            AIBridgeProtocol.CodeExecutePath,
                            StringComparison.Ordinal);
                        var isCommandEndpoint = string.Equals(
                            request.Path,
                            AIBridgeProtocol.CommandExecutePath,
                            StringComparison.Ordinal);
                        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
                            || (!isCodeEndpoint && !isCommandEndpoint))
                        {
                            WriteJson(client.GetStream(), 404, Failure(null, "not_found", "Runtime endpoint not found."), _settings.maxResultBytes);
                            return;
                        }

                        if (isCodeEndpoint)
                        {
                            HandleCodeExecute(client.GetStream(), request.Body);
                        }
                        else
                        {
                            HandleCommandExecute(client.GetStream(), request.Body);
                        }
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

            private void HandleCodeExecute(NetworkStream stream, string body)
            {
                AIBridgeSelfCodeExecuteRequest command;
                try
                {
                    command = JsonConvert.DeserializeObject<AIBridgeSelfCodeExecuteRequest>(body);
                }
                catch (Exception ex)
                {
                    WriteJson(stream, 400, Failure(null, "invalid_json", ex.Message), _settings.maxResultBytes);
                    return;
                }

                PendingCommand pending;
                string error;
                if (!_bridge.Enqueue(command, out pending, out error))
                {
                    WriteJson(stream, 503, Failure(command, "bridge_unavailable", error), _settings.maxResultBytes);
                    return;
                }

                if (!pending.Wait(command == null ? 0 : command.timeoutMs))
                {
                    pending.Complete(Failure(command, "request_timeout", "Timed out waiting for Runtime execution."));
                }

                WriteJson(stream, 200, pending.Result, _settings.maxResultBytes);
            }

            private void HandleCommandExecute(NetworkStream stream, string body)
            {
                AIBridgeSelfCommandExecuteRequest command;
                try
                {
                    command = JsonConvert.DeserializeObject<AIBridgeSelfCommandExecuteRequest>(body);
                }
                catch (Exception ex)
                {
                    WriteJson(stream, 400, CommandFailure(null, "invalid_json", ex.Message), _settings.maxResultBytes);
                    return;
                }

                PendingCommand pending;
                string error;
                if (!_bridge.Enqueue(command, out pending, out error))
                {
                    WriteJson(
                        stream,
                        503,
                        CommandFailure(command, "bridge_unavailable", error),
                        _settings.maxResultBytes);
                    return;
                }

                if (!pending.Wait(command == null ? 0 : command.timeoutMs))
                {
                    pending.Complete(CommandFailure(
                        command,
                        "request_timeout",
                        "Timed out waiting for Runtime command execution."));
                }

                WriteJson(stream, 200, pending.Result, _settings.maxResultBytes);
            }

            private static void HandleArtifact(NetworkStream stream, string requestPath)
            {
                stream.WriteTimeout = ArtifactSendTimeoutMs;
                var encodedFilename = requestPath.Substring(AIBridgeProtocol.ArtifactPathPrefix.Length);
                string filename;
                try
                {
                    filename = Uri.UnescapeDataString(encodedFilename);
                }
                catch (UriFormatException)
                {
                    WriteJson(stream, 400, Failure(null, "invalid_artifact_path", "Artifact path is invalid."),
                        AIBridgeProtocol.MaxResultBytes);
                    return;
                }

                var extension = Path.GetExtension(filename);
                if (string.IsNullOrEmpty(filename)
                    || !string.Equals(filename, Path.GetFileName(filename), StringComparison.Ordinal)
                    || filename.IndexOf('/') >= 0
                    || filename.IndexOf('\\') >= 0
                    || (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)))
                {
                    WriteJson(stream, 404, Failure(null, "artifact_not_found", "Artifact not found."),
                        AIBridgeProtocol.MaxResultBytes);
                    return;
                }

                var path = Path.Combine(AIBridgeScreenshotCommands.GetScreenshotDirectory(), filename);
                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                    if (!file.Exists)
                    {
                        WriteJson(stream, 404, Failure(null, "artifact_not_found", "Artifact not found."),
                            AIBridgeProtocol.MaxResultBytes);
                        return;
                    }

                    if (file.Length < 0 || file.Length > AIBridgeProtocol.MaxArtifactBytes)
                    {
                        WriteJson(stream, 413, Failure(null, "artifact_too_large", "Artifact exceeds the size limit."),
                            AIBridgeProtocol.MaxResultBytes);
                        return;
                    }
                }
                catch (IOException)
                {
                    WriteJson(stream, 404, Failure(null, "artifact_not_found", "Artifact not found."),
                        AIBridgeProtocol.MaxResultBytes);
                    return;
                }

                var contentType = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : "image/gif";
                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var headers = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: " + contentType + "\r\n"
                        + "Content-Length: " + fileStream.Length + "\r\n"
                        + "Connection: close\r\n\r\n");
                    stream.Write(headers, 0, headers.Length);
                    var buffer = new byte[64 * 1024];
                    long remaining = fileStream.Length;
                    while (remaining > 0)
                    {
                        var read = fileStream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0)
                        {
                            throw new EndOfStreamException("Unexpected end of artifact file.");
                        }

                        stream.Write(buffer, 0, read);
                        remaining -= read;
                    }

                    stream.Flush();
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
                    var runtimeResult = value as AIBridgeSelfResult;
                    var tooLargeFailure = runtimeResult != null
                        && string.Equals(
                            runtimeResult.action,
                            AIBridgeProtocol.CommandExecuteAction,
                            StringComparison.Ordinal)
                        ? CommandFailure(null, "result_too_large", "Runtime result exceeds the configured size limit.")
                        : Failure(null, "result_too_large", "Runtime result exceeds the configured size limit.");
                    body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                        tooLargeFailure));
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
