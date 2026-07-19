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
        private const int MaxValidationResults = 10_000;
        private const int MaxAssetsPerKind = 8_192;
        private const int MaxEntriesPerAsset = 250_000;

        private static readonly string[] PluralSuffixes =
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
            ValidateProject(results, null, false);
        }

        public static void ValidateProject(
            List<LocalizationValidationResult> results,
            LocalizationSettings explicitSettings,
            bool showProgress)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            var context = new ValidationContext(results, explicitSettings, showProgress);
            try
            {
                context.Scan();
            }
            finally
            {
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/CycloneGames/Localization/Validation/Validate Project")]
        public static void ValidateProjectFromMenu()
        {
            var results = new List<LocalizationValidationResult>(128);
            ValidateProject(results, null, true);
            LogSummary(results, "Validation");
        }

        public static void ValidateProjectForBatchMode()
        {
            var results = new List<LocalizationValidationResult>(128);
            ValidateProject(results, null, false);
            LogSummary(results, "Batch validation");

            if (Application.isBatchMode)
                EditorApplication.Exit(CountResults(results, MessageType.Error) > 0 ? 1 : 0);
        }

        private static void LogSummary(List<LocalizationValidationResult> results, string operation)
        {
            int errors = CountResults(results, MessageType.Error);
            int warnings = CountResults(results, MessageType.Warning);
            for (int index = 0; index < results.Count; index++)
            {
                LocalizationValidationResult result = results[index];
                if (result.Type == MessageType.Error)
                    Debug.LogError(result.Text, result.Context);
                else if (result.Type == MessageType.Warning)
                    Debug.LogWarning(result.Text, result.Context);
                else
                    Debug.Log(result.Text, result.Context);
            }

            if (errors == 0 && warnings == 0)
                Debug.Log("[Localization] " + operation + " passed.");
            else
                Debug.Log("[Localization] " + operation + " finished. Errors: " + errors + ", Warnings: " + warnings);
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

        private sealed class ValidationContext
        {
            private readonly List<LocalizationValidationResult> _results;
            private readonly LocalizationSettings _explicitSettings;
            private readonly bool _showProgress;
            private readonly HashSet<string> _keySet = new HashSet<string>(128, StringComparer.Ordinal);
            private readonly HashSet<string> _pluralBaseKeys = new HashSet<string>(32, StringComparer.Ordinal);
            private readonly Dictionary<string, Locale> _localeAssets = new Dictionary<string, Locale>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, LocaleId[]> _fallbackChains = new Dictionary<string, LocaleId[]>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, TableScan> _stringTables = new Dictionary<string, TableScan>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, TableScan> _assetTables = new Dictionary<string, TableScan>(16, StringComparer.Ordinal);
            private readonly Dictionary<string, StringTableMetadata> _metadata = new Dictionary<string, StringTableMetadata>(16, StringComparer.Ordinal);
            private readonly HashSet<string> _tableIdentities = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _metadataIdentities = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<Locale> _availableLocales = new List<Locale>(16);

            private LocalizationSettings _settings;
            private Locale _authoringLocale;
            private bool _stopRequested;
            private bool _resultLimitReported;

            public ValidationContext(
                List<LocalizationValidationResult> results,
                LocalizationSettings explicitSettings,
                bool showProgress)
            {
                _results = results;
                _explicitSettings = explicitSettings;
                _showProgress = showProgress;
            }

            public void Scan()
            {
                Reset();
                ResolveSettings();
                if (!ScanLocales() || _stopRequested)
                    return;
                ValidateSettings();
                if (_stopRequested || !ScanMetadata() || !ScanStringTables() || !ScanAssetTables())
                    return;

                ValidateMetadataCoverage();
                ValidateSparseResolutionAndFormats(_stringTables, "StringTable", true);
                ValidateSparseResolutionAndFormats(_assetTables, "AssetTable", false);
            }

            private void Reset()
            {
                _results.Clear();
                _localeAssets.Clear();
                _fallbackChains.Clear();
                _stringTables.Clear();
                _assetTables.Clear();
                _metadata.Clear();
                _tableIdentities.Clear();
                _metadataIdentities.Clear();
                _availableLocales.Clear();
                _settings = null;
                _authoringLocale = null;
                _stopRequested = false;
                _resultLimitReported = false;
            }

            private void ResolveSettings()
            {
                if (!LocalizationEditorSettingsUtility.TryResolve(_explicitSettings, out _settings, out string error))
                    Add(error, MessageType.Error, _explicitSettings);
            }

            private bool ScanLocales()
            {
                string[] guids = AssetDatabase.FindAssets("t:Locale");
                Array.Sort(guids, CompareAssetGuidByPath);
                if (!ValidateAssetCount(guids.Length, "Locale"))
                    return false;

                for (int index = 0; index < guids.Length; index++)
                {
                    if (ReportProgress("Scanning locale assets", index, guids.Length))
                        return Cancel();

                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    var locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
                    if (locale == null)
                    {
                        Add("Locale could not be loaded: " + path, MessageType.Error, null);
                        continue;
                    }

                    string code = locale.Id.Code;
                    if (!LocalizationLocaleCodeUtility.IsWellFormed(code))
                    {
                        Add("Locale has an invalid BCP 47 localeCode: " + path, MessageType.Error, locale);
                    }
                    else if (_localeAssets.TryGetValue(code, out Locale duplicate))
                    {
                        Add(
                            "Duplicate Locale code '" + code + "': " + path + " and " + AssetDatabase.GetAssetPath(duplicate),
                            MessageType.Error,
                            locale);
                    }
                    else
                    {
                        _localeAssets.Add(code, locale);
                    }

                    ValidateLocaleFallbacks(locale, path);
                    if (_stopRequested)
                        return false;
                }

                return true;
            }

            private void ValidateSettings()
            {
                if (_settings == null)
                    return;

                if (_settings.DefaultLocale == null || !_settings.DefaultLocale.Id.IsValid)
                    Add("LocalizationSettings must define a valid Default Locale.", MessageType.Error, _settings);

                IReadOnlyList<Locale> available = _settings.AvailableLocales;
                if (available == null || available.Count == 0)
                {
                    Add("LocalizationSettings must contain at least one Available Locale.", MessageType.Error, _settings);
                    return;
                }

                var codes = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < available.Count; index++)
                {
                    Locale locale = available[index];
                    if (locale == null || !LocalizationLocaleCodeUtility.IsWellFormed(locale.Id.Code))
                    {
                        Add("LocalizationSettings contains a null or invalid Available Locale at index " + index + ".", MessageType.Error, _settings);
                        continue;
                    }
                    if (!codes.Add(locale.Id.Code))
                        Add("LocalizationSettings contains duplicate locale '" + locale.Id.Code + "'.", MessageType.Error, _settings);
                    else
                        _availableLocales.Add(locale);

                    if (!_localeAssets.ContainsKey(locale.Id.Code))
                        Add("LocalizationSettings references a Locale that is not a project asset: " + locale.Id.Code, MessageType.Error, _settings);
                }

                if (_settings.DefaultLocale != null && !codes.Contains(_settings.DefaultLocale.Id.Code))
                    Add("LocalizationSettings Default Locale must be included in Available Locales.", MessageType.Error, _settings);

                if (!LocalizationEditorSettingsUtility.TryResolveAuthoringLocale(_settings, out _authoringLocale, out string authoringError))
                {
                    Add(authoringError, MessageType.Error, _settings);
                }
                else if (!codes.Contains(_authoringLocale.Id.Code))
                {
                    Add("LocalizationSettings Authoring Locale must be included in Available Locales.", MessageType.Error, _settings);
                }

                BuildFallbackChains(codes);
            }

            private void BuildFallbackChains(HashSet<string> availableCodes)
            {
                for (int index = 0; index < _availableLocales.Count; index++)
                {
                    Locale locale = _availableLocales[index];
                    try
                    {
                        LocaleId[] chain = LocaleFallbackChainBuilder.Build(locale);
                        _fallbackChains[locale.Id.Code] = chain;
                        for (int chainIndex = 0; chainIndex < chain.Length; chainIndex++)
                        {
                            if (!availableCodes.Contains(chain[chainIndex].Code))
                            {
                                Add(
                                    "Locale '" + locale.Id.Code + "' falls back to '" + chain[chainIndex].Code +
                                    "', which is not included in Available Locales.",
                                    MessageType.Error,
                                    locale);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Add("Locale fallback graph is invalid for '" + locale.Id.Code + "': " + exception.Message, MessageType.Error, locale);
                    }
                }
            }

            private bool ScanMetadata()
            {
                string[] guids = AssetDatabase.FindAssets("t:StringTableMetadata");
                Array.Sort(guids, CompareAssetGuidByPath);
                if (!ValidateAssetCount(guids.Length, "StringTableMetadata"))
                    return false;

                for (int index = 0; index < guids.Length; index++)
                {
                    if (ReportProgress("Scanning localization metadata", index, guids.Length))
                        return Cancel();

                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    var metadata = AssetDatabase.LoadAssetAtPath<StringTableMetadata>(path);
                    if (metadata == null)
                    {
                        Add("StringTableMetadata could not be loaded: " + path, MessageType.Error, null);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(metadata.TableId))
                        Add("StringTableMetadata has an empty tableId: " + path, MessageType.Error, metadata);

                    string identity = ((int)metadata.TableType).ToString() + "\0" + metadata.TableId;
                    if (!_metadataIdentities.Add(identity))
                        Add("Duplicate StringTableMetadata identity for tableId '" + metadata.TableId + "' and type " + metadata.TableType + ".", MessageType.Error, metadata);
                    else if (!string.IsNullOrEmpty(metadata.TableId))
                        _metadata[identity] = metadata;

                    ValidateMetadataEntries(metadata, path);
                    if (_stopRequested)
                        return false;
                }

                return true;
            }

            private bool ScanStringTables()
            {
                string[] guids = AssetDatabase.FindAssets("t:StringTable");
                Array.Sort(guids, CompareAssetGuidByPath);
                if (!ValidateAssetCount(guids.Length, "StringTable"))
                    return false;

                for (int index = 0; index < guids.Length; index++)
                {
                    if (ReportProgress("Scanning string tables", index, guids.Length))
                        return Cancel();

                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                    if (table == null)
                    {
                        Add("StringTable could not be loaded: " + path, MessageType.Error, null);
                        continue;
                    }

                    LocaleTableScan localeScan = ValidateAndRegisterTable(
                        _stringTables,
                        table.TableId,
                        table.LocaleId.Code,
                        "StringTable",
                        "string",
                        table);
                    ValidateStringEntries(table, localeScan);
                    if (_stopRequested)
                        return false;
                }

                return true;
            }

            private bool ScanAssetTables()
            {
                string[] guids = AssetDatabase.FindAssets("t:AssetTable");
                Array.Sort(guids, CompareAssetGuidByPath);
                if (!ValidateAssetCount(guids.Length, "AssetTable"))
                    return false;

                for (int index = 0; index < guids.Length; index++)
                {
                    if (ReportProgress("Scanning asset tables", index, guids.Length))
                        return Cancel();

                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                    if (table == null)
                    {
                        Add("AssetTable could not be loaded: " + path, MessageType.Error, null);
                        continue;
                    }

                    LocaleTableScan localeScan = ValidateAndRegisterTable(
                        _assetTables,
                        table.TableId,
                        table.LocaleId.Code,
                        "AssetTable",
                        "asset",
                        table);
                    ValidateAssetEntries(table, localeScan);
                    if (_stopRequested)
                        return false;
                }

                return true;
            }

            private LocaleTableScan ValidateAndRegisterTable(
                Dictionary<string, TableScan> scans,
                string tableId,
                string localeCode,
                string typeName,
                string identityType,
                Object context)
            {
                string path = AssetDatabase.GetAssetPath(context);
                bool valid = true;
                if (string.IsNullOrWhiteSpace(tableId))
                {
                    Add(typeName + " has an empty tableId: " + path, MessageType.Error, context);
                    valid = false;
                }
                if (!LocalizationLocaleCodeUtility.IsWellFormed(localeCode))
                {
                    Add(typeName + " has an invalid BCP 47 localeCode: " + path, MessageType.Error, context);
                    valid = false;
                }
                else if (_settings != null && !ContainsAvailableLocale(localeCode))
                {
                    Add(typeName + " locale '" + localeCode + "' is not included in LocalizationSettings Available Locales: " + path, MessageType.Error, context);
                }

                if (!valid)
                    return null;

                string identity = identityType + "\0" + tableId + "\0" + localeCode;
                if (!_tableIdentities.Add(identity))
                {
                    Add("Duplicate " + typeName + " identity '" + tableId + "' / '" + localeCode + "': " + path, MessageType.Error, context);
                    return null;
                }

                if (!scans.TryGetValue(tableId, out TableScan scan))
                {
                    scan = new TableScan(tableId, context);
                    scans.Add(tableId, scan);
                }

                var localeScan = new LocaleTableScan(localeCode, context);
                scan.Locales.Add(localeCode, localeScan);
                return localeScan;
            }

            private void ValidateStringEntries(StringTable table, LocaleTableScan localeScan)
            {
                _keySet.Clear();
                _pluralBaseKeys.Clear();
                var serialized = new SerializedObject(table);
                SerializedProperty entries = serialized.FindProperty("entries");
                if (!ValidateEntryArray(entries, table))
                    return;

                string path = AssetDatabase.GetAssetPath(table);
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    string key = entry.FindPropertyRelative("Key")?.stringValue;
                    if (string.IsNullOrEmpty(key))
                    {
                        Add("StringTable has an empty key at index " + index + ": " + path, MessageType.Error, table);
                        continue;
                    }
                    if (key.Length > LocalizationCatalogBuilder.MaxKeyChars)
                        Add("StringTable key exceeds the supported length at index " + index + ": " + path, MessageType.Error, table);
                    if (!_keySet.Add(key))
                        Add("StringTable has duplicate key '" + key + "': " + path, MessageType.Error, table);

                    string value = entry.FindPropertyRelative("Value")?.stringValue ?? string.Empty;
                    if (value.Length > LocalizationCatalogBuilder.MaxStringValueChars)
                        Add("StringTable value exceeds the supported length for key '" + key + "': " + path, MessageType.Error, table);
                    bool blank = string.IsNullOrWhiteSpace(value);
                    bool authoring = _authoringLocale != null && table.LocaleId == _authoringLocale.Id;
                    if (blank)
                    {
                        Add(
                            authoring
                                ? "Authoring Locale value is blank for key '" + key + "': " + path
                                : "Translation value is blank and will use fallback for key '" + key + "': " + path,
                            authoring ? MessageType.Error : MessageType.Warning,
                            table);
                    }
                    else if (localeScan != null && !localeScan.Values.ContainsKey(key))
                        localeScan.Values.Add(key, value);
                    TrackPluralBaseKey(key);
                    ValidateMaxLength(table, path, key, value);
                }

                ValidatePluralOtherKeys(table, path);
            }

            private void ValidateAssetEntries(AssetTable table, LocaleTableScan localeScan)
            {
                _keySet.Clear();
                var serialized = new SerializedObject(table);
                SerializedProperty entries = serialized.FindProperty("entries");
                if (!ValidateEntryArray(entries, table))
                    return;

                string path = AssetDatabase.GetAssetPath(table);
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    string key = entry.FindPropertyRelative("Key")?.stringValue;
                    if (string.IsNullOrEmpty(key))
                    {
                        Add("AssetTable has an empty key at index " + index + ": " + path, MessageType.Error, table);
                        continue;
                    }
                    if (key.Length > LocalizationCatalogBuilder.MaxKeyChars)
                        Add("AssetTable key exceeds the supported length at index " + index + ": " + path, MessageType.Error, table);
                    if (!_keySet.Add(key))
                        Add("AssetTable has duplicate key '" + key + "': " + path, MessageType.Error, table);

                    SerializedProperty asset = entry.FindPropertyRelative("Asset");
                    string location = asset?.FindPropertyRelative("m_Location")?.stringValue;
                    if (string.IsNullOrWhiteSpace(location))
                        Add("AssetTable key '" + key + "' has an empty AssetRef: " + path, MessageType.Error, table);
                    else if (location.Length > LocalizationCatalogBuilder.MaxAssetReferenceChars)
                        Add("AssetTable key '" + key + "' has an oversized AssetRef: " + path, MessageType.Error, table);

                    if (localeScan != null && !localeScan.Values.ContainsKey(key))
                        localeScan.Values.Add(key, location ?? string.Empty);
                }
            }

            private bool ValidateEntryArray(SerializedProperty entries, Object context)
            {
                if (entries == null || !entries.isArray)
                {
                    Add("Localization table has no serialized entries array: " + AssetDatabase.GetAssetPath(context), MessageType.Error, context);
                    return false;
                }
                if (entries.arraySize > MaxEntriesPerAsset)
                {
                    Add("Localization table exceeds the " + MaxEntriesPerAsset + " entry validation limit: " + AssetDatabase.GetAssetPath(context), MessageType.Error, context);
                    return false;
                }
                return true;
            }

            private void ValidateMetadataEntries(StringTableMetadata metadata, string path)
            {
                _keySet.Clear();
                var serialized = new SerializedObject(metadata);
                SerializedProperty entries = serialized.FindProperty("entries");
                if (!ValidateEntryArray(entries, metadata))
                    return;

                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    string key = entry.FindPropertyRelative("Key")?.stringValue;
                    if (string.IsNullOrEmpty(key))
                    {
                        Add("StringTableMetadata has an empty key at index " + index + ": " + path, MessageType.Error, metadata);
                        continue;
                    }
                    if (!_keySet.Add(key))
                        Add("StringTableMetadata has duplicate key '" + key + "': " + path, MessageType.Error, metadata);

                    int maxLength = entry.FindPropertyRelative("MaxLength")?.intValue ?? 0;
                    if (maxLength < 0)
                        Add("StringTableMetadata key '" + key + "' has negative MaxLength: " + path, MessageType.Error, metadata);

                    int sourceRevision = entry.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
                    if (sourceRevision < 0)
                        Add("StringTableMetadata key '" + key + "' has negative SourceRevision: " + path, MessageType.Error, metadata);

                    ValidateLocaleStatuses(metadata, path, key, sourceRevision, entry.FindPropertyRelative("LocaleStatuses"));
                }
            }

            private void ValidateLocaleStatuses(
                StringTableMetadata metadata,
                string path,
                string key,
                int sourceRevision,
                SerializedProperty statuses)
            {
                if (statuses == null)
                    return;
                if (!statuses.isArray)
                {
                    Add("StringTableMetadata key '" + key + "' has invalid LocaleStatuses data: " + path, MessageType.Error, metadata);
                    return;
                }
                if (statuses.arraySize > StringTableMetadata.MaxLocaleStatusesPerEntry)
                {
                    Add(
                        "StringTableMetadata key '" + key + "' exceeds the " +
                        StringTableMetadata.MaxLocaleStatusesPerEntry + " locale-status limit: " + path,
                        MessageType.Error,
                        metadata);
                    return;
                }

                var locales = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < statuses.arraySize; index++)
                {
                    SerializedProperty status = statuses.GetArrayElementAtIndex(index);
                    string localeCode = status.FindPropertyRelative("LocaleCode")?.stringValue;
                    int statusValue = status.FindPropertyRelative("Status")?.enumValueIndex ?? -1;
                    int translatedRevision = status.FindPropertyRelative("TranslatedSourceRevision")?.intValue ?? -1;

                    if (!LocalizationLocaleCodeUtility.IsWellFormed(localeCode))
                        Add("Metadata key '" + key + "' has an invalid locale status code: " + path, MessageType.Error, metadata);
                    else if (!locales.Add(localeCode))
                        Add("Metadata key '" + key + "' has duplicate locale status '" + localeCode + "': " + path, MessageType.Error, metadata);
                    else if (_settings != null && !ContainsAvailableLocale(localeCode))
                        Add("Metadata key '" + key + "' has a locale status not in LocalizationSettings: " + localeCode, MessageType.Error, metadata);

                    if (statusValue < (int)TranslationStatus.Missing || statusValue > (int)TranslationStatus.Stale)
                        Add("Metadata key '" + key + "' has an invalid TranslationStatus: " + path, MessageType.Error, metadata);
                    if (translatedRevision < 0 || translatedRevision > sourceRevision)
                        Add("Metadata key '" + key + "' has an invalid translated source revision for '" + localeCode + "': " + path, MessageType.Error, metadata);
                    if (statusValue == (int)TranslationStatus.Approved && translatedRevision != sourceRevision)
                        Add("Metadata key '" + key + "' marks an out-of-date translation Approved for '" + localeCode + "': " + path, MessageType.Error, metadata);
                    if (statusValue == (int)TranslationStatus.Stale && translatedRevision >= sourceRevision)
                        Add("Metadata key '" + key + "' marks a current translation Stale for '" + localeCode + "': " + path, MessageType.Warning, metadata);
                }
            }

            private void ValidateSparseResolutionAndFormats(
                Dictionary<string, TableScan> scans,
                string typeName,
                bool validateFormats)
            {
                if (_authoringLocale == null)
                    return;

                string authoringCode = _authoringLocale.Id.Code;
                foreach (KeyValuePair<string, TableScan> pair in scans)
                {
                    if (_stopRequested)
                        return;

                    TableScan scan = pair.Value;
                    if (!scan.Locales.TryGetValue(authoringCode, out LocaleTableScan authoring))
                    {
                        Add(
                            typeName + " tableId '" + scan.TableId + "' has no Authoring Locale table ('" + authoringCode + "').",
                            MessageType.Error,
                            scan.Context);
                        continue;
                    }

                    HashSet<string> invalidAuthoringFormats = validateFormats
                        ? ValidateAuthoringFormats(scan, authoring)
                        : null;

                    foreach (KeyValuePair<string, LocaleTableScan> localePair in scan.Locales)
                    {
                        if (string.Equals(localePair.Key, authoringCode, StringComparison.Ordinal))
                            continue;

                        foreach (string key in localePair.Value.Values.Keys)
                        {
                            if (!authoring.Values.ContainsKey(key))
                                Add(typeName + " tableId '" + scan.TableId + "' locale '" + localePair.Key + "' contains non-authoring key '" + key + "'.", MessageType.Warning, localePair.Value.Context);
                        }

                        if (validateFormats)
                            ValidateFormatPlaceholders(scan, authoring, localePair.Value, invalidAuthoringFormats);
                    }

                    for (int localeIndex = 0; localeIndex < _availableLocales.Count; localeIndex++)
                    {
                        Locale locale = _availableLocales[localeIndex];
                        foreach (string key in authoring.Values.Keys)
                        {
                            if (!CanResolve(scan, locale.Id.Code, key))
                            {
                                Add(
                                    typeName + " tableId '" + scan.TableId + "' locale '" + locale.Id.Code +
                                    "' cannot resolve authoring key '" + key + "' through its fallback chain.",
                                    MessageType.Warning,
                                    scan.Context);
                            }
                        }
                    }
                }
            }

            private void ValidateFormatPlaceholders(
                TableScan scan,
                LocaleTableScan authoring,
                LocaleTableScan translation,
                HashSet<string> invalidAuthoringFormats)
            {
                var sourceIndices = new HashSet<int>();
                var translatedIndices = new HashSet<int>();
                foreach (KeyValuePair<string, string> translatedEntry in translation.Values)
                {
                    if (!authoring.Values.TryGetValue(translatedEntry.Key, out string sourceValue))
                        continue;
                    if (invalidAuthoringFormats != null && invalidAuthoringFormats.Contains(translatedEntry.Key))
                        continue;

                    sourceIndices.Clear();
                    translatedIndices.Clear();
                    TryCollectFormatIndices(sourceValue, sourceIndices, out _);
                    if (!TryCollectFormatIndices(translatedEntry.Value, translatedIndices, out string translationError))
                    {
                        Add(
                            "Translation format string is invalid for '" + scan.TableId + "/" + translatedEntry.Key +
                            "' locale '" + translation.LocaleCode + "': " + translationError,
                            MessageType.Error,
                            translation.Context);
                        continue;
                    }
                    if (!sourceIndices.SetEquals(translatedIndices))
                    {
                        Add(
                            "Translation placeholders differ from the Authoring Locale for '" + scan.TableId + "/" +
                            translatedEntry.Key + "' locale '" + translation.LocaleCode + "'.",
                            MessageType.Error,
                            translation.Context);
                    }
                }
            }

            private HashSet<string> ValidateAuthoringFormats(TableScan scan, LocaleTableScan authoring)
            {
                var invalidKeys = new HashSet<string>(StringComparer.Ordinal);
                var indices = new HashSet<int>();
                foreach (KeyValuePair<string, string> entry in authoring.Values)
                {
                    indices.Clear();
                    if (TryCollectFormatIndices(entry.Value, indices, out string error))
                        continue;
                    invalidKeys.Add(entry.Key);
                    Add(
                        "Authoring format string is invalid for '" + scan.TableId + "/" + entry.Key + "': " + error,
                        MessageType.Error,
                        authoring.Context);
                }
                return invalidKeys;
            }

            private void ValidateMetadataCoverage()
            {
                foreach (KeyValuePair<string, StringTableMetadata> pair in _metadata)
                {
                    StringTableMetadata metadata = pair.Value;
                    Dictionary<string, TableScan> scans = metadata.TableType == TableType.String
                        ? _stringTables
                        : _assetTables;
                    if (!scans.TryGetValue(metadata.TableId, out TableScan scan))
                    {
                        Add(
                            "StringTableMetadata has no matching " + metadata.TableType + " table group: " + metadata.TableId,
                            MessageType.Warning,
                            metadata);
                        continue;
                    }

                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (LocaleTableScan locale in scan.Locales.Values)
                    {
                        foreach (string key in locale.Values.Keys)
                            keys.Add(key);
                    }

                    var serialized = new SerializedObject(metadata);
                    SerializedProperty entries = serialized.FindProperty("entries");
                    if (entries == null || !entries.isArray)
                        continue;
                    for (int index = 0; index < entries.arraySize; index++)
                    {
                        string key = entries.GetArrayElementAtIndex(index).FindPropertyRelative("Key")?.stringValue;
                        if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
                        {
                            Add(
                                "StringTableMetadata key '" + key + "' is orphaned from tableId '" + metadata.TableId + "'.",
                                MessageType.Warning,
                                metadata);
                        }
                    }
                }
            }

            private static bool TryCollectFormatIndices(string value, HashSet<int> indices, out string error)
            {
                value = value ?? string.Empty;
                for (int index = 0; index < value.Length; index++)
                {
                    char character = value[index];
                    if (character == '}')
                    {
                        if (index + 1 < value.Length && value[index + 1] == '}')
                        {
                            index++;
                            continue;
                        }
                        error = "unescaped closing brace at character " + (index + 1);
                        return false;
                    }
                    if (character != '{')
                        continue;
                    if (index + 1 < value.Length && value[index + 1] == '{')
                    {
                        index++;
                        continue;
                    }

                    int cursor = index + 1;
                    SkipSpaces(value, ref cursor);
                    int placeholderIndex = 0;
                    int digitCount = 0;
                    while (cursor < value.Length && value[cursor] >= '0' && value[cursor] <= '9')
                    {
                        int digit = value[cursor] - '0';
                        if (placeholderIndex > (1_000_000 - digit) / 10)
                        {
                            error = "placeholder index exceeds the supported limit";
                            return false;
                        }
                        placeholderIndex = placeholderIndex * 10 + digit;
                        cursor++;
                        digitCount++;
                    }
                    if (digitCount == 0)
                    {
                        error = "placeholder has no numeric index at character " + (index + 1);
                        return false;
                    }
                    indices.Add(placeholderIndex);
                    SkipSpaces(value, ref cursor);

                    if (cursor < value.Length && value[cursor] == ',')
                    {
                        cursor++;
                        SkipSpaces(value, ref cursor);
                        if (cursor < value.Length && (value[cursor] == '-' || value[cursor] == '+'))
                            cursor++;
                        int alignmentDigits = 0;
                        while (cursor < value.Length && value[cursor] >= '0' && value[cursor] <= '9')
                        {
                            cursor++;
                            alignmentDigits++;
                        }
                        if (alignmentDigits == 0)
                        {
                            error = "placeholder alignment has no width at character " + (index + 1);
                            return false;
                        }
                        SkipSpaces(value, ref cursor);
                    }

                    if (cursor < value.Length && value[cursor] == ':')
                    {
                        cursor++;
                        while (cursor < value.Length && value[cursor] != '}')
                        {
                            if (value[cursor] == '{')
                            {
                                error = "format section contains an unescaped opening brace at character " + (cursor + 1);
                                return false;
                            }
                            cursor++;
                        }
                    }

                    if (cursor >= value.Length || value[cursor] != '}')
                    {
                        error = "placeholder is not closed at character " + (index + 1);
                        return false;
                    }

                    index = cursor;
                }

                error = null;
                return true;
            }

            private static void SkipSpaces(string value, ref int index)
            {
                while (index < value.Length && value[index] == ' ')
                    index++;
            }

            private bool CanResolve(TableScan scan, string localeCode, string key)
            {
                if (!_fallbackChains.TryGetValue(localeCode, out LocaleId[] chain))
                    return false;
                for (int index = 0; index < chain.Length; index++)
                {
                    if (scan.Locales.TryGetValue(chain[index].Code, out LocaleTableScan table) && table.Values.ContainsKey(key))
                        return true;
                }
                return false;
            }

            private void ValidateMaxLength(StringTable table, string path, string key, string value)
            {
                if (string.IsNullOrEmpty(table.TableId))
                    return;
                string identity = ((int)TableType.String).ToString() + "\0" + table.TableId;
                if (!_metadata.TryGetValue(identity, out StringTableMetadata metadata))
                    return;

                int maxLength;
                try
                {
                    maxLength = metadata.GetMaxLength(key);
                }
                catch (InvalidOperationException exception)
                {
                    Add("StringTableMetadata lookup failed for tableId '" + table.TableId + "': " + exception.Message, MessageType.Error, metadata);
                    return;
                }
                if (maxLength > 0 && value != null && value.Length > maxLength)
                {
                    Add(
                        "StringTable key '" + key + "' exceeds MaxLength " + maxLength + " (" + value.Length + "): " + path,
                        MessageType.Warning,
                        table);
                }
            }

            private void TrackPluralBaseKey(string key)
            {
                for (int index = 0; index < PluralSuffixes.Length; index++)
                {
                    string suffix = PluralSuffixes[index];
                    if (!key.EndsWith(suffix, StringComparison.Ordinal))
                        continue;
                    _pluralBaseKeys.Add(key.Substring(0, key.Length - suffix.Length));
                    return;
                }
            }

            private void ValidatePluralOtherKeys(StringTable table, string path)
            {
                foreach (string baseKey in _pluralBaseKeys)
                {
                    if (!_keySet.Contains(baseKey + ".other"))
                        Add("StringTable plural group '" + baseKey + "' is missing '.other': " + path, MessageType.Warning, table);
                }
            }

            private void ValidateLocaleFallbacks(Locale locale, string path)
            {
                var directFallbacks = new HashSet<Locale>();
                for (int index = 0; index < locale.FallbackCount; index++)
                {
                    Locale fallback = locale.GetFallback(index);
                    if (fallback == null)
                    {
                        Add("Locale has a null fallback at index " + index + ": " + path, MessageType.Warning, locale);
                        continue;
                    }
                    if (ReferenceEquals(locale, fallback))
                        Add("Locale fallback references itself: " + path, MessageType.Error, locale);
                    if (!directFallbacks.Add(fallback))
                        Add("Locale contains a duplicate fallback '" + fallback.Id.Code + "': " + path, MessageType.Warning, locale);
                }

                if (HasFallbackCycle(locale, new HashSet<Locale>(), new HashSet<Locale>()))
                    Add("Locale fallback chain has a cycle: " + path, MessageType.Error, locale);
            }

            private static bool HasFallbackCycle(Locale locale, HashSet<Locale> visited, HashSet<Locale> stack)
            {
                if (locale == null)
                    return false;
                if (stack.Contains(locale))
                    return true;
                if (!visited.Add(locale))
                    return false;

                stack.Add(locale);
                for (int index = 0; index < locale.FallbackCount; index++)
                {
                    if (HasFallbackCycle(locale.GetFallback(index), visited, stack))
                        return true;
                }
                stack.Remove(locale);
                return false;
            }

            private bool ContainsAvailableLocale(string code)
            {
                for (int index = 0; index < _availableLocales.Count; index++)
                {
                    if (string.Equals(_availableLocales[index].Id.Code, code, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }

            private bool ValidateAssetCount(int count, string kind)
            {
                if (count <= MaxAssetsPerKind)
                    return true;
                Add(kind + " asset count exceeds the " + MaxAssetsPerKind + " validation limit.", MessageType.Error, null);
                _stopRequested = true;
                return false;
            }

            private bool ReportProgress(string detail, int index, int count)
            {
                if (!_showProgress)
                    return false;
                float progress = count <= 0 ? 1f : (float)index / count;
                return EditorUtility.DisplayCancelableProgressBar("Localization Validation", detail, progress);
            }

            private bool Cancel()
            {
                Add("Localization validation was cancelled before completion.", MessageType.Warning, null);
                _stopRequested = true;
                return false;
            }

            private void Add(string text, MessageType type, Object context)
            {
                if (_results.Count < MaxValidationResults - 1)
                {
                    _results.Add(new LocalizationValidationResult(text, type, context));
                    return;
                }
                if (_resultLimitReported)
                    return;

                _resultLimitReported = true;
                _stopRequested = true;
                _results.Add(new LocalizationValidationResult(
                    "Localization validation stopped after reaching the " + MaxValidationResults + " result limit.",
                    MessageType.Error,
                    null));
            }

            private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
            {
                return string.CompareOrdinal(
                    AssetDatabase.GUIDToAssetPath(leftGuid),
                    AssetDatabase.GUIDToAssetPath(rightGuid));
            }
        }

        private sealed class TableScan
        {
            public readonly string TableId;
            public readonly Object Context;
            public readonly Dictionary<string, LocaleTableScan> Locales =
                new Dictionary<string, LocaleTableScan>(8, StringComparer.Ordinal);

            public TableScan(string tableId, Object context)
            {
                TableId = tableId;
                Context = context;
            }
        }

        private sealed class LocaleTableScan
        {
            public readonly string LocaleCode;
            public readonly Object Context;
            public readonly Dictionary<string, string> Values =
                new Dictionary<string, string>(128, StringComparer.Ordinal);

            public LocaleTableScan(string localeCode, Object context)
            {
                LocaleCode = localeCode;
                Context = context;
            }
        }
    }
}
#endif
