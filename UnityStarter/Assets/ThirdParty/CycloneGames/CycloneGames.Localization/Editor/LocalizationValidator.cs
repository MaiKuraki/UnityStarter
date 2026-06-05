#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Localization.Editor
{
    public readonly struct LocalizationValidationResult
    {
        public readonly string Text;
        public readonly MessageType Type;
        public readonly Object Context;

        public LocalizationValidationResult(string text, MessageType type, Object context)
        {
            Text = text;
            Type = type;
            Context = context;
        }
    }

    public static class LocalizationValidator
    {
        private static readonly string[] s_pluralSuffixes =
        {
            ".zero",
            ".one",
            ".two",
            ".few",
            ".many",
            ".other",
        };

        public static void ValidateProject(List<LocalizationValidationResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            var context = new ValidationContext(results);
            context.Scan();
        }

        [MenuItem("Tools/CycloneGames/Localization/Validation/Validate Project")]
        public static void ValidateProjectFromMenu()
        {
            var results = new List<LocalizationValidationResult>(128);
            ValidateProject(results);

            int errors = CountResults(results, MessageType.Error);
            int warnings = CountResults(results, MessageType.Warning);

            if (errors == 0 && warnings == 0)
            {
                Debug.Log("[Localization] Validation passed.");
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result.Type == MessageType.Error)
                    Debug.LogError(result.Text, result.Context);
                else if (result.Type == MessageType.Warning)
                    Debug.LogWarning(result.Text, result.Context);
            }

            Debug.Log("[Localization] Validation finished. Errors: " + errors + ", Warnings: " + warnings);
        }

        public static void ValidateProjectForBatchMode()
        {
            var results = new List<LocalizationValidationResult>(128);
            ValidateProject(results);

            int errors = CountResults(results, MessageType.Error);
            int warnings = CountResults(results, MessageType.Warning);

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result.Type == MessageType.Error)
                    Debug.LogError(result.Text, result.Context);
                else if (result.Type == MessageType.Warning)
                    Debug.LogWarning(result.Text, result.Context);
                else
                    Debug.Log(result.Text, result.Context);
            }

            Debug.Log("[Localization] Batch validation finished. Errors: " + errors + ", Warnings: " + warnings);

            if (Application.isBatchMode)
                EditorApplication.Exit(errors > 0 ? 1 : 0);
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

        private sealed class ValidationContext
        {
            private readonly List<LocalizationValidationResult> _results;
            private readonly HashSet<string> _keySet = new HashSet<string>(128, StringComparer.Ordinal);
            private readonly HashSet<string> _pluralBaseKeys = new HashSet<string>(32, StringComparer.Ordinal);
            private readonly Dictionary<string, TableScan> _stringTables = new Dictionary<string, TableScan>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, TableScan> _assetTables = new Dictionary<string, TableScan>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, StringTableMetadata> _stringMetadata = new Dictionary<string, StringTableMetadata>(16, StringComparer.Ordinal);

            public ValidationContext(List<LocalizationValidationResult> results)
            {
                _results = results;
            }

            public void Scan()
            {
                _results.Clear();
                _stringTables.Clear();
                _assetTables.Clear();
                _stringMetadata.Clear();

                ScanMetadata();
                ScanStringTables();
                ScanAssetTables();
                ScanLocales();
                ValidateTableCompleteness(_stringTables, "StringTable");
                ValidateTableCompleteness(_assetTables, "AssetTable");
            }

            private void ScanStringTables()
            {
                string[] guids = AssetDatabase.FindAssets("t:StringTable");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                    if (table == null) continue;

                    ValidateTableHeader(table.TableId, table.LocaleId.IsValid, "StringTable", table);
                    ValidateStringEntries(table);
                }
            }

            private void ScanAssetTables()
            {
                string[] guids = AssetDatabase.FindAssets("t:AssetTable");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                    if (table == null) continue;

                    ValidateTableHeader(table.TableId, table.LocaleId.IsValid, "AssetTable", table);
                    ValidateAssetEntries(table);
                }
            }

            private void ScanMetadata()
            {
                string[] guids = AssetDatabase.FindAssets("t:StringTableMetadata");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var metadata = AssetDatabase.LoadAssetAtPath<StringTableMetadata>(path);
                    if (metadata == null) continue;

                    if (string.IsNullOrEmpty(metadata.TableId))
                        Add("StringTableMetadata has empty tableId: " + path, MessageType.Error, metadata);

                    ValidateMetadataEntries(metadata, path);

                    if (metadata.TableType == TableType.String && !string.IsNullOrEmpty(metadata.TableId))
                        _stringMetadata[metadata.TableId] = metadata;
                }
            }

            private void ScanLocales()
            {
                string[] guids = AssetDatabase.FindAssets("t:Locale");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
                    if (locale == null) continue;

                    if (!locale.Id.IsValid)
                        Add("Locale has empty localeCode: " + path, MessageType.Error, locale);

                    ValidateLocaleFallbacks(locale, path);
                }
            }

            private void ValidateTableHeader(string tableId, bool localeValid, string typeName, Object context)
            {
                string path = AssetDatabase.GetAssetPath(context);
                if (string.IsNullOrEmpty(tableId))
                    Add(typeName + " has empty tableId: " + path, MessageType.Error, context);
                if (!localeValid)
                    Add(typeName + " has empty localeCode: " + path, MessageType.Error, context);
            }

            private void ValidateStringEntries(StringTable table)
            {
                _keySet.Clear();
                _pluralBaseKeys.Clear();
                var serialized = new SerializedObject(table);
                var entries = serialized.FindProperty("entries");
                if (entries == null || !entries.isArray) return;

                string path = AssetDatabase.GetAssetPath(table);
                TableScan scan = GetTableScan(_stringTables, table.TableId, table);
                HashSet<string> localeKeys = null;
                if (scan != null && table.LocaleId.IsValid)
                    localeKeys = scan.GetLocaleKeys(table.LocaleId.Code);

                for (int i = 0; i < entries.arraySize; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    var keyProperty = entry.FindPropertyRelative("Key");
                    string key = keyProperty != null ? keyProperty.stringValue : null;

                    if (string.IsNullOrEmpty(key))
                    {
                        Add("StringTable has empty key at index " + i + ": " + path, MessageType.Error, table);
                        continue;
                    }

                    if (!_keySet.Add(key))
                        Add("StringTable has duplicate key '" + key + "': " + path, MessageType.Error, table);

                    localeKeys?.Add(key);
                    scan?.AllKeys.Add(key);
                    TrackPluralBaseKey(key);

                    var valueProperty = entry.FindPropertyRelative("Value");
                    ValidateMaxLength(table, path, key, valueProperty != null ? valueProperty.stringValue : null);
                }

                ValidatePluralOtherKeys(table, path);
            }

            private void ValidateAssetEntries(AssetTable table)
            {
                _keySet.Clear();
                var serialized = new SerializedObject(table);
                var entries = serialized.FindProperty("entries");
                if (entries == null || !entries.isArray) return;

                string path = AssetDatabase.GetAssetPath(table);
                TableScan scan = GetTableScan(_assetTables, table.TableId, table);
                HashSet<string> localeKeys = null;
                if (scan != null && table.LocaleId.IsValid)
                    localeKeys = scan.GetLocaleKeys(table.LocaleId.Code);

                for (int i = 0; i < entries.arraySize; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    var keyProperty = entry.FindPropertyRelative("Key");
                    string key = keyProperty != null ? keyProperty.stringValue : null;

                    if (string.IsNullOrEmpty(key))
                    {
                        Add("AssetTable has empty key at index " + i + ": " + path, MessageType.Error, table);
                        continue;
                    }

                    if (!_keySet.Add(key))
                        Add("AssetTable has duplicate key '" + key + "': " + path, MessageType.Error, table);

                    localeKeys?.Add(key);
                    scan?.AllKeys.Add(key);

                    var assetProperty = entry.FindPropertyRelative("Asset");
                    var locationProperty = assetProperty != null ? assetProperty.FindPropertyRelative("m_Location") : null;
                    if (locationProperty == null || string.IsNullOrEmpty(locationProperty.stringValue))
                        Add("AssetTable key '" + key + "' has empty AssetRef: " + path, MessageType.Warning, table);
                }
            }

            private void ValidateMetadataEntries(StringTableMetadata metadata, string path)
            {
                _keySet.Clear();
                var serialized = new SerializedObject(metadata);
                var entries = serialized.FindProperty("entries");
                if (entries == null || !entries.isArray) return;

                for (int i = 0; i < entries.arraySize; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    var keyProperty = entry.FindPropertyRelative("Key");
                    string key = keyProperty != null ? keyProperty.stringValue : null;

                    if (string.IsNullOrEmpty(key))
                    {
                        Add("StringTableMetadata has empty key at index " + i + ": " + path, MessageType.Warning, metadata);
                        continue;
                    }

                    if (!_keySet.Add(key))
                        Add("StringTableMetadata has duplicate key '" + key + "': " + path, MessageType.Warning, metadata);

                    var maxLengthProperty = entry.FindPropertyRelative("MaxLength");
                    if (maxLengthProperty != null && maxLengthProperty.intValue < 0)
                        Add("StringTableMetadata key '" + key + "' has negative MaxLength: " + path, MessageType.Error, metadata);
                }
            }

            private void ValidateMaxLength(StringTable table, string path, string key, string value)
            {
                if (string.IsNullOrEmpty(table.TableId)) return;
                if (!_stringMetadata.TryGetValue(table.TableId, out var metadata)) return;
                int maxLength = metadata.GetMaxLength(key);
                if (maxLength <= 0 || value == null) return;
                if (value.Length > maxLength)
                {
                    Add("StringTable key '" + key + "' exceeds MaxLength " + maxLength + " (" + value.Length + "): " + path,
                        MessageType.Warning,
                        table);
                }
            }

            private void TrackPluralBaseKey(string key)
            {
                for (int i = 0; i < s_pluralSuffixes.Length; i++)
                {
                    string suffix = s_pluralSuffixes[i];
                    if (key.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        _pluralBaseKeys.Add(key.Substring(0, key.Length - suffix.Length));
                        return;
                    }
                }
            }

            private void ValidatePluralOtherKeys(StringTable table, string path)
            {
                foreach (var baseKey in _pluralBaseKeys)
                {
                    if (!_keySet.Contains(baseKey + ".other"))
                        Add("StringTable plural group '" + baseKey + "' is missing '.other': " + path, MessageType.Warning, table);
                }
            }

            private TableScan GetTableScan(Dictionary<string, TableScan> scans, string tableId, Object context)
            {
                if (string.IsNullOrEmpty(tableId)) return null;

                if (!scans.TryGetValue(tableId, out var scan))
                {
                    scan = new TableScan(tableId, context);
                    scans.Add(tableId, scan);
                }

                return scan;
            }

            private void ValidateTableCompleteness(Dictionary<string, TableScan> scans, string typeName)
            {
                foreach (var pair in scans)
                {
                    var scan = pair.Value;
                    if (scan.LocaleKeys.Count <= 1) continue;

                    foreach (var key in scan.AllKeys)
                    {
                        foreach (var localePair in scan.LocaleKeys)
                        {
                            if (!localePair.Value.Contains(key))
                            {
                                Add(typeName + " tableId '" + scan.TableId + "' locale '" + localePair.Key +
                                    "' is missing key '" + key + "'",
                                    MessageType.Warning,
                                    scan.Context);
                            }
                        }
                    }
                }
            }

            private void ValidateLocaleFallbacks(Locale locale, string path)
            {
                for (int i = 0; i < locale.FallbackCount; i++)
                {
                    var fallback = locale.GetFallback(i);
                    if (fallback == null) continue;
                    if (ReferenceEquals(locale, fallback))
                        Add("Locale fallback references itself: " + path, MessageType.Error, locale);
                }

                var visited = new HashSet<string>(8, StringComparer.Ordinal);
                if (HasFallbackCycle(locale, visited, new HashSet<string>(8, StringComparer.Ordinal)))
                    Add("Locale fallback chain has a cycle: " + path, MessageType.Error, locale);
            }

            private static bool HasFallbackCycle(Locale locale, HashSet<string> visited, HashSet<string> stack)
            {
                if (locale == null || !locale.Id.IsValid) return false;

                string code = locale.Id.Code;
                if (stack.Contains(code)) return true;
                if (!visited.Add(code)) return false;

                stack.Add(code);
                for (int i = 0; i < locale.FallbackCount; i++)
                {
                    if (HasFallbackCycle(locale.GetFallback(i), visited, stack))
                        return true;
                }

                stack.Remove(code);
                return false;
            }

            private void Add(string text, MessageType type, Object context)
            {
                _results.Add(new LocalizationValidationResult(text, type, context));
            }
        }

        private sealed class TableScan
        {
            public readonly string TableId;
            public readonly Object Context;
            public readonly HashSet<string> AllKeys = new HashSet<string>(128, StringComparer.Ordinal);
            public readonly Dictionary<string, HashSet<string>> LocaleKeys =
                new Dictionary<string, HashSet<string>>(8, StringComparer.Ordinal);

            public TableScan(string tableId, Object context)
            {
                TableId = tableId;
                Context = context;
            }

            public HashSet<string> GetLocaleKeys(string localeCode)
            {
                if (!LocaleKeys.TryGetValue(localeCode, out var keys))
                {
                    keys = new HashSet<string>(64, StringComparer.Ordinal);
                    LocaleKeys.Add(localeCode, keys);
                }

                return keys;
            }
        }
    }
}
#endif
