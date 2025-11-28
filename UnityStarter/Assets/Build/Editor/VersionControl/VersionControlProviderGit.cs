using System.Diagnostics;
using Build.Data;
using UnityEditor;
using UnityEngine;

namespace Build.VersionControl.Editor
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

        public string GetCommitCount()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-list --count HEAD",
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

        public void UpdateVersionInfoAsset(string assetPath, string commitHash, string commitCount)
        {
            var versionInfoData = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (versionInfoData == null)
            {
                UnityEngine.Debug.Log($"VersionInfoData asset not found at {assetPath}, creating a new one.");
                versionInfoData = ScriptableObject.CreateInstance<VersionInfoData>();

                string directory = System.IO.Path.GetDirectoryName(assetPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(versionInfoData, assetPath);
            }

            versionInfoData.commitHash = commitHash ?? "Unknown";
            versionInfoData.commitCount = commitCount ?? "0";
            versionInfoData.buildDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Mark the object as "dirty" so Unity knows it has changed
            EditorUtility.SetDirty(versionInfoData);
            // Save the changes to disk
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UnityEngine.Debug.Log($"Version information updated in asset: {assetPath}");
        }

        public void ClearVersionInfoAsset(string assetPath)
        {
            var versionInfoData = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (versionInfoData != null)
            {
                versionInfoData.commitHash = string.Empty;
                versionInfoData.commitCount = string.Empty;
                versionInfoData.buildDate = string.Empty;
                EditorUtility.SetDirty(versionInfoData);
                AssetDatabase.SaveAssets();
            }
        }
    }
}