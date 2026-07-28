using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AIBridge.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace AIBridge.Editor
{
    internal static class RuntimeCodeExecuteClient
    {
        public static IEnumerator Execute(
            string baseUrl,
            byte[] assemblyBytes,
            string entryType,
            string methodName,
            int timeoutMs,
            Action<bool, object, string> onCompleted)
        {
            timeoutMs = AIBridgeRuntimeProtocol.NormalizeTimeoutMs(timeoutMs);
            var requestBody = new AIBridgeSelfCodeExecuteRequest
            {
                id = Guid.NewGuid().ToString("N"),
                action = AIBridgeRuntimeProtocol.CodeExecuteAction,
                assemblyBase64 = Convert.ToBase64String(assemblyBytes),
                sha256 = AIBridgeRuntimeProtocol.ComputeSha256(assemblyBytes),
                entryType = entryType,
                methodName = methodName,
                riskAccepted = true,
                timeoutMs = timeoutMs
            };

            var post = Post(
                baseUrl,
                AIBridgeRuntimeProtocol.CodeExecutePath,
                requestBody,
                timeoutMs,
                onCompleted);
            try
            {
                while (post.MoveNext())
                {
                    yield return post.Current;
                }
            }
            finally
            {
                var disposable = post as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        public static IEnumerator ExecuteCommand(
            string baseUrl,
            string command,
            Dictionary<string, object> parameters,
            int timeoutMs)
        {
            timeoutMs = AIBridgeRuntimeProtocol.NormalizeTimeoutMs(timeoutMs);
            var success = false;
            object result = null;
            string error = null;
            var requestBody = new AIBridgeSelfCommandExecuteRequest
            {
                id = Guid.NewGuid().ToString("N"),
                action = AIBridgeRuntimeProtocol.CommandExecuteAction,
                type = command,
                parameters = parameters ?? new Dictionary<string, object>(),
                timeoutMs = timeoutMs
            };

            var post = Post(
                baseUrl,
                AIBridgeRuntimeProtocol.CommandExecutePath,
                requestBody,
                timeoutMs,
                (completedSuccessfully, responseResult, executeError) =>
                {
                    success = completedSuccessfully;
                    result = responseResult;
                    error = executeError;
                });
            try
            {
                while (post.MoveNext())
                {
                    yield return post.Current;
                }
            }
            finally
            {
                var disposable = post as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }

            if (!success)
            {
                yield return CommandResult.Failure(
                    "Player command '" + command + "' failed:\n"
                    + (error ?? "Unknown Runtime error."));
                yield break;
            }

            var resultObject = result as JObject;
            var downloadPath = resultObject == null
                ? null
                : resultObject.Value<string>("downloadPath");
            var downloadUrl = string.IsNullOrEmpty(downloadPath)
                ? null
                : baseUrl.TrimEnd('/') + downloadPath;
            yield return CommandResult.Success(new
            {
                target = "player",
                runtimeUrl = baseUrl,
                command,
                downloadUrl,
                result
            });
        }

        private static IEnumerator Post(
            string baseUrl,
            string path,
            object requestBody,
            int timeoutMs,
            Action<bool, object, string> onCompleted)
        {
            timeoutMs = AIBridgeRuntimeProtocol.NormalizeTimeoutMs(timeoutMs);
            Uri parsedUrl;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out parsedUrl)
                || !string.Equals(parsedUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                onCompleted(false, null, "Runtime URL must be an absolute http:// URL.");
                yield break;
            }

            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBody));
            var url = baseUrl.TrimEnd('/') + path;
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000d) + 2);

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    yield return null;
                }

                if (request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    onCompleted(false, null, "Runtime request failed: " + request.error);
                    yield break;
                }

                try
                {
                    var response = JObject.Parse(request.downloadHandler.text);
                    var success = response.Value<bool?>("success") == true;
                    var error = response.Value<string>("errorMessage");
                    var errorCode = response.Value<string>("errorCode");
                    if (!string.IsNullOrEmpty(errorCode))
                    {
                        error = errorCode + ": " + (error ?? "Runtime request failed.");
                    }
                    var dataToken = response["result"];
                    var data = dataToken == null || dataToken.Type == JTokenType.Null
                        ? null
                        : dataToken.ToObject<object>();
                    if (!success && string.IsNullOrEmpty(error))
                    {
                        error = "Runtime request returned success=false without an error message.";
                    }
                    onCompleted(success, data, error);
                }
                catch (Exception ex)
                {
                    var prefix = request.result == UnityWebRequest.Result.ProtocolError
                        ? "Runtime HTTP error: " + request.error + ". "
                        : string.Empty;
                    onCompleted(false, null, prefix + "Invalid Runtime response: " + ex.Message);
                }
            }
        }
    }
}
