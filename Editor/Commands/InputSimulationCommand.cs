using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using AIBridge.Runtime;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// 输入模拟命令，支持点击、拖拽、长按。
    /// </summary>
    public static class InputSimulationCommand
    {
        [AIBridge(
            "通过完整层级路径、坐标或实例 ID 模拟点击。Runtime 重名路径需同时提供 path + instanceId；" +
            "可用 CodeExecuteCommand_Execute 检查层级路径和 GetInstanceID (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_Click --path \"Canvas/Button\" --instanceId -123")]
        public static IEnumerator Click(
            [Description("GameObject 的完整层级路径；可用 CodeExecuteCommand_Execute 检查")] string path = null,
            [Description("屏幕坐标对象：{\"x\":number,\"y\":number}")] object point = null,
            [Description("GameObject 的实例 ID；Editor 可单独使用，Runtime 必须与 path 一起使用")]
            object instanceId = null,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return RunShared(
                AIBridgeInputCommands.Click(path, point, instanceId, EditorInputTargetResolver.Instance));
        }

        [AIBridge(
            "通过完整层级路径、坐标或实例 ID 点数组模拟拖拽。Runtime 重名路径点需同时提供 path + " +
            "instanceId；可用 CodeExecuteCommand_Execute 检查层级路径和 GetInstanceID (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_Drag --points " +
            "'[{\"path\":\"Canvas/Item\",\"instanceId\":-123},{\"x\":480,\"y\":720}]'")]
        public static IEnumerator Drag(
            [Description(
                "拖拽点数组，每项为 {\"path\":\"...\"}、{\"path\":\"...\",\"instanceId\":integer}、" +
                "{\"instanceId\":integer} 或 {\"x\":number,\"y\":number}；Runtime 不接受单独 instanceId")]
            object points,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return RunShared(
                AIBridgeInputCommands.Drag(points, EditorInputTargetResolver.Instance));
        }

        [AIBridge(
            "通过完整层级路径、坐标或实例 ID 模拟长按。Runtime 重名路径需同时提供 path + instanceId；" +
            "可用 CodeExecuteCommand_Execute 检查层级路径和 GetInstanceID (Only Runtime)",
            "AIBridgeCLI InputSimulationCommand_LongPress --path \"Canvas/Button\" " +
            "--instanceId -123 --duration 1000")]
        public static IEnumerator LongPress(
            [Description("GameObject 的完整层级路径；可用 CodeExecuteCommand_Execute 检查")] string path = null,
            [Description("屏幕坐标对象：{\"x\":number,\"y\":number}")] object point = null,
            [Description("GameObject 的实例 ID；Editor 可单独使用，Runtime 必须与 path 一起使用")]
            object instanceId = null,
            [Description("按压持续时间（毫秒）")] int duration = 1000,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return RunShared(
                AIBridgeInputCommands.LongPress(
                    path,
                    point,
                    instanceId,
                    duration,
                    EditorInputTargetResolver.Instance));
        }

        private static IEnumerator RunShared(IEnumerator routine)
        {
            if (routine == null)
            {
                yield return CommandResult.Success();
                yield break;
            }

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            try
            {
                while (stack.Count > 0)
                {
                    var current = stack.Peek();
                    if (!current.MoveNext())
                    {
                        Dispose(stack.Pop());
                        continue;
                    }

                    var yielded = current.Current;
                    var outcome = yielded as AIBridgeCommandOutcome;
                    if (outcome != null)
                    {
                        yield return outcome.Success
                            ? CommandResult.Success(outcome.Result)
                            : CommandResult.Failure(FormatError(outcome));
                        yield break;
                    }

                    var customYield = yielded as CustomYieldInstruction;
                    if (customYield != null)
                    {
                        while (customYield.keepWaiting)
                        {
                            yield return null;
                        }
                        continue;
                    }

                    var nested = yielded as IEnumerator;
                    if (nested != null)
                    {
                        stack.Push(nested);
                        continue;
                    }

                    yield return yielded;
                }

                yield return CommandResult.Success();
            }
            finally
            {
                while (stack.Count > 0)
                {
                    Dispose(stack.Pop());
                }
            }
        }

        private static string FormatError(AIBridgeCommandOutcome outcome)
        {
            if (string.IsNullOrEmpty(outcome.ErrorCode) ||
                outcome.ErrorCode == "command_failed")
            {
                return outcome.ErrorMessage;
            }

            return outcome.ErrorCode + ": " + outcome.ErrorMessage;
        }

        private static void Dispose(IEnumerator routine)
        {
            var disposable = routine as System.IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
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