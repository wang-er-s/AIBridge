using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    internal static class AIBridgeRuntimeScreenshotCommands
    {
        private const string ScreenshotDirectoryName = "screenshots";
        private static string _screenshotDirectory;

        internal static void Initialize()
        {
            GetScreenshotDirectory();
        }

        public static IEnumerator Image(AIBridgeRuntimeCommandContext context)
        {
            yield return new WaitForEndOfFrame();

            Texture2D texture = null;
            try
            {
                var width = Screen.width;
                var height = Screen.height;
                if (width <= 0 || height <= 0)
                {
                    yield return AIBridgeRuntimeCommandOutcome.Failed(
                        "capture_failed",
                        "Runtime framebuffer size is invalid.");
                    yield break;
                }

                texture = CaptureFramebuffer(width, height);
                var bytes = texture.EncodeToPNG();
                var timestamp = DateTime.UtcNow;
                var filename = "screenshot_" + timestamp.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                var directory = GetScreenshotDirectory();
                var path = Path.Combine(directory, filename);
                var partialPath = path + ".part";
                var published = false;
                try
                {
                    if (bytes.LongLength > AIBridgeRuntimeProtocol.MaxArtifactBytes)
                    {
                        yield return AIBridgeRuntimeCommandOutcome.Failed(
                            "artifact_too_large",
                            "Screenshot exceeds the artifact size limit.");
                        yield break;
                    }

                    File.WriteAllBytes(partialPath, bytes);
                    if (context.IsClosed)
                    {
                        yield break;
                    }

                    File.Move(partialPath, path);
                    if (context.IsClosed)
                    {
                        File.Delete(path);
                        yield break;
                    }

                    published = true;
                    yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
                    {
                        { "action", "game" },
                        { "imagePath", path },
                        { "path", path },
                        { "downloadPath", GetDownloadPath(filename) },
                        { "filename", filename },
                        { "width", width },
                        { "height", height },
                        { "timestamp", timestamp.ToString("o") }
                    });
                }
                finally
                {
                    if (!published && File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }
                }
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        public static IEnumerator Gif(AIBridgeRuntimeCommandContext context)
        {
            var frameCount = Mathf.Clamp(context.Parameters.GetInt32("frameCount", 30), 1, 200);
            var delay = Mathf.Clamp(context.Parameters.GetSingle("delay", 0.1f), 0.1f, 2f);
            var scale = Mathf.Clamp(context.Parameters.GetSingle("scale", 0.5f), 0.25f, 1f);
            var colorCount = Mathf.Clamp(context.Parameters.GetInt32("colorCount", 128), 64, 256);
            var fps = Mathf.Clamp(context.Parameters.GetInt32("fps", 15), 10, 30);
            var sourceWidth = Screen.width;
            var sourceHeight = Screen.height;
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                yield return AIBridgeRuntimeCommandOutcome.Failed(
                    "capture_failed",
                    "Runtime framebuffer size is invalid.");
                yield break;
            }

            var width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
            var timestamp = DateTime.UtcNow;
            var filename = "screenshot_" + timestamp.ToString("yyyyMMdd_HHmmss_fff") + ".gif";
            var path = Path.Combine(GetScreenshotDirectory(), filename);
            var partialPath = path + ".part";
            FileStream stream = null;
            AIBridgeRuntimeGifEncoder encoder = null;
            var published = false;

            try
            {
                stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder = new AIBridgeRuntimeGifEncoder(stream, width, height, fps, colorCount);

                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    yield return new WaitForEndOfFrame();
                    Texture2D texture = null;
                    try
                    {
                        // 直接捕获到目标尺寸，并在编码后立即释放，避免创建和持有全分辨率帧副本。
                        texture = CaptureFramebufferScaled(width, height);
                        encoder.AddFrame(ScaleAndFlip(texture.GetPixels32(), width, height, width, height));
                    }
                    finally
                    {
                        if (texture != null)
                        {
                            UnityEngine.Object.Destroy(texture);
                        }
                    }

                    if (stream.Length > AIBridgeRuntimeProtocol.MaxArtifactBytes)
                    {
                        yield return AIBridgeRuntimeCommandOutcome.Failed(
                            "artifact_too_large",
                            "GIF exceeds the artifact size limit.");
                        yield break;
                    }

                    if (frameIndex < frameCount - 1)
                    {
                        yield return new WaitForSecondsRealtime(delay);
                    }
                }

                encoder.Finish();
                encoder.Dispose();
                encoder = null;
                stream.Flush();
                var fileSize = stream.Length;
                stream.Dispose();
                stream = null;
                if (context.IsClosed)
                {
                    yield break;
                }

                File.Move(partialPath, path);
                if (context.IsClosed)
                {
                    File.Delete(path);
                    yield break;
                }

                published = true;
                yield return AIBridgeRuntimeCommandOutcome.Succeeded(new Dictionary<string, object>
                {
                    { "action", "gif" },
                    { "gifPath", path },
                    { "path", path },
                    { "downloadPath", GetDownloadPath(filename) },
                    { "filename", filename },
                    { "frameCount", frameCount },
                    { "width", width },
                    { "height", height },
                    { "duration", frameCount / (float)fps },
                    { "fileSize", fileSize },
                    { "timestamp", timestamp.ToString("o") }
                });
            }
            finally
            {
                try
                {
                    if (encoder != null)
                    {
                        encoder.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        if (stream != null)
                        {
                            stream.Dispose();
                        }
                    }
                    finally
                    {
                        if (!published && File.Exists(partialPath))
                        {
                            File.Delete(partialPath);
                        }
                    }
                }
            }
        }

        private static Texture2D CaptureFramebuffer(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CaptureFramebufferScaled(int width, int height)
        {
            var renderTexture = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            catch
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static byte[] ScaleAndFlip(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            var pixels = new byte[targetWidth * targetHeight * 4];
            for (var targetY = 0; targetY < targetHeight; targetY++)
            {
                var sourceY = sourceHeight - 1
                    - Mathf.Min(sourceHeight - 1, targetY * sourceHeight / targetHeight);
                for (var targetX = 0; targetX < targetWidth; targetX++)
                {
                    var sourceX = Mathf.Min(sourceWidth - 1, targetX * sourceWidth / targetWidth);
                    var color = source[sourceY * sourceWidth + sourceX];
                    var offset = (targetY * targetWidth + targetX) * 4;
                    pixels[offset] = color.r;
                    pixels[offset + 1] = color.g;
                    pixels[offset + 2] = color.b;
                    pixels[offset + 3] = color.a;
                }
            }

            return pixels;
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

        private static string GetDownloadPath(string filename)
        {
            return AIBridgeRuntimeProtocol.ArtifactPathPrefix + Uri.EscapeDataString(filename);
        }
    }
}
