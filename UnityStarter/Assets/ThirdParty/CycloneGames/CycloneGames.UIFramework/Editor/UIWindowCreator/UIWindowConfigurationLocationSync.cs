using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Keeps AssetReference-mode window configurations in sync when their tracked prefab moves or is renamed.
    /// The Editor GUID survives the move, so the provider runtime location is re-derived from the new path and
    /// written back without prompting. A location the author set by hand is preserved: it only differs from the
    /// value derived from the old path, so it is treated as an explicit override and left alone.
    /// </summary>
    internal sealed class UIWindowConfigurationLocationSync : AssetPostprocessor
    {
        private const string SourcePropertyName = "source";
        private const string PrefabAssetRefPropertyName = "prefabAssetRef";
        private const string LocationPropertyName = "location";
        private const string GuidPropertyName = "editorGuid";

        private readonly struct PrefabMove
        {
            public readonly string FromPath;
            public readonly string ToPath;

            public PrefabMove(string fromPath, string toPath)
            {
                FromPath = fromPath;
                ToPath = toPath;
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssetPaths,
            string[] deletedAssetPaths,
            string[] movedAssetPaths,
            string[] movedFromAssetPaths)
        {
            if (movedAssetPaths == null || movedAssetPaths.Length == 0)
            {
                return;
            }

            var movedPrefabs = new Dictionary<string, PrefabMove>(StringComparer.Ordinal);
            for (int i = 0; i < movedAssetPaths.Length; i++)
            {
                if (!movedAssetPaths[i].EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(movedAssetPaths[i]);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                string fromPath = i < movedFromAssetPaths.Length
                    ? movedFromAssetPaths[i]
                    : string.Empty;
                movedPrefabs[guid] = new PrefabMove(fromPath, movedAssetPaths[i]);
            }

            if (movedPrefabs.Count == 0)
            {
                return;
            }

            string[] configGuids = AssetDatabase.FindAssets("t:UIWindowConfiguration");
            bool anyChanged = false;
            for (int i = 0; i < configGuids.Length; i++)
            {
                string configPath = AssetDatabase.GUIDToAssetPath(configGuids[i]);
                if (string.IsNullOrEmpty(configPath))
                {
                    continue;
                }

                UIWindowConfiguration config =
                    AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(configPath);
                if (config == null)
                {
                    continue;
                }

                if (ResyncIfTracked(config, movedPrefabs))
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static bool ResyncIfTracked(
            UIWindowConfiguration config,
            Dictionary<string, PrefabMove> movedPrefabs)
        {
            SerializedObject serialized = new SerializedObject(config);
            try
            {
                SerializedProperty source = serialized.FindProperty(SourcePropertyName);
                if (source == null ||
                    (UIWindowConfiguration.PrefabSource)source.enumValueIndex !=
                    UIWindowConfiguration.PrefabSource.AssetReference)
                {
                    return false;
                }

                SerializedProperty assetRef = serialized.FindProperty(PrefabAssetRefPropertyName);
                if (assetRef == null)
                {
                    return false;
                }

                SerializedProperty guid = assetRef.FindPropertyRelative(GuidPropertyName);
                SerializedProperty location = assetRef.FindPropertyRelative(LocationPropertyName);
                if (guid == null || location == null || string.IsNullOrEmpty(guid.stringValue))
                {
                    return false;
                }

                if (!movedPrefabs.TryGetValue(guid.stringValue, out PrefabMove move))
                {
                    return false;
                }

                string current = location.stringValue ?? string.Empty;
                string fromAuto = string.IsNullOrEmpty(move.FromPath)
                    ? null
                    : UIWindowLocationResolverRegistry.Resolve(guid.stringValue, move.FromPath);
                string toAuto = UIWindowLocationResolverRegistry.Resolve(guid.stringValue, move.ToPath);

                // The config is still "following" the derived value when its location is empty or matches the
                // value derived from the pre-move path. An author-provided override differs from that value and
                // must not be silently replaced.
                bool wasAuto = string.IsNullOrEmpty(current) ||
                               (fromAuto != null && string.Equals(current, fromAuto, StringComparison.Ordinal));
                if (!wasAuto || string.IsNullOrEmpty(toAuto) ||
                    string.Equals(current, toAuto, StringComparison.Ordinal))
                {
                    return false;
                }

                location.stringValue = toAuto;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
                return true;
            }
            finally
            {
                serialized.Dispose();
            }
        }
    }
}
