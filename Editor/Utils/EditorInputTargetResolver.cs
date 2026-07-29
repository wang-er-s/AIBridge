using AIBridge.Runtime;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    [InitializeOnLoad]
    internal static class EditorAIBridgeCommandHost
    {
        static EditorAIBridgeCommandHost()
        {
            Configure();
        }

        public static void Configure()
        {
            AIBridgeCommandHost.ConfigureCurrent(new AIBridgeCommandHost(
                EditorInputTargetResolver.Instance,
                ScreenshotHelper.Backend,
                ScreenshotHelper.Storage,
                ScreenshotHelper.Progress,
                true));
        }
    }

    internal sealed class EditorInputTargetResolver : IAIBridgeInputTargetResolver
    {
        private static readonly EditorInputTargetResolver SharedInstance =
            new EditorInputTargetResolver();

        private EditorInputTargetResolver()
        {
        }

        public static EditorInputTargetResolver Instance
        {
            get { return SharedInstance; }
        }

        public bool TryResolve(
            AIBridgeInputTarget target,
            out GameObject gameObject,
            out string errorCode,
            out string errorMessage)
        {
            gameObject = null;
            errorCode = null;
            errorMessage = null;
            if (target == null)
            {
                errorCode = "binding_failed";
                errorMessage = "Input target is required.";
                return false;
            }

            if (target.Path == null)
            {
                gameObject = EditorUtility.InstanceIDToObject(target.InstanceId.Value) as GameObject;
                if (gameObject == null)
                {
                    errorCode = "target_not_found";
                    errorMessage = "GameObject not found with instanceId: " + target.InstanceId.Value;
                    return false;
                }

                return true;
            }

            var normalizedPath = target.Path.Trim('/');
            if (!target.InstanceId.HasValue)
            {
                gameObject = GameObject.Find(normalizedPath);
                if (gameObject == null)
                {
                    errorCode = "target_not_found";
                    errorMessage = "GameObject not found: " + normalizedPath;
                    return false;
                }

                return true;
            }

            var candidate =
                EditorUtility.InstanceIDToObject(target.InstanceId.Value) as GameObject;
            if (!AIBridgeGameObjectResolver.IsLoadedPathMatch(
                candidate,
                normalizedPath))
            {
                errorCode = "target_not_found";
                errorMessage = "GameObject not found for path '" + normalizedPath +
                    "' with instanceId " + target.InstanceId.Value + ".";
                return false;
            }

            gameObject = candidate;
            return true;
        }
    }
}