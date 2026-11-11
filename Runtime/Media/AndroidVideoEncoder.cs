using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SimpleTools.Media
{
    /// <summary>
    /// MP4 encoder with the phone's own encoders (MediaCodec + MediaMuxer, Android/MediaCodecVideoEncoder.java): no
    /// external program and no licensing obligations. Converts each RGBA frame (bottom-up) to I420 with the BT.709 limited
    /// range matrix, the same as the ffmpeg path, and hands the planes to Java.
    /// </summary>
    public sealed class AndroidVideoEncoder : IVideoEncoder
    {
        private const string JavaClass = "com.simpletools.media.MediaCodecVideoEncoder";

        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _y;
        private readonly byte[] _u;
        private readonly byte[] _v;
        private byte[] _frame;
#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly AndroidJavaObject _encoder;
#endif

        public static bool IsSupported => Application.platform == RuntimePlatform.Android;

        public bool FinishesOffMainThread => false;

        /// <summary>Width and height must be even.</summary>
        public AndroidVideoEncoder(int width, int height, int framesPerSecond, int bitrateMegabits, string audioPath,
            string outputPath)
        {
            _width = width;
            _height = height;
            _y = new byte[width * height];
            _u = new byte[width * height / 4];
            _v = new byte[width * height / 4];
#if UNITY_ANDROID && !UNITY_EDITOR
            _encoder = new AndroidJavaObject(JavaClass, outputPath, width, height, framesPerSecond, bitrateMegabits * 1000000,
                audioPath ?? string.Empty);
#else
            throw new PlatformNotSupportedException("AndroidVideoEncoder works on Android only.");
#endif
        }

        public byte[] RentFrame(int length)
        {
            if (_frame == null || _frame.Length != length)
            {
                _frame = new byte[length];
            }

            return _frame;
        }

        public bool WriteFrame(byte[] rgba)
        {
            ToI420(rgba);
#if UNITY_ANDROID && !UNITY_EDITOR
            return _encoder.Call<bool>("encodeFrame", _y, _u, _v);
#else
            return false;
#endif
        }

        public bool Finish()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return _encoder.Call<bool>("finish");
#else
            return false;
#endif
        }

        public void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _encoder.Call("cancel");
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _encoder.Dispose();
#endif
        }

        // RGBA bottom-up to Y, U, V top-down, rows in parallel; chroma from the average of each 2×2 block
        private void ToI420(byte[] rgba)
        {
            int width = _width;
            int height = _height;
            Parallel.For(0, height / 2, pair =>
            {
                for (int half = 0; half < 2; half++)
                {
                    int row = pair * 2 + half;
                    int source = (height - 1 - row) * width * 4;
                    int destination = row * width;
                    for (int column = 0; column < width; column++)
                    {
                        int pixel = source + column * 4;
                        _y[destination + column] = Luma(rgba[pixel], rgba[pixel + 1], rgba[pixel + 2]);
                    }
                }

                int top = (height - 1 - pair * 2) * width * 4;
                int bottom = (height - 2 - pair * 2) * width * 4;
                int chroma = pair * (width / 2);
                for (int column = 0; column < width / 2; column++)
                {
                    int a = top + column * 8;
                    int b = bottom + column * 8;
                    int r = rgba[a] + rgba[a + 4] + rgba[b] + rgba[b + 4];
                    int g = rgba[a + 1] + rgba[a + 5] + rgba[b + 1] + rgba[b + 5];
                    int bl = rgba[a + 2] + rgba[a + 6] + rgba[b + 2] + rgba[b + 6];
                    _u[chroma + column] = Cb(r, g, bl);
                    _v[chroma + column] = Cr(r, g, bl);
                }
            });
        }

        // BT.709, limited range (16-235 luma, 16-240 chroma). Chroma inputs are sums of four pixels.
        private static byte Luma(int r, int g, int b) => (byte)(16 + ((47 * r + 157 * g + 16 * b + 128) >> 8));

        private static byte Cb(int r, int g, int b) => Clamp(128 + ((-26 * r - 87 * g + 112 * b + 512) >> 10));

        private static byte Cr(int r, int g, int b) => Clamp(128 + ((112 * r - 102 * g - 10 * b + 512) >> 10));

        private static byte Clamp(int value) => (byte)(value < 16 ? 16 : value > 240 ? 240 : value);
    }
}
