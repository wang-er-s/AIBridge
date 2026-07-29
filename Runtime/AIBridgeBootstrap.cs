using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBridge.Runtime
{
    public static class AIBridgeBootstrap
    {
        private const string RuntimeObjectName = "AIBridgeSelf Runtime Bridge";
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_EDITOR
            return;
#else
            if (_initialized)
            {
                return;
            }

            _initialized = true;
#if AIBRIDGESELF_RUNTIME_AUTO_INJECT_DISABLED
            return;
#else
            if (!Debug.isDebugBuild)
            {
#if !AIBRIDGESELF_RUNTIME_ALLOW_RELEASE_BUILD
                return;
#endif
            }

            SceneManager.sceneLoaded += HandleFirstSceneLoaded;
#endif
#endif
        }

#if !UNITY_EDITOR
        private static void HandleFirstSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= HandleFirstSceneLoaded;
            var injectedSettings = TakeInjectedSettings();
            if (FindExistingBridge() != null)
            {
                return;
            }

            var gameObject = new GameObject(RuntimeObjectName);
            gameObject.hideFlags = HideFlags.HideInHierarchy;
            gameObject.SetActive(false);

            var bridge = gameObject.AddComponent<AIBridgeBridge>();
            bridge.settings = injectedSettings ?? new AIBridgeSettings();
            gameObject.SetActive(true);
        }

        private static AIBridgeSettings TakeInjectedSettings()
        {
            AIBridgeSettings result = null;
            var carriers = Resources.FindObjectsOfTypeAll<AIBridgeSelfSettingsCarrier>();
            for (var i = 0; i < carriers.Length; i++)
            {
                var carrier = carriers[i];
                if (carrier == null
                    || carrier.gameObject == null
                    || !carrier.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (result == null && carrier.settings != null)
                {
                    result = carrier.settings.Clone();
                }

                Object.Destroy(carrier.gameObject);
            }

            return result;
        }

        private static AIBridgeBridge FindExistingBridge()
        {
            var bridges = Resources.FindObjectsOfTypeAll<AIBridgeBridge>();
            for (var i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge != null
                    && bridge.gameObject != null
                    && bridge.gameObject.scene.IsValid())
                {
                    return bridge;
                }
            }

            return null;
        }
#endif
    }
}
