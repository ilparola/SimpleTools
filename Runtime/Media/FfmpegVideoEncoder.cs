using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimpleTools.Media
{
    // Encodes raw frames to MP4 by piping them to a separate ffmpeg executable. Being a separate process it does not
    // link with the game, which keeps the LGPL obligations to shipping the binary, its license and its sources.
    public sealed class FfmpegVideoEncoder : IDisposable
    {
        private const int QueuedFrames = 6;
        private const int ProbeTimeoutMilliseconds = 8000;

        // Best first. Hardware and OS encoders need no GPL code; libx264 only works with a user's own GPL ffmpeg.
        private static readonly string[] Candidates =
        {
            "h264_videotoolbox", "h264_nvenc", "h264_amf", "h264_qsv", "h264_mf", "h264_vaapi",
            "libopenh264", "libx264", "mpeg4"
        };

        private static readonly Dictionary<string, string> ProbedEncoders = new Dictionary<string, string>();

        private readonly Process _process;
        private readonly BlockingCollection<byte[]> _frames = new BlockingCollection<byte[]>(QueuedFrames);
        private readonly ConcurrentBag<byte[]> _freeFrames = new ConcurrentBag<byte[]>();
        private readonly Thread _writer;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly string _outputPath;
        private volatile bool _writeFailed;

        public string Log
        {
            get
            {
                lock (_log)
                {
                    return _log.ToString();
                }
            }
        }

        // ffmpeg next to the game in the given folders, then on the PATH and in the usual install locations
        public static string FindExecutable(IEnumerable<string> folders)
        {
            string name = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? "ffmpeg.exe"
                : "ffmpeg";
            var search = new List<string>(folders);
            search.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));
            search.Add("/opt/homebrew/bin");
            search.Add("/usr/local/bin");
            search.Add("/usr/bin");
            foreach (string folder in search)
            {
                if (string.IsNullOrEmpty(folder))
                {
                    continue;
                }

                string path = Path.Combine(folder, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        // First encoder that really works on this machine: being compiled in is not enough (nvenc without an NVIDIA
        // card, for example). Blocking, a few seconds at worst: call it off the main thread. Null if none works.
        public static string FindH264Encoder(string executable)
        {
            lock (ProbedEncoders)
            {
                if (ProbedEncoders.TryGetValue(executable, out string known))
                {
                    return known;
                }
            }

            string found = null;
            foreach (string candidate in Candidates)
            {
                if (Probe(executable, candidate))
                {
                    found = candidate;
                    break;
                }
            }

            lock (ProbedEncoders)
            {
                ProbedEncoders[executable] = found;
            }

            return found;
        }

        // Starts ffmpeg. Frames are RGBA32; flipVertically for textures read bottom-up. Width and height must be even.
        public FfmpegVideoEncoder(string executable, string encoder, int width, int height, int framesPerSecond, int bitrateMegabits,
            string audioPath, string outputPath, bool flipVertically)
        {
            _outputPath = outputPath;
            var arguments = new StringBuilder("-hide_banner -loglevel warning -y ");
            if (encoder == "h264_vaapi")
            {
                arguments.Append("-vaapi_device /dev/dri/renderD128 ");
            }

            arguments.Append($"-f rawvideo -pix_fmt rgba -s {width}x{height} -r {framesPerSecond} -i pipe:0 ");
            if (!string.IsNullOrEmpty(audioPath))
            {
                arguments.Append($"-i {Quote(audioPath)} ");
            }

            string flip = flipVertically ? "vflip," : string.Empty;
            string pixels = encoder == "h264_vaapi" ? "format=nv12,hwupload" : "format=yuv420p";
            arguments.Append($"-vf \"{flip}scale=out_color_matrix=bt709:out_range=tv,{pixels}\" ");
            arguments.Append("-colorspace bt709 -color_primaries bt709 -color_trc bt709 ");
            arguments.Append(EncoderOptions(encoder, bitrateMegabits)).Append(' ');
            if (!string.IsNullOrEmpty(audioPath))
            {
                arguments.Append("-c:a aac -b:a 192k -shortest ");
            }

            arguments.Append($"-movflags +faststart {Quote(outputPath)}");
            _process = Start(executable, arguments.ToString(), true);
            _process.ErrorDataReceived += (sender, data) =>
            {
                if (data.Data != null)
                {
                    lock (_log)
                    {
                        _log.AppendLine(data.Data);
                    }
                }
            };
            _process.BeginErrorReadLine();
            _writer = new Thread(WriteFrames) { IsBackground = true, Name = "FfmpegVideoEncoder" };
            _writer.Start();
        }

        // A frame buffer to fill and pass to WriteFrame: reusing them spares a large allocation per frame
        public byte[] RentFrame(int length)
        {
            return _freeFrames.TryTake(out byte[] frame) && frame.Length == length ? frame : new byte[length];
        }

        // Blocks while ffmpeg is behind. False if ffmpeg stopped accepting frames.
        public bool WriteFrame(byte[] rgba)
        {
            if (_writeFailed)
            {
                return false;
            }

            _frames.Add(rgba);
            return true;
        }

        // Waits for the end of the encoding. Blocking: call it off the main thread.
        public bool Finish()
        {
            _frames.CompleteAdding();
            _writer.Join();
            _process.WaitForExit();
            bool success = !_writeFailed && _process.ExitCode == 0 && File.Exists(_outputPath);
            if (!success)
            {
                Debug.LogError($"FfmpegVideoEncoder: exit code {_process.ExitCode}\n{Log}");
            }

            return success;
        }

        // Stops ffmpeg and removes the partial file
        public void Cancel()
        {
            _frames.CompleteAdding();
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }

                _process.WaitForExit();
                _writer.Join();
                if (File.Exists(_outputPath))
                {
                    File.Delete(_outputPath);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                Debug.LogWarning("FfmpegVideoEncoder: " + exception.Message);
            }
        }

        public void Dispose()
        {
            _frames.Dispose();
            _process.Dispose();
        }

        private void WriteFrames()
        {
            Stream input = _process.StandardInput.BaseStream;
            try
            {
                foreach (byte[] frame in _frames.GetConsumingEnumerable())
                {
                    if (!_writeFailed)
                    {
                        input.Write(frame, 0, frame.Length);
                        _freeFrames.Add(frame);
                    }
                }

                input.Flush();
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
            {
                // ffmpeg has gone: keep draining, so that WriteFrame never blocks on a full queue
                _writeFailed = true;
                foreach (byte[] _ in _frames.GetConsumingEnumerable())
                {
                }
            }
            finally
            {
                try
                {
                    input.Close();
                }
                catch (IOException)
                {
                    _writeFailed = true;
                }
            }
        }

        private static bool Probe(string executable, string encoder)
        {
            string device = encoder == "h264_vaapi" ? "-vaapi_device /dev/dri/renderD128 " : string.Empty;
            string filter = encoder == "h264_vaapi" ? "-vf format=nv12,hwupload " : "-pix_fmt yuv420p ";
            string arguments = $"-hide_banner -loglevel error {device}-f lavfi -i color=c=black:s=640x360:r=30:d=0.1 " +
                               $"{filter}{EncoderOptions(encoder, 4)} -f null -";
            try
            {
                using (Process process = Start(executable, arguments, false))
                {
                    process.ErrorDataReceived += (sender, data) => { };
                    process.BeginErrorReadLine();
                    if (process.WaitForExit(ProbeTimeoutMilliseconds))
                    {
                        return process.ExitCode == 0;
                    }

                    process.Kill();
                    return false;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            {
                Debug.LogWarning($"FfmpegVideoEncoder: cannot run {executable}: {exception.Message}");
                return false;
            }
        }

        private static string EncoderOptions(string encoder, int bitrateMegabits)
        {
            string bitrate = $"-b:v {bitrateMegabits}M";
            switch (encoder)
            {
                case "h264_videotoolbox": return $"-c:v h264_videotoolbox -profile:v high {bitrate}";
                case "h264_nvenc": return $"-c:v h264_nvenc -preset p5 {bitrate}";
                case "libx264": return $"-c:v libx264 -preset fast {bitrate}";
                // Part 2 video needs a far higher rate for the same picture
                case "mpeg4": return $"-c:v mpeg4 -b:v {bitrateMegabits * 2}M";
                default: return $"-c:v {encoder} {bitrate}";
            }
        }

        private static Process Start(string executable, string arguments, bool redirectInput)
        {
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput,
                RedirectStandardError = true
            };
            return Process.Start(info);
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }
    }
}
