using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AIBridge.Runtime
{
    public interface IAIBridgeInputTargetResolver
    {
        bool TryResolve(
            AIBridgeInputTarget target,
            out GameObject gameObject,
            out string errorCode,
            out string errorMessage);
    }

    public static class AIBridgeInputCommands
    {
        public static IEnumerator Click(AIBridgeCommandContext context)
        {
            yield return Click(
                context.Parameters.GetObject("path"),
                context.Parameters.GetObject("point"),
                context.Parameters.GetObject("instanceId"),
                new AIBridgePathInputTargetResolver());
        }

        public static IEnumerator Drag(AIBridgeCommandContext context)
        {
            yield return Drag(
                context.Parameters.GetRequiredObject("points"),
                new AIBridgePathInputTargetResolver());
        }

        public static IEnumerator LongPress(AIBridgeCommandContext context)
        {
            yield return LongPress(
                context.Parameters.GetObject("path"),
                context.Parameters.GetObject("point"),
                context.Parameters.GetObject("instanceId"),
                context.Parameters.GetInt32("duration", 1000),
                new AIBridgePathInputTargetResolver());
        }

        public static IEnumerator Click(
            object path,
            object point,
            object instanceId,
            IAIBridgeInputTargetResolver resolver)
        {
            AIBridgeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            AIBridgeInputTarget target;
            string error;
            if (!AIBridgeInputTargetParser.TryParse(
                    path,
                    point,
                    instanceId,
                    out target,
                    out error))
            {
                yield return AIBridgeCommandOutcome.Failed("binding_failed", error);
                yield break;
            }

            GameObject gameObject;
            Vector2 screenPosition;
            string errorCode;
            if (!TryResolveTarget(
                    target,
                    resolver,
                    out gameObject,
                    out screenPosition,
                    out errorCode,
                    out error) &&
                target.Position.HasValue)
            {
                yield return null;
                TryResolveTarget(
                    target,
                    resolver,
                    out gameObject,
                    out screenPosition,
                    out errorCode,
                    out error);
            }

            if (gameObject == null)
            {
                yield return AIBridgeCommandOutcome.Failed(errorCode, error);
                yield break;
            }

            yield return PerformClick(gameObject, screenPosition);
        }

        public static IEnumerator Drag(object rawPoints, IAIBridgeInputTargetResolver resolver)
        {
            AIBridgeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            List<Vector2> positions;
            GameObject source;
            string errorCode;
            string error;
            if (!TryResolveDragPoints(
                    rawPoints,
                    resolver,
                    out positions,
                    out source,
                    out errorCode,
                    out error))
            {
                yield return AIBridgeCommandOutcome.Failed(errorCode, error);
                yield break;
            }

            if (!source.activeInHierarchy)
            {
                yield return AIBridgeCommandOutcome.Failed(
                    "target_inactive",
                    "The first GameObject is inactive.");
                yield break;
            }

            yield return PerformDrag(source, positions);
        }

        public static IEnumerator LongPress(
            object path,
            object point,
            object instanceId,
            int duration,
            IAIBridgeInputTargetResolver resolver)
        {
            AIBridgeCommandOutcome failure;
            if (!CanUse(out failure))
            {
                yield return failure;
                yield break;
            }

            AIBridgeInputTarget target;
            string error;
            if (!AIBridgeInputTargetParser.TryParse(
                    path,
                    point,
                    instanceId,
                    out target,
                    out error))
            {
                yield return AIBridgeCommandOutcome.Failed("binding_failed", error);
                yield break;
            }

            GameObject gameObject;
            Vector2 screenPosition;
            string errorCode;
            if (!TryResolveTarget(
                    target,
                    resolver,
                    out gameObject,
                    out screenPosition,
                    out errorCode,
                    out error) &&
                target.Position.HasValue)
            {
                yield return null;
                TryResolveTarget(
                    target,
                    resolver,
                    out gameObject,
                    out screenPosition,
                    out errorCode,
                    out error);
            }

            if (gameObject == null)
            {
                yield return AIBridgeCommandOutcome.Failed(errorCode, error);
                yield break;
            }

            yield return PerformLongPress(gameObject, screenPosition, duration);
        }

        private static IEnumerator PerformClick(GameObject gameObject, Vector2 screenPosition)
        {
            if (!gameObject.activeInHierarchy)
            {
                yield return AIBridgeCommandOutcome.Failed("target_inactive", "GameObject is inactive.");
                yield break;
            }

            var eventSent = SimulateClick(gameObject, screenPosition);
            yield return AIBridgeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "click" },
                { "path", AIBridgeGameObjectResolver.GetHierarchyPath(gameObject) },
                { "instanceId", gameObject.GetInstanceID() },
                { "screenPosition", Position(screenPosition) },
                { "eventSent", eventSent }
            });
        }

        private static bool TryResolveTarget(
            AIBridgeInputTarget target,
            IAIBridgeInputTargetResolver resolver,
            out GameObject gameObject,
            out Vector2 screenPosition,
            out string errorCode,
            out string error)
        {
            gameObject = null;
            screenPosition = default;
            errorCode = null;
            error = null;

            if (target.Position.HasValue)
            {
                screenPosition = target.Position.Value;
                gameObject = RaycastUi(screenPosition);
                if (gameObject == null)
                {
                    errorCode = "target_not_found";
                    error = "The point does not hit a GameObject.";
                    return false;
                }

                return true;
            }

            if (resolver == null)
            {
                errorCode = "resolver_missing";
                error = "Input target resolver is required.";
                return false;
            }

            if (!resolver.TryResolve(target, out gameObject, out errorCode, out error))
            {
                return false;
            }

            var resolvedPosition = GetScreenPosition(gameObject);
            if (!resolvedPosition.HasValue)
            {
                errorCode = "screen_position_unavailable";
                error = "Unable to get screen position.";
                gameObject = null;
                return false;
            }

            screenPosition = resolvedPosition.Value;
            return true;
        }

        private static bool TryResolveDragPoints(
            object rawPoints,
            IAIBridgeInputTargetResolver resolver,
            out List<Vector2> positions,
            out GameObject source,
            out string errorCode,
            out string error)
        {
            positions = new List<Vector2>();
            source = null;
            errorCode = null;

            List<AIBridgeDragPoint> dragPoints;
            if (!AIBridgeDragPointParser.TryParse(rawPoints, out dragPoints, out error))
            {
                errorCode = "binding_failed";
                return false;
            }

            for (var index = 0; index < dragPoints.Count; index++)
            {
                var point = dragPoints[index];
                if (point.Position.HasValue)
                {
                    positions.Add(point.Position.Value);
                    continue;
                }

                var target = point.IsPath
                    ? (point.InstanceId.HasValue
                        ? AIBridgeInputTarget.FromPath(point.Path, point.InstanceId.Value)
                        : AIBridgeInputTarget.FromPath(point.Path))
                    : AIBridgeInputTarget.FromInstanceId(point.InstanceId.Value);
                GameObject gameObject;
                if (resolver == null)
                {
                    errorCode = "resolver_missing";
                    error = "Input target resolver is required.";
                    return false;
                }

                if (!resolver.TryResolve(target, out gameObject, out errorCode, out error))
                {
                    return false;
                }

                var screenPosition = GetScreenPosition(gameObject);
                if (!screenPosition.HasValue)
                {
                    errorCode = "screen_position_unavailable";
                    error = "Unable to get screen position: " + point.Path;
                    return false;
                }

                if (index == 0)
                {
                    source = gameObject;
                }

                positions.Add(screenPosition.Value);
            }

            if (source == null)
            {
                source = RaycastUi(positions[0]);
                if (source == null)
                {
                    errorCode = "target_not_found";
                    error = "The first coordinate does not hit a draggable GameObject.";
                    return false;
                }
            }

            errorCode = null;
            error = null;
            return true;
        }

        private static IEnumerator PerformDrag(GameObject gameObject, List<Vector2> positions)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return AIBridgeCommandOutcome.Failed("event_system_missing", "Scene has no EventSystem.");
                yield break;
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = positions[0],
                button = PointerEventData.InputButton.Left,
                pointerDrag = gameObject,
                pointerPress = gameObject,
                rawPointerPress = gameObject,
                pressPosition = positions[0],
                dragging = false,
                eligibleForClick = true
            };

            var releaseStarted = false;
            GameObject dropTarget = null;
            try
            {
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.beginDragHandler);
                pointerData.dragging = true;
                pointerData.eligibleForClick = false;

                for (var index = 1; index < positions.Count; index++)
                {
                    var position = positions[index];
                    pointerData.delta = position - pointerData.position;
                    pointerData.position = position;
                    ExecuteEvents.Execute(gameObject, pointerData, ExecuteEvents.dragHandler);
                    yield return null;
                }

                dropTarget = RaycastUi(pointerData.position);
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

            yield return AIBridgeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "drag" },
                { "from", Position(positions[0]) },
                { "to", Position(positions[positions.Count - 1]) },
                { "pointCount", positions.Count },
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

        private static IEnumerator PerformLongPress(GameObject gameObject, Vector2 screenPosition, int duration)
        {
            if (!gameObject.activeInHierarchy)
            {
                yield return AIBridgeCommandOutcome.Failed(
                    "target_inactive",
                    "GameObject is inactive.");
                yield break;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return AIBridgeCommandOutcome.Failed("event_system_missing", "Scene has no EventSystem.");
                yield break;
            }

            duration = Mathf.Max(0, duration);
            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
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

            yield return AIBridgeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "long_press" },
                { "path", AIBridgeGameObjectResolver.GetHierarchyPath(gameObject) },
                { "instanceId", gameObject.GetInstanceID() },
                { "screenPosition", Position(screenPosition) },
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

        private static bool CanUse(out AIBridgeCommandOutcome failure)
        {
            if (!Application.isPlaying)
            {
                failure = AIBridgeCommandOutcome.Failed(
                    "not_playing",
                    "InputSimulation is only available while playing.");
                return false;
            }

            if (EventSystem.current == null)
            {
                failure = AIBridgeCommandOutcome.Failed(
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

    public sealed class AIBridgePathInputTargetResolver : IAIBridgeInputTargetResolver
    {
        private Dictionary<string, List<GameObject>> _matchesByPath;

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
                errorCode = "path_required";
                errorMessage =
                    "Runtime target selection requires 'path'; instanceId alone is not supported.";
                return false;
            }

            var normalizedPath = target.Path.Trim('/');
            EnsurePathLookup();
            List<GameObject> pathMatches;
            if (!_matchesByPath.TryGetValue(normalizedPath, out pathMatches))
            {
                errorCode = "target_not_found";
                errorMessage = "GameObject not found: " + normalizedPath;
                return false;
            }

            if (target.InstanceId.HasValue)
            {
                for (var index = 0; index < pathMatches.Count; index++)
                {
                    var candidate = pathMatches[index];
                    if (candidate.GetInstanceID() == target.InstanceId.Value)
                    {
                        gameObject = candidate;
                        return true;
                    }
                }

                errorCode = "target_not_found";
                errorMessage = "GameObject not found for path '" + normalizedPath +
                    "' with instanceId " + target.InstanceId.Value + ".";
                return false;
            }

            if (pathMatches.Count > 1)
            {
                errorCode = "ambiguous_path";
                errorMessage = "Multiple loaded GameObjects match full hierarchy path '" +
                    normalizedPath + "'. Provide path + instanceId.";
                return false;
            }

            gameObject = pathMatches[0];
            return true;
        }

        private void EnsurePathLookup()
        {
            if (_matchesByPath != null)
            {
                return;
            }

            _matchesByPath = new Dictionary<string, List<GameObject>>(
                System.StringComparer.Ordinal);
            var gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (var index = 0; index < gameObjects.Length; index++)
            {
                var gameObject = gameObjects[index];
                if (gameObject == null)
                {
                    continue;
                }

                var scene = gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var path = AIBridgeGameObjectResolver.GetHierarchyPath(gameObject);
                List<GameObject> matches;
                if (!_matchesByPath.TryGetValue(path, out matches))
                {
                    matches = new List<GameObject>();
                    _matchesByPath.Add(path, matches);
                }

                matches.Add(gameObject);
            }
        }
    }

    public static class AIBridgeGameObjectResolver
    {
        public static bool IsLoadedPathMatch(GameObject gameObject, string normalizedPath)
        {
            if (gameObject == null)
            {
                return false;
            }

            var scene = gameObject.scene;
            return scene.IsValid()
                && scene.isLoaded
                && string.Equals(
                    GetHierarchyPath(gameObject),
                    normalizedPath,
                    System.StringComparison.Ordinal);
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
    }
}
