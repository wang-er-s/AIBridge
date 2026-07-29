using System.Collections.Generic;
using AIBridge.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBridge.Editor
{
    [InitializeOnLoad]
    internal sealed class AIBridgeSelfRuntimeBuildProcessor : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        private const string AutoInjectDisabledDefine = "AIBRIDGESELF_RUNTIME_AUTO_INJECT_DISABLED";
        private const string AllowReleaseBuildDefine = "AIBRIDGESELF_RUNTIME_ALLOW_RELEASE_BUILD";
        private static bool _carrierInjected;

        static AIBridgeSelfRuntimeBuildProcessor()
        {
            EditorApplication.delayCall += SyncDefinesForActiveTarget;
        }

        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            _carrierInjected = false;
            if (AIBridgeRuntimeEditorSettings.EnableRuntimeBridge
                && AIBridgeRuntimeEditorSettings.EnableRuntimeCodeExecution
                && !AIBridgeHybridClrUtility.IsInstalled())
            {
                Debug.LogWarning(
                    "[AIBridgeSelf] HybridCLR is not installed. Runtime code execution will not be included; "
                    + "generic Runtime commands remain available.");
            }

            var group = report == null
                ? BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)
                : BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            if (SyncDefines(group))
            {
                throw new BuildFailedException(
                    "AIBridgeSelf Runtime build symbols changed. Wait for Unity compilation, then build again.");
            }
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (_carrierInjected || !ShouldInject(report) || !scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var carrier = FindCarrier(scene);
            if (carrier == null)
            {
                var gameObject = new GameObject(AIBridgeSelfSettingsCarrier.ObjectName);
                gameObject.hideFlags = HideFlags.HideInHierarchy;
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                carrier = gameObject.AddComponent<AIBridgeSelfSettingsCarrier>();
            }

            carrier.settings = new AIBridgeSettings
            {
                enableRuntimeBridge = AIBridgeRuntimeEditorSettings.EnableRuntimeBridge,
                enableRuntimeCodeExecution = AIBridgeRuntimeEditorSettings.EnableRuntimeCodeExecution
                    && AIBridgeHybridClrUtility.IsInstalled(),
                allowInReleaseBuild = AIBridgeRuntimeEditorSettings.AllowReleaseBuild,
                httpBindAddress = "0.0.0.0",
                httpPort = AIBridgeRuntimeEditorSettings.HttpPort
            };
            _carrierInjected = true;
        }

        internal static void SyncDefinesForActiveTarget()
        {
            SyncDefines(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        }

        private static bool SyncDefines(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Unknown)
            {
                return false;
            }

            var defines = ParseDefines(PlayerSettings.GetScriptingDefineSymbolsForGroup(group));
            var changed = false;
            var runtimeAvailable = AIBridgeRuntimeEditorSettings.EnableRuntimeBridge;
            changed |= SetDefine(defines, AutoInjectDisabledDefine, !runtimeAvailable);
            changed |= SetDefine(
                defines,
                AllowReleaseBuildDefine,
                runtimeAvailable && AIBridgeRuntimeEditorSettings.AllowReleaseBuild);
            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines.ToArray()));
            }

            return changed;
        }

        private static bool ShouldInject(BuildReport report)
        {
            if (!AIBridgeRuntimeEditorSettings.EnableRuntimeBridge)
            {
                return false;
            }

            var developmentBuild = report == null
                ? EditorUserBuildSettings.development
                : (report.summary.options & BuildOptions.Development) == BuildOptions.Development;
            return developmentBuild || AIBridgeRuntimeEditorSettings.AllowReleaseBuild;
        }

        private static AIBridgeSelfSettingsCarrier FindCarrier(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var carriers = roots[i].GetComponentsInChildren<AIBridgeSelfSettingsCarrier>(true);
                if (carriers.Length > 0)
                {
                    return carriers[0];
                }
            }

            return null;
        }

        private static List<string> ParseDefines(string symbols)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(symbols))
            {
                return result;
            }

            var parts = symbols.Split(';');
            for (var i = 0; i < parts.Length; i++)
            {
                var value = parts[i].Trim();
                if (!string.IsNullOrEmpty(value) && !result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static bool SetDefine(List<string> defines, string define, bool enabled)
        {
            var exists = defines.Contains(define);
            if (exists == enabled)
            {
                return false;
            }

            if (enabled)
            {
                defines.Add(define);
            }
            else
            {
                defines.Remove(define);
            }

            return true;
        }
    }
}
