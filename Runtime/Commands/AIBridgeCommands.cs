using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AIBridge.Runtime
{
    public delegate IEnumerator AIBridgeCommandDelegate(AIBridgeCommandContext context);

    public sealed class AIBridgeBindingException : Exception
    {
        public AIBridgeBindingException(string message) : base(message)
        {
        }
    }

    public sealed class AIBridgeCommandContext
    {
        private readonly Func<bool> _isClosed;

        public AIBridgeCommandContext(
            AIBridgeSelfCommandExecuteRequest request,
            Func<bool> isClosed)
            : this(request, isClosed, AIBridgeCommandHost.Player)
        {
        }

        public AIBridgeCommandContext(
            AIBridgeSelfCommandExecuteRequest request,
            Func<bool> isClosed,
            AIBridgeCommandHost host)
        {
            Request = request;
            Parameters = new AIBridgeParameterGetter(request == null ? null : request.parameters);
            _isClosed = isClosed;
            Host = host ?? AIBridgeCommandHost.Player;
        }

        public AIBridgeSelfCommandExecuteRequest Request { get; private set; }
        public AIBridgeParameterGetter Parameters { get; private set; }
        public AIBridgeCommandHost Host { get; private set; }
        public bool IsClosed { get { return _isClosed != null && _isClosed(); } }
    }

    public sealed class AIBridgeParameterGetter
    {
        private readonly IDictionary<string, object> _parameters;

        public AIBridgeParameterGetter(IDictionary<string, object> parameters)
        {
            _parameters = parameters ?? new Dictionary<string, object>();
        }

        public string GetRequiredString(string name)
        {
            var value = GetString(name, null);
            if (string.IsNullOrEmpty(value))
            {
                throw new AIBridgeBindingException("Parameter '" + name + "' is required.");
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

        public object GetRequiredObject(string name)
        {
            var value = GetObject(name);
            if (value == null)
            {
                throw new AIBridgeBindingException("Parameter '" + name + "' is required.");
            }

            return value;
        }

        public object GetObject(string name)
        {
            object value;
            return TryGet(name, out value) ? value : null;
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

        private static AIBridgeBindingException InvalidType(string name, string type)
        {
            return new AIBridgeBindingException(
                "Parameter '" + name + "' must be a " + type + ".");
        }
    }

    public sealed class AIBridgeCommandOutcome
    {
        private AIBridgeCommandOutcome(bool success, object result, string errorCode, string errorMessage)
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

        public static AIBridgeCommandOutcome Succeeded(object result = null)
        {
            return new AIBridgeCommandOutcome(true, result, null, null);
        }

        public static AIBridgeCommandOutcome Failed(string errorMessage)
        {
            return Failed("command_failed", errorMessage);
        }

        public static AIBridgeCommandOutcome Failed(string errorCode, string errorMessage)
        {
            return new AIBridgeCommandOutcome(false, null, errorCode, errorMessage);
        }
    }

    public static class AIBridgeCommandRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, AIBridgeCommandDelegate> Commands =
            new Dictionary<string, AIBridgeCommandDelegate>(StringComparer.Ordinal);
        private static bool _builtInsRegistered;

        public static void Register(string name, AIBridgeCommandDelegate command)
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
                AIBridgeCommandDelegate existing;
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

        public static bool TryGet(string name, out AIBridgeCommandDelegate command)
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

                RegisterBuiltIns();
                _builtInsRegistered = true;
            }
        }

        private static void RegisterBuiltIns()
        {
            Register("EditorCommand_Log", AIBridgeLogCommands.ExecuteLog);
            Register("Log", AIBridgeLogCommands.ExecuteGetLogs);
            Register("GetLogsCommand_StartCapture", AIBridgeLogCommands.ExecuteStartCapture);
            Register("GetLogsCommand_StopCapture", AIBridgeLogCommands.ExecuteStopCapture);
            Register("InputSimulationCommand_Click", AIBridgeInputCommands.ExecuteClick);
            Register("InputSimulationCommand_Drag", AIBridgeInputCommands.ExecuteDrag);
            Register("InputSimulationCommand_LongPress", AIBridgeInputCommands.ExecuteLongPress);
            Register("ScreenshotCommand_Image", AIBridgeScreenshotCommands.ExecuteImage);
            Register("ScreenshotCommand_Gif", AIBridgeScreenshotCommands.ExecuteGif);
        }
    }

    public static class AIBridgeCoroutineExecutor
    {
        private const int MaxStepsPerFrame = 256;

        public static IEnumerator Execute(
            IEnumerator routine,
            AIBridgeCommandContext context,
            int timeoutMs,
            Action<AIBridgeCommandOutcome> completed)
        {
            if (routine == null)
            {
                completed(AIBridgeCommandOutcome.Succeeded());
                yield break;
            }

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            var startedAt = Time.realtimeSinceStartup;
            timeoutMs = AIBridgeProtocol.NormalizeTimeoutMs(timeoutMs);
            AIBridgeCommandOutcome outcome = null;
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
                    completed(AIBridgeCommandOutcome.Failed(
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
                catch (AIBridgeBindingException ex)
                {
                    DisposeAll(stack);
                    completed(AIBridgeCommandOutcome.Failed("binding_failed", ex.Message));
                    yield break;
                }
                catch (Exception ex)
                {
                    DisposeAll(stack);
                    completed(AIBridgeCommandOutcome.Failed(
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

                var commandOutcome = yielded as AIBridgeCommandOutcome;
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
                completed(outcome ?? AIBridgeCommandOutcome.Succeeded());
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

    public static class AIBridgeLogCommands
    {
        [AIBridge(
            "向 Unity 控制台输出日志消息",
            "AIBridgeCLI EditorCommand_Log --message \"Hello World\"",
            "EditorCommand_Log")]
        public static IEnumerator Log(
            [Description("要记录的消息")] string message,
            [Description("日志类型：Log, Warning, Error")] string logType = "Log",
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return ToCommandOutcome(AIBridgeLogService.Emit(message, logType));
        }

        [AIBridge("从 Unity 编辑器获取控制台日志",
            @"默认模式：获取Unity控制台的历史日志（不精确，无时间戳）
精准模式：配合 StartCapture/StopCapture 使用，可获取精确时间段的日志（带毫秒级时间戳）

精准模式使用流程：
1. AIBridgeCLI GetLogsCommand_StartCapture  # 开始捕获
2. 执行需要监控的操作
3. AIBridgeCLI Log --count 100  # 获取捕获的日志（带时间戳）
4. AIBridgeCLI GetLogsCommand_StopCapture  # 停止捕获

示例：
AIBridgeCLI Log --count 50
AIBridgeCLI Log --logType Error --count 20
AIBridgeCLI Log --filter ""NullReference"" --count 30",
            "Log")]
        public static IEnumerator GetLogs(
            [Description("日志类型过滤器：All, Error, Warning, Log")] string logType = "All",
            [Description("文本过滤器（子字符串匹配）")] string filter = null,
            [Description("返回的最大日志数量")] int count = 50,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return ToCommandOutcome(AIBridgeLogService.GetLogs(
                logType,
                filter,
                count,
                AIBridgeCommandHost.Current.UseEditorLogHistory));
        }

        [AIBridge("开始捕获日志到缓冲区（精准模式），捕获的日志带毫秒级时间戳",
            @"用于精确监控某段时间的日志输出。开启后，使用 Log 命令获取的日志将带有精确时间戳。

AIBridgeCLI GetLogsCommand_StartCapture",
            "GetLogsCommand_StartCapture")]
        public static IEnumerator StartCapture(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return ToCommandOutcome(AIBridgeLogService.StartCapture());
        }

        [AIBridge("停止捕获日志，返回捕获的日志总数",
            @"停止精准模式的日志捕获。

AIBridgeCLI GetLogsCommand_StopCapture",
            "GetLogsCommand_StopCapture")]
        public static IEnumerator StopCapture(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return ToCommandOutcome(AIBridgeLogService.StopCapture());
        }

        public static IEnumerator ExecuteLog(AIBridgeCommandContext context)
        {
            var message = context.Parameters.GetRequiredString("message");
            var logType = context.Parameters.GetString("logType", "Log");
            yield return ToCommandOutcome(AIBridgeLogService.Emit(message, logType));
        }

        public static IEnumerator ExecuteGetLogs(AIBridgeCommandContext context)
        {
            var logType = context.Parameters.GetString("logType", "All");
            var filter = context.Parameters.GetString("filter", null);
            var count = context.Parameters.GetInt32("count", 50);
            yield return ToCommandOutcome(
                AIBridgeLogService.GetLogs(
                    logType,
                    filter,
                    count,
                    context.Host.UseEditorLogHistory));
        }

        public static IEnumerator ExecuteStartCapture(AIBridgeCommandContext context)
        {
            yield return ToCommandOutcome(AIBridgeLogService.StartCapture());
        }

        public static IEnumerator ExecuteStopCapture(AIBridgeCommandContext context)
        {
            yield return ToCommandOutcome(AIBridgeLogService.StopCapture());
        }

        internal static void StopAndReset()
        {
            AIBridgeLogService.StopAndReset();
        }

        private static AIBridgeCommandOutcome ToCommandOutcome(AIBridgeLogServiceResult result)
        {
            return result.Success
                ? AIBridgeCommandOutcome.Succeeded(result.Data)
                : AIBridgeCommandOutcome.Failed(result.ErrorCode, result.ErrorMessage);
        }
    }
}
