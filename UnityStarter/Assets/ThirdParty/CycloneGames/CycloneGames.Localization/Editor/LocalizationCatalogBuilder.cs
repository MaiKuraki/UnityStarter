#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class LocalizationCatalogBuildOptions
    {
        public const string DefaultVersion = "1.0.0";

        public LocalizationSettings Settings;
        public string OutputPath;
        public string Version = DefaultVersion;
        public LocalizationCatalogContentKind ContentKind = LocalizationCatalogContentKind.All;
        public IReadOnlyList<Locale> IncludedLocales;
        public IReadOnlyList<string> IncludedTableIds;
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
        internal const int MaxTableAssets = 8_192;
        internal const int MaxEntriesPerTable = 250_000;
        internal const int MaxTotalEntries = 1_000_000;
        internal const int MaxTableIdChars = 256;
        internal const int MaxKeyChars = 1_024;
        internal const int MaxStringValueChars = 1_048_576;
        internal const int MaxAssetReferenceChars = 4_096;
        internal const int MaxCatalogVersionChars = 128;

        [MenuItem("Tools/CycloneGames/Localization/Catalog/Build Once...")]
        public static void BuildCatalogFromMenu()
        {
            string outputPath = SelectOutputPath();
            if (string.IsNullOrEmpty(outputPath))
                return;

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
            if (!TryFindSettingsAsset(out LocalizationCatalogBuildSettings settings, out string error))
            {
                EditorUtility.DisplayDialog("Localization Catalog", error, "OK");
                return;
            }

            try
            {
                BuildCatalog(settings);
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

        [MenuItem("Tools/CycloneGames/Localization/Catalog/Select Build Settings")]
        public static void SelectSettingsMenu()
        {
            if (!TryFindSettingsAsset(out LocalizationCatalogBuildSettings settings, out string error))
            {
                EditorUtility.DisplayDialog("Localization Catalog", error, "OK");
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
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return BuildCatalog(settings.ToBuildOptions());
        }

        public static LocalizationCatalogBuildResult BuildCatalog(LocalizationCatalogBuildOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            string outputPath = NormalizeOutputPath(options.OutputPath);
            string version = ValidateVersion(options.Version);
            if (!LocalizationEditorSettingsUtility.TryResolve(options.Settings, out LocalizationSettings settings, out string settingsError))
                throw new InvalidOperationException(settingsError);

            if (options.ValidateBeforeBuild)
                ValidateProjectOrThrow(settings);

            // All source data is read, bounded, validated and canonicalized before output assets are touched.
            CatalogBuildData data = BuildCatalogData(settings, options);
            string hash = ComputeContentHash(data.StringTables, data.AssetTables);

            EnsureParentFolders(outputPath);
            LocalizationCatalog catalog = ReplaceCatalogAtomically(outputPath, version, hash, data);

            if (options.SelectBuiltAsset)
                Selection.activeObject = catalog;

            var result = new LocalizationCatalogBuildResult(
                catalog,
                outputPath,
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
            return LocalizationCatalog.ComputeContentHash(stringTables, assetTables);
        }

        private static CatalogBuildData BuildCatalogData(
            LocalizationSettings settings,
            LocalizationCatalogBuildOptions options)
        {
            var allowedLocales = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<Locale> availableLocales = settings.AvailableLocales;
            if (availableLocales == null || availableLocales.Count == 0)
                throw new InvalidDataException("LocalizationSettings must contain at least one Available Locale.");

            for (int index = 0; index < availableLocales.Count; index++)
            {
                Locale locale = availableLocales[index];
                if (locale == null || !locale.Id.IsValid)
                    throw new InvalidDataException($"LocalizationSettings Available Locales contains an invalid item at index {index}.");
                if (!allowedLocales.Add(locale.Id.Code))
                    throw new InvalidDataException($"LocalizationSettings contains duplicate locale '{locale.Id.Code}'.");
            }

            LocalizationCatalogBuildFilter filter = LocalizationCatalogBuildFilter.Create(
                options.ContentKind,
                options.IncludedLocales,
                options.IncludedTableIds,
                allowedLocales);
            var data = new CatalogBuildData();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (filter.IncludeStrings)
                AppendStringTables(data, allowedLocales, identities, filter);
            if (filter.IncludeAssets)
                AppendAssetTables(data, allowedLocales, identities, filter);
            if (data.StringTables.Count == 0 && data.AssetTables.Count == 0)
                throw new InvalidDataException("Catalog filters did not match any localization tables.");
            data.StringTables.Sort(CompareStringTables);
            data.AssetTables.Sort(CompareAssetTables);
            return data;
        }

        private static void AppendStringTables(
            CatalogBuildData data,
            HashSet<string> allowedLocales,
            HashSet<string> identities,
            LocalizationCatalogBuildFilter filter)
        {
            string[] guids = AssetDatabase.FindAssets("t:StringTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            int includedCount = 0;

            for (int tableIndex = 0; tableIndex < guids.Length; tableIndex++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[tableIndex]);
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null)
                    throw new InvalidDataException($"StringTable could not be loaded: {path}");
                if (!filter.Includes(table.TableId, table.LocaleId.Code))
                    continue;
                ValidateTableAssetCount(++includedCount, "string");

                ValidateTableIdentity(table.TableId, table.LocaleId.Code, allowedLocales, "string", path, identities);
                var serialized = new SerializedObject(table);
                SerializedProperty entries = serialized.FindProperty("entries");
                ValidateEntriesProperty(entries, path);

                var catalogEntries = new List<CatalogStringEntry>(entries.arraySize);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                    string key = RequireString(entry.FindPropertyRelative("Key"), "key", path, entryIndex, MaxKeyChars, false);
                    if (!keys.Add(key))
                        throw new InvalidDataException($"StringTable '{path}' contains duplicate key '{key}'.");

                    string value = RequireString(entry.FindPropertyRelative("Value"), "value", path, entryIndex, MaxStringValueChars, true);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    catalogEntries.Add(new CatalogStringEntry(key, value));
                    IncrementEntryCount(data, true);
                }

                catalogEntries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                data.StringTables.Add(new CatalogStringTable(table.TableId, table.LocaleId.Code, catalogEntries));
            }
        }

        private static void AppendAssetTables(
            CatalogBuildData data,
            HashSet<string> allowedLocales,
            HashSet<string> identities,
            LocalizationCatalogBuildFilter filter)
        {
            string[] guids = AssetDatabase.FindAssets("t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            int includedCount = 0;

            for (int tableIndex = 0; tableIndex < guids.Length; tableIndex++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[tableIndex]);
                var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                if (table == null)
                    throw new InvalidDataException($"AssetTable could not be loaded: {path}");
                if (!filter.Includes(table.TableId, table.LocaleId.Code))
                    continue;
                ValidateTableAssetCount(++includedCount, "asset");

                ValidateTableIdentity(table.TableId, table.LocaleId.Code, allowedLocales, "asset", path, identities);
                var serialized = new SerializedObject(table);
                SerializedProperty entries = serialized.FindProperty("entries");
                ValidateEntriesProperty(entries, path);

                var catalogEntries = new List<CatalogAssetEntry>(entries.arraySize);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                    string key = RequireString(entry.FindPropertyRelative("Key"), "key", path, entryIndex, MaxKeyChars, false);
                    if (!keys.Add(key))
                        throw new InvalidDataException($"AssetTable '{path}' contains duplicate key '{key}'.");

                    SerializedProperty asset = entry.FindPropertyRelative("Asset");
                    if (asset == null)
                        throw new InvalidDataException($"AssetTable '{path}' entry {entryIndex} has no Asset field.");

                    string location = RequireString(asset.FindPropertyRelative("m_Location"), "asset location", path, entryIndex, MaxAssetReferenceChars, false);
                    string guid = RequireString(asset.FindPropertyRelative("m_GUID"), "asset GUID", path, entryIndex, MaxAssetReferenceChars, true);
                    catalogEntries.Add(new CatalogAssetEntry(key, new AssetRef(location, guid)));
                    IncrementEntryCount(data, false);
                }

                catalogEntries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                data.AssetTables.Add(new CatalogAssetTable(table.TableId, table.LocaleId.Code, catalogEntries));
            }
        }

        private static LocalizationCatalog ReplaceCatalogAtomically(
            string outputPath,
            string version,
            string hash,
            CatalogBuildData data)
        {
            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(outputPath);
            if (existingAsset != null && !(existingAsset is LocalizationCatalog))
                throw new InvalidOperationException("Catalog output path is used by another asset type: " + outputPath);
            if (existingAsset != null && EditorUtility.IsDirty(existingAsset))
                throw new InvalidOperationException("Catalog output asset has unsaved changes and cannot be replaced: " + outputPath);

            string folder = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            string temporaryAssetPath = folder + "/__LocalizationCatalog_" + Guid.NewGuid().ToString("N") + ".asset";
            var temporaryCatalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            temporaryCatalog.SetData(version, hash, data.StringTables, data.AssetTables);

            try
            {
                AssetDatabase.CreateAsset(temporaryCatalog, temporaryAssetPath);
                AssetDatabase.SaveAssetIfDirty(temporaryCatalog);

                if (existingAsset == null)
                {
                    string moveError = AssetDatabase.MoveAsset(temporaryAssetPath, outputPath);
                    if (!string.IsNullOrEmpty(moveError))
                        throw new IOException("Could not move generated catalog into place: " + moveError);

                    temporaryAssetPath = null;
                    return RequireImportedCatalog(outputPath, version, hash);
                }

                return ReplaceExistingCatalogFile(temporaryAssetPath, outputPath, version, hash);
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryAssetPath))
                    AssetDatabase.DeleteAsset(temporaryAssetPath);
            }
        }

        private static LocalizationCatalog ReplaceExistingCatalogFile(
            string temporaryAssetPath,
            string outputPath,
            string version,
            string hash)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Unity project root is unavailable.");

            string temporaryFullPath = ToFullPath(projectRoot, temporaryAssetPath);
            string outputFullPath = ToFullPath(projectRoot, outputPath);
            string token = Guid.NewGuid().ToString("N");
            string replacementPath = outputFullPath + "." + token + ".replacement";
            string backupPath = outputFullPath + "." + token + ".backup";
            bool replacementCommitted = false;

            try
            {
                CopyFileDurably(temporaryFullPath, replacementPath);
                if (!AssetDatabase.DeleteAsset(temporaryAssetPath))
                    throw new IOException("Could not clean the temporary catalog asset before replacement.");

                File.Replace(replacementPath, outputFullPath, backupPath);
                replacementCommitted = true;
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                LocalizationCatalog catalog = RequireImportedCatalog(outputPath, version, hash);
                File.Delete(backupPath);
                return catalog;
            }
            catch
            {
                if (replacementCommitted && File.Exists(backupPath))
                {
                    try
                    {
                        File.Replace(backupPath, outputFullPath, null);
                        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    }
                    catch (Exception rollbackException)
                    {
                        Debug.LogError("[Localization] Catalog replacement rollback failed: " + rollbackException);
                    }
                }

                throw;
            }
            finally
            {
                DeleteFileBestEffort(replacementPath);
                DeleteFileBestEffort(backupPath);
            }
        }

        private static LocalizationCatalog RequireImportedCatalog(string outputPath, string version, string hash)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LocalizationCatalog>(outputPath);
            if (catalog == null || catalog.SchemaVersion != LocalizationCatalog.CurrentSchemaVersion ||
                !string.Equals(catalog.CatalogVersion, version, StringComparison.Ordinal) ||
                !string.Equals(catalog.ContentHash, hash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Generated catalog failed post-write verification: " + outputPath);
            }

            return catalog;
        }

        private static void ValidateTableIdentity(
            string tableId,
            string localeCode,
            HashSet<string> allowedLocales,
            string kind,
            string path,
            HashSet<string> identities)
        {
            if (string.IsNullOrWhiteSpace(tableId) || tableId.Length > MaxTableIdChars)
                throw new InvalidDataException($"{kind} table '{path}' has an empty or oversized Table Id.");
            if (string.IsNullOrEmpty(localeCode) || !allowedLocales.Contains(localeCode))
                throw new InvalidDataException($"{kind} table '{path}' uses locale '{localeCode}' which is not in LocalizationSettings.");

            string identity = kind + "\0" + tableId + "\0" + localeCode;
            if (!identities.Add(identity))
                throw new InvalidDataException($"Duplicate {kind} table identity '{tableId}' / '{localeCode}'.");
        }

        private static void ValidateEntriesProperty(SerializedProperty entries, string path)
        {
            if (entries == null || !entries.isArray)
                throw new InvalidDataException("Table has no serialized entries array: " + path);
            if (entries.arraySize > MaxEntriesPerTable)
                throw new InvalidDataException($"Table '{path}' exceeds the {MaxEntriesPerTable} entry limit.");
        }

        private static string RequireString(
            SerializedProperty property,
            string fieldName,
            string path,
            int entryIndex,
            int maxChars,
            bool allowEmpty)
        {
            if (property == null || property.propertyType != SerializedPropertyType.String)
                throw new InvalidDataException($"Table '{path}' entry {entryIndex} has no valid {fieldName} field.");

            string value = property.stringValue ?? string.Empty;
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maxChars)
                throw new InvalidDataException($"Table '{path}' entry {entryIndex} has an empty or oversized {fieldName}.");
            return value;
        }

        private static void IncrementEntryCount(CatalogBuildData data, bool isString)
        {
            if (isString)
                data.StringEntryCount++;
            else
                data.AssetEntryCount++;

            if ((long)data.StringEntryCount + data.AssetEntryCount > MaxTotalEntries)
                throw new InvalidDataException($"Localization catalog exceeds the {MaxTotalEntries} total entry limit.");
        }

        private static void ValidateTableAssetCount(int count, string kind)
        {
            if (count > MaxTableAssets)
                throw new InvalidDataException($"Localization project exceeds the {MaxTableAssets} {kind} table asset limit.");
        }

        private static string NormalizeOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            string folder = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            string fileName = Path.GetFileName(outputPath);
            if (!LocalizationAssetPathUtility.TryNormalizeCatalogAssetPath(
                    folder,
                    fileName,
                    out string normalizedPath,
                    out string error))
            {
                throw new ArgumentException(error, nameof(outputPath));
            }

            return normalizedPath;
        }

        private static string ValidateVersion(string version)
        {
            version = string.IsNullOrWhiteSpace(version) ? LocalizationCatalogBuildOptions.DefaultVersion : version.Trim();
            if (version.Length > MaxCatalogVersionChars)
                throw new ArgumentException($"Catalog version exceeds {MaxCatalogVersionChars} characters.", nameof(version));
            for (int index = 0; index < version.Length; index++)
            {
                if (char.IsControl(version[index]))
                    throw new ArgumentException("Catalog version contains a control character.", nameof(version));
            }
            return version;
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
            if (!TryFindSettingsAsset(out LocalizationCatalogBuildSettings settings, out string error))
                throw new InvalidOperationException(error);
            return settings;
        }

        private static bool TryFindSettingsAsset(
            out LocalizationCatalogBuildSettings settings,
            out string error)
        {
            string[] guids = AssetDatabase.FindAssets("t:LocalizationCatalogBuildSettings");
            Array.Sort(guids, CompareAssetGuidByPath);
            if (guids.Length == 0)
            {
                settings = null;
                error = "No LocalizationCatalogBuildSettings asset was found. Create one with Create > CycloneGames > Localization > Catalog Build Settings.";
                return false;
            }
            if (guids.Length > 1)
            {
                settings = null;
                error = $"Multiple LocalizationCatalogBuildSettings assets exist ({guids.Length}). Select and build the intended asset from its Inspector.";
                return false;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            settings = AssetDatabase.LoadAssetAtPath<LocalizationCatalogBuildSettings>(path);
            error = settings == null ? "LocalizationCatalogBuildSettings could not be loaded: " + path : null;
            return settings != null;
        }

        private static void ValidateProjectOrThrow(LocalizationSettings settings)
        {
            var results = new List<LocalizationValidationResult>(128);
            LocalizationValidator.ValidateProject(results, settings, false);
            int errors = CountResults(results, MessageType.Error);
            if (errors == 0)
                return;

            LogValidationResults(results);
            throw new InvalidOperationException(
                "Catalog build aborted because localization validation has " + errors + " error(s)." );
        }

        private static void EnsureParentFolders(string outputPath)
        {
            string[] parts = outputPath.Split('/');
            if (parts.Length <= 2)
                return;

            string current = parts[0];
            for (int index = 1; index < parts.Length - 1; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[index]);
                    if (string.IsNullOrEmpty(guid))
                        throw new IOException("Could not create catalog output folder: " + next);
                }
                current = next;
            }
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            return string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(leftGuid),
                AssetDatabase.GUIDToAssetPath(rightGuid));
        }

        private static int CompareStringTables(CatalogStringTable left, CatalogStringTable right)
        {
            int tableCompare = string.CompareOrdinal(left.TableId, right.TableId);
            return tableCompare != 0 ? tableCompare : string.CompareOrdinal(left.LocaleId.Code, right.LocaleId.Code);
        }

        private static int CompareAssetTables(CatalogAssetTable left, CatalogAssetTable right)
        {
            int tableCompare = string.CompareOrdinal(left.TableId, right.TableId);
            return tableCompare != 0 ? tableCompare : string.CompareOrdinal(left.LocaleId.Code, right.LocaleId.Code);
        }

        private static int CountResults(List<LocalizationValidationResult> results, MessageType type)
        {
            int count = 0;
            for (int index = 0; index < results.Count; index++)
            {
                if (results[index].Type == type)
                    count++;
            }
            return count;
        }

        private static void LogValidationResults(List<LocalizationValidationResult> results)
        {
            for (int index = 0; index < results.Count; index++)
            {
                LocalizationValidationResult result = results[index];
                if (result.Type == MessageType.Error)
                    Debug.LogError(result.Text, result.Context);
                else if (result.Type == MessageType.Warning)
                    Debug.LogWarning(result.Text, result.Context);
            }
        }

        private static string ToFullPath(string projectRoot, string assetPath)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void CopyFileDurably(string source, string destination)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output, 64 * 1024);
                output.Flush(true);
            }
        }

        private static void DeleteFileBestEffort(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Localization] Could not remove temporary catalog file: " + exception.Message);
            }
        }

        private sealed class CatalogBuildData
        {
            public readonly List<CatalogStringTable> StringTables = new List<CatalogStringTable>(32);
            public readonly List<CatalogAssetTable> AssetTables = new List<CatalogAssetTable>(16);
            public int StringEntryCount;
            public int AssetEntryCount;
        }

        internal sealed class LocalizationCatalogBuildFilter
        {
            private readonly HashSet<string> _localeCodes;
            private readonly HashSet<string> _tableIds;

            private LocalizationCatalogBuildFilter(
                LocalizationCatalogContentKind contentKind,
                HashSet<string> localeCodes,
                HashSet<string> tableIds)
            {
                IncludeStrings = (contentKind & LocalizationCatalogContentKind.Strings) != 0;
                IncludeAssets = (contentKind & LocalizationCatalogContentKind.Assets) != 0;
                _localeCodes = localeCodes;
                _tableIds = tableIds;
            }

            public bool IncludeStrings { get; }
            public bool IncludeAssets { get; }

            public bool Includes(string tableId, string localeCode)
            {
                return (_localeCodes == null || _localeCodes.Contains(localeCode)) &&
                       (_tableIds == null || _tableIds.Contains(tableId));
            }

            public static LocalizationCatalogBuildFilter Create(
                LocalizationCatalogContentKind contentKind,
                IReadOnlyList<Locale> locales,
                IReadOnlyList<string> tableIds,
                HashSet<string> allowedLocales)
            {
                if (contentKind == 0 || (contentKind & ~LocalizationCatalogContentKind.All) != 0)
                    throw new InvalidDataException("Catalog Content Kind must include Strings, Assets, or both.");

                HashSet<string> localeCodes = null;
                if (locales != null && locales.Count > 0)
                {
                    localeCodes = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = 0; index < locales.Count; index++)
                    {
                        Locale locale = locales[index];
                        if (locale == null || !locale.Id.IsValid || !allowedLocales.Contains(locale.Id.Code))
                            throw new InvalidDataException("Included Locales contains an invalid or unavailable locale.");
                        if (!localeCodes.Add(locale.Id.Code))
                            throw new InvalidDataException("Included Locales contains duplicate locale '" + locale.Id.Code + "'.");
                    }
                }

                HashSet<string> includedTableIds = null;
                if (tableIds != null && tableIds.Count > 0)
                {
                    includedTableIds = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = 0; index < tableIds.Count; index++)
                    {
                        string tableId = tableIds[index];
                        if (string.IsNullOrWhiteSpace(tableId) || tableId.Length > MaxTableIdChars)
                            throw new InvalidDataException("Included Table IDs contains an empty or oversized value.");
                        if (!includedTableIds.Add(tableId))
                            throw new InvalidDataException("Included Table IDs contains duplicate table ID '" + tableId + "'.");
                    }
                }

                return new LocalizationCatalogBuildFilter(contentKind, localeCodes, includedTableIds);
            }
        }
    }
}
#endif
