using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace AIBridge.Runtime
{
    /// <summary>
    /// Shared log emission, capture, filtering, and Editor history implementation.
    /// UnityEditor APIs are isolated behind UNITY_EDITOR so this assembly remains Player-safe.
    /// </summary>
    public static class AIBridgeLogService
    {
        private const int MaxLogs = 1000;
        private const int MaxMessageCharacters = 16 * 1024;
        private const int MaxTotalCharacters = 512 * 1024;

        // Unity LogEntry mode bits (from UnityCsReference).
        private const int ModeError = 1;
        private const int ModeAssert = 2;
        private const int ModeLog = 4;
        private const int ModeFatal = 16;
        private const int ModeAssetImportError = 64;
        private const int ModeAssetImportWarning = 128;
        private const int ModeScriptingError = 256;
        private const int ModeScriptingWarning = 512;
        private const int ModeScriptingLog = 1024;
        private const int ModeScriptCompileError = 2048;
        private const int ModeScriptCompileWarning = 4096;
        private const int ModeScriptingException = 131072;

        private const int ErrorModeMask =
            ModeError | ModeAssert | ModeFatal | ModeAssetImportError | ModeScriptingError
            | ModeScriptCompileError | ModeScriptingException;
        private const int WarningModeMask =
            ModeAssetImportWarning | ModeScriptingWarning | ModeScriptCompileWarning;
        private const int LogModeMask = ModeLog | ModeScriptingLog;

        private static readonly object LogLock = new object();
        private static readonly Queue<LogEntry> Logs = new Queue<LogEntry>();
        private static bool _capturing;
        private static int _totalCharacters;

#if UNITY_EDITOR
        private static readonly object HistoryLock = new object();
        private static Type _logEntriesType;
        private static Type _logEntryType;
        private static MethodInfo _getCountMethod;
        private static MethodInfo _getEntryMethod;
        private static MethodInfo _startMethod;
        private static MethodInfo _endMethod;
        private static FieldInfo _modeField;
        private static FieldInfo _messageField;
        private static FieldInfo _fileField;
        private static FieldInfo _lineField;
#endif

        public static bool IsCapturing
        {
            get
            {
                lock (LogLock)
                {
                    return _capturing;
                }
            }
        }

        public static AIBridgeLogServiceResult Emit(string message, string logType)
        {
            if (string.IsNullOrEmpty(message))
            {
                return AIBridgeLogServiceResult.Failed(
                    "binding_failed",
                    "Parameter 'message' is required.");
            }

            var requestedType = logType ?? "Log";
            string normalizedType;
            if (!TryNormalizeType(requestedType, false, out normalizedType))
            {
                return InvalidLogType(false);
            }

            switch (normalizedType)
            {
                case "Warning":
                    Debug.LogWarning("[AIBridge] " + message);
                    break;
                case "Error":
                    Debug.LogError("[AIBridge] " + message);
                    break;
                default:
                    Debug.Log("[AIBridge] " + message);
                    break;
            }

            return AIBridgeLogServiceResult.Succeeded(new Dictionary<string, object>
            {
                { "action", "log" },
                { "message", message },
                { "logType", requestedType }
            });
        }

        /// <summary>
        /// Reads captured logs, or (for Editor adapters) uncaptured historical Console entries.
        /// </summary>
        public static AIBridgeLogServiceResult GetLogs(
            string logType,
            string filter,
            int count,
            bool useEditorHistoryWhenNotCapturing)
        {
            var requestedType = logType ?? "All";
            string normalizedType;
            if (!TryNormalizeType(requestedType, true, out normalizedType))
            {
                return InvalidLogType(true);
            }

            count = Math.Max(0, count);
            if (useEditorHistoryWhenNotCapturing && !IsCapturing)
            {
#if UNITY_EDITOR
                List<object> history;
                string historyError;
                if (!TryReadEditorLogEntries(
                    GetModeMask(normalizedType),
                    filter,
                    count,
                    out history,
                    out historyError))
                {
                    return AIBridgeLogServiceResult.Failed(
                        "editor_history_unavailable",
                        historyError);
                }

                return AIBridgeLogServiceResult.Succeeded(
                    history.Count == 0 ? (object)"No logs found" : history);
#else
                return AIBridgeLogServiceResult.Failed(
                    "editor_history_unavailable",
                    "Unity Console history is only available in the Editor.");
#endif
            }

            var results = new List<object>();
            LogEntry[] entries;
            lock (LogLock)
            {
                entries = Logs.ToArray();
            }

            for (var i = entries.Length - 1; i >= 0 && results.Count < count; i--)
            {
                var entry = entries[i];
                if (!MatchesType(entry.Type, normalizedType)
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

            results.Reverse();
            return AIBridgeLogServiceResult.Succeeded(new Dictionary<string, object>
            {
                { "count", results.Count },
                { "logs", results }
            });
        }

        public static AIBridgeLogServiceResult StartCapture()
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

            return AIBridgeLogServiceResult.Succeeded(new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Console capture started" }
            });
        }

        public static AIBridgeLogServiceResult StopCapture()
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

            return AIBridgeLogServiceResult.Succeeded(new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Console capture stopped" },
                { "capturedCount", count }
            });
        }

        public static void StopAndReset()
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

        private static bool TryReadEditorLogEntries(
            int targetMask,
            string filter,
            int limit,
            out List<object> results,
            out string error)
        {
            results = new List<object>();
            error = null;
#if UNITY_EDITOR
            limit = Math.Max(0, limit);
            lock (HistoryLock)
            {
                if (!EnsureEditorLogReflectionLocked())
                {
                    error = "Unity Console history APIs are unavailable.";
                    return false;
                }

                var entry = Activator.CreateInstance(_logEntryType);
                var started = false;
                try
                {
                    _startMethod.Invoke(null, null);
                    started = true;
                    var count = (int)_getCountMethod.Invoke(null, null);
                    for (var i = count - 1; i >= 0 && results.Count < limit; i--)
                    {
                        _getEntryMethod.Invoke(null, new[] { (object)i, entry });
                        var mode = (int)_modeField.GetValue(entry);
                        if ((mode & targetMask) == 0)
                        {
                            continue;
                        }

                        var message = (string)_messageField.GetValue(entry) ?? string.Empty;
                        if (!string.IsNullOrEmpty(filter)
                            && message.IndexOf(filter, StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        var file = (string)_fileField.GetValue(entry) ?? string.Empty;
                        var line = (int)_lineField.GetValue(entry);
                        var entryType = (mode & ErrorModeMask) != 0
                            ? "Error"
                            : (mode & WarningModeMask) != 0 ? "Warning" : "Log";

                        results.Add(new Dictionary<string, object>
                        {
                            { "type", entryType },
                            {
                                "message",
                                message.Length > 500 ? message.Substring(0, 500) + "..." : message
                            },
                            { "file", file },
                            { "line", line }
                        });
                    }
                }
                catch (Exception ex)
                {
                    error = "Failed to read Unity Console history: " + ex.Message;
                    ClearEditorReflection();
                    return false;
                }
                finally
                {
                    if (started)
                    {
                        try
                        {
                            _endMethod.Invoke(null, null);
                        }
                        catch (Exception ex)
                        {
                            if (error == null)
                            {
                                error = "Failed to finish reading Unity Console history: "
                                    + ex.Message;
                            }
                        }
                    }
                }
            }
#else
            error = "Unity Console history is only available in the Editor.";
            return false;
#endif
            return error == null;
        }

        private static bool TryNormalizeType(string logType, bool allowAll, out string normalizedType)
        {
            if (allowAll && string.Equals(logType, "All", StringComparison.OrdinalIgnoreCase))
            {
                normalizedType = "All";
                return true;
            }

            if (string.Equals(logType, "Error", StringComparison.OrdinalIgnoreCase))
            {
                normalizedType = "Error";
                return true;
            }

            if (string.Equals(logType, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                normalizedType = "Warning";
                return true;
            }

            if (string.Equals(logType, "Log", StringComparison.OrdinalIgnoreCase))
            {
                normalizedType = "Log";
                return true;
            }

            normalizedType = null;
            return false;
        }

        private static AIBridgeLogServiceResult InvalidLogType(bool allowAll)
        {
            return AIBridgeLogServiceResult.Failed(
                "binding_failed",
                "Parameter 'logType' must be "
                + (allowAll ? "All, Error, Warning, or Log." : "Log, Warning, or Error."));
        }

        private static bool MatchesType(LogType type, string filter)
        {
            if (filter == "All")
            {
                return true;
            }

            if (filter == "Error")
            {
                return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            }

            if (filter == "Warning")
            {
                return type == LogType.Warning;
            }

            return type == LogType.Log;
        }

        private static int GetModeMask(string logType)
        {
            switch (logType)
            {
                case "Error":
                    return ErrorModeMask;
                case "Warning":
                    return WarningModeMask;
                case "Log":
                    return LogModeMask;
                default:
                    return ErrorModeMask | WarningModeMask | LogModeMask;
            }
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

#if UNITY_EDITOR
        private static bool EnsureEditorLogReflectionLocked()
        {
            if (_getEntryMethod != null)
            {
                return true;
            }

            var editorAssembly = Assembly.GetAssembly(typeof(UnityEditor.SceneView));
            _logEntriesType = editorAssembly == null
                ? null
                : editorAssembly.GetType("UnityEditor.LogEntries");
            _logEntryType = editorAssembly == null
                ? null
                : editorAssembly.GetType("UnityEditor.LogEntry");
            if (_logEntriesType == null || _logEntryType == null)
            {
                Debug.LogError(
                    "[AIBridge] UnityEditor.LogEntries or UnityEditor.LogEntry type was not found.");
                ClearEditorReflection();
                return false;
            }

            const BindingFlags StaticFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            _getCountMethod = _logEntriesType.GetMethod("GetCount", StaticFlags);
            _getEntryMethod = _logEntriesType.GetMethod("GetEntryInternal", StaticFlags);
            _startMethod = _logEntriesType.GetMethod("StartGettingEntries", StaticFlags);
            _endMethod = _logEntriesType.GetMethod("EndGettingEntries", StaticFlags);

            const BindingFlags InstanceFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _modeField = _logEntryType.GetField("mode", InstanceFlags);
            _messageField = _logEntryType.GetField("message", InstanceFlags);
            _fileField = _logEntryType.GetField("file", InstanceFlags);
            _lineField = _logEntryType.GetField("line", InstanceFlags);

            var valid = _getCountMethod != null
                && _getEntryMethod != null
                && _startMethod != null
                && _endMethod != null
                && _modeField != null
                && _messageField != null
                && _fileField != null
                && _lineField != null;
            if (!valid)
            {
                Debug.LogError(
                    "[AIBridge] Failed to reflect required UnityEditor LogEntries members.");
                ClearEditorReflection();
            }

            return valid;
        }

        private static void ClearEditorReflection()
        {
            _logEntriesType = null;
            _logEntryType = null;
            _getCountMethod = null;
            _getEntryMethod = null;
            _startMethod = null;
            _endMethod = null;
            _modeField = null;
            _messageField = null;
            _fileField = null;
            _lineField = null;
        }
#endif

        private sealed class LogEntry
        {
            public string Message;
            public LogType Type;
            public DateTime Time;
            public int CharacterCount;
        }
    }

    public sealed class AIBridgeLogServiceResult
    {
        private AIBridgeLogServiceResult(
            bool success,
            object data,
            string errorCode,
            string errorMessage)
        {
            Success = success;
            Data = data;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; private set; }
        public object Data { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }

        public static AIBridgeLogServiceResult Succeeded(object data)
        {
            return new AIBridgeLogServiceResult(true, data, null, null);
        }

        public static AIBridgeLogServiceResult Failed(string errorCode, string errorMessage)
        {
            return new AIBridgeLogServiceResult(false, null, errorCode, errorMessage);
        }
    }
}
