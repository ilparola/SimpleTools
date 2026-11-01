using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SimpleTools.Steam.Editor
{
    // Copies steam_appid.txt from the project root next to the built executable: without it the game cannot
    // start Steam when launched outside the Steam client. On macOS it also goes inside the .app bundle,
    // which is the working directory when Steam launches the game. Not needed once the game ships on Steam.
    public sealed class SteamAppIdBuildCopy : IPostprocessBuildWithReport
    {
        private const string FileName = "steam_appid.txt";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string source = Path.Combine(Directory.GetCurrentDirectory(), FileName);
            if (!File.Exists(source))
            {
                Debug.LogWarning("SteamAppIdBuildCopy: missing " + source + ", the build will not find Steam.");
                return;
            }

            string output = report.summary.outputPath;
            File.Copy(source, Path.Combine(Path.GetDirectoryName(output), FileName), true);
            if (report.summary.platform == BuildTarget.StandaloneOSX)
            {
                File.Copy(source, Path.Combine(output, "Contents", "MacOS", FileName), true);
            }
        }
    }
}
