using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SimpleTools.Media
{
    // Global palette for a GIF, built with median cut on sample frames. Index 255 stays free for transparency.
    public sealed class GifPalette
    {
        public const int TransparentIndex = 255;
        private const int MaxColors = 255;
        private const int BucketCount = 1 << 15;

        private readonly Color32[] _colors;
        private readonly byte[] _lookup = new byte[BucketCount];

        public IReadOnlyList<Color32> Colors => _colors;

        private GifPalette(Color32[] colors)
        {
            _colors = colors;
            for (int bucket = 0; bucket < BucketCount; bucket++)
            {
                _lookup[bucket] = Nearest(bucket);
            }
        }

        // Frames are tightly packed RGBA32
        public static GifPalette FromFrames(IEnumerable<byte[]> frames)
        {
            var histogram = new int[BucketCount];
            foreach (byte[] frame in frames)
            {
                for (int offset = 0; offset + 3 < frame.Length; offset += 4)
                {
                    histogram[Bucket(frame[offset], frame[offset + 1], frame[offset + 2])]++;
                }
            }

            var used = new List<int>();
            for (int bucket = 0; bucket < BucketCount; bucket++)
            {
                if (histogram[bucket] > 0)
                {
                    used.Add(bucket);
                }
            }

            var boxes = new List<List<int>> { used };
            while (boxes.Count < MaxColors)
            {
                int widest = -1;
                int widestRange = 0;
                int widestChannel = 0;
                for (int box = 0; box < boxes.Count; box++)
                {
                    if (boxes[box].Count < 2)
                    {
                        continue;
                    }

                    for (int channel = 0; channel < 3; channel++)
                    {
                        int range = Range(boxes[box], channel);
                        if (range > widestRange)
                        {
                            widestRange = range;
                            widest = box;
                            widestChannel = channel;
                        }
                    }
                }

                if (widest < 0)
                {
                    break;
                }

                List<int> split = boxes[widest];
                int sortChannel = widestChannel;
                split.Sort((first, second) => Channel(first, sortChannel).CompareTo(Channel(second, sortChannel)));
                long total = 0;
                foreach (int bucket in split)
                {
                    total += histogram[bucket];
                }

                long half = 0;
                int cut = 0;
                while (cut < split.Count - 2 && half + histogram[split[cut]] < total / 2)
                {
                    half += histogram[split[cut]];
                    cut++;
                }

                boxes[widest] = split.GetRange(0, cut + 1);
                boxes.Add(split.GetRange(cut + 1, split.Count - cut - 1));
            }

            var colors = new Color32[Mathf.Max(1, boxes.Count)];
            for (int box = 0; box < boxes.Count; box++)
            {
                long weight = 0, red = 0, green = 0, blue = 0;
                foreach (int bucket in boxes[box])
                {
                    int count = histogram[bucket];
                    weight += count;
                    red += count * (Channel(bucket, 0) * 255L / 31L);
                    green += count * (Channel(bucket, 1) * 255L / 31L);
                    blue += count * (Channel(bucket, 2) * 255L / 31L);
                }

                weight = Math.Max(1L, weight);
                colors[box] = new Color32((byte)(red / weight), (byte)(green / weight), (byte)(blue / weight), 255);
            }

            return new GifPalette(colors);
        }

        public byte Index(byte red, byte green, byte blue)
        {
            return _lookup[Bucket(red, green, blue)];
        }

        private byte Nearest(int bucket)
        {
            int red = Channel(bucket, 0) * 255 / 31;
            int green = Channel(bucket, 1) * 255 / 31;
            int blue = Channel(bucket, 2) * 255 / 31;
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int index = 0; index < _colors.Length; index++)
            {
                int deltaRed = red - _colors[index].r;
                int deltaGreen = green - _colors[index].g;
                int deltaBlue = blue - _colors[index].b;
                int distance = deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            return (byte)best;
        }

        private static int Bucket(byte red, byte green, byte blue)
        {
            return ((red >> 3) << 10) | ((green >> 3) << 5) | (blue >> 3);
        }

        private static int Channel(int bucket, int channel)
        {
            return (bucket >> (10 - channel * 5)) & 31;
        }

        private static int Range(List<int> box, int channel)
        {
            int minimum = 31;
            int maximum = 0;
            foreach (int bucket in box)
            {
                int value = Channel(bucket, channel);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }

            return maximum - minimum;
        }
    }

    // Streaming animated GIF writer (GIF89a, looping, one global palette). Pixels equal to the previous frame become
    // transparent and each frame is cropped to what changed, which keeps a mostly still scene small.
    public sealed class GifEncoder : IDisposable
    {
        private const int MinimumCodeSize = 8;
        private const int MaxCode = 4095;

        // 4×4 Bayer matrix scaled to about one palette bucket (8 levels), centered on zero
        private static readonly int[] Bayer = { -4, 0, -3, 1, 2, -2, 3, -1, -3, 1, -4, 0, 3, -1, 2, -2 };

        private readonly Stream _stream;
        private readonly int _width;
        private readonly int _height;
        private readonly int _delayCentiseconds;
        private readonly GifPalette _palette;
        private readonly byte[] _indices;
        private readonly byte[] _previous;
        private readonly byte[] _crop;
        private readonly int[] _dictionary = new int[1 << 20];
        private readonly byte[] _block = new byte[255];
        private int _generation;
        private int _blockLength;
        private int _bitBuffer;
        private int _bitCount;
        private bool _hasPrevious;
        private bool _finished;

        public GifEncoder(Stream stream, int width, int height, int delayCentiseconds, GifPalette palette)
        {
            _stream = stream;
            _width = width;
            _height = height;
            _delayCentiseconds = Mathf.Max(2, delayCentiseconds);
            _palette = palette;
            _indices = new byte[width * height];
            _previous = new byte[width * height];
            _crop = new byte[width * height];
            WriteHeader();
        }

        // rgba: tightly packed RGBA32, width × height. flipVertically for textures read bottom-up.
        public void AddFrame(byte[] rgba, bool flipVertically)
        {
            if (rgba.Length < _width * _height * 4)
            {
                throw new ArgumentException("Frame smaller than the GIF.", nameof(rgba));
            }

            for (int row = 0; row < _height; row++)
            {
                int source = (flipVertically ? _height - 1 - row : row) * _width * 4;
                int target = row * _width;
                for (int column = 0; column < _width; column++, source += 4, target++)
                {
                    // Ordered dither, fixed on screen: hides the banding of smooth gradients, and a pixel that does not
                    // change keeps its index, so the frame difference stays small
                    int dither = Bayer[(row & 3) * 4 + (column & 3)];
                    _indices[target] = _palette.Index(Dither(rgba[source], dither), Dither(rgba[source + 1], dither), Dither(rgba[source + 2], dither));
                }
            }

            int left = 0, top = 0, right = _width - 1, bottom = _height - 1;
            if (_hasPrevious)
            {
                ChangedBounds(out left, out top, out right, out bottom);
            }

            int cropWidth = right - left + 1;
            int cropHeight = bottom - top + 1;
            int written = 0;
            for (int row = top; row <= bottom; row++)
            {
                int offset = row * _width + left;
                for (int column = 0; column < cropWidth; column++, offset++)
                {
                    _crop[written++] = _hasPrevious && _indices[offset] == _previous[offset]
                        ? (byte)GifPalette.TransparentIndex
                        : _indices[offset];
                }
            }

            WriteFrameHeader(left, top, cropWidth, cropHeight);
            WriteLzw(written);
            Array.Copy(_indices, _previous, _indices.Length);
            _hasPrevious = true;
        }

        public void Finish()
        {
            if (!_finished)
            {
                _finished = true;
                _stream.WriteByte(0x3B);
                _stream.Flush();
            }
        }

        public void Dispose()
        {
            Finish();
            _stream.Dispose();
        }

        private static byte Dither(byte value, int offset)
        {
            int result = value + offset;
            return (byte)(result < 0 ? 0 : result > 255 ? 255 : result);
        }

        private void ChangedBounds(out int left, out int top, out int right, out int bottom)
        {
            left = _width;
            top = _height;
            right = -1;
            bottom = -1;
            for (int row = 0; row < _height; row++)
            {
                int offset = row * _width;
                for (int column = 0; column < _width; column++, offset++)
                {
                    if (_indices[offset] != _previous[offset])
                    {
                        left = Mathf.Min(left, column);
                        right = Mathf.Max(right, column);
                        top = Mathf.Min(top, row);
                        bottom = Mathf.Max(bottom, row);
                    }
                }
            }

            if (right < 0)
            {
                left = top = right = bottom = 0;
            }
        }

        private void WriteHeader()
        {
            WriteAscii("GIF89a");
            WriteShort(_width);
            WriteShort(_height);
            _stream.WriteByte(0xF7);
            _stream.WriteByte(0);
            _stream.WriteByte(0);
            for (int index = 0; index < 256; index++)
            {
                Color32 color = index < _palette.Colors.Count ? _palette.Colors[index] : new Color32(0, 0, 0, 255);
                _stream.WriteByte(color.r);
                _stream.WriteByte(color.g);
                _stream.WriteByte(color.b);
            }

            // Netscape extension: loop forever
            _stream.WriteByte(0x21);
            _stream.WriteByte(0xFF);
            _stream.WriteByte(11);
            WriteAscii("NETSCAPE2.0");
            _stream.WriteByte(3);
            _stream.WriteByte(1);
            WriteShort(0);
            _stream.WriteByte(0);
        }

        private void WriteFrameHeader(int left, int top, int width, int height)
        {
            // Graphic control: keep the previous frame underneath, transparent index set
            _stream.WriteByte(0x21);
            _stream.WriteByte(0xF9);
            _stream.WriteByte(4);
            _stream.WriteByte(0x05);
            WriteShort(_delayCentiseconds);
            _stream.WriteByte(GifPalette.TransparentIndex);
            _stream.WriteByte(0);

            _stream.WriteByte(0x2C);
            WriteShort(left);
            WriteShort(top);
            WriteShort(width);
            WriteShort(height);
            _stream.WriteByte(0);
        }

        private void WriteLzw(int length)
        {
            _stream.WriteByte(MinimumCodeSize);
            int clear = 1 << MinimumCodeSize;
            int end = clear + 1;
            int codeSize = MinimumCodeSize + 1;
            int next = end + 1;
            _blockLength = 0;
            _bitBuffer = 0;
            _bitCount = 0;
            NewDictionary();

            WriteCode(clear, codeSize);
            int prefix = _crop[0];
            for (int position = 1; position < length; position++)
            {
                int value = _crop[position];
                int key = (prefix << 8) | value;
                int entry = _dictionary[key];
                if ((entry >> 12) == _generation)
                {
                    prefix = entry & MaxCode;
                    continue;
                }

                WriteCode(prefix, codeSize);
                if (next <= MaxCode)
                {
                    _dictionary[key] = (_generation << 12) | next;
                    // The decoder sees the new code one step later: grow when the code just assigned needs the bits
                    if (next == 1 << codeSize && codeSize < 12)
                    {
                        codeSize++;
                    }

                    next++;
                }
                else
                {
                    WriteCode(clear, codeSize);
                    codeSize = MinimumCodeSize + 1;
                    next = end + 1;
                    NewDictionary();
                }

                prefix = value;
            }

            WriteCode(prefix, codeSize);
            WriteCode(end, codeSize);
            if (_bitCount > 0)
            {
                WriteDataByte(_bitBuffer & 0xFF);
            }

            FlushBlock();
            _stream.WriteByte(0);
        }

        private void NewDictionary()
        {
            _generation++;
            if (_generation >= 1 << 19)
            {
                Array.Clear(_dictionary, 0, _dictionary.Length);
                _generation = 1;
            }
        }

        private void WriteCode(int code, int codeSize)
        {
            _bitBuffer |= code << _bitCount;
            _bitCount += codeSize;
            while (_bitCount >= 8)
            {
                WriteDataByte(_bitBuffer & 0xFF);
                _bitBuffer >>= 8;
                _bitCount -= 8;
            }
        }

        private void WriteDataByte(int value)
        {
            _block[_blockLength++] = (byte)value;
            if (_blockLength == _block.Length)
            {
                FlushBlock();
            }
        }

        private void FlushBlock()
        {
            if (_blockLength > 0)
            {
                _stream.WriteByte((byte)_blockLength);
                _stream.Write(_block, 0, _blockLength);
                _blockLength = 0;
            }
        }

        private void WriteShort(int value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private void WriteAscii(string text)
        {
            foreach (char character in text)
            {
                _stream.WriteByte((byte)character);
            }
        }
    }
}
