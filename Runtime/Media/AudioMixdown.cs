using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SimpleTools.Media
{
    // Offline mix of timed sounds into a stereo track, without the audio system: the result is the same whatever the
    // machine or the frame rate, so a recorded sequence can be exported faster or slower than real time.
    public static class AudioMixdown
    {
        public readonly struct Sound
        {
            public float[] Samples { get; }
            public int Channels { get; }
            public int SampleRate { get; }
            public float Time { get; }
            public float Volume { get; }

            public Sound(float[] samples, int channels, int sampleRate, float time, float volume)
            {
                Samples = samples;
                Channels = channels;
                SampleRate = sampleRate;
                Time = time;
                Volume = volume;
            }
        }

        // Interleaved stereo samples in [-1, 1]. A sound past the end is cut; one before the start loses its head.
        public static float[] Mix(IReadOnlyList<Sound> sounds, float duration, int sampleRate)
        {
            int frames = Mathf.CeilToInt(duration * sampleRate);
            var output = new float[frames * 2];
            foreach (Sound sound in sounds)
            {
                if (sound.Samples == null || sound.Channels <= 0 || sound.SampleRate <= 0)
                {
                    continue;
                }

                int sourceFrames = sound.Samples.Length / sound.Channels;
                double step = (double)sound.SampleRate / sampleRate;
                int start = Mathf.RoundToInt(sound.Time * sampleRate);
                int last = Mathf.Min(frames, start + (int)(sourceFrames / step));
                for (int frame = Mathf.Max(0, start); frame < last; frame++)
                {
                    double position = (frame - start) * step;
                    int first = (int)position;
                    int second = Mathf.Min(first + 1, sourceFrames - 1);
                    float blend = (float)(position - first);
                    int rightChannel = sound.Channels > 1 ? 1 : 0;
                    output[frame * 2] += sound.Volume * Mathf.Lerp(sound.Samples[first * sound.Channels],
                        sound.Samples[second * sound.Channels], blend);
                    output[frame * 2 + 1] += sound.Volume * Mathf.Lerp(sound.Samples[first * sound.Channels + rightChannel],
                        sound.Samples[second * sound.Channels + rightChannel], blend);
                }
            }

            for (int index = 0; index < output.Length; index++)
            {
                output[index] = Mathf.Clamp(output[index], -1f, 1f);
            }

            return output;
        }

        // 16-bit PCM WAV
        public static void WriteWav(Stream stream, float[] interleaved, int channels, int sampleRate)
        {
            using (var writer = new BinaryWriter(stream))
            {
                int dataLength = interleaved.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);
                foreach (float sample in interleaved)
                {
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
                }
            }
        }
    }
}
