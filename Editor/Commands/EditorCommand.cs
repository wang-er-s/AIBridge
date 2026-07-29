using System.Collections;
using System.ComponentModel;
using AIBridge.Runtime;
using UnityEditor;

namespace AIBridge.Editor
{
    public static class EditorCommand
    {
        [AIBridge("进入播放模式",
            "AIBridgeCLI EditorCommand_Play")]
        public static IEnumerator Play()
        {
            if (EditorApplication.isPlaying)
                yield return CommandResult.Success(new { action = "play", alreadyPlaying = true });
            else
            {
                EditorApplication.isPlaying = true;
                yield return CommandResult.Success(new { action = "play", started = true });
            }
        }

        [AIBridge("退出播放模式",
            "AIBridgeCLI EditorCommand_Stop")]
        public static IEnumerator Stop()
        {
            if (!EditorApplication.isPlaying)
                yield return CommandResult.Success(new { action = "stop", alreadyStopped = true });
            else
            {
                EditorApplication.isPlaying = false;
                yield return CommandResult.Success(new { action = "stop", stopped = true });
            }
        }

        [AIBridge("切换或设置暂停状态",
            "AIBridgeCLI EditorCommand_Pause")]
        public static IEnumerator Pause(
            [Description("切换暂停（true）或设置特定值（false）")] bool toggle = true,
            [Description("当 toggle 为 false 时要设置的暂停状态")] bool pause = true)
        {
            EditorApplication.isPaused = toggle ? !EditorApplication.isPaused : pause;
            yield return CommandResult.Success(new { action = "pause", isPaused = EditorApplication.isPaused });
        }

        [AIBridge("获取当前编辑器状态（播放/暂停/编译状态）",
            "AIBridgeCLI EditorCommand_GetState")]
        public static IEnumerator GetState()
        {
            yield return CommandResult.Success(new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                applicationPath = EditorApplication.applicationPath,
                applicationContentsPath = EditorApplication.applicationContentsPath
            });
        }

        [AIBridge("向 Unity 控制台输出日志消息",
            "AIBridgeCLI EditorCommand_Log --message \"Hello World\"")]
        public static IEnumerator Log(
            [Description("要记录的消息")] string message,
            [Description("日志类型：Log, Warning, Error")] string logType = "Log",
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            var result = AIBridgeLogService.Emit(message, logType);
            yield return result.Success
                ? CommandResult.Success(result.Data)
                : CommandResult.Failure(result.ErrorMessage);
        }
    }
}
