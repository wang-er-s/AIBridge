using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AIBridge.Runtime
{
    public delegate IEnumerator AIBridgeRuntimeCommandDelegate(AIBridgeRuntimeCommandContext context);

    public sealed class AIBridgeRuntimeBindingException : Exception
    {
        public AIBridgeRuntimeBindingException(string message) : base(message)
        {
        }
    }

    public sealed class AIBridgeRuntimeCommandContext
    {
        private readonly Func<bool> _isClosed;

        public AIBridgeRuntimeCommandContext(
            AIBridgeSelfCommandExecuteRequest request,
            Func<bool> isClosed)
        {
            Request = request;
            Parameters = new AIBridgeRuntimeParameterGetter(request == null ? null : request.parameters);
            _isClosed = isClosed;
        }

        public AIBridgeSelfCommandExecuteRequest Request { get; private set; }
        public AIBridgeRuntimeParameterGetter Parameters { get; private set; }
        public bool IsClosed { get { return _isClosed != null && _isClosed(); } }
    }

    public sealed class AIBridgeRuntimeParameterGetter
    {
        private readonly IDictionary<string, object> _parameters;

        public AIBridgeRuntimeParameterGetter(IDictionary<string, object> parameters)
        {
            _parameters = parameters ?? new Dictionary<string, object>();
        }

        public string GetRequiredString(string name)
        {
            var value = GetString(name, null);
            if (string.IsNullOrEmpty(value))
            {
                throw new AIBridgeRuntimeBindingException("Parameter '" + name + "' is required.");
            }

            return value;
        }

        public string GetString(string name, string defaultValue)
        {
            object value;
            if (!TryGet(name, out value) || value == null)
            {
                return defaultValue;
            }

            var token = value as JToken;
            if (token != null)
            {
                if (token.Type == JTokenType.Null)
                {
                    return defaultValue;
                }

                if (token.Type != JTokenType.String)
                {
                    throw InvalidType(name, "string");
                }

                return token.Value<string>();
            }

            var text = value as string;
            if (text == null)
            {
                throw InvalidType(name, "string");
            }

            return text;
        }

        public int GetInt32(string name, int defaultValue)
        {
            object value;
            if (!TryGet(name, out value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                var token = value as JToken;
                if (token != null)
                {
                    return token.Value<int>();
                }

                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw InvalidType(name, "integer");
            }
        }

        public float GetSingle(string name, float defaultValue)
        {
            object value;
            if (!TryGet(name, out value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                var token = value as JToken;
                if (token != null)
                {
                    return token.Value<float>();
                }

                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw InvalidType(name, "number");
            }
        }

        public bool GetBoolean(string name, bool defaultValue)
        {
            object value;
            if (!TryGet(name, out value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                var token = value as JToken;
                if (token != null)
                {
                    return token.Value<bool>();
                }

                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw InvalidType(name, "boolean");
            }
        }

        private bool TryGet(string name, out object value)
        {
            if (_parameters.TryGetValue(name, out value))
            {
                return true;
            }

            foreach (var pair in _parameters)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static AIBridgeRuntimeBindingException InvalidType(string name, string type)
        {
            return new AIBridgeRuntimeBindingException(
                "Parameter '" + name + "' must be a " + type + ".");
        }
    }

    public sealed class AIBridgeRuntimeCommandOutcome
    {
        private AIBridgeRuntimeCommandOutcome(bool success, object result, string errorCode, string errorMessage)
        {
            Success = success;
            Result = result;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; private set; }
        public object Result { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }

        public static AIBridgeRuntimeCommandOutcome Succeeded(object result = null)
        {
            return new AIBridgeRuntimeCommandOutcome(true, result, null, null);
        }

        public static AIBridgeRuntimeCommandOutcome Failed(string errorMessage)
        {
            return Failed("command_failed", errorMessage);
        }

        public static AIBridgeRuntimeCommandOutcome Failed(string errorCode, string errorMessage)
        {
            return new AIBridgeRuntimeCommandOutcome(false, null, errorCode, errorMessage);
        }
    }

    public static class AIBridgeRuntimeCommandRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, AIBridgeRuntimeCommandDelegate> Commands =
            new Dictionary<string, AIBridgeRuntimeCommandDelegate>(StringComparer.Ordinal);
        private static bool _builtInsRegistered;

        public static void Register(string name, AIBridgeRuntimeCommandDelegate command)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Runtime command name is required.", "name");
            }

            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            lock (SyncRoot)
            {
                AIBridgeRuntimeCommandDelegate existing;
                if (Commands.TryGetValue(name, out existing))
                {
                    if (existing == command)
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        "A conflicting Runtime command is already registered: " + name);
                }

                Commands.Add(name, command);
            }
        }

        public static bool TryGet(string name, out AIBridgeRuntimeCommandDelegate command)
        {
            EnsureBuiltInsRegistered();
            lock (SyncRoot)
            {
                return Commands.TryGetValue(name ?? string.Empty, out command);
            }
        }

        public static void EnsureBuiltInsRegistered()
        {
            lock (SyncRoot)
            {
                if (_builtInsRegistered)
                {
                    return;
                }

                _builtInsRegistered = true;
                RegisterBuiltIns();
            }
        }

        private static void RegisterBuiltIns()
        {
            Register("EditorCommand_Log", AIBridgeRuntimeLogCommands.Log);
            Register("Log", AIBridgeRuntimeLogCommands.GetLogs);
            Register("GetLogsCommand_StartCapture", AIBridgeRuntimeLogCommands.StartCapture);
            Register("GetLogsCommand_StopCapture", AIBridgeRuntimeLogCommands.StopCapture);
            Register("InputSimulationCommand_Click", AIBridgeRuntimeInputCommands.Click);
            Register("InputSimulationCommand_ClickByInstanceId", AIBridgeRuntimeInputCommands.ClickByInstanceId);
            Register("InputSimulationCommand_ClickAt", AIBridgeRuntimeInputCommands.ClickAt);
            Register("InputSimulationCommand_Drag", AIBridgeRuntimeInputCommands.Drag);
            Register("InputSimulationCommand_DragByInstanceId", AIBridgeRuntimeInputCommands.DragByInstanceId);
            Register("InputSimulationCommand_LongPress", AIBridgeRuntimeInputCommands.LongPress);
            Register("InputSimulationCommand_LongPressByInstanceId", AIBridgeRuntimeInputCommands.LongPressByInstanceId);
            Register("ScreenshotCommand_Image", AIBridgeRuntimeScreenshotCommands.Image);
            Register("ScreenshotCommand_Gif", AIBridgeRuntimeScreenshotCommands.Gif);
        }
    }

    public static class AIBridgeRuntimeCoroutineExecutor
    {
        private const int MaxStepsPerFrame = 256;

        public static IEnumerator Execute(
            IEnumerator routine,
            AIBridgeRuntimeCommandContext context,
            int timeoutMs,
            Action<AIBridgeRuntimeCommandOutcome> completed)
        {
            if (routine == null)
            {
                completed(AIBridgeRuntimeCommandOutcome.Succeeded());
                yield break;
            }

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            var startedAt = Time.realtimeSinceStartup;
            timeoutMs = AIBridgeRuntimeProtocol.NormalizeTimeoutMs(timeoutMs);
            AIBridgeRuntimeCommandOutcome outcome = null;
            var stepsThisFrame = 0;

            // 手动展开嵌套枚举器，既保留 Unity yield 语义，也能截获最深层命令结果。
            while (stack.Count > 0)
            {
                if (context.IsClosed)
                {
                    DisposeAll(stack);
                    yield break;
                }

                if ((Time.realtimeSinceStartup - startedAt) * 1000f >= timeoutMs)
                {
                    DisposeAll(stack);
                    completed(AIBridgeRuntimeCommandOutcome.Failed(
                        "execution_timeout",
                        "Runtime command execution timed out."));
                    yield break;
                }

                if (stepsThisFrame >= MaxStepsPerFrame)
                {
                    stepsThisFrame = 0;
                    yield return null;
                    continue;
                }

                var current = stack.Peek();
                bool moved;
                object yielded;
                try
                {
                    moved = current.MoveNext();
                    yielded = moved ? current.Current : null;
                }
                catch (AIBridgeRuntimeBindingException ex)
                {
                    DisposeAll(stack);
                    completed(AIBridgeRuntimeCommandOutcome.Failed("binding_failed", ex.Message));
                    yield break;
                }
                catch (Exception ex)
                {
                    DisposeAll(stack);
                    completed(AIBridgeRuntimeCommandOutcome.Failed(
                        "execution_failed",
                        ex.GetType().Name + ": " + ex.Message));
                    yield break;
                }
                stepsThisFrame++;

                if (!moved)
                {
                    DisposeEnumerator(stack.Pop());
                    continue;
                }

                var customYield = yielded as CustomYieldInstruction;
                if (customYield != null)
                {
                    // CustomYieldInstruction 由执行器逐帧轮询，确保取消和超时不会被 Unity 接管后延迟处理。
                    stack.Push(customYield);
                    continue;
                }

                var nested = yielded as IEnumerator;
                if (nested != null)
                {
                    stack.Push(nested);
                    continue;
                }

                var commandOutcome = yielded as AIBridgeRuntimeCommandOutcome;
                if (commandOutcome != null)
                {
                    outcome = commandOutcome;
                    if (!commandOutcome.Success)
                    {
                        DisposeAll(stack);
                        completed(commandOutcome);
                        yield break;
                    }

                    continue;
                }

                stepsThisFrame = 0;
                yield return yielded;
            }

            if (!context.IsClosed)
            {
                completed(outcome ?? AIBridgeRuntimeCommandOutcome.Succeeded());
            }
        }

        private static void DisposeAll(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                try
                {
                    DisposeEnumerator(stack.Pop());
                }
                catch
                {
                    // 清理单个枚举器失败时仍继续释放外层枚举器，避免其 finally 永远不执行。
                }
            }
        }

        private static void DisposeEnumerator(IEnumerator enumerator)
        {
            var disposable = enumerator as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }

    internal static class AIBridgeRuntimeLogCommands
    {
        private const int MaxLogs = 1000;
        private const int MaxMessageCharacters = 16 * 1024;
        private const int MaxTotalCharacters = 512 * 1024;
        private static readonly object LogLock = new object();
        private static readonly Queue<LogEntry> Logs = new Queue<LogEntry>();
        private static bool _capturing;
        private static int _totalCharacters;

        public static IEnumerator Log(AIBridgeRuntimeCommandContext context)
        {
            var message = context.Parameters.GetRequiredString("message");
            var logType = context.Parameters.GetString("logType", "Log");
            switch (logType.ToLowerInvariant())
            {
                case "warning":
                    Debug.LogWarning("[AIBridge] " + message);
                    break;
                case "error":
                    Debug.LogError("[AIBridge] " + message);
                    break;
                case "log":
                    Debug.Log("[AIBridge] " + message);
                    break;
                default:
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "binding_failed",
                        "Parameter 'logType' must be Log, Warning, or Error.");
                    yield break;
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "log" },
                { "message", message },
                { "logType", logType }
            });
        }

        public static IEnumerator GetLogs(AIBridgeRuntimeCommandContext context)
        {
            var logType = context.Parameters.GetString("logType", "All");
            var filter = context.Parameters.GetString("filter", null);
            var count = Mathf.Max(0, context.Parameters.GetInt32("count", 50));
            var results = new List<object>();

            lock (LogLock)
            {
                var entries = Logs.ToArray();
                for (var i = entries.Length - 1; i >= 0 && results.Count < count; i--)
                {
                    var entry = entries[i];
                    if (!MatchesType(entry.Type, logType)
                        || (!string.IsNullOrEmpty(filter)
                            && entry.Message.IndexOf(filter, StringComparison.Ordinal) < 0))
                    {
                        continue;
                    }

                    results.Add(new Dictionary<string, object>
                    {
                        { "type", entry.Type.ToString() },
                        { "message", entry.Message },
                        { "time", entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) }
                    });
                }
            }

            results.Reverse();
            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "count", results.Count },
                { "logs", results }
            });
        }

        public static IEnumerator StartCapture(AIBridgeRuntimeCommandContext context)
        {
            lock (LogLock)
            {
                Logs.Clear();
                _totalCharacters = 0;
                if (!_capturing)
                {
                    Application.logMessageReceivedThreaded += OnLogMessage;
                    _capturing = true;
                }
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Console capture started" }
            });
        }

        public static IEnumerator StopCapture(AIBridgeRuntimeCommandContext context)
        {
            int count;
            lock (LogLock)
            {
                if (_capturing)
                {
                    Application.logMessageReceivedThreaded -= OnLogMessage;
                    _capturing = false;
                }

                count = Logs.Count;
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Console capture stopped" },
                { "capturedCount", count }
            });
        }

        internal static void StopAndReset()
        {
            lock (LogLock)
            {
                if (_capturing)
                {
                    Application.logMessageReceivedThreaded -= OnLogMessage;
                    _capturing = false;
                }

                Logs.Clear();
                _totalCharacters = 0;
            }
        }

        private static bool MatchesType(LogType type, string filter)
        {
            if (string.IsNullOrEmpty(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(filter, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            }

            if (string.Equals(filter, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                return type == LogType.Warning;
            }

            if (string.Equals(filter, "Log", StringComparison.OrdinalIgnoreCase))
            {
                return type == LogType.Log;
            }

            return false;
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            lock (LogLock)
            {
                if (!_capturing)
                {
                    return;
                }

                var boundedMessage = message ?? string.Empty;
                if (boundedMessage.Length > MaxMessageCharacters)
                {
                    boundedMessage = boundedMessage.Substring(0, MaxMessageCharacters);
                }

                var entry = new LogEntry
                {
                    Message = boundedMessage,
                    Type = type,
                    Time = DateTime.Now,
                    CharacterCount = boundedMessage.Length
                };
                Logs.Enqueue(entry);
                _totalCharacters += entry.CharacterCount;
                while (Logs.Count > MaxLogs || _totalCharacters > MaxTotalCharacters)
                {
                    _totalCharacters -= Logs.Dequeue().CharacterCount;
                }
            }
        }

        private sealed class LogEntry
        {
            public string Message;
            public LogType Type;
            public DateTime Time;
            public int CharacterCount;
        }
    }
}
