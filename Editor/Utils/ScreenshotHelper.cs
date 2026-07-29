using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AIBridge.Runtime;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// Screenshot result data retained for callers of CaptureGameView.
    /// </summary>
    public class ScreenshotResult
    {
        public bool Success;
        public string ImagePath;
        public string Filename;
        public int Width;
        public int Height;
        public string Timestamp;
        public string Error;
    }

    /// <summary>
    /// Editor adapters for the Runtime-owned screenshot pipeline.
    /// </summary>
    public static class ScreenshotHelper
    {
        private static string _screenshotsDir;
        private static readonly EditorGameViewScreenshotBackend BackendInstance =
            new EditorGameViewScreenshotBackend();
        private static readonly EditorScreenshotStorage StorageInstance =
            new EditorScreenshotStorage();
        private static readonly EditorScreenshotProgress ProgressInstance =
            new EditorScreenshotProgress();

        public static IAIBridgeScreenshotBackend Backend
        {
            get { return BackendInstance; }
        }

        public static IAIBridgeScreenshotStorage Storage
        {
            get { return StorageInstance; }
        }

        public static IAIBridgeScreenshotProgress Progress
        {
            get { return ProgressInstance; }
        }

        public static string ScreenshotsDir
        {
            get
            {
                if (string.IsNullOrEmpty(_screenshotsDir))
                {
                    var projectRoot = Path.GetDirectoryName(Application.dataPath);
                    _screenshotsDir = Path.Combine(projectRoot, "AIBridgeCache", "screenshots");
                }

                return _screenshotsDir;
            }
        }

        /// <summary>
        /// Compatibility entry point for Editor callers that only need one image.
        /// </summary>
        public static IEnumerator CaptureGameView(Action<ScreenshotResult> onFinish)
        {
            AIBridgeScreenshotPipelineResult result = null;
            var pipeline = AIBridgeScreenshotPipeline.CaptureImage(
                Backend,
                Storage,
                null,
                0,
                value => { result = value; });
            try
            {
                while (pipeline.MoveNext())
                {
                    yield return pipeline.Current;
                }
            }
            finally
            {
                var disposablePipeline = pipeline as IDisposable;
                if (disposablePipeline != null)
                {
                    disposablePipeline.Dispose();
                }
            }

            onFinish?.Invoke(new ScreenshotResult
            {
                Success = result != null && result.Success,
                ImagePath = result == null ? null : result.Path,
                Filename = result == null ? null : result.Filename,
                Width = result == null ? 0 : result.Width,
                Height = result == null ? 0 : result.Height,
                Timestamp = result == null ? null : result.Timestamp,
                Error = result == null ? "Screenshot capture did not complete." : result.Error
            });
        }

        public static void EnsureScreenshotsDirectory()
        {
            if (!Directory.Exists(ScreenshotsDir))
            {
                Directory.CreateDirectory(ScreenshotsDir);
            }
        }

        private static EditorWindow GetGameView()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
            if (gameViewType == null)
            {
                Debug.LogError("[AIBridge] Cannot find GameView type in UnityEditor assembly");
                return null;
            }

            var getMainGameView = gameViewType.GetMethod(
                "GetMainGameView",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (getMainGameView != null)
            {
                return getMainGameView.Invoke(null, null) as EditorWindow;
            }

            return EditorWindow.GetWindow(gameViewType);
        }

        private static Vector2 GetGameViewSize(EditorWindow gameView)
        {
            var prop = gameView.GetType().GetProperty(
                "gameViewRenderResolution",
                BindingFlags.Instance | BindingFlags.Public);
            if (prop != null)
            {
                var value = prop.GetValue(gameView);
                if (value is Vector2 size && size.x > 0 && size.y > 0)
                {
                    return size;
                }
            }

            var getSizeMethod = gameView.GetType().GetMethod(
                "GetSize",
                BindingFlags.Instance | BindingFlags.Public);
            if (getSizeMethod != null)
            {
                var result = getSizeMethod.Invoke(gameView, null);
                if (result is Vector2 size && size.x > 0 && size.y > 0)
                {
                    return size;
                }
            }

            var sizeProp = gameView.GetType().GetProperty(
                "GameViewSize",
                BindingFlags.Instance | BindingFlags.Public);
            if (sizeProp != null)
            {
                var gameViewSize = sizeProp.GetValue(gameView);
                if (gameViewSize != null)
                {
                    var widthProp = gameViewSize.GetType().GetProperty(
                        "Width",
                        BindingFlags.Instance | BindingFlags.Public);
                    var heightProp = gameViewSize.GetType().GetProperty(
                        "Height",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (widthProp != null && heightProp != null)
                    {
                        var width = (int)widthProp.GetValue(gameViewSize);
                        var height = (int)heightProp.GetValue(gameViewSize);
                        if (width > 0 && height > 0)
                        {
                            return new Vector2(width, height);
                        }
                    }
                }
            }

            return new Vector2(1080, 1920);
        }

        private sealed class EditorGameViewScreenshotBackend : IAIBridgeScreenshotBackend
        {
            public bool TryGetSize(out int width, out int height, out string error)
            {
                width = 0;
                height = 0;
                error = null;
                try
                {
                    var gameView = GetGameView();
                    if (gameView == null)
                    {
                        error = "Cannot find Game View window. Make sure Game View is open.";
                        return false;
                    }

                    var size = GetGameViewSize(gameView);
                    width = (int)size.x;
                    height = (int)size.y;
                    if (width <= 0 || height <= 0)
                    {
                        error = "Game View size is invalid.";
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public IEnumerator CaptureImage(Action<AIBridgeScreenshotFrame> completed)
            {
                int width;
                int height;
                string error;
                if (!TryGetSize(out width, out height, out error))
                {
                    completed(AIBridgeScreenshotFrame.Failed(error));
                    yield break;
                }

                byte[] pngBytes = null;
                var capture = CapturePng(
                    bytes => { pngBytes = bytes; },
                    value => { error = value; });
                try
                {
                    while (capture.MoveNext())
                    {
                        yield return capture.Current;
                    }
                }
                finally
                {
                    var disposableCapture = capture as IDisposable;
                    if (disposableCapture != null)
                    {
                        disposableCapture.Dispose();
                    }
                }

                if (pngBytes == null)
                {
                    completed(AIBridgeScreenshotFrame.Failed(
                        string.IsNullOrEmpty(error) ? "Failed to capture Game View." : error));
                    yield break;
                }

                completed(new AIBridgeScreenshotFrame
                {
                    Success = true,
                    PngBytes = pngBytes,
                    Width = width,
                    Height = height
                });
            }

            public IEnumerator CaptureGifFrame(
                int width,
                int height,
                Action<AIBridgeScreenshotFrame> completed)
            {
                byte[] pngBytes = null;
                string error = null;
                var capture = CapturePng(
                    bytes => { pngBytes = bytes; },
                    value => { error = value; });
                try
                {
                    while (capture.MoveNext())
                    {
                        yield return capture.Current;
                    }
                }
                finally
                {
                    var disposableCapture = capture as IDisposable;
                    if (disposableCapture != null)
                    {
                        disposableCapture.Dispose();
                    }
                }

                if (pngBytes == null)
                {
                    completed(AIBridgeScreenshotFrame.Failed(
                        string.IsNullOrEmpty(error) ? "Failed to capture Game View." : error));
                    yield break;
                }

                try
                {
                    completed(new AIBridgeScreenshotFrame
                    {
                        Success = true,
                        RgbaBytes = AIBridgeScreenshotPixelConverter.DecodePngScaleAndFlip(
                            pngBytes,
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
            }

            public object CreateDelay(float seconds)
            {
                return new WaitForSeconds(seconds);
            }

            private static IEnumerator CapturePng(
                Action<byte[]> completed,
                Action<string> failed)
            {
                var tempPath = Path.Combine(
                    Path.GetTempPath(),
                    "aibridge_capture_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var captureStarted = true;
                    try
                    {
                        ScreenCapture.CaptureScreenshot(tempPath);
                    }
                    catch (Exception ex)
                    {
                        failed(ex.Message);
                        captureStarted = false;
                    }

                    if (!captureStarted)
                    {
                        yield break;
                    }

                    var retryCount = 0;
                    while (!File.Exists(tempPath) && retryCount < 100)
                    {
                        yield return new WaitForSeconds(0.1f);
                        retryCount++;
                    }

                    if (!File.Exists(tempPath))
                    {
                        failed("Failed to capture screenshot - file was not created.");
                        yield break;
                    }

                    try
                    {
                        completed(File.ReadAllBytes(tempPath));
                    }
                    catch (Exception ex)
                    {
                        failed(ex.Message);
                    }
                }
                finally
                {
                    TryDelete(tempPath);
                }
            }
        }

        private sealed class EditorScreenshotStorage : IAIBridgeScreenshotStorage
        {
            public AIBridgeScreenshotOutput CreateOutput(AIBridgeScreenshotKind kind)
            {
                EnsureScreenshotsDirectory();
                var timestamp = DateTime.Now;
                string filename;
                string formattedTimestamp;
                if (kind == AIBridgeScreenshotKind.Image)
                {
                    filename = "game_" + timestamp.ToString("yyyyMMdd_HHmmss") + "_"
                        + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
                    formattedTimestamp = timestamp.ToString("yyyyMMdd_HHmmss");
                }
                else
                {
                    filename = "gif_" + timestamp.ToString("yyyyMMdd_HHmmss") + "_"
                        + Guid.NewGuid().ToString("N").Substring(0, 8) + ".gif";
                    formattedTimestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss");
                }

                var path = Path.Combine(ScreenshotsDir, filename);
                return new AIBridgeScreenshotOutput
                {
                    Path = path,
                    WritePath = path + ".part",
                    Filename = filename,
                    Timestamp = formattedTimestamp
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
        }

        private sealed class EditorScreenshotProgress : IAIBridgeScreenshotProgress
        {
            public void Report(AIBridgeScreenshotProgressStage stage, int current, int total)
            {
                if (stage == AIBridgeScreenshotProgressStage.Finalizing)
                {
                    EditorUtility.DisplayProgressBar("Creating GIF", "Finalizing GIF...", 1f);
                    return;
                }

                if (current == 1 || current == total || current % 5 == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "Capturing Frames",
                        "Frame " + current + "/" + total,
                        current / (float)total);
                }
            }

            public void Clear()
            {
                EditorUtility.ClearProgressBar();
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
