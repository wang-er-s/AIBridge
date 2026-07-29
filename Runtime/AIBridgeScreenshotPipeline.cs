using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    public enum AIBridgeScreenshotKind
    {
        Image,
        Gif
    }

    public enum AIBridgeScreenshotProgressStage
    {
        Capturing,
        Finalizing
    }

    public sealed class AIBridgeScreenshotGifOptions
    {
        public int FrameCount;
        public float Delay;
        public float Scale;
        public int ColorCount;
        public int Fps;

        public static bool TryNormalize(
            int frameCount,
            float delay,
            float scale,
            int colorCount,
            int fps,
            out AIBridgeScreenshotGifOptions options,
            out string error)
        {
            options = null;
            error = null;
            if (frameCount <= 0)
            {
                error = "Parameter 'frameCount' must be > 0";
                return false;
            }

            options = new AIBridgeScreenshotGifOptions
            {
                FrameCount = Mathf.Clamp(frameCount, 1, 200),
                Delay = Mathf.Clamp(delay, 0.1f, 2f),
                Scale = Mathf.Clamp(scale, 0.25f, 1f),
                ColorCount = Mathf.Clamp(colorCount, 64, 256),
                Fps = Mathf.Clamp(fps, 10, 30)
            };
            return true;
        }
    }

    public sealed class AIBridgeScreenshotFrame
    {
        public bool Success;
        public byte[] PngBytes;
        public byte[] RgbaBytes;
        public int Width;
        public int Height;
        public string Error;

        public static AIBridgeScreenshotFrame Failed(string error)
        {
            return new AIBridgeScreenshotFrame
            {
                Success = false,
                Error = error
            };
        }
    }

    public sealed class AIBridgeScreenshotOutput
    {
        public string Path;
        public string WritePath;
        public string Filename;
        public string DownloadPath;
        public string Timestamp;
    }

    public sealed class AIBridgeScreenshotPipelineResult
    {
        public bool Success;
        public bool Cancelled;
        public string ErrorCode;
        public string Error;
        public string Path;
        public string DownloadPath;
        public string Filename;
        public int FrameCount;
        public int Width;
        public int Height;
        public float Duration;
        public long FileSize;
        public string Timestamp;

        public static AIBridgeScreenshotPipelineResult Failed(string errorCode, string error)
        {
            return new AIBridgeScreenshotPipelineResult
            {
                Success = false,
                ErrorCode = errorCode,
                Error = error
            };
        }

        public static AIBridgeScreenshotPipelineResult CancelledResult()
        {
            return new AIBridgeScreenshotPipelineResult
            {
                Success = false,
                Cancelled = true
            };
        }
    }

    public interface IAIBridgeScreenshotBackend
    {
        bool TryGetSize(out int width, out int height, out string error);
        IEnumerator CaptureImage(Action<AIBridgeScreenshotFrame> completed);
        IEnumerator CaptureGifFrame(int width, int height, Action<AIBridgeScreenshotFrame> completed);
        object CreateDelay(float seconds);
    }

    public interface IAIBridgeScreenshotStorage
    {
        AIBridgeScreenshotOutput CreateOutput(AIBridgeScreenshotKind kind);
        bool TryPublish(AIBridgeScreenshotOutput output, out string error);
        void Cleanup(AIBridgeScreenshotOutput output, bool published);
    }

    public interface IAIBridgeScreenshotProgress
    {
        void Report(AIBridgeScreenshotProgressStage stage, int current, int total);
        void Clear();
    }

    public static class AIBridgeScreenshotPixelConverter
    {
        public static byte[] ScaleAndFlip(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            if (source == null || source.Length != sourceWidth * sourceHeight)
            {
                throw new ArgumentException("Invalid source pixel data.", "source");
            }

            if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            {
                throw new ArgumentOutOfRangeException("targetWidth", "Pixel dimensions must be positive.");
            }

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

        public static byte[] DecodePngScaleAndFlip(byte[] pngBytes, int targetWidth, int targetHeight)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                throw new ArgumentException("PNG data is empty.", "pngBytes");
            }

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(pngBytes, false))
                {
                    throw new InvalidDataException("Failed to decode captured PNG.");
                }

                return ScaleAndFlip(
                    texture.GetPixels32(),
                    texture.width,
                    texture.height,
                    targetWidth,
                    targetHeight);
            }
            finally
            {
                if (texture != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
        }
    }

    public static class AIBridgeScreenshotPipeline
    {
        public static IEnumerator CaptureImage(
            IAIBridgeScreenshotBackend backend,
            IAIBridgeScreenshotStorage storage,
            Func<bool> isCancelled,
            long maxArtifactBytes,
            Action<AIBridgeScreenshotPipelineResult> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException("completed");
            }

            if (backend == null || storage == null)
            {
                completed(AIBridgeScreenshotPipelineResult.Failed(
                    "configuration_error",
                    "Screenshot backend and storage are required."));
                yield break;
            }

            int width;
            int height;
            string sizeError;
            if (!backend.TryGetSize(out width, out height, out sizeError))
            {
                completed(AIBridgeScreenshotPipelineResult.Failed("capture_failed", sizeError));
                yield break;
            }

            AIBridgeScreenshotFrame frame = null;
            var captureRoutine = backend.CaptureImage(value => { frame = value; });
            try
            {
                while (captureRoutine.MoveNext())
                {
                    yield return captureRoutine.Current;
                }
            }
            finally
            {
                var disposableCapture = captureRoutine as IDisposable;
                if (disposableCapture != null)
                {
                    disposableCapture.Dispose();
                }
            }

            if (IsCancelled(isCancelled))
            {
                completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                yield break;
            }

            if (frame == null || !frame.Success || frame.PngBytes == null)
            {
                completed(AIBridgeScreenshotPipelineResult.Failed(
                    "capture_failed",
                    frame == null ? "Screenshot capture did not return a frame." : frame.Error));
                yield break;
            }

            if (ExceedsLimit(frame.PngBytes.LongLength, maxArtifactBytes))
            {
                completed(AIBridgeScreenshotPipelineResult.Failed(
                    "artifact_too_large",
                    "Screenshot exceeds the artifact size limit."));
                yield break;
            }

            AIBridgeScreenshotOutput output;
            string error;
            if (!TryCreateOutput(storage, AIBridgeScreenshotKind.Image, out output, out error))
            {
                completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                yield break;
            }

            var published = false;
            try
            {
                if (!TryWriteAllBytes(output.WritePath, frame.PngBytes, out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                frame.PngBytes = null;
                if (IsCancelled(isCancelled))
                {
                    completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                    yield break;
                }

                if (!storage.TryPublish(output, out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                published = true;
                if (IsCancelled(isCancelled))
                {
                    completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                    yield break;
                }

                long fileSize;
                if (!TryGetFileSize(output.Path, out fileSize, out error))
                {
                    published = false;
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                completed(new AIBridgeScreenshotPipelineResult
                {
                    Success = true,
                    Path = output.Path,
                    DownloadPath = output.DownloadPath,
                    Filename = output.Filename,
                    Width = frame.Width > 0 ? frame.Width : width,
                    Height = frame.Height > 0 ? frame.Height : height,
                    FileSize = fileSize,
                    Timestamp = output.Timestamp
                });
            }
            finally
            {
                storage.Cleanup(output, published && !IsCancelled(isCancelled));
            }
        }

        public static IEnumerator CaptureGif(
            IAIBridgeScreenshotBackend backend,
            IAIBridgeScreenshotStorage storage,
            IAIBridgeScreenshotProgress progress,
            AIBridgeScreenshotGifOptions options,
            Func<bool> isCancelled,
            long maxArtifactBytes,
            Action<AIBridgeScreenshotPipelineResult> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException("completed");
            }

            if (backend == null || storage == null || options == null)
            {
                completed(AIBridgeScreenshotPipelineResult.Failed(
                    "configuration_error",
                    "Screenshot backend, storage, and GIF options are required."));
                yield break;
            }

            int sourceWidth;
            int sourceHeight;
            string sizeError;
            if (!backend.TryGetSize(out sourceWidth, out sourceHeight, out sizeError))
            {
                completed(AIBridgeScreenshotPipelineResult.Failed("capture_failed", sizeError));
                yield break;
            }

            var width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * options.Scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * options.Scale));
            AIBridgeScreenshotOutput output;
            string error;
            if (!TryCreateOutput(storage, AIBridgeScreenshotKind.Gif, out output, out error))
            {
                completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                yield break;
            }

            FileStream stream = null;
            AIBridgeGifEncoder encoder = null;
            var published = false;
            try
            {
                if (!TryCreateGifWriter(
                    output.WritePath,
                    width,
                    height,
                    options.Fps,
                    options.ColorCount,
                    out stream,
                    out encoder,
                    out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                for (var frameIndex = 0; frameIndex < options.FrameCount; frameIndex++)
                {
                    if (IsCancelled(isCancelled))
                    {
                        completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                        yield break;
                    }

                    if (progress != null)
                    {
                        progress.Report(
                            AIBridgeScreenshotProgressStage.Capturing,
                            frameIndex + 1,
                            options.FrameCount);
                    }

                    AIBridgeScreenshotFrame frame = null;
                    var captureRoutine = backend.CaptureGifFrame(
                        width,
                        height,
                        value => { frame = value; });
                    try
                    {
                        while (captureRoutine.MoveNext())
                        {
                            yield return captureRoutine.Current;
                        }
                    }
                    finally
                    {
                        var disposableCapture = captureRoutine as IDisposable;
                        if (disposableCapture != null)
                        {
                            disposableCapture.Dispose();
                        }
                    }

                    if (IsCancelled(isCancelled))
                    {
                        completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                        yield break;
                    }

                    if (frame == null || !frame.Success || frame.RgbaBytes == null)
                    {
                        completed(AIBridgeScreenshotPipelineResult.Failed(
                            "capture_failed",
                            frame == null
                                ? "Screenshot capture did not return a frame."
                                : "Failed to capture frame " + (frameIndex + 1) + ": " + frame.Error));
                        yield break;
                    }

                    if (!TryAddFrame(encoder, frame.RgbaBytes, out error))
                    {
                        completed(AIBridgeScreenshotPipelineResult.Failed("encoding_failed", error));
                        yield break;
                    }

                    frame.RgbaBytes = null;
                    if (ExceedsLimit(stream.Length, maxArtifactBytes))
                    {
                        completed(AIBridgeScreenshotPipelineResult.Failed(
                            "artifact_too_large",
                            "GIF exceeds the artifact size limit."));
                        yield break;
                    }

                    if (frameIndex < options.FrameCount - 1)
                    {
                        yield return backend.CreateDelay(options.Delay);
                    }
                }

                if (progress != null)
                {
                    progress.Report(
                        AIBridgeScreenshotProgressStage.Finalizing,
                        options.FrameCount,
                        options.FrameCount);
                }

                if (!TryFinishGif(ref encoder, ref stream, out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("encoding_failed", error));
                    yield break;
                }

                long fileSize;
                if (!TryGetFileSize(output.WritePath, out fileSize, out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                if (ExceedsLimit(fileSize, maxArtifactBytes))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed(
                        "artifact_too_large",
                        "GIF exceeds the artifact size limit."));
                    yield break;
                }

                if (IsCancelled(isCancelled))
                {
                    completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                    yield break;
                }

                if (!storage.TryPublish(output, out error))
                {
                    completed(AIBridgeScreenshotPipelineResult.Failed("io_failed", error));
                    yield break;
                }

                published = true;
                if (IsCancelled(isCancelled))
                {
                    completed(AIBridgeScreenshotPipelineResult.CancelledResult());
                    yield break;
                }

                completed(new AIBridgeScreenshotPipelineResult
                {
                    Success = true,
                    Path = output.Path,
                    DownloadPath = output.DownloadPath,
                    Filename = output.Filename,
                    FrameCount = options.FrameCount,
                    Width = width,
                    Height = height,
                    Duration = options.FrameCount / (float)options.Fps,
                    FileSize = fileSize,
                    Timestamp = output.Timestamp
                });
            }
            finally
            {
                if (progress != null)
                {
                    progress.Clear();
                }

                if (encoder != null)
                {
                    encoder.Dispose();
                }

                if (stream != null)
                {
                    stream.Dispose();
                }

                storage.Cleanup(output, published && !IsCancelled(isCancelled));
            }
        }

        private static bool IsCancelled(Func<bool> isCancelled)
        {
            return isCancelled != null && isCancelled();
        }

        private static bool ExceedsLimit(long size, long maxArtifactBytes)
        {
            return maxArtifactBytes > 0 && size > maxArtifactBytes;
        }

        private static bool TryCreateOutput(
            IAIBridgeScreenshotStorage storage,
            AIBridgeScreenshotKind kind,
            out AIBridgeScreenshotOutput output,
            out string error)
        {
            try
            {
                output = storage.CreateOutput(kind);
                error = output == null ? "Screenshot storage did not create an output." : null;
                return output != null;
            }
            catch (Exception ex)
            {
                output = null;
                error = ex.Message;
                return false;
            }
        }

        private static bool TryWriteAllBytes(string path, byte[] bytes, out string error)
        {
            try
            {
                File.WriteAllBytes(path, bytes);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryGetFileSize(string path, out long fileSize, out string error)
        {
            try
            {
                fileSize = new FileInfo(path).Length;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                fileSize = 0;
                error = ex.Message;
                return false;
            }
        }

        private static bool TryCreateGifWriter(
            string path,
            int width,
            int height,
            int fps,
            int colorCount,
            out FileStream stream,
            out AIBridgeGifEncoder encoder,
            out string error)
        {
            stream = null;
            encoder = null;
            try
            {
                stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                encoder = new AIBridgeGifEncoder(stream, width, height, fps, colorCount);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }

                error = ex.Message;
                return false;
            }
        }

        private static bool TryAddFrame(AIBridgeGifEncoder encoder, byte[] rgbaBytes, out string error)
        {
            try
            {
                encoder.AddFrame(rgbaBytes);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryFinishGif(
            ref AIBridgeGifEncoder encoder,
            ref FileStream stream,
            out string error)
        {
            try
            {
                encoder.Finish();
                encoder.Dispose();
                encoder = null;
                stream.Flush();
                stream.Dispose();
                stream = null;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (encoder != null)
                    {
                        encoder.Dispose();
                    }
                }
                catch
                {
                }
                finally
                {
                    encoder = null;
                }

                try
                {
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
                }
                catch
                {
                }
                finally
                {
                    stream = null;
                }

                error = ex.Message;
                return false;
            }
        }
    }
}
