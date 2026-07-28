using System;
using UnityEngine;

namespace AIBridge.Runtime
{
    [Serializable]
    public sealed class AIBridgeRuntimeSettings
    {
        public bool enableRuntimeBridge = true;
        public bool enableRuntimeCodeExecution = true;
        public bool allowInReleaseBuild;
        public string httpBindAddress = "0.0.0.0";
        public int httpPort = AIBridgeRuntimeProtocol.DefaultHttpPort;
        public int maxAssemblyBytes = AIBridgeRuntimeProtocol.MaxAssemblyBytes;
        public int maxRequestBytes = AIBridgeRuntimeProtocol.MaxRequestBytes;
        public int maxResultBytes = AIBridgeRuntimeProtocol.MaxResultBytes;

        public AIBridgeRuntimeSettings Clone()
        {
            return new AIBridgeRuntimeSettings
            {
                enableRuntimeBridge = enableRuntimeBridge,
                enableRuntimeCodeExecution = enableRuntimeCodeExecution,
                allowInReleaseBuild = allowInReleaseBuild,
                httpBindAddress = httpBindAddress,
                httpPort = httpPort,
                maxAssemblyBytes = maxAssemblyBytes,
                maxRequestBytes = maxRequestBytes,
                maxResultBytes = maxResultBytes
            };
        }
    }

    [AddComponentMenu("")]
    public sealed class AIBridgeSelfRuntimeSettingsCarrier : MonoBehaviour
    {
        public const string ObjectName = "AIBridgeSelf Runtime Settings";

        public AIBridgeRuntimeSettings settings = new AIBridgeRuntimeSettings();
    }
}
