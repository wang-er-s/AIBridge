using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIBridge.Runtime
{
    public sealed class AIBridgeRuntimeGifEncoder : IDisposable
    {
        private readonly Stream _stream;
        private readonly int _width;
        private readonly int _height;
        private readonly int _colorCount;
        private readonly int _frameDelay;
        private readonly byte[] _indexedPixels;
        private readonly LzwEncoder _lzwEncoder = new LzwEncoder();
        private Color32[] _palette;
        private byte[] _colorLookup;
        private int _colorTableBits;
        private bool _headerWritten;
        private bool _finished;
        private bool _disposed;

        public AIBridgeRuntimeGifEncoder(Stream stream, int width, int height, int fps, int colorCount)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "GIF dimensions must be positive.");
            }

            _stream = stream;
            _width = width;
            _height = height;
            _colorCount = Mathf.Clamp(colorCount, 2, 256);
            _frameDelay = Mathf.Max(1, 100 / Mathf.Max(1, fps));
            _indexedPixels = new byte[width * height];
        }

        public void AddFrame(byte[] pixels, int frameDelay = -1)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("AIBridgeRuntimeGifEncoder");
            }

            if (pixels == null || pixels.Length != _width * _height * 4)
            {
                throw new ArgumentException("Invalid GIF frame pixel data.", "pixels");
            }

            if (!_headerWritten)
            {
                Initialize(pixels);
            }

            QuantizePixels(pixels);
            WriteGraphicControlExtension(frameDelay > 0 ? frameDelay : _frameDelay);
            WriteImageDescriptor();
            WriteLzwData();
        }

        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            if (_headerWritten)
            {
                _stream.WriteByte(0x3b);
            }

            _stream.Flush();
            _finished = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Finish();
            _disposed = true;
        }

        public void Initialize(byte[] pixels)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("AIBridgeRuntimeGifEncoder");
            }

            if (_headerWritten)
            {
                return;
            }

            if (pixels == null || pixels.Length != _width * _height * 4)
            {
                throw new ArgumentException("Invalid GIF frame pixel data.", "pixels");
            }

            _palette = BuildPalette(pixels, _colorCount);
            _colorTableBits = GetColorTableBits();
            BuildColorLookup();
            WriteHeader();
            _headerWritten = true;
        }

        private void WriteHeader()
        {
            WriteString("GIF89a");
            WriteUInt16(_width);
            WriteUInt16(_height);
            _stream.WriteByte((byte)(0x80 | ((_colorTableBits - 1) << 4) | (_colorTableBits - 1)));
            _stream.WriteByte(0);
            _stream.WriteByte(0);

            var tableSize = 1 << _colorTableBits;
            for (var i = 0; i < tableSize; i++)
            {
                var color = i < _palette.Length ? _palette[i] : new Color32(0, 0, 0, 255);
                _stream.WriteByte(color.r);
                _stream.WriteByte(color.g);
                _stream.WriteByte(color.b);
            }

            _stream.WriteByte(0x21);
            _stream.WriteByte(0xff);
            _stream.WriteByte(0x0b);
            WriteString("NETSCAPE2.0");
            _stream.WriteByte(3);
            _stream.WriteByte(1);
            WriteUInt16(0);
            _stream.WriteByte(0);
        }

        private void WriteGraphicControlExtension(int delay)
        {
            _stream.WriteByte(0x21);
            _stream.WriteByte(0xf9);
            _stream.WriteByte(4);
            _stream.WriteByte(0);
            WriteUInt16(delay);
            _stream.WriteByte(0);
            _stream.WriteByte(0);
        }

        private void WriteImageDescriptor()
        {
            _stream.WriteByte(0x2c);
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt16(_width);
            WriteUInt16(_height);
            _stream.WriteByte(0);
        }

        private Color32[] BuildPalette(byte[] pixels, int maxColors)
        {
            var samples = new List<Color32>(20000);
            var step = Mathf.Max(1, pixels.Length / 4 / 20000);
            for (var i = 0; i < pixels.Length; i += step * 4)
            {
                samples.Add(new Color32(pixels[i], pixels[i + 1], pixels[i + 2], 255));
            }

            var boxes = new List<ColorBox> { new ColorBox(samples) };
            while (boxes.Count < maxColors)
            {
                var bestIndex = -1;
                var bestRange = 0;
                for (var i = 0; i < boxes.Count; i++)
                {
                    var range = boxes[i].GetLargestRange();
                    if (boxes[i].Colors.Count > 1 && range > bestRange)
                    {
                        bestIndex = i;
                        bestRange = range;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                var split = boxes[bestIndex].Split();
                boxes.RemoveAt(bestIndex);
                if (split.First.Colors.Count > 0)
                {
                    boxes.Add(split.First);
                }

                if (split.Second.Colors.Count > 0)
                {
                    boxes.Add(split.Second);
                }
            }

            var palette = new Color32[maxColors];
            for (var i = 0; i < boxes.Count && i < palette.Length; i++)
            {
                palette[i] = boxes[i].GetAverageColor();
            }

            return palette;
        }

        private void BuildColorLookup()
        {
            _colorLookup = new byte[32768];
            for (var red = 0; red < 32; red++)
            {
                for (var green = 0; green < 32; green++)
                {
                    for (var blue = 0; blue < 32; blue++)
                    {
                        var index = (red << 10) | (green << 5) | blue;
                        _colorLookup[index] = FindClosestColor(
                            (byte)((red << 3) | (red >> 2)),
                            (byte)((green << 3) | (green >> 2)),
                            (byte)((blue << 3) | (blue >> 2)));
                    }
                }
            }
        }

        private byte FindClosestColor(byte red, byte green, byte blue)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < _palette.Length; i++)
            {
                var redDelta = red - _palette[i].r;
                var greenDelta = green - _palette[i].g;
                var blueDelta = blue - _palette[i].b;
                var distance = redDelta * redDelta + greenDelta * greenDelta + blueDelta * blueDelta;
                if (distance < bestDistance)
                {
                    bestIndex = i;
                    bestDistance = distance;
                }
            }

            return (byte)bestIndex;
        }

        private void QuantizePixels(byte[] pixels)
        {
            for (var i = 0; i < _indexedPixels.Length; i++)
            {
                var offset = i * 4;
                var lookupIndex = ((pixels[offset] >> 3) << 10)
                    | ((pixels[offset + 1] >> 3) << 5)
                    | (pixels[offset + 2] >> 3);
                _indexedPixels[i] = _colorLookup[lookupIndex];
            }
        }

        private void WriteLzwData()
        {
            var minimumCodeSize = Mathf.Max(2, _colorTableBits);
            _stream.WriteByte((byte)minimumCodeSize);
            var compressed = _lzwEncoder.Encode(_indexedPixels, minimumCodeSize);
            var offset = 0;
            while (offset < compressed.Length)
            {
                var blockSize = Mathf.Min(255, compressed.Length - offset);
                _stream.WriteByte((byte)blockSize);
                _stream.Write(compressed, offset, blockSize);
                offset += blockSize;
            }

            _stream.WriteByte(0);
        }

        private int GetColorTableBits()
        {
            var bits = 1;
            while ((1 << bits) < _colorCount)
            {
                bits++;
            }

            return Mathf.Clamp(bits, 2, 8);
        }

        private void WriteString(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                _stream.WriteByte((byte)value[i]);
            }
        }

        private void WriteUInt16(int value)
        {
            _stream.WriteByte((byte)(value & 0xff));
            _stream.WriteByte((byte)((value >> 8) & 0xff));
        }

        private sealed class ColorBox
        {
            public ColorBox(List<Color32> colors)
            {
                Colors = colors;
            }

            public List<Color32> Colors { get; private set; }

            public int GetLargestRange()
            {
                if (Colors.Count == 0)
                {
                    return 0;
                }

                int minRed;
                int maxRed;
                int minGreen;
                int maxGreen;
                int minBlue;
                int maxBlue;
                GetRanges(out minRed, out maxRed, out minGreen, out maxGreen, out minBlue, out maxBlue);
                return Mathf.Max(maxRed - minRed, Mathf.Max(maxGreen - minGreen, maxBlue - minBlue));
            }

            public ColorBoxPair Split()
            {
                if (Colors.Count <= 1)
                {
                    return new ColorBoxPair(
                        new ColorBox(new List<Color32>(Colors)),
                        new ColorBox(new List<Color32>()));
                }

                int minRed;
                int maxRed;
                int minGreen;
                int maxGreen;
                int minBlue;
                int maxBlue;
                GetRanges(out minRed, out maxRed, out minGreen, out maxGreen, out minBlue, out maxBlue);
                var redRange = maxRed - minRed;
                var greenRange = maxGreen - minGreen;
                var blueRange = maxBlue - minBlue;
                if (redRange >= greenRange && redRange >= blueRange)
                {
                    Colors.Sort((left, right) => left.r.CompareTo(right.r));
                }
                else if (greenRange >= blueRange)
                {
                    Colors.Sort((left, right) => left.g.CompareTo(right.g));
                }
                else
                {
                    Colors.Sort((left, right) => left.b.CompareTo(right.b));
                }

                var middle = Colors.Count / 2;
                return new ColorBoxPair(
                    new ColorBox(Colors.GetRange(0, middle)),
                    new ColorBox(Colors.GetRange(middle, Colors.Count - middle)));
            }

            public Color32 GetAverageColor()
            {
                if (Colors.Count == 0)
                {
                    return new Color32(0, 0, 0, 255);
                }

                long red = 0;
                long green = 0;
                long blue = 0;
                for (var i = 0; i < Colors.Count; i++)
                {
                    red += Colors[i].r;
                    green += Colors[i].g;
                    blue += Colors[i].b;
                }

                return new Color32(
                    (byte)(red / Colors.Count),
                    (byte)(green / Colors.Count),
                    (byte)(blue / Colors.Count),
                    255);
            }

            private void GetRanges(
                out int minRed,
                out int maxRed,
                out int minGreen,
                out int maxGreen,
                out int minBlue,
                out int maxBlue)
            {
                minRed = minGreen = minBlue = 255;
                maxRed = maxGreen = maxBlue = 0;
                for (var i = 0; i < Colors.Count; i++)
                {
                    var color = Colors[i];
                    minRed = Mathf.Min(minRed, color.r);
                    maxRed = Mathf.Max(maxRed, color.r);
                    minGreen = Mathf.Min(minGreen, color.g);
                    maxGreen = Mathf.Max(maxGreen, color.g);
                    minBlue = Mathf.Min(minBlue, color.b);
                    maxBlue = Mathf.Max(maxBlue, color.b);
                }
            }
        }

        private sealed class ColorBoxPair
        {
            public ColorBoxPair(ColorBox first, ColorBox second)
            {
                First = first;
                Second = second;
            }

            public ColorBox First { get; private set; }
            public ColorBox Second { get; private set; }
        }

        private sealed class LzwEncoder
        {
            private const int MaxCodeTableSize = 4096;
            private const int HashSize = 5003;
            private readonly int[] _hashTable = new int[HashSize];
            private readonly int[] _codeTable = new int[HashSize];
            private readonly List<byte> _output = new List<byte>(65536);
            private int _codeSize;
            private int _nextCode;
            private int _clearCode;
            private int _endCode;
            private int _bitBuffer;
            private int _bitCount;

            public byte[] Encode(byte[] data, int minimumCodeSize)
            {
                InitializeCodeTable(minimumCodeSize);
                _output.Clear();
                _bitBuffer = 0;
                _bitCount = 0;
                WriteBits(_clearCode, _codeSize);
                if (data.Length == 0)
                {
                    WriteBits(_endCode, _codeSize);
                    FlushBits();
                    return _output.ToArray();
                }

                int currentCode = data[0];
                for (var i = 1; i < data.Length; i++)
                {
                    var pixel = data[i];
                    var hash = GetHash(currentCode, pixel);
                    while (_hashTable[hash] != -1)
                    {
                        if (_hashTable[hash] == ((currentCode << 8) | pixel))
                        {
                            currentCode = _codeTable[hash];
                            goto NextPixel;
                        }

                        hash = (hash + 1) % HashSize;
                    }

                    WriteBits(currentCode, _codeSize);
                    if (_nextCode < MaxCodeTableSize)
                    {
                        _hashTable[hash] = (currentCode << 8) | pixel;
                        _codeTable[hash] = _nextCode++;
                        if (_nextCode > (1 << _codeSize) && _codeSize < 12)
                        {
                            _codeSize++;
                        }
                    }
                    else
                    {
                        WriteBits(_clearCode, _codeSize);
                        InitializeCodeTable(minimumCodeSize);
                    }

                    currentCode = pixel;
                NextPixel:
                    ;
                }

                WriteBits(currentCode, _codeSize);
                WriteBits(_endCode, _codeSize);
                FlushBits();
                return _output.ToArray();
            }

            private void InitializeCodeTable(int minimumCodeSize)
            {
                for (var i = 0; i < HashSize; i++)
                {
                    _hashTable[i] = -1;
                }

                var tableSize = 1 << minimumCodeSize;
                _clearCode = tableSize;
                _endCode = tableSize + 1;
                _nextCode = tableSize + 2;
                _codeSize = minimumCodeSize + 1;
            }

            private static int GetHash(int code, int pixel)
            {
                return ((code << 8) ^ pixel) % HashSize;
            }

            private void WriteBits(int value, int count)
            {
                _bitBuffer |= value << _bitCount;
                _bitCount += count;
                while (_bitCount >= 8)
                {
                    _output.Add((byte)(_bitBuffer & 0xff));
                    _bitBuffer >>= 8;
                    _bitCount -= 8;
                }
            }

            private void FlushBits()
            {
                if (_bitCount > 0)
                {
                    _output.Add((byte)(_bitBuffer & 0xff));
                }
            }
        }
    }
}
