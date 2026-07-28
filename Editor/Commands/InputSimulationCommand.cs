using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using AIBridge.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AIBridge.Editor
{
    /// <summary>
    /// 输入模拟命令，支持点击、拖拽、长按。
    /// </summary>
    public static class InputSimulationCommand
    {
        [AIBridge("通过路径模拟点击 GameObject (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_Click --path \"Canvas/Button\"")]
        public static IEnumerator Click(
            [Description("GameObject 的层级路径")] string path,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            if (string.IsNullOrEmpty(path))
            {
                yield return CommandResult.Failure("参数 'path' 不能为空");
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject: {path}");
            }

            yield return PerformClick(go);
        }

        [AIBridge("通过实例 ID 模拟点击 GameObject (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_ClickByInstanceId --instanceId 12345")]
        public static IEnumerator ClickByInstanceId(
            [Description("GameObject 的实例 ID")] int instanceId,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject with instanceId: {instanceId}");
            }

            yield return PerformClick(go);
        }

        private static IEnumerator PerformClick(GameObject go)
        {

            if (!go.activeInHierarchy)
            {
                yield return CommandResult.Failure($"GameObject 未激活");
            }

            var screenPos = GetScreenPosition(go);
            if (screenPos == null)
            {
                yield return CommandResult.Failure($"无法获取屏幕坐标");
            }

            var result = SimulateClick(go, screenPos.Value);
            yield return CommandResult.Success(new
            {
                action = "click",
                path = GameObjectHelper.GetGameObjectPath(go),
                instanceId = go.GetInstanceID(),
                screenPosition = screenPos.Value,
                eventSent = result
            });
        }

        [AIBridge("在屏幕坐标处模拟点击 (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_ClickAt --x 100 --y 200")]
        public static IEnumerator ClickAt(
            [Description("屏幕 X 坐标")] float x,
            [Description("屏幕 Y 坐标")] float y,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            if (x < 0 || y < 0)
            {
                yield return CommandResult.Failure("参数 'x' 和 'y' 必须为非负数");
            }

            var screenPos = new Vector2(x, y);
            var hitObject = RaycastUI(screenPos);
            var targetName = hitObject != null ? hitObject.name : "none";

            if (hitObject != null)
            {
                SimulateClick(hitObject, screenPos);
            }

            yield return CommandResult.Success(new
            {
                action = "click_at",
                screenPosition = new { x, y },
                hitObject = targetName,
                eventSent = hitObject != null ? "PointerClick" : "NoTarget"
            });
        }

        [AIBridge("通过路径模拟从一个对象拖动到另一个对象 (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_Drag --path \"Canvas/Item\" --toPath \"Canvas/Slot\" --frames 10")]
        public static IEnumerator Drag(
            [Description("源 GameObject 路径")] string path,
            [Description("目标 GameObject 路径（可选）")] string toPath = null,
            [Description("目标屏幕 X 坐标（如果未提供 toPath）")]
            float toX = -1,
            [Description("目标屏幕 Y 坐标（如果未提供 toPath）")]
            float toY = -1,
            [Description("拖动动画的帧数")] int frames = 10,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            if (string.IsNullOrEmpty(path))
            {
                yield return CommandResult.Failure("参数 'path' 不能为空");
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject: {path}");
            }

            var startPos = GetScreenPosition(go);
            if (startPos == null)
            {
                yield return CommandResult.Failure($"无法获取起始屏幕坐标: {path}");

            }

            Vector2 endPos;
            if (!string.IsNullOrEmpty(toPath))
            {
                var toGo = GameObject.Find(toPath);
                if (toGo == null)
                {
                    yield return CommandResult.Failure($"找不到目标 GameObject: {toPath}");
                }

                var toScreenPos = GetScreenPosition(toGo);
                if (toScreenPos == null)
                {
                    yield return CommandResult.Failure($"无法获取目标屏幕坐标: {toPath}");
                }

                endPos = toScreenPos.Value;
            }
            else
            {
                if (toX < 0 || toY < 0)
                {
                    yield return CommandResult.Failure("拖拽需要 'toPath' 或 'toX'+'toY' 参数");
                }

                endPos = new Vector2(toX, toY);
            }

            yield return PerformDrag(go, startPos.Value, endPos, frames);
        }

        [AIBridge("通过实例 ID 模拟从一个对象拖动到另一个对象 (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_DragByInstanceId --instanceId 12345 --toInstanceId 67890 --frames 10")]
        public static IEnumerator DragByInstanceId(
            [Description("源 GameObject 实例 ID")] int instanceId,
            [Description("目标 GameObject 实例 ID（可选）")]
            int toInstanceId = 0,
            [Description("目标屏幕 X 坐标（如果未提供 toInstanceId）")]
            float toX = -1,
            [Description("目标屏幕 Y 坐标（如果未提供 toInstanceId）")]
            float toY = -1,
            [Description("拖动动画的帧数")] int frames = 10,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject with instanceId: {instanceId}");
            }

            var startPos = GetScreenPosition(go);
            if (startPos == null)
            {
                yield return CommandResult.Failure($"无法获取起始屏幕坐标");
            }

            Vector2 endPos;
            if (toInstanceId != 0)
            {
                var toGo = EditorUtility.InstanceIDToObject(toInstanceId) as GameObject;
                if (toGo == null)
                {
                    yield return CommandResult.Failure($"找不到目标 GameObject with instanceId: {toInstanceId}");
                }

                var toScreenPos = GetScreenPosition(toGo);
                if (toScreenPos == null)
                {
                    yield return CommandResult.Failure($"无法获取目标屏幕坐标");
                }

                endPos = toScreenPos.Value;
            }
            else
            {
                if (toX < 0 || toY < 0)
                {
                    yield return CommandResult.Failure("拖拽需要 'toInstanceId' 或 'toX'+'toY' 参数");
                }

                endPos = new Vector2(toX, toY);
            }

            yield return PerformDrag(go, startPos.Value, endPos, frames);
        }

        private static IEnumerator PerformDrag(GameObject go, Vector2 startPos, Vector2 endPos, int frames)
        {

            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return CommandResult.Failure("场景中没有 EventSystem");
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = startPos,
                button = PointerEventData.InputButton.Left
            };

            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.beginDragHandler);

            frames = Mathf.Clamp(frames, 3, 60);

            for (int i = 0; i <= frames; i++)
            {
                float t = i / (float)frames;
                Vector2 newPos = Vector2.Lerp(startPos, endPos, t);
                Vector2 delta = newPos - pointerData.position;
                pointerData.position = newPos;
                pointerData.delta = delta;
                ExecuteEvents.Execute(go, pointerData, ExecuteEvents.dragHandler);
                yield return null;
            }

            pointerData.position = endPos;
            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerUpHandler);

            var dropTarget = RaycastUI(endPos);
            if (dropTarget != null)
            {
                ExecuteEvents.Execute(dropTarget, pointerData, ExecuteEvents.dropHandler);
            }

            yield return CommandResult.Success(new
            {
                action = "drag",
                from = startPos,
                to = endPos,
                frames,
                dropTarget = dropTarget != null ? dropTarget.name : "none"
            });
        }

        [AIBridge("通过路径模拟长按 GameObject (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_LongPress --path \"Canvas/Button\" --duration 1000")]
        public static IEnumerator LongPress(
            [Description("GameObject 的层级路径")] string path,
            [Description("按压持续时间（毫秒）")] int duration = 1000,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            if (string.IsNullOrEmpty(path))
            {
                yield return CommandResult.Failure("参数 'path' 不能为空");
            }

            var go = GameObject.Find(path);
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject: {path}");
            }

            yield return PerformLongPress(go, duration);
        }

        [AIBridge("通过实例 ID 模拟长按 GameObject (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_LongPressByInstanceId --instanceId 12345 --duration 1000")]
        public static IEnumerator LongPressByInstanceId(
            [Description("GameObject 的实例 ID")] int instanceId,
            [Description("按压持续时间（毫秒）")] int duration = 1000,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs)
        {
            if (!CanUse(out var result))
            {
                yield return result;
            }
            
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null)
            {
                yield return CommandResult.Failure($"找不到 GameObject with instanceId: {instanceId}");
            }

            yield return PerformLongPress(go, duration);
        }

        private static IEnumerator PerformLongPress(GameObject go, int duration)
        {

            var screenPos = GetScreenPosition(go);
            if (screenPos == null)
            {
                yield return CommandResult.Failure($"无法获取屏幕坐标");
            }

            float durationSec = duration / 1000f;

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                yield return CommandResult.Failure("场景中没有 EventSystem");
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPos.Value,
                button = PointerEventData.InputButton.Left
            };

            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerDownHandler);

            yield return new WaitForSeconds(durationSec);

            ExecuteEvents.Execute(go, pointerData, ExecuteEvents.pointerUpHandler);

            yield return CommandResult.Success(new
            {
                action = "long_press",
                path = GameObjectHelper.GetGameObjectPath(go),
                instanceId = go.GetInstanceID(),
                screenPosition = screenPos.Value,
                durationMs = duration
            });
        }

        private static string SimulateClick(GameObject target, Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return "NoEventSystem";
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPos,
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

        private static Vector2? GetScreenPosition(GameObject go)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    return RectTransformUtility.WorldToScreenPoint(cam, rectTransform.position);
                }
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 screenPoint = cam.WorldToScreenPoint(renderer.bounds.center);
                    return new Vector2(screenPoint.x, screenPoint.y);
                }
            }

            if (go.transform != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 screenPoint = cam.WorldToScreenPoint(go.transform.position);
                    return new Vector2(screenPoint.x, screenPoint.y);
                }
            }

            return null;
        }

        private static GameObject RaycastUI(Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return null;

            var pointerData = new PointerEventData(eventSystem) { position = screenPos };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            return results.Count > 0 ? results[0].gameObject : null;
        }

        private static bool CanUse(out CommandResult result)
        {
            if (!Application.isPlaying)
            {
                result = CommandResult.Failure("InputSimulation 只能在运行时使用");
                return false;
            }

            result = CommandResult.Success();
            return true;
        }

    }
}