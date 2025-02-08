using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CycloneGames.Editor.VersionControl
{
    public class VersionControlProviderGit : IVersionControlProvider
    {
        public string GetCommitHash()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Trim();
            }
        }

        public void RemoveVersionJson()
        {
            if (File.Exists(Path.Combine(Application.streamingAssetsPath, "VersionInfo.json")))
            {
                File.Delete(Path.Combine(Application.streamingAssetsPath, "VersionInfo.json"));
                UnityEngine.Debug.Log("Version information file removed.");
            }
        }

        public void SaveVersionToJson(string commitHash)
        {
            string finalCommitHash = commitHash ?? "Unknown";
            RemoveVersionJson();

            string directoryPath = Application.streamingAssetsPath;
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                UnityEngine.Debug.Log("Created directory: " + directoryPath);
            }

            string jsonFilePath = Path.Combine(directoryPath, "VersionInfo.json");

            var versionInfo = new
            {
                CommitHash = finalCommitHash,
                CreatedDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            string jsonContent = JsonConvert.SerializeObject(versionInfo, Formatting.Indented);
            File.WriteAllText(jsonFilePath, jsonContent);
            UnityEngine.Debug.Log("Version information saved to " + jsonFilePath);
        }
    }
}