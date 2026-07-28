using System;
using System.Collections;
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
            Uri parsedUrl;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out parsedUrl)
                || !string.Equals(parsedUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                onCompleted(false, null, "Runtime URL must be an absolute http:// URL.");
                yield break;
            }

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

            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBody));
            var url = baseUrl.TrimEnd('/') + AIBridgeRuntimeProtocol.CodeExecutePath;
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000d) + 2);

                yield return request.SendWebRequest();

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
                    var dataToken = response["result"];
                    var data = dataToken == null || dataToken.Type == JTokenType.Null
                        ? null
                        : dataToken.ToObject<object>();
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
