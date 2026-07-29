using System.Collections;
using System.ComponentModel;
using AIBridge.Runtime;

namespace AIBridge.Editor
{
    public static class ScreenshotCommand
    {
        [AIBridge("捕获 Game 视图的截图",
            "AIBridgeCLI ScreenshotCommand_Image")]
        public static IEnumerator Image(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            AIBridgeScreenshotPipelineResult result = null;
            yield return AIBridgeScreenshotPipeline.CaptureImage(
                ScreenshotHelper.Backend,
                ScreenshotHelper.Storage,
                null,
                0,
                value => { result = value; });

            if (result == null || !result.Success)
            {
                yield return CommandResult.Failure(
                    result == null ? "Screenshot capture did not complete." : result.Error);
            }

            yield return CommandResult.Success(new
            {
                action = "game",
                imagePath = result.Path,
                width = result.Width,
                height = result.Height,
                timestamp = result.Timestamp,
                filename = result.Filename
            });
        }

        [AIBridge("捕获多个截图并合成 GIF，至少需要 15 秒超时",
            "AIBridgeCLI ScreenshotCommand_Gif --frameCount 30 --fps 15")]
        public static IEnumerator Gif(
            [Description("要捕获的帧数（1-200）")]
            int frameCount = 30,
            [Description("帧之间的延迟（秒）（0.1-2.0）")]
            float delay = 0.1f,
            [Description("缩放因子（0.25-1.0）")]
            float scale = 0.5f,
            [Description("颜色数量（64-256）")] int colorCount = 128,
            [Description("GIF 播放的 FPS（10-30）")]
            int fps = 15,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            AIBridgeScreenshotGifOptions options;
            string optionsError;
            if (!AIBridgeScreenshotGifOptions.TryNormalize(
                frameCount,
                delay,
                scale,
                colorCount,
                fps,
                out options,
                out optionsError))
            {
                yield return CommandResult.Failure(optionsError);
            }

            AIBridgeScreenshotPipelineResult result = null;
            yield return AIBridgeScreenshotPipeline.CaptureGif(
                ScreenshotHelper.Backend,
                ScreenshotHelper.Storage,
                ScreenshotHelper.Progress,
                options,
                null,
                0,
                value => { result = value; });

            if (result == null || !result.Success)
            {
                yield return CommandResult.Failure(
                    result == null ? "GIF capture did not complete." : result.Error);
            }

            AIBridgeLogger.LogInfo(
                "GIF created: " + result.Path + " (" + result.FileSize / 1024
                + "KB, " + result.FrameCount + " frames)");

            yield return CommandResult.Success(new
            {
                action = "gif",
                gifPath = result.Path,
                filename = result.Filename,
                frameCount = result.FrameCount,
                width = result.Width,
                height = result.Height,
                duration = result.Duration,
                fileSize = result.FileSize,
                timestamp = result.Timestamp
            });
        }
    }
}
