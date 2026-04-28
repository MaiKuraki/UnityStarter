using System;
using System.IO;
using Build.Data;
using UnityEditor;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public abstract class VersionControlProviderBase : IVersionControlProvider
    {
        public abstract string GetCommitHash();
        public abstract string GetCommitCount();
        public abstract string GetBranchName();
        public abstract string GetCommitDate();

        public void UpdateVersionInfoAsset(string assetPath, string commitHash, string commitCount, string commitBranch, string commitDate)
        {
            var versionInfoData = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (versionInfoData == null)
            {
                Debug.Log($"[VC] VersionInfoData asset not found at {assetPath}, creating a new one.");
                versionInfoData = ScriptableObject.CreateInstance<VersionInfoData>();

                string directory = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(versionInfoData, assetPath);
            }

            versionInfoData.commitHash = commitHash ?? "Unknown";
            versionInfoData.commitCount = commitCount ?? "0";
            versionInfoData.commitBranch = commitBranch ?? "Unknown";
            versionInfoData.commitDate = commitDate ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            versionInfoData.buildDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            EditorUtility.SetDirty(versionInfoData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VC] Version information updated: {commitBranch}@{commitHash} (#{commitCount})");
        }

        public void ClearVersionInfoAsset(string assetPath)
        {
            var versionInfoData = AssetDatabase.LoadAssetAtPath<VersionInfoData>(assetPath);
            if (versionInfoData != null)
            {
                versionInfoData.commitHash = string.Empty;
                versionInfoData.commitCount = string.Empty;
                versionInfoData.commitBranch = string.Empty;
                versionInfoData.commitDate = string.Empty;
                versionInfoData.buildDate = string.Empty;
                EditorUtility.SetDirty(versionInfoData);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
