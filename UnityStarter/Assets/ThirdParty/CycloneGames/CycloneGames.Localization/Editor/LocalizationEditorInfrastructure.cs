using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Localization.Core;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    internal static class LocalizationLocaleCodeUtility
    {
        public const int MaxCodeLength = LocaleId.MaxCodeLength;

        public static bool IsWellFormed(string code)
        {
            return LocaleId.TryCreate(code, out _);
        }

        public static bool TryCanonicalize(string code, out string canonicalCode)
        {
            if (LocaleId.TryCreate(code, out LocaleId localeId))
            {
                canonicalCode = localeId.Code;
                return true;
            }

            canonicalCode = null;
            return false;
        }
    }

    internal static class LocalizationEditorSettingsUtility
    {
        public static bool TryResolve(
            LocalizationSettings explicitSettings,
            out LocalizationSettings resolvedSettings,
            out string error)
        {
            if (explicitSettings != null)
            {
                resolvedSettings = explicitSettings;
                error = null;
                return true;
            }

            string[] guids = AssetDatabase.FindAssets("t:LocalizationSettings");
            if (guids.Length == 0)
            {
                resolvedSettings = null;
                error = "No LocalizationSettings asset exists. Assign one explicitly or create exactly one project settings asset.";
                return false;
            }

            Array.Sort(guids, CompareAssetGuidByPath);
            if (guids.Length > 1)
            {
                var paths = new StringBuilder(256);
                int displayCount = Math.Min(guids.Length, 8);
                for (int index = 0; index < displayCount; index++)
                {
                    if (index > 0)
                        paths.Append("; ");
                    paths.Append(AssetDatabase.GUIDToAssetPath(guids[index]));
                }

                if (guids.Length > displayCount)
                    paths.Append("; ...");

                resolvedSettings = null;
                error = $"Multiple LocalizationSettings assets exist ({guids.Length}). Assign the intended asset explicitly. {paths}";
                return false;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            resolvedSettings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(path);
            if (resolvedSettings == null)
            {
                error = $"LocalizationSettings could not be loaded: {path}";
                return false;
            }

            error = null;
            return true;
        }

        public static bool TryResolveAuthoringLocale(
            LocalizationSettings settings,
            out Locale authoringLocale,
            out string error)
        {
            authoringLocale = null;
            if (settings == null)
            {
                error = "LocalizationSettings is required.";
                return false;
            }

            authoringLocale = settings.AuthoringLocale;

            if (authoringLocale == null || !authoringLocale.Id.IsValid)
            {
                error = "LocalizationSettings must define a valid Authoring Locale or Default Locale fallback.";
                authoringLocale = null;
                return false;
            }

            error = null;
            return true;
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            return string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(leftGuid),
                AssetDatabase.GUIDToAssetPath(rightGuid));
        }
    }

    internal static class LocalizationLocaleAssetUtility
    {
        public static bool TryEnsureRegistered(
            LocalizationSettings settings,
            string localeCode,
            out Locale locale,
            out bool created,
            out string error)
        {
            locale = null;
            created = false;
            error = null;
            if (settings == null)
            {
                error = "LocalizationSettings is required.";
                return false;
            }
            if (!LocalizationLocaleCodeUtility.TryCanonicalize(localeCode, out localeCode))
            {
                error = "Locale code is not a supported BCP 47 tag: " + localeCode;
                return false;
            }

            string[] localeGuids = AssetDatabase.FindAssets("t:Locale");
            Array.Sort(localeGuids, CompareAssetGuidByPath);
            for (int index = 0; index < localeGuids.Length; index++)
            {
                Locale candidate = AssetDatabase.LoadAssetAtPath<Locale>(
                    AssetDatabase.GUIDToAssetPath(localeGuids[index]));
                if (candidate == null || !candidate.Id.IsValid ||
                    !string.Equals(candidate.Id.Code, localeCode, StringComparison.Ordinal))
                {
                    continue;
                }

                if (locale != null)
                {
                    error = "Multiple Locale assets use code '" + localeCode + "'. Resolve the duplicate before continuing.";
                    return false;
                }
                locale = candidate;
            }

            string createdPath = null;
            try
            {
                if (locale == null)
                {
                    string settingsPath = AssetDatabase.GetAssetPath(settings);
                    string directory = Path.GetDirectoryName(settingsPath)?.Replace('\\', '/') ?? "Assets";
                    createdPath = AssetDatabase.GenerateUniqueAssetPath(
                        directory + "/Locale_" + localeCode + ".asset");
                    locale = ScriptableObject.CreateInstance<Locale>();
                    var localeSerialized = new SerializedObject(locale);
                    localeSerialized.FindProperty("localeCode").stringValue = localeCode;
                    localeSerialized.FindProperty("displayName").stringValue = localeCode;
                    localeSerialized.FindProperty("nativeName").stringValue = localeCode;
                    localeSerialized.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.CreateAsset(locale, createdPath);
                    Undo.RegisterCreatedObjectUndo(locale, "Create Localization Locale");
                    created = true;
                }

                var settingsSerialized = new SerializedObject(settings);
                settingsSerialized.Update();
                SerializedProperty availableLocales = settingsSerialized.FindProperty("availableLocales");
                bool registered = false;
                for (int index = 0; index < availableLocales.arraySize; index++)
                {
                    Locale candidate = availableLocales.GetArrayElementAtIndex(index).objectReferenceValue as Locale;
                    if (candidate == locale ||
                        (candidate != null && candidate.Id.IsValid && candidate.Id == locale.Id))
                    {
                        registered = true;
                        break;
                    }
                }

                Undo.RecordObject(settings, "Register Localization Locale");
                if (!registered)
                {
                    int index = availableLocales.arraySize;
                    availableLocales.InsertArrayElementAtIndex(index);
                    availableLocales.GetArrayElementAtIndex(index).objectReferenceValue = locale;
                }

                SerializedProperty defaultLocale = settingsSerialized.FindProperty("defaultLocale");
                SerializedProperty authoringLocale = settingsSerialized.FindProperty("authoringLocale");
                if (defaultLocale.objectReferenceValue == null)
                    defaultLocale.objectReferenceValue = locale;
                if (authoringLocale.objectReferenceValue == null)
                    authoringLocale.objectReferenceValue = locale;
                settingsSerialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                AssetDatabase.SaveAssetIfDirty(locale);
                return true;
            }
            catch (Exception exception)
            {
                if (!string.IsNullOrEmpty(createdPath))
                    AssetDatabase.DeleteAsset(createdPath);
                else if (created && locale != null)
                    UnityEngine.Object.DestroyImmediate(locale);
                locale = null;
                created = false;
                error = exception.Message;
                return false;
            }
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            return string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(leftGuid),
                AssetDatabase.GUIDToAssetPath(rightGuid));
        }
    }

    internal sealed class LocalizationMetadataIndex
    {
        private readonly Dictionary<string, int> _keyToIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public StringTableMetadata Metadata { get; private set; }
        public SerializedObject Serialized { get; private set; }
        public SerializedProperty Entries { get; private set; }

        public void Bind(StringTableMetadata metadata)
        {
            Metadata = metadata;
            Serialized = metadata != null ? new SerializedObject(metadata) : null;
            Entries = Serialized?.FindProperty("entries");
            Rebuild();
        }

        public void Update()
        {
            Serialized?.UpdateIfRequiredOrScript();
        }

        public void Rebuild()
        {
            _keyToIndex.Clear();
            if (Entries == null || !Entries.isArray)
                return;

            for (int index = 0; index < Entries.arraySize; index++)
            {
                SerializedProperty key = Entries.GetArrayElementAtIndex(index).FindPropertyRelative("Key");
                if (key != null && !string.IsNullOrEmpty(key.stringValue) && !_keyToIndex.ContainsKey(key.stringValue))
                    _keyToIndex.Add(key.stringValue, index);
            }
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _keyToIndex.ContainsKey(key);
        }

        public bool IsLocked(string key)
        {
            SerializedProperty entry = GetEntry(key);
            return entry?.FindPropertyRelative("Locked")?.boolValue ?? false;
        }

        public SerializedProperty GetEntry(string key)
        {
            if (Entries == null || !_keyToIndex.TryGetValue(key, out int index) ||
                index < 0 || index >= Entries.arraySize)
            {
                return null;
            }

            return Entries.GetArrayElementAtIndex(index);
        }

        public bool CreateEntry(string key, string undoName, out string error)
        {
            error = null;
            if (Metadata == null || Entries == null)
            {
                error = "A metadata asset is required.";
                return false;
            }
            if (string.IsNullOrEmpty(key))
            {
                error = "Metadata key is required.";
                return false;
            }
            if (Contains(key))
                return true;

            Undo.RecordObject(Metadata, undoName);
            int index = Entries.arraySize;
            Entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = Entries.GetArrayElementAtIndex(index);
            ResetEntry(entry, key);
            Serialized.ApplyModifiedProperties();
            Serialized.UpdateIfRequiredOrScript();
            Rebuild();
            return true;
        }

        public bool RemoveEntry(string key, string undoName, out string error)
        {
            error = null;
            if (Metadata == null || Entries == null)
            {
                error = "A metadata asset is required.";
                return false;
            }
            if (string.IsNullOrEmpty(key))
            {
                error = "Metadata key is required.";
                return false;
            }
            if (!_keyToIndex.TryGetValue(key, out int index) || index < 0 || index >= Entries.arraySize)
                return true;

            SerializedProperty entry = Entries.GetArrayElementAtIndex(index);
            if (entry.FindPropertyRelative("Locked")?.boolValue == true)
            {
                error = "Locked metadata must be unlocked before removal.";
                return false;
            }

            Undo.RecordObject(Metadata, undoName);
            Entries.DeleteArrayElementAtIndex(index);
            Serialized.ApplyModifiedProperties();
            Serialized.UpdateIfRequiredOrScript();
            Rebuild();
            return true;
        }

        public bool CreateEntries(
            IReadOnlyList<string> keys,
            string undoName,
            out string error)
        {
            error = null;
            if (Metadata == null || Entries == null)
            {
                error = "A metadata asset is required.";
                return false;
            }
            if (keys == null || keys.Count == 0)
                return true;

            Undo.RecordObject(Metadata, undoName);
            bool changed = false;
            for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                string key = keys[keyIndex];
                if (string.IsNullOrEmpty(key) || Contains(key))
                    continue;

                int entryIndex = Entries.arraySize;
                Entries.InsertArrayElementAtIndex(entryIndex);
                ResetEntry(Entries.GetArrayElementAtIndex(entryIndex), key);
                _keyToIndex.Add(key, entryIndex);
                changed = true;
            }

            if (changed)
            {
                Serialized.ApplyModifiedProperties();
                Serialized.UpdateIfRequiredOrScript();
                Rebuild();
            }
            return true;
        }

        public bool MarkAuthoringChanged(
            string key,
            IReadOnlyList<string> translatedLocales,
            string undoName,
            out string error)
        {
            error = null;
            SerializedProperty entry = GetEntry(key);
            if (entry == null)
                return true;
            if (entry.FindPropertyRelative("Locked")?.boolValue == true)
            {
                error = "Locked metadata prevents authoring changes.";
                return false;
            }

            SerializedProperty sourceRevision = entry.FindPropertyRelative("SourceRevision");
            if (sourceRevision == null || sourceRevision.intValue == int.MaxValue)
            {
                error = "SourceRevision reached its supported maximum.";
                return false;
            }

            Undo.RecordObject(Metadata, undoName);
            sourceRevision.intValue++;
            if (translatedLocales != null)
            {
                for (int index = 0; index < translatedLocales.Count; index++)
                {
                    if (!TrySetTranslationState(
                            entry,
                            translatedLocales[index],
                            TranslationStatus.Stale,
                            null,
                            out error))
                    {
                        Serialized.UpdateIfRequiredOrScript();
                        return false;
                    }
                }
            }

            Serialized.ApplyModifiedProperties();
            Rebuild();
            return true;
        }

        public bool MarkTranslationChanged(
            string key,
            string localeCode,
            bool hasValue,
            string undoName,
            out string error)
        {
            error = null;
            SerializedProperty entry = GetEntry(key);
            if (entry == null)
                return true;
            if (entry.FindPropertyRelative("Locked")?.boolValue == true)
            {
                error = "Locked metadata prevents translation changes.";
                return false;
            }

            int sourceRevision = entry.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
            Undo.RecordObject(Metadata, undoName);
            if (!TrySetTranslationState(
                    entry,
                    localeCode,
                    hasValue ? TranslationStatus.Draft : TranslationStatus.Missing,
                    sourceRevision,
                    out error))
            {
                Serialized.UpdateIfRequiredOrScript();
                return false;
            }

            Serialized.ApplyModifiedProperties();
            Rebuild();
            return true;
        }

        public static bool TrySetTranslationState(
            SerializedProperty entry,
            string localeCode,
            TranslationStatus status,
            int? translatedSourceRevision,
            out string error)
        {
            error = null;
            if (entry == null || string.IsNullOrEmpty(localeCode))
            {
                error = "Translation state requires an entry and locale code.";
                return false;
            }

            SerializedProperty statuses = entry.FindPropertyRelative("LocaleStatuses");
            if (statuses == null || !statuses.isArray)
            {
                error = "Entry metadata has no valid LocaleStatuses array.";
                return false;
            }

            SerializedProperty state = null;
            for (int index = 0; index < statuses.arraySize; index++)
            {
                SerializedProperty candidate = statuses.GetArrayElementAtIndex(index);
                if (string.Equals(
                        candidate.FindPropertyRelative("LocaleCode")?.stringValue,
                        localeCode,
                        StringComparison.Ordinal))
                {
                    state = candidate;
                    break;
                }
            }

            if (state == null)
            {
                if (statuses.arraySize >= StringTableMetadata.MaxLocaleStatusesPerEntry)
                {
                    error = $"Entry exceeds the {StringTableMetadata.MaxLocaleStatusesPerEntry} locale-status limit.";
                    return false;
                }

                int index = statuses.arraySize;
                statuses.InsertArrayElementAtIndex(index);
                state = statuses.GetArrayElementAtIndex(index);
                state.FindPropertyRelative("LocaleCode").stringValue = localeCode;
                state.FindPropertyRelative("Status").enumValueIndex = (int)TranslationStatus.Missing;
                state.FindPropertyRelative("TranslatedSourceRevision").intValue = 0;
            }

            state.FindPropertyRelative("Status").enumValueIndex = (int)status;
            if (translatedSourceRevision.HasValue)
                state.FindPropertyRelative("TranslatedSourceRevision").intValue = translatedSourceRevision.Value;
            return true;
        }

        private static void ResetEntry(SerializedProperty entry, string key)
        {
            entry.FindPropertyRelative("Key").stringValue = key;
            entry.FindPropertyRelative("SourceRevision").intValue = 0;
            SerializedProperty statuses = entry.FindPropertyRelative("LocaleStatuses");
            if (statuses != null && statuses.isArray)
                statuses.arraySize = 0;
            entry.FindPropertyRelative("Comment").stringValue = string.Empty;
            entry.FindPropertyRelative("MaxLength").intValue = 0;
            entry.FindPropertyRelative("Locked").boolValue = false;
            entry.FindPropertyRelative("Tags").stringValue = string.Empty;
            SerializedProperty screenshot = entry.FindPropertyRelative("Screenshot");
            if (screenshot != null)
                screenshot.objectReferenceValue = null;
        }
    }

    internal static class LocalizationUndoTransaction
    {
        public static bool TryExecute(
            string name,
            UnityEngine.Object[] targets,
            Action mutation,
            out string error)
        {
            if (mutation == null)
                throw new ArgumentNullException(nameof(mutation));

            int group = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            if (targets != null && targets.Length > 0)
                Undo.RecordObjects(targets, name);

            try
            {
                mutation();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                error = exception.Message;
                return false;
            }
        }
    }

    internal readonly struct LocalizationCsvLimits
    {
        public const long DefaultMaxUtf8Bytes = 16L * 1024L * 1024L;
        public const int DefaultMaxRows = 100_000;
        public const int DefaultMaxColumns = 256;
        public const int DefaultMaxFieldChars = 1_048_576;
        public const int DefaultMaxCells = 4_000_000;

        public static LocalizationCsvLimits Default => new LocalizationCsvLimits(
            DefaultMaxUtf8Bytes,
            DefaultMaxRows,
            DefaultMaxColumns,
            DefaultMaxFieldChars,
            DefaultMaxCells);

        public readonly long MaxUtf8Bytes;
        public readonly int MaxRows;
        public readonly int MaxColumns;
        public readonly int MaxFieldChars;
        public readonly int MaxCells;

        public LocalizationCsvLimits(
            long maxUtf8Bytes,
            int maxRows,
            int maxColumns,
            int maxFieldChars,
            int maxCells)
        {
            if (maxUtf8Bytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
            if (maxRows <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRows));
            if (maxColumns <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxColumns));
            if (maxFieldChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFieldChars));
            if (maxCells <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCells));

            MaxUtf8Bytes = maxUtf8Bytes;
            MaxRows = maxRows;
            MaxColumns = maxColumns;
            MaxFieldChars = maxFieldChars;
            MaxCells = maxCells;
        }
    }

    internal sealed class LocalizationCsvDocument
    {
        private readonly List<string[]> _rows;

        public IReadOnlyList<string[]> Rows => _rows;

        public LocalizationCsvDocument(List<string[]> rows)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }
    }

    internal enum LocalizationCsvEncoding : byte
    {
        Utf8WithBom,
        Utf8WithoutBom,
    }

    /// <summary>
    /// Bounded RFC 4180 reader/writer used by localization authoring workflows.
    /// Parsing completes into a detached model before any Unity object can be changed.
    /// </summary>
    internal static class LocalizationCsv
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public static bool TryReadFile(
            string path,
            LocalizationCsvLimits limits,
            out LocalizationCsvDocument document,
            out string error)
        {
            document = null;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "CSV path is required.";
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    error = $"CSV file does not exist: {path}";
                    return false;
                }

                if (fileInfo.Length > limits.MaxUtf8Bytes)
                {
                    error = $"CSV file exceeds the {limits.MaxUtf8Bytes} byte limit.";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                int offset = HasUtf8Preamble(bytes) ? 3 : 0;
                string text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
                return TryParse(text, limits, out document, out error);
            }
            catch (DecoderFallbackException)
            {
                error = "CSV file is not valid UTF-8.";
                return false;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = $"Could not read CSV file: {exception.Message}";
                return false;
            }
        }

        public static bool TryParse(
            string text,
            LocalizationCsvLimits limits,
            out LocalizationCsvDocument document,
            out string error)
        {
            document = null;
            error = null;

            if (text == null)
            {
                error = "CSV content is null.";
                return false;
            }

            try
            {
                if (StrictUtf8.GetByteCount(text) > limits.MaxUtf8Bytes)
                {
                    error = $"CSV content exceeds the {limits.MaxUtf8Bytes} byte limit.";
                    return false;
                }
            }
            catch (EncoderFallbackException)
            {
                error = "CSV content contains invalid Unicode surrogate data.";
                return false;
            }

            var rows = new List<string[]>(Math.Min(256, limits.MaxRows));
            var row = new List<string>(Math.Min(16, limits.MaxColumns));
            var field = new StringBuilder(Math.Min(128, limits.MaxFieldChars));
            bool inQuotedField = false;
            bool quotedFieldClosed = false;
            bool fieldStarted = false;
            int cellCount = 0;

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];

                if (inQuotedField)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            if (!TryAppend(field, '"', limits, out error))
                                return false;

                            index++;
                        }
                        else
                        {
                            inQuotedField = false;
                            quotedFieldClosed = true;
                        }
                    }
                    else if (!TryAppend(field, character, limits, out error))
                    {
                        return false;
                    }

                    continue;
                }

                if (quotedFieldClosed)
                {
                    if (character == ',')
                    {
                        if (!TryFinishField(row, field, limits, ref cellCount, out error))
                            return false;

                        quotedFieldClosed = false;
                        fieldStarted = false;
                        continue;
                    }

                    if (character == '\r' || character == '\n')
                    {
                        if (!TryFinishField(row, field, limits, ref cellCount, out error) ||
                            !TryFinishRow(rows, row, limits, out error))
                        {
                            return false;
                        }

                        if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                            index++;

                        quotedFieldClosed = false;
                        fieldStarted = false;
                        continue;
                    }

                    error = $"Unexpected character after a closing quote at character {index + 1}.";
                    return false;
                }

                if (character == '"')
                {
                    if (fieldStarted || field.Length != 0)
                    {
                        error = $"A quote may only start an empty field (character {index + 1}).";
                        return false;
                    }

                    inQuotedField = true;
                    fieldStarted = true;
                    continue;
                }

                if (character == ',')
                {
                    if (!TryFinishField(row, field, limits, ref cellCount, out error))
                        return false;

                    fieldStarted = false;
                    continue;
                }

                if (character == '\r' || character == '\n')
                {
                    if (!TryFinishField(row, field, limits, ref cellCount, out error) ||
                        !TryFinishRow(rows, row, limits, out error))
                    {
                        return false;
                    }

                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                        index++;

                    fieldStarted = false;
                    continue;
                }

                fieldStarted = true;
                if (!TryAppend(field, character, limits, out error))
                    return false;
            }

            if (inQuotedField)
            {
                error = "CSV content ends inside a quoted field.";
                return false;
            }

            if (quotedFieldClosed || fieldStarted || field.Length > 0 || row.Count > 0)
            {
                if (!TryFinishField(row, field, limits, ref cellCount, out error) ||
                    !TryFinishRow(rows, row, limits, out error))
                {
                    return false;
                }
            }

            document = new LocalizationCsvDocument(rows);
            return true;
        }

        public static bool TryWriteFile(
            string path,
            IReadOnlyList<string[]> rows,
            LocalizationCsvLimits limits,
            LocalizationCsvEncoding encoding,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "CSV path is required.";
                return false;
            }

            UTF8Encoding textEncoding;
            int preambleByteCount;
            switch (encoding)
            {
                case LocalizationCsvEncoding.Utf8WithBom:
                    textEncoding = Utf8WithBom;
                    preambleByteCount = 3;
                    break;
                case LocalizationCsvEncoding.Utf8WithoutBom:
                    textEncoding = Utf8WithoutBom;
                    preambleByteCount = 0;
                    break;
                default:
                    error = "Unsupported CSV encoding policy.";
                    return false;
            }

            if (!TryValidateForWrite(rows, limits, preambleByteCount, out error))
                return false;

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, textEncoding, 16 * 1024, true))
                {
                    WriteRows(writer, rows);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);

                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = $"Could not write CSV file: {exception.Message}";
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // The original output is already safe; a temporary-file cleanup failure is non-destructive.
                }
            }
        }

        private static bool TryValidateForWrite(
            IReadOnlyList<string[]> rows,
            LocalizationCsvLimits limits,
            int preambleByteCount,
            out string error)
        {
            error = null;
            if (rows == null)
            {
                error = "CSV rows are null.";
                return false;
            }

            if (rows.Count > limits.MaxRows)
            {
                error = $"CSV row count exceeds the {limits.MaxRows} row limit.";
                return false;
            }

            long byteCount = preambleByteCount;
            if (byteCount > limits.MaxUtf8Bytes)
            {
                error = $"CSV output exceeds the {limits.MaxUtf8Bytes} byte limit.";
                return false;
            }
            int cellCount = 0;
            try
            {
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    string[] row = rows[rowIndex];
                    if (row == null)
                    {
                        error = $"CSV row {rowIndex + 1} is null.";
                        return false;
                    }

                    if (row.Length > limits.MaxColumns)
                    {
                        error = $"CSV row {rowIndex + 1} exceeds the {limits.MaxColumns} column limit.";
                        return false;
                    }

                    cellCount = checked(cellCount + row.Length);
                    if (cellCount > limits.MaxCells)
                    {
                        error = $"CSV content exceeds the {limits.MaxCells} cell limit.";
                        return false;
                    }

                    for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                    {
                        string value = row[columnIndex] ?? string.Empty;
                        if (value.Length > limits.MaxFieldChars)
                        {
                            error = $"CSV field at row {rowIndex + 1}, column {columnIndex + 1} exceeds the {limits.MaxFieldChars} character limit.";
                            return false;
                        }

                        byteCount = checked(byteCount + StrictUtf8.GetByteCount(value));
                        if (NeedsQuotes(value))
                        {
                            byteCount = checked(byteCount + 2);
                            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
                            {
                                if (value[characterIndex] == '"')
                                    byteCount++;
                            }
                        }

                        if (columnIndex + 1 < row.Length)
                            byteCount++;
                    }

                    if (rowIndex + 1 < rows.Count)
                        byteCount += 2;

                    if (byteCount > limits.MaxUtf8Bytes)
                    {
                        error = $"CSV output exceeds the {limits.MaxUtf8Bytes} byte limit.";
                        return false;
                    }
                }
            }
            catch (OverflowException)
            {
                error = "CSV size exceeds supported limits.";
                return false;
            }
            catch (EncoderFallbackException)
            {
                error = "CSV content contains invalid Unicode surrogate data.";
                return false;
            }

            return true;
        }

        private static void WriteRows(TextWriter writer, IReadOnlyList<string[]> rows)
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    if (columnIndex > 0)
                        writer.Write(',');

                    string value = row[columnIndex] ?? string.Empty;
                    if (!NeedsQuotes(value))
                    {
                        writer.Write(value);
                        continue;
                    }

                    writer.Write('"');
                    for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
                    {
                        char character = value[characterIndex];
                        if (character == '"')
                            writer.Write('"');
                        writer.Write(character);
                    }
                    writer.Write('"');
                }

                if (rowIndex + 1 < rows.Count)
                    writer.Write("\r\n");
            }
        }

        private static bool NeedsQuotes(string value)
        {
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        }

        private static bool TryAppend(
            StringBuilder field,
            char character,
            LocalizationCsvLimits limits,
            out string error)
        {
            if (field.Length >= limits.MaxFieldChars)
            {
                error = $"CSV field exceeds the {limits.MaxFieldChars} character limit.";
                return false;
            }

            field.Append(character);
            error = null;
            return true;
        }

        private static bool TryFinishField(
            List<string> row,
            StringBuilder field,
            LocalizationCsvLimits limits,
            ref int cellCount,
            out string error)
        {
            if (row.Count >= limits.MaxColumns)
            {
                error = $"CSV row exceeds the {limits.MaxColumns} column limit.";
                return false;
            }

            if (cellCount >= limits.MaxCells)
            {
                error = $"CSV content exceeds the {limits.MaxCells} cell limit.";
                return false;
            }

            row.Add(field.ToString());
            field.Length = 0;
            cellCount++;
            error = null;
            return true;
        }

        private static bool TryFinishRow(
            List<string[]> rows,
            List<string> row,
            LocalizationCsvLimits limits,
            out string error)
        {
            if (rows.Count >= limits.MaxRows)
            {
                error = $"CSV content exceeds the {limits.MaxRows} row limit.";
                return false;
            }

            rows.Add(row.ToArray());
            row.Clear();
            error = null;
            return true;
        }

        private static bool HasUtf8Preamble(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }
    }

    internal static class LocalizationAssetPathUtility
    {
        private const int MaxAssetPathLength = 240;
        private static readonly char[] PortableInvalidSegmentCharacters = { '<', '>', ':', '"', '|', '?', '*', '\0' };

        public static bool TryNormalizeCatalogAssetPath(
            string outputFolder,
            string fileName,
            out string assetPath,
            out string error)
        {
            string assetsPath = Application.dataPath;
            string projectRoot = Directory.GetParent(assetsPath)?.FullName;
            return TryNormalizeCatalogAssetPath(projectRoot, outputFolder, fileName, out assetPath, out error);
        }

        internal static bool TryNormalizeCatalogAssetPath(
            string projectRoot,
            string outputFolder,
            string fileName,
            out string assetPath,
            out string error)
        {
            assetPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "Project root is unavailable.";
                return false;
            }

            if (!TryNormalizeRelativePath(outputFolder, false, out string normalizedFolder, out error))
                return false;

            if (!string.Equals(normalizedFolder, "Assets", StringComparison.Ordinal) &&
                !normalizedFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Catalog output folder must be Assets or a child of Assets.";
                return false;
            }

            if (!TryNormalizeRelativePath(fileName, true, out string normalizedFileName, out error))
                return false;

            if (normalizedFileName.IndexOf('/') >= 0)
            {
                error = "Catalog file name must not contain directory segments.";
                return false;
            }

            string extension = Path.GetExtension(normalizedFileName);
            if (string.IsNullOrEmpty(extension))
                normalizedFileName += ".asset";
            else if (!string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                error = "Catalog file name must use the .asset extension.";
                return false;
            }

            assetPath = normalizedFolder + "/" + normalizedFileName;
            if (assetPath.Length > MaxAssetPathLength)
            {
                error = $"Catalog asset path exceeds the {MaxAssetPathLength} character portability limit.";
                assetPath = null;
                return false;
            }

            try
            {
                string canonicalRoot = Path.GetFullPath(projectRoot);
                string canonicalAssets = Path.GetFullPath(Path.Combine(canonicalRoot, "Assets"));
                string canonicalCandidate = Path.GetFullPath(Path.Combine(canonicalRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
                StringComparison comparison = IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                string assetsPrefix = canonicalAssets.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!canonicalCandidate.StartsWith(assetsPrefix, comparison))
                {
                    error = "Catalog output path escapes the project Assets directory.";
                    assetPath = null;
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is IOException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = $"Catalog output path is invalid: {exception.Message}";
                assetPath = null;
                return false;
            }

            return true;
        }

        private static bool TryNormalizeRelativePath(
            string value,
            bool isFileName,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = isFileName ? "Catalog file name is required." : "Catalog output folder is required.";
                return false;
            }

            value = value.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(value) || value.StartsWith("/", StringComparison.Ordinal) ||
                (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':'))
            {
                error = "Catalog output must use a project-relative path.";
                return false;
            }

            string[] segments = value.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    error = "Catalog output path contains an empty or traversal segment.";
                    return false;
                }

                if (segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal))
                {
                    error = "Catalog output path contains a segment with a non-portable trailing character.";
                    return false;
                }

                if (segment.IndexOfAny(PortableInvalidSegmentCharacters) >= 0 || ContainsControlCharacter(segment))
                {
                    error = "Catalog output path contains a non-portable character.";
                    return false;
                }

                string deviceName = Path.GetFileNameWithoutExtension(segment);
                if (IsReservedWindowsDeviceName(deviceName))
                {
                    error = $"Catalog output path contains the reserved device name '{deviceName}'.";
                    return false;
                }
            }

            normalized = string.Join("/", segments);
            return true;
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                    return true;
            }

            return false;
        }

        private static bool IsReservedWindowsDeviceName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string upper = value.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL")
                return true;

            if (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)))
                return upper[3] >= '1' && upper[3] <= '9';

            return false;
        }

        private static bool IsWindows()
        {
            PlatformID platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT ||
                   platform == PlatformID.Win32S ||
                   platform == PlatformID.Win32Windows ||
                   platform == PlatformID.WinCE;
        }
    }

    internal readonly struct LocalizationScrollViewport
    {
        private const float DefaultScrollbarExtent = 16f;

        public readonly float Width;
        public readonly float Height;
        public readonly bool ShowsHorizontalScrollbar;
        public readonly bool ShowsVerticalScrollbar;

        private LocalizationScrollViewport(
            float width,
            float height,
            bool showsHorizontalScrollbar,
            bool showsVerticalScrollbar)
        {
            Width = width;
            Height = height;
            ShowsHorizontalScrollbar = showsHorizontalScrollbar;
            ShowsVerticalScrollbar = showsVerticalScrollbar;
        }

        public static LocalizationScrollViewport CalculateForCurrentSkin(
            float containerWidth,
            float containerHeight,
            float contentWidth,
            float contentHeight)
        {
            GUIStyle verticalStyle = GUI.skin?.verticalScrollbar;
            GUIStyle horizontalStyle = GUI.skin?.horizontalScrollbar;
            float verticalExtent = verticalStyle != null && verticalStyle.fixedWidth > 0f
                ? verticalStyle.fixedWidth
                : DefaultScrollbarExtent;
            float horizontalExtent = horizontalStyle != null && horizontalStyle.fixedHeight > 0f
                ? horizontalStyle.fixedHeight
                : DefaultScrollbarExtent;
            return Calculate(
                containerWidth,
                containerHeight,
                contentWidth,
                contentHeight,
                verticalExtent,
                horizontalExtent);
        }

        public static LocalizationScrollViewport Calculate(
            float containerWidth,
            float containerHeight,
            float contentWidth,
            float contentHeight,
            float verticalScrollbarWidth,
            float horizontalScrollbarHeight)
        {
            containerWidth = Mathf.Max(0f, containerWidth);
            containerHeight = Mathf.Max(0f, containerHeight);
            contentWidth = Mathf.Max(0f, contentWidth);
            contentHeight = Mathf.Max(0f, contentHeight);
            verticalScrollbarWidth = Mathf.Max(0f, verticalScrollbarWidth);
            horizontalScrollbarHeight = Mathf.Max(0f, horizontalScrollbarHeight);

            bool horizontal = false;
            bool vertical = false;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                float availableWidth = Mathf.Max(0f, containerWidth - (vertical ? verticalScrollbarWidth : 0f));
                float availableHeight = Mathf.Max(0f, containerHeight - (horizontal ? horizontalScrollbarHeight : 0f));
                bool nextHorizontal = contentWidth > availableWidth;
                bool nextVertical = contentHeight > availableHeight;
                if (nextHorizontal == horizontal && nextVertical == vertical)
                    break;
                horizontal = nextHorizontal;
                vertical = nextVertical;
            }

            return new LocalizationScrollViewport(
                Mathf.Max(0f, containerWidth - (vertical ? verticalScrollbarWidth : 0f)),
                Mathf.Max(0f, containerHeight - (horizontal ? horizontalScrollbarHeight : 0f)),
                horizontal,
                vertical);
        }
    }

    internal readonly struct LocalizationVisibleRange
    {
        public readonly int Start;
        public readonly int EndExclusive;

        public int Count => EndExclusive - Start;

        public LocalizationVisibleRange(int start, int endExclusive)
        {
            Start = start;
            EndExclusive = endExclusive;
        }
    }

    /// <summary>
    /// Stores filtered source indices and row offsets so repaint only visits rows intersecting the viewport.
    /// </summary>
    internal sealed class LocalizationVisibleRowCache
    {
        private readonly List<int> _sourceIndices = new List<int>();
        private readonly List<float> _rowOffsets = new List<float> { 0f };

        public int Count => _sourceIndices.Count;
        public float TotalHeight => _rowOffsets[_rowOffsets.Count - 1];

        public void Rebuild(int sourceCount, Func<int, bool> include, Func<int, float> getHeight)
        {
            if (sourceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceCount));
            if (getHeight == null)
                throw new ArgumentNullException(nameof(getHeight));

            _sourceIndices.Clear();
            _rowOffsets.Clear();
            _rowOffsets.Add(0f);

            float offset = 0f;
            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                if (include != null && !include(sourceIndex))
                    continue;

                float height = getHeight(sourceIndex);
                if (float.IsNaN(height) || float.IsInfinity(height) || height <= 0f)
                    throw new ArgumentOutOfRangeException(nameof(getHeight), "Row heights must be finite and positive.");

                _sourceIndices.Add(sourceIndex);
                offset += height;
                _rowOffsets.Add(offset);
            }
        }

        public int GetSourceIndex(int visibleIndex)
        {
            return _sourceIndices[visibleIndex];
        }

        public float GetTop(int visibleIndex)
        {
            return _rowOffsets[visibleIndex];
        }

        public float GetHeight(int visibleIndex)
        {
            return _rowOffsets[visibleIndex + 1] - _rowOffsets[visibleIndex];
        }

        public LocalizationVisibleRange FindVisibleRange(float scrollY, float viewportHeight)
        {
            if (_sourceIndices.Count == 0 || viewportHeight <= 0f)
                return new LocalizationVisibleRange(0, 0);

            float viewportStart = Math.Max(0f, scrollY);
            float viewportEnd = Math.Max(viewportStart, viewportStart + viewportHeight);
            int start = FindFirstRowWithBottomAfter(viewportStart);
            int end = FindFirstRowStartingAtOrAfter(viewportEnd);
            if (end < start)
                end = start;

            return new LocalizationVisibleRange(start, end);
        }

        private int FindFirstRowWithBottomAfter(float position)
        {
            int low = 0;
            int high = _sourceIndices.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (_rowOffsets[middle + 1] <= position)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private int FindFirstRowStartingAtOrAfter(float position)
        {
            int low = 0;
            int high = _sourceIndices.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (_rowOffsets[middle] < position)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }
}
