using System;
using UnityEngine;

namespace AIBridge.Runtime
{
    [Serializable]
    public sealed class AIBridgeSettings
    {
        public bool enableRuntimeBridge = true;
        public bool enableRuntimeCodeExecution = true;
        public bool allowInReleaseBuild;
        public string httpBindAddress = "0.0.0.0";
        public int httpPort = AIBridgeProtocol.DefaultHttpPort;
        public int maxAssemblyBytes = AIBridgeProtocol.MaxAssemblyBytes;
        public int maxRequestBytes = AIBridgeProtocol.MaxRequestBytes;
        public int maxResultBytes = AIBridgeProtocol.MaxResultBytes;

        public AIBridgeSettings Clone()
        {
            return new AIBridgeSettings
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
    public sealed class AIBridgeSelfSettingsCarrier : MonoBehaviour
    {
        public const string ObjectName = "AIBridgeSelf Runtime Settings";

        public AIBridgeSettings settings = new AIBridgeSettings();
    }
}
