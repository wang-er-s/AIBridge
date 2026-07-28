using System;
using AIBridge.Runtime;
using UnityEditor;

namespace AIBridge.Editor
{
    internal static class AIBridgeRuntimeEditorSettings
    {
        private const string Prefix = "AIBridgeSelf.Runtime.";
        private const string EnableRuntimeBridgeKey = Prefix + "EnableRuntimeBridge";
        private const string EnableRuntimeCodeExecutionKey = Prefix + "EnableRuntimeCodeExecution";
        private const string AllowReleaseBuildKey = Prefix + "AllowReleaseBuild";
        private const string HttpPortKey = Prefix + "HttpPort";

        public static bool EnableRuntimeBridge
        {
            get { return EditorPrefs.GetBool(EnableRuntimeBridgeKey, true); }
            set { EditorPrefs.SetBool(EnableRuntimeBridgeKey, value); }
        }

        public static bool EnableRuntimeCodeExecution
        {
            get { return EditorPrefs.GetBool(EnableRuntimeCodeExecutionKey, true); }
            set { EditorPrefs.SetBool(EnableRuntimeCodeExecutionKey, value); }
        }

        public static bool AllowReleaseBuild
        {
            get { return EditorPrefs.GetBool(AllowReleaseBuildKey, false); }
            set { EditorPrefs.SetBool(AllowReleaseBuildKey, value); }
        }

        public static int HttpPort
        {
            get { return EditorPrefs.GetInt(HttpPortKey, AIBridgeRuntimeProtocol.DefaultHttpPort); }
            set { EditorPrefs.SetInt(HttpPortKey, Math.Max(1, Math.Min(65535, value))); }
        }

        public static void Reset()
        {
            EditorPrefs.DeleteKey(EnableRuntimeBridgeKey);
            EditorPrefs.DeleteKey(EnableRuntimeCodeExecutionKey);
            EditorPrefs.DeleteKey(AllowReleaseBuildKey);
            EditorPrefs.DeleteKey(HttpPortKey);
        }
    }
}
