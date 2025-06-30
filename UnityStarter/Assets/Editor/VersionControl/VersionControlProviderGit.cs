using System.Diagnostics;
using UnityEditor;

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

        public void UpdateVersionInfoAsset(string assetPath, string commitHash)
        {
            var versionInfoData = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (versionInfoData == null)
            {
                UnityEngine.Debug.LogError($"Could not find VersionInfoData asset at path: {assetPath}");
                return;
            }

            versionInfoData.commitHash = commitHash ?? "Unknown";
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
                versionInfoData.buildDate = string.Empty;
                EditorUtility.SetDirty(versionInfoData);
                AssetDatabase.SaveAssets();
            }
        }
    }
}