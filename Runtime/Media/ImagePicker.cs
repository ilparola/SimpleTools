using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimpleTools.Media
{
    /// <summary>
    /// Lets the player pick an image file with the system dialog: the Editor file panel, the macOS "choose file" dialog
    /// (osascript), the Windows open dialog (PowerShell), zenity or kdialog on Linux, and the system picker on Android
    /// (the image is copied into the cache, since content URIs are not files). Returns the path, or null if the player
    /// cancelled or no dialog is available. Await it from the main thread: Unity resumes there.
    /// </summary>
    public static class ImagePicker
    {
        public static bool IsSupported =>
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_ANDROID
            true;
#else
            false;
#endif

        public static Task<string> PickAsync(string title)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "",
                new[] { "Images", "png,jpg,jpeg" });
            return Task.FromResult(string.IsNullOrEmpty(path) ? null : path);
#elif UNITY_ANDROID
            return PickAndroid();
#elif UNITY_STANDALONE_OSX
            string script = "POSIX path of (choose file of type {\"public.png\", \"public.jpeg\"} with prompt \"" +
                            title.Replace("\"", "'") + "\")";
            return Task.Run(() => Run("/usr/bin/osascript", "-e " + Quote(script)));
#elif UNITY_STANDALONE_WIN
            string command = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Add-Type -AssemblyName System.Windows.Forms; " +
                             "$d=New-Object System.Windows.Forms.OpenFileDialog; $d.Title='" + title.Replace("'", "''") + "'; " +
                             "$d.Filter='Images|*.png;*.jpg;*.jpeg'; if($d.ShowDialog() -eq 'OK'){ $d.FileName }";
            return Task.Run(() => Run("powershell.exe", "-NoProfile -STA -Command " + Quote(command)));
#elif UNITY_STANDALONE_LINUX
            return Task.Run(() =>
                Run("zenity", "--file-selection --title=" + Quote(title) + " --file-filter=" + Quote("*.png *.jpg *.jpeg")) ??
                Run("kdialog", "--getopenfilename . " + Quote("*.png *.jpg *.jpeg") + " --title " + Quote(title)));
#else
            return Task.FromResult<string>(null);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidCallback : AndroidJavaProxy
        {
            private readonly TaskCompletionSource<string> _result;

            public AndroidCallback(TaskCompletionSource<string> result)
                : base("com.simpletools.media.ImagePicker$Callback")
            {
                _result = result;
            }

            // Called on the Android UI thread; the awaiting code resumes on Unity's main thread
            public void onPicked(string path)
            {
                _result.TrySetResult(string.IsNullOrEmpty(path) ? null : path);
            }
        }

        private static Task<string> PickAndroid()
        {
            var result = new TaskCompletionSource<string>();
            string destination = Path.Combine(Application.temporaryCachePath, "picked-image");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var picker = new AndroidJavaClass("com.simpletools.media.ImagePicker"))
            {
                picker.CallStatic("pick", activity, destination, new AndroidCallback(result));
            }

            return result.Task;
        }
#endif

        private static string Quote(string value)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return "\"" + value.Replace("\"", "\\\"") + "\"";
#else
            return "'" + value.Replace("'", "'\\''") + "'";
#endif
        }

        // Runs the dialog process and returns the first line it prints, or null (cancelled, or the program is missing)
        private static string Run(string program, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(program, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    string path = output.Trim();
                    return process.ExitCode == 0 && path.Length > 0 && File.Exists(path) ? path : null;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"ImagePicker: {program} not available ({exception.Message})");
                return null;
            }
        }
    }
}
