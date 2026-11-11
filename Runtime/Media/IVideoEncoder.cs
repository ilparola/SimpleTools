using System;

namespace SimpleTools.Media
{
    /// <summary>
    /// An MP4 (H.264 + AAC) encoder fed frame by frame: ffmpeg on desktop (<see cref="FfmpegVideoEncoder"/>), the phone's
    /// MediaCodec on Android (<see cref="AndroidVideoEncoder"/>). Frames are RGBA32, rows bottom-up as ReadPixels gives them.
    /// </summary>
    public interface IVideoEncoder : IDisposable
    {
        /// <summary>A frame buffer to fill and pass to <see cref="WriteFrame"/> (reused to spare allocations).</summary>
        byte[] RentFrame(int length);

        /// <summary>False if the encoder stopped accepting frames.</summary>
        bool WriteFrame(byte[] rgba);

        /// <summary>Completes the file. May block for a while.</summary>
        bool Finish();

        /// <summary>Stops and removes the partial file.</summary>
        void Cancel();

        /// <summary>True when <see cref="Finish"/> may run on a worker thread.</summary>
        bool FinishesOffMainThread { get; }
    }
}
