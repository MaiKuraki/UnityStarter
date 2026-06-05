#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class LocalizationCatalogBuildOptions
    {
        public const string DefaultVersion = "1.0.0";

        public string OutputPath;
        public string Version = DefaultVersion;
        public bool ValidateBeforeBuild = true;
        public bool SelectBuiltAsset = true;
    }

    public readonly struct LocalizationCatalogBuildResult
    {
        public readonly LocalizationCatalog Catalog;
        public readonly string OutputPath;
        public readonly string ContentHash;
        public readonly int StringTableCount;
        public readonly int StringEntryCount;
        public readonly int AssetTableCount;
        public readonly int AssetEntryCount;

        public LocalizationCatalogBuildResult(
            LocalizationCatalog catalog,
            string outputPath,
            string contentHash,
            int stringTableCount,
            int stringEntryCount,
            int assetTableCount,
            int assetEntryCount)
        {
            Catalog = catalog;
            OutputPath = outputPath;
            ContentHash = contentHash;
            StringTableCount = stringTableCount;
            StringEntryCount = stringEntryCount;
            AssetTableCount = assetTableCount;
            AssetEntryCount = assetEntryCount;
        }
    }

    public static class LocalizationCatalogBuilder
    {
        private const string DefaultCatalogPath = "Assets/LocalizationCatalog.asset";

        [MenuItem("Tools/CycloneGames/Localization/Catalog/Build Once...")]
        public static void BuildCatalogFromMenu()
        {
            string outputPath = SelectOutputPath();
            if (string.IsNullOrEmpty(outputPath)) return;

            var options = new LocalizationCatalogBuildOptions
            {
                OutputPath = outputPath,
                Version = LocalizationCatalogBuildOptions.DefaultVersion,
                ValidateBeforeBuild = true,
                SelectBuiltAsset = true,
            };

            try
            {
                LocalizationCatalogBuildResult result = BuildCatalog(options);
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "Catalog build completed.\n\n" +
                    "Output: " + result.OutputPath + "\n" +
                    "String tables: " + result.StringTableCount + " (" + result.StringEntryCount + " entries)\n" +
                    "Asset tables: " + result.AssetTableCount + " (" + result.AssetEntryCount + " entries)\n" +
                    "Hash: " + result.ContentHash,
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "Catalog build failed. See Console for details.",
                    "OK");
            }
        }

        [MenuItem("Tools/CycloneGames/Localization/Catalog/Build From Settings")]
        public static void BuildCatalogFromSettingsMenu()
        {
            LocalizationCatalogBuildSettings settings = FindSettingsAsset();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "No LocalizationCatalogBuildSettings asset was found. Create one with Create > CycloneGames > Localization > Catalog Build Settings.",
                    "OK");
                return;
            }

            BuildCatalog(settings);
        }

        [MenuItem("Tools/CycloneGames/Localization/Catalog/Select Build Settings")]
        public static void SelectSettingsMenu()
        {
            LocalizationCatalogBuildSettings settings = FindSettingsAsset();
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "No LocalizationCatalogBuildSettings asset was found.",
                    "OK");
                return;
            }

            Selection.activeObject = settings;
        }

        public static LocalizationCatalog BuildCatalog(string outputPath, string version)
        {
            var options = new LocalizationCatalogBuildOptions
            {
                OutputPath = outputPath,
                Version = version,
            };

            return BuildCatalog(options).Catalog;
        }

        public static LocalizationCatalogBuildResult BuildCatalog(LocalizationCatalogBuildSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return BuildCatalog(settings.ToBuildOptions());
        }

        public static LocalizationCatalogBuildResult BuildCatalog(LocalizationCatalogBuildOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.OutputPath)) throw new ArgumentNullException(nameof(options.OutputPath));
            if (!options.OutputPath.StartsWith("Assets/", StringComparison.Ordinal) &&
                !string.Equals(options.OutputPath, "Assets", StringComparison.Ordinal))
            {
                throw new ArgumentException("Catalog output path must be inside the Assets folder.", nameof(options));
            }
            if (!options.OutputPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Catalog output path must use the .asset extension.", nameof(options));

            if (options.ValidateBeforeBuild)
                ValidateProjectOrThrow();

            var data = BuildCatalogData();
            string hash = ComputeContentHash(data.StringTables, data.AssetTables);
            EnsureParentFolders(options.OutputPath);

            var existingAsset = AssetDatabase.LoadMainAssetAtPath(options.OutputPath);
            if (existingAsset != null && !(existingAsset is LocalizationCatalog))
            {
                throw new InvalidOperationException(
                    "Catalog output path is already used by another asset type: " + options.OutputPath);
            }

            var catalog = existingAsset as LocalizationCatalog;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
                catalog.SetData(options.Version, hash, data.StringTables, data.AssetTables);
                AssetDatabase.CreateAsset(catalog, options.OutputPath);
            }
            else
            {
                catalog.SetData(options.Version, hash, data.StringTables, data.AssetTables);
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            if (options.SelectBuiltAsset)
                Selection.activeObject = catalog;

            var result = new LocalizationCatalogBuildResult(
                catalog,
                options.OutputPath,
                hash,
                data.StringTables.Count,
                data.StringEntryCount,
                data.AssetTables.Count,
                data.AssetEntryCount);

            Debug.Log(
                "[Localization] Catalog built: " + result.OutputPath +
                " (strings: " + result.StringTableCount + "/" + result.StringEntryCount +
                ", assets: " + result.AssetTableCount + "/" + result.AssetEntryCount +
                ", hash: " + result.ContentHash + ")",
                catalog);

            return result;
        }

        public static string ComputeContentHash(
            IReadOnlyList<CatalogStringTable> stringTables,
            IReadOnlyList<CatalogAssetTable> assetTables)
        {
            if (stringTables == null) throw new ArgumentNullException(nameof(stringTables));
            if (assetTables == null) throw new ArgumentNullException(nameof(assetTables));

            var builder = new StringBuilder(4096);
            builder.Append(LocalizationCatalog.CurrentSchemaVersion).Append('\n');

            for (int i = 0; i < stringTables.Count; i++)
            {
                var table = stringTables[i];
                if (table == null) continue;

                AppendValue(builder, "S");
                AppendValue(builder, table.TableId);
                AppendValue(builder, table.LocaleId.Code);

                var entries = table.Entries;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    AppendValue(builder, entry.Key);
                    AppendValue(builder, entry.Value);
                }
            }

            for (int i = 0; i < assetTables.Count; i++)
            {
                var table = assetTables[i];
                if (table == null) continue;

                AppendValue(builder, "A");
                AppendValue(builder, table.TableId);
                AppendValue(builder, table.LocaleId.Code);

                var entries = table.Entries;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    AppendValue(builder, entry.Key);
                    AppendValue(builder, entry.Asset.Location);
                    AppendValue(builder, entry.Asset.Guid);
                }
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
                byte[] hash = sha.ComputeHash(bytes);
                var hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        private static CatalogBuildData BuildCatalogData()
        {
            var data = new CatalogBuildData();
            AppendStringTables(data);
            AppendAssetTables(data);
            return data;
        }

        private static void AppendStringTables(CatalogBuildData data)
        {
            string[] guids = AssetDatabase.FindAssets("t:StringTable");
            Array.Sort(guids, CompareAssetGuidByPath);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null) continue;
                if (string.IsNullOrEmpty(table.TableId) || !table.LocaleId.IsValid) continue;

                var serialized = new SerializedObject(table);
                var entries = serialized.FindProperty("entries");
                var catalogEntries = new List<CatalogStringEntry>(entries != null && entries.isArray ? entries.arraySize : 0);
                if (entries != null && entries.isArray)
                {
                    for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
                    {
                        var entry = entries.GetArrayElementAtIndex(entryIndex);
                        var key = entry.FindPropertyRelative("Key");
                        if (key == null || string.IsNullOrEmpty(key.stringValue)) continue;

                        var value = entry.FindPropertyRelative("Value");
                        catalogEntries.Add(new CatalogStringEntry(key.stringValue, value != null ? value.stringValue : string.Empty));
                    }
                }

                data.StringEntryCount += catalogEntries.Count;
                data.StringTables.Add(new CatalogStringTable(table.TableId, table.LocaleId.Code, catalogEntries));
            }
        }

        private static void AppendAssetTables(CatalogBuildData data)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                if (table == null) continue;
                if (string.IsNullOrEmpty(table.TableId) || !table.LocaleId.IsValid) continue;

                var serialized = new SerializedObject(table);
                var entries = serialized.FindProperty("entries");
                var catalogEntries = new List<CatalogAssetEntry>(entries != null && entries.isArray ? entries.arraySize : 0);
                if (entries != null && entries.isArray)
                {
                    for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
                    {
                        var entry = entries.GetArrayElementAtIndex(entryIndex);
                        var key = entry.FindPropertyRelative("Key");
                        if (key == null || string.IsNullOrEmpty(key.stringValue)) continue;

                        var asset = entry.FindPropertyRelative("Asset");
                        var location = asset != null ? asset.FindPropertyRelative("m_Location") : null;
                        var guid = asset != null ? asset.FindPropertyRelative("m_GUID") : null;
                        catalogEntries.Add(new CatalogAssetEntry(
                            key.stringValue,
                            new AssetRef(
                                location != null ? location.stringValue : string.Empty,
                                guid != null ? guid.stringValue : string.Empty)));
                    }
                }

                data.AssetEntryCount += catalogEntries.Count;
                data.AssetTables.Add(new CatalogAssetTable(table.TableId, table.LocaleId.Code, catalogEntries));
            }
        }

        private static string SelectOutputPath()
        {
            return EditorUtility.SaveFilePanelInProject(
                "Build Localization Catalog",
                "LocalizationCatalog",
                "asset",
                "Select where the generated LocalizationCatalog asset should be saved.",
                "Assets");
        }

        internal static LocalizationCatalogBuildSettings FindSettingsAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:LocalizationCatalogBuildSettings");
            if (guids.Length == 0) return null;

            Array.Sort(guids, CompareAssetGuidByPath);
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<LocalizationCatalogBuildSettings>(path);
        }

        private static void ValidateProjectOrThrow()
        {
            var results = new List<LocalizationValidationResult>(128);
            LocalizationValidator.ValidateProject(results);
            int errors = CountResults(results, MessageType.Error);
            if (errors == 0) return;

            LogValidationResults(results);
            throw new InvalidOperationException(
                "Catalog build aborted because localization validation has " + errors + " error(s).");
        }

        private static void AppendValue(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(value.Length).Append(':').Append(value).Append('\n');
        }

        private static void EnsureParentFolders(string outputPath)
        {
            string[] parts = outputPath.Split('/');
            if (parts.Length <= 2) return;

            string current = parts[0];
            for (int i = 1; i < parts.Length - 1; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            string leftPath = AssetDatabase.GUIDToAssetPath(leftGuid);
            string rightPath = AssetDatabase.GUIDToAssetPath(rightGuid);
            return string.CompareOrdinal(leftPath, rightPath);
        }

        private static int CountResults(List<LocalizationValidationResult> results, MessageType type)
        {
            int count = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Type == type)
                    count++;
            }

            return count;
        }

        private static void LogValidationResults(List<LocalizationValidationResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result.Type == MessageType.Error)
                    Debug.LogError(result.Text, result.Context);
                else if (result.Type == MessageType.Warning)
                    Debug.LogWarning(result.Text, result.Context);
            }
        }

        private sealed class CatalogBuildData
        {
            public readonly List<CatalogStringTable> StringTables = new List<CatalogStringTable>(32);
            public readonly List<CatalogAssetTable> AssetTables = new List<CatalogAssetTable>(16);
            public int StringEntryCount;
            public int AssetEntryCount;
        }
    }
}
#endif
