using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AIBridge.Runtime;
using UnityEngine;

namespace AIBridge.Editor
{
    public static class GetLogsCommand
    {
        private static bool _capturing;
        private static readonly List<LogEntry> _logs = new List<LogEntry>();

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
        public static IEnumerator Log(
            [Description("日志类型过滤器：All, Error, Warning, Log")] string logType = "All",
            [Description("文本过滤器（子字符串匹配）")] string filter = null,
            [Description("返回的最大日志数量")] int count = 50,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (_capturing)
            {
                IEnumerable<LogEntry> results = _logs;
                if (!logType.Equals("All", StringComparison.OrdinalIgnoreCase))
                    results = results.Where(l => MatchesType(l.type, logType));
                if (!string.IsNullOrEmpty(filter))
                    results = results.Where(l => l.message.Contains(filter));

                var captured = results.TakeLast(count).Select(l => new
                {
                    type = l.type.ToString(),
                    message = l.message,
                    time = l.time.ToString("HH:mm:ss.fff")
                }).ToArray();

                yield return CommandResult.Success(new { count = captured.Length, logs = captured });
            }
            else
            {
                int targetMask = 0;
                if (logType == "All" || logType.Contains("Error"))   targetMask |= DebugSkills.ErrorModeMask;
                if (logType == "All" || logType.Contains("Warning")) targetMask |= DebugSkills.WarningModeMask;
                if (logType == "All" || logType.Contains("Log"))     targetMask |= DebugSkills.LogModeMask;

                var log = DebugSkills.ReadLogEntries(targetMask, filter, count);
                if (log.Count <= 0)
                {
                    yield return CommandResult.Success("No logs found");
                }
                else
                {
                    yield return CommandResult.Success(log);
                }
            }
        }

        [AIBridge("开始捕获日志到缓冲区（精准模式），捕获的日志带毫秒级时间戳",
            @"用于精确监控某段时间的日志输出。开启后，使用 Log 命令获取的日志将带有精确时间戳。

AIBridgeCLI GetLogsCommand_StartCapture")]
        public static IEnumerator StartCapture(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!_capturing)
            {
                Application.logMessageReceived += OnLogMessage;
                _capturing = true;
            }
            _logs.Clear();
            yield return CommandResult.Success(new { success = true, message = "Console capture started" });
        }

        [AIBridge("停止捕获日志，返回捕获的日志总数",
            @"停止精准模式的日志捕获。

AIBridgeCLI GetLogsCommand_StopCapture")]
        public static IEnumerator StopCapture(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (_capturing)
            {
                Application.logMessageReceived -= OnLogMessage;
                _capturing = false;
            }
            yield return CommandResult.Success(new { success = true, message = "Console capture stopped", capturedCount = _logs.Count });
        }

        private static bool MatchesType(LogType logType, string typeFilter)
        {
            switch (typeFilter)
            {
                case "Error": return logType == LogType.Error || logType == LogType.Exception || logType == LogType.Assert;
                case "Warning": return logType == LogType.Warning;
                case "Log": return logType == LogType.Log;
                default: return true;
            }
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            _logs.Add(new LogEntry { message = message, stackTrace = stackTrace, type = type, time = DateTime.Now });
            if (_logs.Count > 1000) _logs.RemoveAt(0);
        }

        private class LogEntry
        {
            public string message;
            public string stackTrace;
            public LogType type;
            public DateTime time;
        }
    }
}
