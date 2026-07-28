using System;
using System.IO;
using AIBridge.Runtime;

namespace AIBridge.Editor
{
    public sealed class GifEncoder : IDisposable
    {
        private readonly AIBridgeRuntimeGifEncoder _encoder;

        public GifEncoder(Stream stream, int width, int height, int fps = 20, int colorCount = 128)
        {
            _encoder = new AIBridgeRuntimeGifEncoder(stream, width, height, fps, colorCount);
        }

        public void Initialize(byte[] firstFramePixels)
        {
            _encoder.Initialize(firstFramePixels);
        }

        public void AddFrame(byte[] pixels, int frameDelay = -1)
        {
            _encoder.AddFrame(pixels, frameDelay);
        }

        public void Finish()
        {
            _encoder.Finish();
        }

        public void Dispose()
        {
            _encoder.Dispose();
        }
    }
}
