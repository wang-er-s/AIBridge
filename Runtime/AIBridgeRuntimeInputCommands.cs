using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace AIBridge.Runtime
{
    internal static class AIBridgeRuntimeInputCommands
    {
        public static IEnumerator Click(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var path = context.Parameters.GetRequiredString("path");
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByPath(path);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("target_not_found", "GameObject not found: " + path);
                yield break;
            }

            yield return PerformClick(gameObject);
        }

        public static IEnumerator ClickByInstanceId(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var instanceId = context.Parameters.GetInt32("instanceId", 0);
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByInstanceId(instanceId);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "target_not_found",
                    "GameObject not found with instanceId: " + instanceId);
                yield break;
            }

            yield return PerformClick(gameObject);
        }

        public static IEnumerator ClickAt(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var x = context.Parameters.GetSingle("x", -1f);
            var y = context.Parameters.GetSingle("y", -1f);
            if (x < 0f || y < 0f)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "binding_failed",
                    "Parameters 'x' and 'y' must be non-negative.");
                yield break;
            }

            var screenPosition = new Vector2(x, y);
            var hitObject = RaycastUi(screenPosition);
            if (hitObject != null)
            {
                SimulateClick(hitObject, screenPosition);
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "click_at" },
                { "screenPosition", Position(screenPosition) },
                { "hitObject", hitObject != null ? hitObject.name : "none" },
                { "eventSent", hitObject != null ? "PointerClick" : "NoTarget" }
            });
        }

        public static IEnumerator Drag(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var path = context.Parameters.GetRequiredString("path");
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByPath(path);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("target_not_found", "GameObject not found: " + path);
                yield break;
            }

            var startPosition = GetScreenPosition(gameObject);
            if (!startPosition.HasValue)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "screen_position_unavailable",
                    "Unable to get start screen position: " + path);
                yield break;
            }

            Vector2 endPosition;
            var toPath = context.Parameters.GetString("toPath", null);
            if (!string.IsNullOrEmpty(toPath))
            {
                var target = AIBridgeRuntimeGameObjectResolver.FindByPath(toPath);
                if (target == null)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "target_not_found",
                        "Target GameObject not found: " + toPath);
                    yield break;
                }

                var targetPosition = GetScreenPosition(target);
                if (!targetPosition.HasValue)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "screen_position_unavailable",
                        "Unable to get target screen position: " + toPath);
                    yield break;
                }

                endPosition = targetPosition.Value;
            }
            else
            {
                var toX = context.Parameters.GetSingle("toX", -1f);
                var toY = context.Parameters.GetSingle("toY", -1f);
                if (toX < 0f || toY < 0f)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "binding_failed",
                        "Drag requires 'toPath' or non-negative 'toX' and 'toY'.");
                    yield break;
                }

                endPosition = new Vector2(toX, toY);
            }

            yield return PerformDrag(
                gameObject,
                startPosition.Value,
                endPosition,
                context.Parameters.GetInt32("frames", 10));
        }

        public static IEnumerator DragByInstanceId(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var instanceId = context.Parameters.GetInt32("instanceId", 0);
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByInstanceId(instanceId);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "target_not_found",
                    "GameObject not found with instanceId: " + instanceId);
                yield break;
            }

            var startPosition = GetScreenPosition(gameObject);
            if (!startPosition.HasValue)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "screen_position_unavailable",
                    "Unable to get start screen position.");
                yield break;
            }

            Vector2 endPosition;
            var toInstanceId = context.Parameters.GetInt32("toInstanceId", 0);
            if (toInstanceId != 0)
            {
                var target = AIBridgeRuntimeGameObjectResolver.FindByInstanceId(toInstanceId);
                if (target == null)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "target_not_found",
                        "Target GameObject not found with instanceId: " + toInstanceId);
                    yield break;
                }

                var targetPosition = GetScreenPosition(target);
                if (!targetPosition.HasValue)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "screen_position_unavailable",
                        "Unable to get target screen position.");
                    yield break;
                }

                endPosition = targetPosition.Value;
            }
            else
            {
                var toX = context.Parameters.GetSingle("toX", -1f);
                var toY = context.Parameters.GetSingle("toY", -1f);
                if (toX < 0f || toY < 0f)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "binding_failed",
                        "Drag requires 'toInstanceId' or non-negative 'toX' and 'toY'.");
                    yield break;
                }

                endPosition = new Vector2(toX, toY);
            }

            yield return PerformDrag(
                gameObject,
                startPosition.Value,
                endPosition,
                context.Parameters.GetInt32("frames", 10));
        }

        public static IEnumerator LongPress(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var path = context.Parameters.GetRequiredString("path");
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByPath(path);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("target_not_found", "GameObject not found: " + path);
                yield break;
            }

            yield return PerformLongPress(gameObject, context.Parameters.GetInt32("duration", 1000));
        }

        public static IEnumerator LongPressByInstanceId(AIBridgeRuntimeCommandContext context)
        {
            AIBridgeRuntimeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            var instanceId = context.Parameters.GetInt32("instanceId", 0);
            var gameObject = AIBridgeRuntimeGameObjectResolver.FindByInstanceId(instanceId);
            if (gameObject == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "target_not_found",
                    "GameObject not found with instanceId: " + instanceId);
                yield break;
            }

            yield return PerformLongPress(gameObject, context.Parameters.GetInt32("duration", 1000));
        }

        private static IEnumerator PerformClick(GameObject gameObject)
        {
            if (!gameObject.activeInHierarchy)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("target_inactive", "GameObject is inactive.");
                yield break;
            }

            var screenPosition = GetScreenPosition(gameObject);
            if (!screenPosition.HasValue)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "screen_position_unavailable",
                    "Unable to get screen position.");
                yield break;
            }

            var eventSent = SimulateClick(gameObject, screenPosition.Value);
            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "click" },
                { "path", AIBridgeRuntimeGameObjectResolver.GetHierarchyPath(gameObject) },
                { "instanceId", gameObject.GetInstanceID() },
                { "screenPosition", Position(screenPosition.Value) },
                { "eventSent", eventSent }
            });
        }

        private static IEnumerator PerformDrag(
            GameObject gameObject,
            Vector2 startPosition,
            Vector2 endPosition,
            int frames)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("event_system_missing", "Scene has no EventSystem.");
                yield break;
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = startPosition,
                button = PointerEventData.InputButton.Left,
                pointerDrag = gameObject,
                pointerPress = gameObject,
                rawPointerPress = gameObject,
                pressPosition = startPosition,
                dragging = false,
                eligibleForClick = true
            };

            frames = Mathf.Clamp(frames, 3, 60);
            var releaseStarted = false;
            GameObject dropTarget = null;
            try
            {
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.beginDragHandler);
                pointerData.dragging = true;
                pointerData.eligibleForClick = false;

                for (var i = 0; i <= frames; i++)
                {
                    var position = Vector2.Lerp(startPosition, endPosition, i / (float)frames);
                    pointerData.delta = position - pointerData.position;
                    pointerData.position = position;
                    ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.dragHandler);
                    yield return null;
                }

                pointerData.position = endPosition;
                dropTarget = RaycastUi(endPosition);
                releaseStarted = true;
                ReleaseDrag(pointerData, gameObject, dropTarget);
            }
            finally
            {
                if (!releaseStarted)
                {
                    dropTarget = RaycastUi(pointerData.position);
                    ReleaseDrag(pointerData, gameObject, dropTarget);
                }
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "drag" },
                { "from", Position(startPosition) },
                { "to", Position(endPosition) },
                { "frames", frames },
                { "dropTarget", dropTarget != null ? dropTarget.name : "none" }
            });
        }

        private static void ReleaseDrag(
            PointerEventData pointerData,
            GameObject pressTarget,
            GameObject dropTarget)
        {
            // 即使用户事件处理器抛错，也按 pointerUp、drop、endDrag 顺序完成剩余清理。
            try
            {
                if (pressTarget != null)
                {
                    ExecuteEvents.Execute(pressTarget, pointerData, ExecuteEvents.pointerUpHandler);
                }
            }
            finally
            {
                try
                {
                    if (dropTarget != null)
                    {
                        ExecuteEvents.Execute(dropTarget, pointerData, ExecuteEvents.dropHandler);
                    }
                }
                finally
                {
                    try
                    {
                        if (pointerData.pointerDrag != null)
                        {
                            ExecuteEvents.Execute(
                                pointerData.pointerDrag,
                                pointerData,
                                ExecuteEvents.endDragHandler);
                        }
                    }
                    finally
                    {
                        pointerData.dragging = false;
                        pointerData.eligibleForClick = false;
                        pointerData.pointerDrag = null;
                        pointerData.pointerPress = null;
                        pointerData.rawPointerPress = null;
                    }
                }
            }
        }

        private static IEnumerator PerformLongPress(GameObject gameObject, int duration)
        {
            var screenPosition = GetScreenPosition(gameObject);
            if (!screenPosition.HasValue)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "screen_position_unavailable",
                    "Unable to get screen position.");
                yield break;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed("event_system_missing", "Scene has no EventSystem.");
                yield break;
            }

            duration = Mathf.Max(0, duration);
            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition.Value,
                button = PointerEventData.InputButton.Left
            };

            ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.pointerDownHandler);
            try
            {
                yield return new WaitForSecondsRealtime(duration / 1000f);
            }
            finally
            {
                if (gameObject != null)
                {
                    ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.pointerUpHandler);
                }
            }

            yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "long_press" },
                { "path", AIBridgeRuntimeGameObjectResolver.GetHierarchyPath(gameObject) },
                { "instanceId", gameObject.GetInstanceID() },
                { "screenPosition", Position(screenPosition.Value) },
                { "durationMs", duration }
            });
        }

        private static string SimulateClick(GameObject target, Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return "NoEventSystem";
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            var raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, raycastResults);
            if (raycastResults.Count > 0)
            {
                pointerData.pointerCurrentRaycast = raycastResults[0];
            }

            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler);
            return "PointerClick";
        }

        private static Vector2? GetScreenPosition(GameObject gameObject)
        {
            var rectTransform = gameObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                var canvas = gameObject.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    return RectTransformUtility.WorldToScreenPoint(camera, rectTransform.position);
                }
            }

            var renderer = gameObject.GetComponent<Renderer>();
            var mainCamera = Camera.main;
            if (renderer != null && mainCamera != null)
            {
                var point = mainCamera.WorldToScreenPoint(renderer.bounds.center);
                return new Vector2(point.x, point.y);
            }

            if (gameObject.transform != null && mainCamera != null)
            {
                var point = mainCamera.WorldToScreenPoint(gameObject.transform.position);
                return new Vector2(point.x, point.y);
            }

            return null;
        }

        private static GameObject RaycastUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return null;
            }

            var pointerData = new PointerEventData(eventSystem) { position = screenPosition };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        private static bool CanUse(out AIBridgeRuntimeCommandOutcome failure)
        {
            if (!Application.isPlaying)
            {
                failure = AIBridgeRuntimeCommandOutcome.Failed(
                    "not_playing",
                    "InputSimulation is only available while playing.");
                return false;
            }

            if (EventSystem.current == null)
            {
                failure = AIBridgeRuntimeCommandOutcome.Failed(
                    "event_system_missing",
                    "Scene has no EventSystem.");
                return false;
            }

            failure = null;
            return true;
        }

        private static Dictionary<string, object> Position(Vector2 value)
        {
            return new Dictionary<string, object>
            {
                { "x", value.x },
                { "y", value.y }
            };
        }
    }

    internal static class AIBridgeRuntimeGameObjectResolver
    {
        public static GameObject FindByPath(string path)
        {
            var direct = GameObject.Find(path);
            if (direct != null)
            {
                return direct;
            }

            var normalized = path.Trim('/');
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var root = roots[rootIndex];
                    if (root == null)
                    {
                        continue;
                    }

                    if (string.Equals(root.name, normalized, System.StringComparison.Ordinal))
                    {
                        return root;
                    }

                    var prefix = root.name + "/";
                    if (normalized.StartsWith(prefix, System.StringComparison.Ordinal))
                    {
                        var child = root.transform.Find(normalized.Substring(prefix.Length));
                        if (child != null)
                        {
                            return child.gameObject;
                        }
                    }
                }
            }

            return null;
        }

        public static GameObject FindByInstanceId(int instanceId)
        {
            // 只遍历已加载场景，避免把 Resources 中的 Prefab 或其他资源误识别为运行时对象。
            var activeScene = SceneManager.GetActiveScene();
            var result = FindInScene(activeScene, instanceId);
            if (result != null)
            {
                return result;
            }

            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene == activeScene)
                {
                    continue;
                }

                result = FindInScene(scene, instanceId);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static GameObject FindInScene(Scene scene, int instanceId)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var result = FindInTransform(roots[i].transform, instanceId);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static GameObject FindInTransform(Transform transform, int instanceId)
        {
            if (transform.gameObject.GetInstanceID() == instanceId)
            {
                return transform.gameObject;
            }

            for (var i = 0; i < transform.childCount; i++)
            {
                var result = FindInTransform(transform.GetChild(i), instanceId);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
