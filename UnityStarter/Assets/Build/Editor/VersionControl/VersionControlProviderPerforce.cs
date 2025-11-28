using System.Diagnostics;
using Build.Data;
using UnityEditor;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public class VersionControlProviderPerforce : IVersionControlProvider
    {
        public string GetCommitHash()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "p4",    // Ensure 'p4' is in your PATH
                Arguments = "changes -m 1 #have", // Get the latest changelist number
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                //  TODO: This implementation has not been checked
                //  Example output: Change 12345 on 2023/01/01 by user@workspace 'description'
                //  We need to parse the changelist number from this string.
                if (!string.IsNullOrEmpty(output))
                {
                    string[] parts = output.Split(' ');
                    if (parts.Length > 1)
                    {
                        return parts[1]; // Should be the changelist number
                    }
                }
                return "0";
            }
        }

        public string GetCommitCount()
        {
            // Perforce doesn't have a direct equivalent of a global commit count.
            // The changelist number from GetCommitHash is often used as the build number.
            // Returning the changelist number again, or a placeholder.
            // For simplicity, we can return the changelist number from GetCommitHash or "0".
            // Let's return "0" as a safe default, assuming GetCommitHash provides the primary identifier.
            return "0"; // Placeholder
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

            versionInfoData.commitHash = commitHash ?? "Unknown"; // This will be the changelist number
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