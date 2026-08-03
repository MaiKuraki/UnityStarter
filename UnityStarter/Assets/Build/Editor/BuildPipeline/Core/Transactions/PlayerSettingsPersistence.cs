using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    internal sealed class BuildPipelineAssetSaveFilter : AssetModificationProcessor
    {
        internal const string PlayerSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";

        private static bool saveScopeActive;
        private static bool sawPlayerSettings;
        private static int ownerThreadId;

        internal static void SaveOnlyPlayerSettings(PlayerSettings playerSettingsAsset)
        {
            if (playerSettingsAsset == null)
            {
                throw new ArgumentNullException(nameof(playerSettingsAsset));
            }

            if (saveScopeActive)
            {
                throw new InvalidOperationException(
                    "A targeted PlayerSettings save is already active.");
            }

            saveScopeActive = true;
            sawPlayerSettings = false;
            ownerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                EditorUtility.SetDirty(playerSettingsAsset);
                AssetDatabase.SaveAssets();
                if (!sawPlayerSettings)
                {
                    throw new IOException(
                        $"Unity did not offer '{PlayerSettingsAssetPath}' to the targeted save filter.");
                }

                EditorUtility.ClearDirty(playerSettingsAsset);
                if (EditorUtility.IsDirty(playerSettingsAsset))
                {
                    throw new IOException(
                        "PlayerSettings remained dirty after the targeted project save completed.");
                }
            }
            finally
            {
                ownerThreadId = 0;
                sawPlayerSettings = false;
                saveScopeActive = false;
            }
        }

        internal static string[] FilterPathsForTests(string[] paths, out bool foundPlayerSettings)
        {
            return FilterPaths(paths, PlayerSettingsAssetPath, out foundPlayerSettings);
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (!saveScopeActive)
            {
                return paths ?? Array.Empty<string>();
            }

            if (Environment.CurrentManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "The targeted PlayerSettings save callback moved away from its owning thread.");
            }

            string[] filtered = FilterPaths(paths, PlayerSettingsAssetPath, out bool found);
            sawPlayerSettings |= found;
            return filtered;
        }

        private static string[] FilterPaths(
            string[] paths,
            string allowedPath,
            out bool foundAllowedPath)
        {
            foundAllowedPath = false;
            if (paths == null || paths.Length == 0)
            {
                return Array.Empty<string>();
            }

            var filtered = new List<string>(1);
            for (int index = 0; index < paths.Length; index++)
            {
                string path = paths[index]?.Replace('\\', '/');
                if (!string.Equals(path, allowedPath, StringComparison.Ordinal))
                {
                    continue;
                }

                foundAllowedPath = true;
                if (filtered.Count == 0)
                {
                    filtered.Add(allowedPath);
                }
            }

            return filtered.Count == 0 ? Array.Empty<string>() : filtered.ToArray();
        }
    }
}
