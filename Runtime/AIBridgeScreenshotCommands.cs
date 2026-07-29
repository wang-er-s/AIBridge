using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    internal static class AIBridgeScreenshotCommands
    {
        private const string ScreenshotDirectoryName = "screenshots";
        private static string _screenshotDirectory;
        private static readonly IAIBridgeScreenshotBackend Backend =
            AIBridgePlayerScreenshotBackend.Instance;
        private static readonly IAIBridgeScreenshotStorage Storage =
            AIBridgePlayerScreenshotStorage.Instance;

        internal static void Initialize()
        {
            GetScreenshotDirectory();
        }

        public static IEnumerator Image(AIBridgeCommandContext context)
        {
            AIBridgeScreenshotPipelineResult result = null;
            yield return AIBridgeScreenshotPipeline.CaptureImage(
                Backend,
                Storage,
                () => context.IsClosed,
                AIBridgeProtocol.MaxArtifactBytes,
                value => { result = value; });

            if (context.IsClosed || result == null || result.Cancelled)
            {
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

        public static IEnumerator Gif(AIBridgeCommandContext context)
        {
            AIBridgeScreenshotGifOptions options;
            string optionsError;
            if (!AIBridgeScreenshotGifOptions.TryNormalize(
                context.Parameters.GetInt32("frameCount", 30),
                context.Parameters.GetSingle("delay", 0.1f),
                context.Parameters.GetSingle("scale", 0.5f),
                context.Parameters.GetInt32("colorCount", 128),
                context.Parameters.GetInt32("fps", 15),
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
                Backend,
                Storage,
                null,
                options,
                () => context.IsClosed,
                AIBridgeProtocol.MaxArtifactBytes,
                value => { result = value; });

            if (context.IsClosed || result == null || result.Cancelled)
            {
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
