using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace SimpleTools.Media.Editor
{
    // Copies the ffmpeg binary and its license for the build target from Strumenti/ffmpeg/<mac|windows|linux> (kept out
    // of Assets, so that each build only carries its own) into the ffmpeg folder next to the game: inside the bundle on
    // macOS, where the bundle is signed again. Without that folder the build works, but video export is unavailable.
    public sealed class FfmpegBuildCopy : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10;

        public void OnPostprocessBuild(BuildReport report)
        {
            string platform = PlatformFolder(report.summary.platform);
            if (platform == null)
            {
                return;
            }

            string source = Path.Combine(Directory.GetCurrentDirectory(), "Strumenti", "ffmpeg", platform);
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"FfmpegBuildCopy: missing {source}, the build will not export videos.");
                return;
            }

            string output = report.summary.outputPath;
            bool mac = report.summary.platform == BuildTarget.StandaloneOSX;
            string target = mac
                ? Path.Combine(output, "Contents", "Resources", "ffmpeg")
                : Path.Combine(Path.GetDirectoryName(output), "ffmpeg");
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }

            if (mac)
            {
                Run("codesign", $"--force --deep --sign - \"{output}\"");
            }
        }

        private static string PlatformFolder(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX: return "mac";
                case BuildTarget.StandaloneWindows64: return "windows";
                case BuildTarget.StandaloneLinux64: return "linux";
                default: return null;
            }
        }

        private static void Run(string command, string arguments)
        {
            var info = new ProcessStartInfo(command, arguments) { UseShellExecute = false, RedirectStandardError = true };
            using (Process process = Process.Start(info))
            {
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Debug.LogError($"FfmpegBuildCopy: {command} failed: {errors}");
                }
            }
        }
    }
}
