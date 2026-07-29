using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    public static class AIBridgeScreenshotCommands
    {
        private const string ScreenshotDirectoryName = "screenshots";
        private static string _screenshotDirectory;

        internal static void Initialize()
        {
            GetScreenshotDirectory();
        }

        [AIBridge(
            "捕获 Game 视图的截图",
            "AIBridgeCLI ScreenshotCommand_Image",
            "ScreenshotCommand_Image")]
        public static IEnumerator Image(
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return RunImage(AIBridgeCommandHost.Current, null);
        }

        [AIBridge(
            "捕获多个截图并合成 GIF，至少需要 15 秒超时",
            "AIBridgeCLI ScreenshotCommand_Gif --frameCount 30 --fps 15",
            "ScreenshotCommand_Gif")]
        public static IEnumerator Gif(
            [Description("要捕获的帧数（1-200）")] int frameCount = 30,
            [Description("帧之间的延迟（秒）（0.1-2.0）")] float delay = 0.1f,
            [Description("缩放因子（0.25-1.0）")] float scale = 0.5f,
            [Description("颜色数量（64-256）")] int colorCount = 128,
            [Description("GIF 播放的 FPS（10-30）")] int fps = 15,
            [Description("手机 Runtime URL；为空时在 Editor 执行")] string url = null,
            [Description("手机 Runtime 执行超时，单位毫秒")]
            int runtimeTimeout = AIBridgeProtocol.DefaultExecutionTimeoutMs)
        {
            yield return RunGif(
                frameCount,
                delay,
                scale,
                colorCount,
                fps,
                AIBridgeCommandHost.Current,
                null);
        }

        public static IEnumerator ExecuteImage(AIBridgeCommandContext context)
        {
            yield return RunImage(context.Host, () => context.IsClosed);
        }

        public static IEnumerator ExecuteGif(AIBridgeCommandContext context)
        {
            yield return RunGif(
                context.Parameters.GetInt32("frameCount", 30),
                context.Parameters.GetSingle("delay", 0.1f),
                context.Parameters.GetSingle("scale", 0.5f),
                context.Parameters.GetInt32("colorCount", 128),
                context.Parameters.GetInt32("fps", 15),
                context.Host,
                () => context.IsClosed);
        }

        private static IEnumerator RunImage(
            AIBridgeCommandHost host,
            Func<bool> isCancelled)
        {
            AIBridgeScreenshotPipelineResult result = null;
            yield return AIBridgeScreenshotPipeline.CaptureImage(
                host.ScreenshotBackend,
                host.ScreenshotStorage,
                isCancelled,
                host == AIBridgeCommandHost.Player ? AIBridgeProtocol.MaxArtifactBytes : 0,
                value => { result = value; });

            if (IsCancelled(isCancelled) || (result != null && result.Cancelled))
            {
                yield break;
            }

            if (result == null)
            {
                yield return AIBridgeCommandOutcome.Failed(
                    "capture_failed",
                    "Screenshot capture did not complete.");
                yield break;
            }

            if (!result.Success)
            {
                yield return AIBridgeCommandOutcome.Failed(result.ErrorCode, result.Error);
                yield break;
            }

            yield return AIBridgeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "game" },
                { "imagePath", result.Path },
                { "path", result.Path },
                { "downloadPath", result.DownloadPath },
                { "filename", result.Filename },
                { "width", result.Width },
                { "height", result.Height },
                { "timestamp", result.Timestamp }
            });
        }

        private static IEnumerator RunGif(
            int frameCount,
            float delay,
            float scale,
            int colorCount,
            int fps,
            AIBridgeCommandHost host,
            Func<bool> isCancelled)
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
                yield return AIBridgeCommandOutcome.Failed(
                    "binding_failed",
                    optionsError);
                yield break;
            }

            AIBridgeScreenshotPipelineResult result = null;
            yield return AIBridgeScreenshotPipeline.CaptureGif(
                host.ScreenshotBackend,
                host.ScreenshotStorage,
                host.ScreenshotProgress,
                options,
                isCancelled,
                host == AIBridgeCommandHost.Player ? AIBridgeProtocol.MaxArtifactBytes : 0,
                value => { result = value; });

            if (IsCancelled(isCancelled) || (result != null && result.Cancelled))
            {
                yield break;
            }

            if (result == null)
            {
                yield return AIBridgeCommandOutcome.Failed(
                    "capture_failed",
                    "GIF capture did not complete.");
                yield break;
            }

            if (!result.Success)
            {
                yield return AIBridgeCommandOutcome.Failed(result.ErrorCode, result.Error);
                yield break;
            }

            yield return AIBridgeCommandOutcome.Succeeded(new Dictionary<string, object>
            {
                { "action", "gif" },
                { "gifPath", result.Path },
                { "path", result.Path },
                { "downloadPath", result.DownloadPath },
                { "filename", result.Filename },
                { "frameCount", result.FrameCount },
                { "width", result.Width },
                { "height", result.Height },
                { "duration", result.Duration },
                { "fileSize", result.FileSize },
                { "timestamp", result.Timestamp }
            });
        }

        private static bool IsCancelled(Func<bool> isCancelled)
        {
            return isCancelled != null && isCancelled();
        }

        internal static string GetScreenshotDirectory()
        {
            if (string.IsNullOrEmpty(_screenshotDirectory))
            {
                _screenshotDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "AIBridgeSelf",
                    ScreenshotDirectoryName);
                Directory.CreateDirectory(_screenshotDirectory);
            }

            return _screenshotDirectory;
        }

        internal static string GetDownloadPath(string filename)
        {
            return AIBridgeProtocol.ArtifactPathPrefix + Uri.EscapeDataString(filename);
        }
    }
}
