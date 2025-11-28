using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// This script moves specified files and folders outside the project directory before a build starts,
    /// and restores them after the build is complete.
    /// </summary>
    public class BuildProcessorMoveFiles : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // ===================================================================================
        // CONFIGURATION
        // ===================================================================================

        /// <summary>
        /// Add the project-relative paths of files or folders you want to exclude from the build.
        /// Paths must start with "Assets/".
        /// Examples: "Assets/_Developer", "Assets/StreamingAssets/someFile.txt"
        /// </summary>
        private static readonly string[] pathsToMove = new string[]
        {
            "Assets/_Developer",
            // "Assets/Art/SourceFiles", // Example: Source files for art assets
            // "Assets/StreamingAssets/SecretBuildInfo.txt" // Example: A specific file
        };

        // ===================================================================================
        // IMPLEMENTATION
        // ===================================================================================

        private const string tempFolderName = "TempBuildFiles";
        private static readonly Dictionary<string, string> movedAssetOriginalPaths = new Dictionary<string, string>();

        /// <summary>
        /// The order in which this build processor should be executed. 0 is the default.
        /// </summary>
        public int callbackOrder => 0;

        /// <summary>
        /// Gets the full path to the temporary folder, located in the project's parent directory.
        /// </summary>
        private string GetTempFolderPath()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, tempFolderName);
        }

        /// <summary>
        /// Called immediately before the build process begins.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("<b>[BuildProcessor]</b> Starting pre-build process. Moving specified files to a temporary location.");
            movedAssetOriginalPaths.Clear();

            string tempFolderPath = GetTempFolderPath();

            // If the temp folder exists from a previous failed build, delete it.
            if (Directory.Exists(tempFolderPath))
            {
                Debug.LogWarning($"<b>[BuildProcessor]</b> Temporary folder '{tempFolderPath}' already exists and will be deleted.");
                Directory.Delete(tempFolderPath, true);
            }

            Directory.CreateDirectory(tempFolderPath);

            foreach (var path in pathsToMove)
            {
                string sourcePath = Path.GetFullPath(path);
                string metaPath = sourcePath + ".meta";

                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    Debug.LogWarning($"<b>[BuildProcessor]</b> Path not found, skipping: {path}");
                    continue;
                }

                string destinationPath = Path.Combine(tempFolderPath, Path.GetFileName(path));

                try
                {
                    // Move the file or directory.
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, destinationPath);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        File.Move(sourcePath, destinationPath);
                    }
                    movedAssetOriginalPaths.Add(destinationPath, sourcePath);
                    Debug.Log($"<b>[BuildProcessor]</b> Moved: {path} -> {destinationPath}");

                    // Also move the corresponding .meta file.
                    if (File.Exists(metaPath))
                    {
                        string destMetaPath = destinationPath + ".meta";
                        File.Move(metaPath, destMetaPath);
                        movedAssetOriginalPaths.Add(destMetaPath, metaPath);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"<b>[BuildProcessor]</b> Failed to move '{path}'. Error: {e.Message}");
                    // If an error occurs, restore any files that were already moved and then cancel the build.
                    RestoreFiles();
                    throw new BuildFailedException($"[BuildProcessor] Failed to move files, canceling build. See console for details.");
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("<b>[BuildProcessor]</b> Finished moving files.");
        }

        /// <summary>
        /// Called after the build has completed.
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            Debug.Log("<b>[BuildProcessor]</b> Starting post-build process. Restoring moved files.");
            RestoreFiles();
            AssetDatabase.Refresh();
            Debug.Log("<b>[BuildProcessor]</b> Finished restoring files.");
        }

        /// <summary>
        /// Restores all moved files back to their original locations.
        /// </summary>
        private void RestoreFiles()
        {
            if (movedAssetOriginalPaths.Count == 0)
            {
                return;
            }

            foreach (var entry in movedAssetOriginalPaths)
            {
                string tempPath = entry.Key;
                string originalPath = entry.Value;

                try
                {
                    // Ensure the parent directory for the original path exists before restoring.
                    string parentDir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    // Move the file or directory back to its original location.
                    if (File.Exists(tempPath) || Directory.Exists(tempPath))
                    {
                        // Directory.Move works for both files and directories, simplifying the logic.
                        Directory.Move(tempPath, originalPath);
                        Debug.Log($"<b>[BuildProcessor]</b> Restored: {tempPath} -> {originalPath}");
                    }
                }
                catch (System.Exception e)
                {
                    // A failure here is critical and could lead to data loss, so we show a strong error message.
                    Debug.LogError($"<b>[BuildProcessor]</b> Failed to restore '{originalPath}'. Error: {e.Message}");
                    Debug.LogError($"<b>!!! IMPORTANT !!!</b> To prevent data loss, please manually move the contents of '{GetTempFolderPath()}' back into your project's Assets folder.");
                }
            }

            // Clean up by deleting the temporary folder.
            string tempFolderPath = GetTempFolderPath();
            try
            {
                if (Directory.Exists(tempFolderPath))
                {
                    Directory.Delete(tempFolderPath, true);
                    Debug.Log($"<b>[BuildProcessor]</b> Deleted temporary folder: {tempFolderPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"<b>[BuildProcessor]</b> Failed to delete temporary folder '{tempFolderPath}'. You may need to delete it manually. Error: {e.Message}");
            }

            movedAssetOriginalPaths.Clear();
        }
    }
}