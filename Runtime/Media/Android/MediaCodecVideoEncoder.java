package com.simpletools.media;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.Build;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;

// H.264 + AAC to MP4 with the phone's own encoders (MediaCodec, usually in hardware) and MediaMuxer.
// Used by SimpleTools.Media.AndroidVideoEncoder: frames arrive as I420 planes (Y full size, U and V half size),
// the audio as a 16-bit PCM WAV that is encoded first, so the muxer knows both tracks before it starts.
public final class MediaCodecVideoEncoder {
    private static final String TAG = "SimpleTools";
    private static final long TIMEOUT_US = 10000;
    private static final int AUDIO_BITRATE = 192000;

    private final String outputPath;
    private final int width;
    private final int height;
    private final int framesPerSecond;
    private final MediaCodec video;
    private final MediaMuxer muxer;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    private final List<byte[]> audioSamples = new ArrayList<>();
    private final List<long[]> audioTimes = new ArrayList<>();
    private MediaFormat audioFormat;
    private int videoTrack = -1;
    private int audioTrack = -1;
    private boolean started;
    private long frame;

    public MediaCodecVideoEncoder(String outputPath, int width, int height, int framesPerSecond, int bitrate, String wavPath)
            throws IOException {
        this.outputPath = outputPath;
        this.width = width;
        this.height = height;
        this.framesPerSecond = framesPerSecond;
        if (wavPath != null && !wavPath.isEmpty()) {
            encodeAudio(wavPath);
        }

        MediaFormat format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height);
        format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
        format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
        format.setInteger(MediaFormat.KEY_FRAME_RATE, framesPerSecond);
        format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
        if (Build.VERSION.SDK_INT >= 24) {
            format.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT709);
            format.setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_LIMITED);
            format.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        }

        video = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
        video.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        video.start();
        muxer = new MediaMuxer(outputPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
        if (audioFormat != null) {
            audioTrack = muxer.addTrack(audioFormat);
        }
    }

    public boolean encodeFrame(byte[] y, byte[] u, byte[] v) {
        try {
            int index;
            while ((index = video.dequeueInputBuffer(TIMEOUT_US)) < 0) {
                drain(false);
            }

            Image image = video.getInputImage(index);
            Image.Plane[] planes = image.getPlanes();
            copyPlane(planes[0], y, width, height);
            copyPlane(planes[1], u, width / 2, height / 2);
            copyPlane(planes[2], v, width / 2, height / 2);
            video.queueInputBuffer(index, 0, width * height * 3 / 2, frame * 1000000L / framesPerSecond, 0);
            frame++;
            drain(false);
            return true;
        } catch (RuntimeException exception) {
            Log.e(TAG, "MediaCodecVideoEncoder: frame " + frame + " failed", exception);
            return false;
        }
    }

    public boolean finish() {
        try {
            int index;
            while ((index = video.dequeueInputBuffer(TIMEOUT_US)) < 0) {
                drain(false);
            }

            video.queueInputBuffer(index, 0, 0, frame * 1000000L / framesPerSecond, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
            drain(true);
            release();
            return started;
        } catch (RuntimeException exception) {
            Log.e(TAG, "MediaCodecVideoEncoder: finish failed", exception);
            cancel();
            return false;
        }
    }

    public void cancel() {
        try {
            release();
        } catch (RuntimeException ignored) {
            // Already stopped
        }

        new File(outputPath).delete();
    }

    private void release() {
        video.stop();
        video.release();
        if (started) {
            muxer.stop();
        }

        muxer.release();
    }

    // Row by row when the pixels are contiguous, pixel by pixel when U and V are interleaved (semi-planar layouts)
    private static void copyPlane(Image.Plane plane, byte[] source, int planeWidth, int planeHeight) {
        ByteBuffer buffer = plane.getBuffer();
        int rowStride = plane.getRowStride();
        int pixelStride = plane.getPixelStride();
        for (int row = 0; row < planeHeight; row++) {
            int start = row * planeWidth;
            if (pixelStride == 1) {
                buffer.position(row * rowStride);
                buffer.put(source, start, planeWidth);
            } else {
                int base = row * rowStride;
                for (int column = 0; column < planeWidth; column++) {
                    buffer.put(base + column * pixelStride, source[start + column]);
                }
            }
        }
    }

    private void drain(boolean endOfStream) {
        while (true) {
            int index = video.dequeueOutputBuffer(info, endOfStream ? TIMEOUT_US : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) {
                    return;
                }

                continue;
            }

            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                videoTrack = muxer.addTrack(video.getOutputFormat());
                muxer.start();
                started = true;
                writeAudio();
                continue;
            }

            if (index < 0) {
                continue;
            }

            ByteBuffer data = video.getOutputBuffer(index);
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
                info.size = 0;
            }

            if (info.size > 0 && started && data != null) {
                data.position(info.offset);
                data.limit(info.offset + info.size);
                muxer.writeSampleData(videoTrack, data, info);
            }

            video.releaseOutputBuffer(index, false);
            if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                return;
            }
        }
    }

    private void writeAudio() {
        if (audioTrack < 0) {
            return;
        }

        MediaCodec.BufferInfo sample = new MediaCodec.BufferInfo();
        for (int index = 0; index < audioSamples.size(); index++) {
            byte[] bytes = audioSamples.get(index);
            long[] time = audioTimes.get(index);
            sample.set(0, bytes.length, time[0], (int) time[1]);
            muxer.writeSampleData(audioTrack, ByteBuffer.wrap(bytes), sample);
        }

        audioSamples.clear();
        audioTimes.clear();
    }

    // The whole PCM WAV to AAC in memory (a replay is a few seconds: some hundred kilobytes)
    private void encodeAudio(String wavPath) throws IOException {
        byte[] wav;
        try (FileInputStream input = new FileInputStream(wavPath)) {
            wav = new byte[(int) new File(wavPath).length()];
            int read = 0;
            while (read < wav.length) {
                int count = input.read(wav, read, wav.length - read);
                if (count < 0) {
                    break;
                }

                read += count;
            }
        }

        ByteBuffer header = ByteBuffer.wrap(wav).order(ByteOrder.LITTLE_ENDIAN);
        int channels = header.getShort(22);
        int sampleRate = header.getInt(24);
        int dataStart = 44;
        int bytesPerSecond = sampleRate * channels * 2;

        MediaFormat format = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, sampleRate, channels);
        format.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
        format.setInteger(MediaFormat.KEY_BIT_RATE, AUDIO_BITRATE);
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);
        MediaCodec audio = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
        audio.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        audio.start();
        MediaCodec.BufferInfo audioInfo = new MediaCodec.BufferInfo();
        int position = dataStart;
        boolean inputDone = false;
        while (true) {
            if (!inputDone) {
                int index = audio.dequeueInputBuffer(TIMEOUT_US);
                if (index >= 0) {
                    ByteBuffer input = audio.getInputBuffer(index);
                    long time = (long) (position - dataStart) * 1000000L / bytesPerSecond;
                    int count = Math.min(input.remaining(), wav.length - position);
                    // Whole samples on all channels
                    count -= count % (channels * 2);
                    if (count <= 0) {
                        audio.queueInputBuffer(index, 0, 0, time, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                        inputDone = true;
                    } else {
                        input.put(wav, position, count);
                        audio.queueInputBuffer(index, 0, count, time, 0);
                        position += count;
                    }
                }
            }

            int index = audio.dequeueOutputBuffer(audioInfo, TIMEOUT_US);
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                audioFormat = audio.getOutputFormat();
            } else if (index >= 0) {
                if ((audioInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0 && audioInfo.size > 0) {
                    ByteBuffer output = audio.getOutputBuffer(index);
                    byte[] bytes = new byte[audioInfo.size];
                    output.position(audioInfo.offset);
                    output.get(bytes);
                    audioSamples.add(bytes);
                    audioTimes.add(new long[] { audioInfo.presentationTimeUs, audioInfo.flags & ~MediaCodec.BUFFER_FLAG_END_OF_STREAM });
                }

                audio.releaseOutputBuffer(index, false);
                if ((audioInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                    break;
                }
            }
        }

        audio.stop();
        audio.release();
    }
}
