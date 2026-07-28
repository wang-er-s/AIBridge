using System;
using System.Security.Cryptography;
using System.Text;

namespace AIBridge.Runtime
{
    /// <summary>
    /// 手机 Player Runtime Bridge 的 HTTP 协议常量。
    /// </summary>
    public static class AIBridgeRuntimeProtocol
    {
        public const string HealthPath = "/aibridge-self/health";
        public const string CodeExecutePath = "/aibridge-self/code/execute";
        public const string CodeExecuteAction = "aibridge-self.code.execute";
        public const string EntryTypeName = "CodeExecutor";
        public const string EntryMethodName = "Execute";
        public const string AsyncEntryMethodName = "ExecuteAsync";
        public const int DefaultHttpPort = 27182;
        public const int DefaultExecutionTimeoutMs = 30000;
        public const int MaxAssemblyBytes = 16 * 1024 * 1024;
        public const int MaxRequestBytes = 24 * 1024 * 1024;
        public const int MaxResultBytes = 1024 * 1024;

        public static string GetEntryMethodName(string code)
        {
            return code != null && code.IndexOf("await ", StringComparison.Ordinal) >= 0
                ? AsyncEntryMethodName
                : EntryMethodName;
        }

        public static string ComputeSha256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// 来自 Editor 的远程程序集执行请求。
    /// </summary>
    [Serializable]
    public class AIBridgeSelfCodeExecuteRequest
    {
        public string id;
        public string action;
        public string assemblyBase64;
        public string sha256;
        public string entryType;
        public string methodName;
        public bool riskAccepted;
        public int timeoutMs;
    }

    /// <summary>
    /// Runtime Bridge 的统一响应。errorCode 非空表示执行失败。
    /// </summary>
    [Serializable]
    public class AIBridgeSelfRuntimeResult
    {
        public string id;
        public string action;
        public bool success;
        public object result;
        public string errorCode;
        public string errorMessage;
    }

    /// <summary>
    /// Health endpoint 返回的运行状态。
    /// </summary>
    [Serializable]
    public class AIBridgeSelfRuntimeHealth
    {
        public string service;
        public bool ready;
        public string endpoint;
    }
}
