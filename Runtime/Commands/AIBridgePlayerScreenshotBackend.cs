using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    internal sealed class AIBridgePlayerScreenshotBackend : IAIBridgeScreenshotBackend
    {
        internal static readonly AIBridgePlayerScreenshotBackend Instance =
            new AIBridgePlayerScreenshotBackend();

        private AIBridgePlayerScreenshotBackend()
        {
        }

        public bool TryGetSize(out int width, out int height, out string error)
        {
            width = Screen.width;
            height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                error = "Runtime framebuffer size is invalid.";
                return false;
            }

            error = null;
            return true;
        }

        public IEnumerator CaptureImage(Action<AIBridgeScreenshotFrame> completed)
        {
            yield return new WaitForEndOfFrame();

            var width = Screen.width;
            var height = Screen.height;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                completed(new AIBridgeScreenshotFrame
                {
                    Success = true,
                    PngBytes = texture.EncodeToPNG(),
                    Width = width,
                    Height = height
                });
            }
            catch (Exception ex)
            {
                completed(AIBridgeScreenshotFrame.Failed(ex.Message));
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        public IEnumerator CaptureGifFrame(
            int width,
            int height,
            Action<AIBridgeScreenshotFrame> completed)
        {
            yield return new WaitForEndOfFrame();

            RenderTexture renderTexture = null;
            var previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                renderTexture = RenderTexture.GetTemporary(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                completed(new AIBridgeScreenshotFrame
                {
                    Success = true,
                    RgbaBytes = AIBridgeScreenshotPixelConverter.ScaleAndFlip(
                        texture.GetPixels32(),
                        width,
                        height,
                        width,
                        height),
                    Width = width,
                    Height = height
                });
            }
            catch (Exception ex)
            {
                completed(AIBridgeScreenshotFrame.Failed(ex.Message));
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                RenderTexture.active = previous;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }
        }

        public object CreateDelay(float seconds)
        {
            return new WaitForSecondsRealtime(seconds);
        }
    }

    internal sealed class AIBridgePlayerScreenshotStorage : IAIBridgeScreenshotStorage
    {
        internal static readonly AIBridgePlayerScreenshotStorage Instance =
            new AIBridgePlayerScreenshotStorage();

        private AIBridgePlayerScreenshotStorage()
        {
        }

        public AIBridgeScreenshotOutput CreateOutput(AIBridgeScreenshotKind kind)
        {
            var timestamp = DateTime.UtcNow;
            var extension = kind == AIBridgeScreenshotKind.Image ? ".png" : ".gif";
            var filename = "screenshot_" + timestamp.ToString("yyyyMMdd_HHmmss_fff") + extension;
            var path = Path.Combine(AIBridgeScreenshotCommands.GetScreenshotDirectory(), filename);
            return new AIBridgeScreenshotOutput
            {
                Path = path,
                WritePath = path + ".part",
                Filename = filename,
                DownloadPath = AIBridgeScreenshotCommands.GetDownloadPath(filename),
                Timestamp = timestamp.ToString("o")
            };
        }

        public bool TryPublish(AIBridgeScreenshotOutput output, out string error)
        {
            try
            {
                File.Move(output.WritePath, output.Path);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Cleanup(AIBridgeScreenshotOutput output, bool published)
        {
            TryDelete(output.WritePath);
            if (!published)
            {
                TryDelete(output.Path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
