#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    internal enum LocalizationCsvExportProfile : byte
    {
        Spreadsheet,
        Automation,
    }

    internal readonly struct LocalizationCsvExportLanguageOption
    {
        public readonly string LocaleCode;
        public readonly bool IsRegistered;

        public LocalizationCsvExportLanguageOption(string localeCode, bool isRegistered)
        {
            LocaleCode = localeCode ?? string.Empty;
            IsRegistered = isRegistered;
        }
    }

    internal readonly struct LocalizationCsvExportSelection
    {
        public readonly LocalizationCsvExportProfile Profile;
        public readonly bool FilteredOnly;
        public readonly int? TargetColumnIndex;
        public readonly bool RegisteredLocalesOnly;

        public LocalizationCsvEncoding Encoding
        {
            get
            {
                switch (Profile)
                {
                    case LocalizationCsvExportProfile.Spreadsheet:
                        return LocalizationCsvEncoding.Utf8WithBom;
                    case LocalizationCsvExportProfile.Automation:
                        return LocalizationCsvEncoding.Utf8WithoutBom;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Profile));
                }
            }
        }

        public LocalizationCsvExportSelection(
            LocalizationCsvExportProfile profile,
            bool filteredOnly,
            int? targetColumnIndex,
            bool registeredLocalesOnly)
        {
            Profile = profile;
            FilteredOnly = filteredOnly;
            TargetColumnIndex = targetColumnIndex;
            RegisteredLocalesOnly = registeredLocalesOnly;
        }
    }

    internal sealed class LocalizationCsvExportDialog : EditorWindow
    {
        private static readonly string[] ProfileLabels =
        {
            "Spreadsheet (Recommended)",
            "Automation & CI",
        };

        private const float WindowWidth = 520f;
        private const float WindowHeight = 400f;

        private string _tableId;
        private int _allKeyCount;
        private bool _hasActiveFilter;
        private int _filteredKeyCount;
        private string[] _languageOptions = Array.Empty<string>();
        private bool[] _targetLocaleRegistration = Array.Empty<bool>();
        private int _registeredLanguageCount;
        private bool _hasUnregisteredLocales;
        private LocalizationCsvExportProfile _profile;
        private bool _filteredOnly;
        private int _languageSelection;
        private Action<LocalizationCsvExportSelection> _onExport;

        public static void Open(
            EditorWindow owner,
            string tableId,
            int allKeyCount,
            bool hasActiveFilter,
            int filteredKeyCount,
            IReadOnlyList<LocalizationCsvExportLanguageOption> targetLocales,
            Action<LocalizationCsvExportSelection> onExport)
        {
            if (onExport == null)
                throw new ArgumentNullException(nameof(onExport));

            var window = CreateInstance<LocalizationCsvExportDialog>();
            window.titleContent = new GUIContent("Export Localization");
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = window.minSize;
            window._tableId = tableId ?? string.Empty;
            window._allKeyCount = Math.Max(0, allKeyCount);
            window._hasActiveFilter = hasActiveFilter;
            window._filteredKeyCount = Math.Max(0, filteredKeyCount);
            window._profile = LocalizationCsvExportProfile.Spreadsheet;
            window._filteredOnly = false;
            window._languageSelection = 0;
            window._onExport = onExport;

            int targetCount = targetLocales?.Count ?? 0;
            window._languageOptions = new string[targetCount + 1];
            window._targetLocaleRegistration = new bool[targetCount];
            window._registeredLanguageCount = 1;
            for (int index = 0; index < targetCount; index++)
            {
                LocalizationCsvExportLanguageOption option = targetLocales[index];
                window._targetLocaleRegistration[index] = option.IsRegistered;
                if (option.IsRegistered)
                    window._registeredLanguageCount++;
                else
                    window._hasUnregisteredLocales = true;
            }

            window._languageOptions[0] = window._hasUnregisteredLocales
                ? "All Registered Languages (" + window._registeredLanguageCount + ")"
                : "All Languages (" + (targetCount + 1) + ")";
            for (int index = 0; index < targetCount; index++)
            {
                LocalizationCsvExportLanguageOption option = targetLocales[index];
                window._languageOptions[index + 1] = "Source + " + option.LocaleCode +
                                                     (option.IsRegistered ? string.Empty : "  [Not in Settings]");
            }

            if (owner != null)
            {
                Rect ownerPosition = owner.position;
                window.position = new Rect(
                    ownerPosition.center.x - WindowWidth * 0.5f,
                    ownerPosition.center.y - WindowHeight * 0.5f,
                    WindowWidth,
                    WindowHeight);
            }

            window.ShowUtility();
            window.Focus();
        }

        private void OnDisable()
        {
            _onExport = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Export Localization", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Table: " + _tableId, EditorStyles.miniLabel);
            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
            _profile = (LocalizationCsvExportProfile)GUILayout.Toolbar((int)_profile, ProfileLabels);
            EditorGUILayout.HelpBox(
                _profile == LocalizationCsvExportProfile.Spreadsheet
                    ? "For planners and translators using Excel or other spreadsheet applications. Encoding: UTF-8 with BOM."
                    : "For scripts, source control, build pipelines, and CI. Encoding: UTF-8 without BOM.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Key Scope", EditorStyles.boldLabel);
            if (GUILayout.Toggle(!_filteredOnly, "All Keys (" + _allKeyCount + ")", EditorStyles.radioButton))
                _filteredOnly = false;

            bool canUseFiltered = _hasActiveFilter && _filteredKeyCount > 0;
            using (new EditorGUI.DisabledScope(!canUseFiltered))
            {
                string filteredLabel = _hasActiveFilter
                    ? "Current Results (" + _filteredKeyCount + ")"
                    : "Current Results (No Filter)";
                if (GUILayout.Toggle(_filteredOnly, filteredLabel, EditorStyles.radioButton))
                    _filteredOnly = true;
            }
            if (!canUseFiltered)
                _filteredOnly = false;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Languages", EditorStyles.boldLabel);
            _languageSelection = EditorGUILayout.Popup(_languageSelection, _languageOptions);

            bool selectedUnregisteredLocale = _languageSelection > 0 &&
                                              _languageSelection <= _targetLocaleRegistration.Length &&
                                              !_targetLocaleRegistration[_languageSelection - 1];
            if (selectedUnregisteredLocale)
            {
                EditorGUILayout.HelpBox(
                    "This locale table is not registered in LocalizationSettings. Export is allowed for backup or migration, but the locale is not active runtime content.",
                    MessageType.Warning);
            }

            int keyCount = _filteredOnly ? _filteredKeyCount : _allKeyCount;
            int languageCount = _languageSelection == 0
                ? (_hasUnregisteredLocales ? _registeredLanguageCount : _languageOptions.Length)
                : Math.Min(2, _languageOptions.Length);
            string profileLabel = _profile == LocalizationCsvExportProfile.Spreadsheet
                ? "Spreadsheet"
                : "Automation & CI";
            string encodingLabel = _profile == LocalizationCsvExportProfile.Spreadsheet
                ? "UTF-8 with BOM"
                : "UTF-8 without BOM";
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Summary: " + keyCount + " keys, " + languageCount + " languages\n" +
                "Profile: " + profileLabel + "    Encoding: " + encodingLabel +
                (_languageSelection == 0 && _hasUnregisteredLocales
                    ? "\nScope: registered locales only; inactive table assets are excluded."
                    : string.Empty),
                MessageType.None);

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
            {
                Close();
                return;
            }

            using (new EditorGUI.DisabledScope(keyCount <= 0 || _languageOptions.Length == 0))
            {
                if (GUILayout.Button("Export...", GUILayout.Width(110f)))
                {
                    var selection = new LocalizationCsvExportSelection(
                        _profile,
                        _filteredOnly,
                        _languageSelection == 0 ? (int?)null : _languageSelection,
                        _languageSelection == 0 && _hasUnregisteredLocales);
                    Action<LocalizationCsvExportSelection> callback = _onExport;
                    _onExport = null;
                    Close();
                    EditorApplication.delayCall += () => callback?.Invoke(selection);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
        }
    }

    /// <summary>
    /// Bounded export, detached validation/diff, and single-Undo import for string tables.
    /// </summary>
    internal sealed class LocalizationStringCsvWorkflow
    {
        private readonly LocalizationTableWorkspace _workspace;
        private readonly LocalizationMetadataIndex _metadata;
        private readonly Action _reload;

        public LocalizationStringCsvWorkflow(
            LocalizationTableWorkspace workspace,
            LocalizationMetadataIndex metadata,
            Action reload)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _reload = reload ?? throw new ArgumentNullException(nameof(reload));
        }

        public void ShowExportDialog(EditorWindow owner = null)
        {
            int keyCount = _workspace.HasAuthoringColumn
                ? _workspace.Columns[0].KeyToIndex.Count
                : 0;
            var targetLocales = new LocalizationCsvExportLanguageOption[Math.Max(0, _workspace.Columns.Count - 1)];
            for (int index = 1; index < _workspace.Columns.Count; index++)
            {
                targetLocales[index - 1] = new LocalizationCsvExportLanguageOption(
                    _workspace.Columns[index].LocaleCode,
                    true);
            }

            LocalizationCsvExportDialog.Open(
                owner,
                _workspace.TableId,
                keyCount,
                false,
                0,
                targetLocales,
                selection => Export(selection.TargetColumnIndex, selection.Encoding));
        }

        public void Import()
        {
            string path = EditorUtility.OpenFilePanel("Import Localization CSV", string.Empty, "csv");
            if (string.IsNullOrEmpty(path))
                return;
            if (!LocalizationCsv.TryReadFile(
                    path,
                    LocalizationCsvLimits.Default,
                    out LocalizationCsvDocument document,
                    out string readError))
            {
                EditorUtility.DisplayDialog("Import Localization CSV", readError, "OK");
                return;
            }
            if (!TryBuildImportPlan(document, out CsvImportPlan plan, out string validationError))
            {
                EditorUtility.DisplayDialog(
                    "Import Localization CSV",
                    "No assets were changed.\n\n" + validationError,
                    "OK");
                return;
            }
            if (plan.ValueChanges.Count == 0 && plan.StatusChanges.Count == 0)
            {
                EditorUtility.DisplayDialog("Import Localization CSV", "CSV is valid and already matches the project.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Import Localization CSV",
                    "Validated " + plan.RowCount + " row(s) for " + plan.LocaleCount + " included locale(s).\n" +
                    "Value changes: " + plan.ValueChanges.Count + "\n" +
                    "Status changes: " + plan.StatusChanges.Count + "\n" +
                    "Metadata entries to create: " + plan.MetadataKeysToCreate.Count + "\n\n" +
                    "Only locales included in this CSV will be changed.",
                    "Apply",
                    "Cancel"))
            {
                return;
            }
            ApplyImportPlan(plan);
        }

        private void Export(int? targetColumnIndex, LocalizationCsvEncoding encoding)
        {
            if (!_workspace.HasAuthoringColumn)
                return;
            string path = EditorUtility.SaveFilePanel(
                encoding == LocalizationCsvEncoding.Utf8WithBom
                    ? "Export Localization for Spreadsheet"
                    : "Export Localization for Automation",
                string.Empty,
                _workspace.TableId + ".csv",
                "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var exportColumns = new List<int>(_workspace.Columns.Count);
            if (targetColumnIndex.HasValue)
            {
                exportColumns.Add(targetColumnIndex.Value);
            }
            else
            {
                for (int index = 1; index < _workspace.Columns.Count; index++)
                    exportColumns.Add(index);
            }

            int csvColumnCount = 3 + exportColumns.Count * 3;
            if (csvColumnCount > LocalizationCsvLimits.Default.MaxColumns)
            {
                EditorUtility.DisplayDialog("Export Localization CSV", "Selected locales exceed the CSV column limit.", "OK");
                return;
            }

            LocalizationTableColumn authoring = _workspace.Columns[0];
            var keys = new List<string>(authoring.KeyToIndex.Keys);
            keys.Sort(StringComparer.Ordinal);
            var rows = new List<string[]>(keys.Count + 1);
            var header = new string[csvColumnCount];
            header[0] = "Key";
            header[1] = "SourceRevision";
            header[2] = _workspace.AuthoringLocaleCode;
            int headerIndex = 3;
            for (int index = 0; index < exportColumns.Count; index++)
            {
                string localeCode = _workspace.Columns[exportColumns[index]].LocaleCode;
                header[headerIndex++] = localeCode;
                header[headerIndex++] = localeCode + ".Status";
                header[headerIndex++] = localeCode + ".TranslatedSourceRevision";
            }
            rows.Add(header);

            for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                string key = keys[keyIndex];
                var row = new string[csvColumnCount];
                row[0] = key;
                row[1] = GetSourceRevision(key).ToString(CultureInfo.InvariantCulture);
                row[2] = authoring.PrimaryValues.TryGetValue(key, out string source) ? source : string.Empty;
                int cellIndex = 3;
                for (int exportIndex = 0; exportIndex < exportColumns.Count; exportIndex++)
                {
                    LocalizationTableColumn column = _workspace.Columns[exportColumns[exportIndex]];
                    bool hasValue = column.PrimaryValues.TryGetValue(key, out string value) &&
                                    !string.IsNullOrWhiteSpace(value);
                    GetTranslationState(
                        key,
                        column.LocaleCode,
                        out TranslationStatus status,
                        out int translatedRevision);
                    if (!hasValue)
                    {
                        value = string.Empty;
                        status = TranslationStatus.Missing;
                    }
                    row[cellIndex++] = value ?? string.Empty;
                    row[cellIndex++] = status.ToString();
                    row[cellIndex++] = translatedRevision.ToString(CultureInfo.InvariantCulture);
                }
                rows.Add(row);
            }

            if (!LocalizationCsv.TryWriteFile(
                    path,
                    rows,
                    LocalizationCsvLimits.Default,
                    encoding,
                    out string error))
            {
                EditorUtility.DisplayDialog("Export Localization CSV", error, "OK");
            }
        }

        private bool TryBuildImportPlan(
            LocalizationCsvDocument document,
            out CsvImportPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (!_workspace.HasAuthoringColumn)
            {
                error = "The selected table has no Authoring Locale column.";
                return false;
            }
            if (_metadata.Metadata == null)
            {
                error = "Create a StringTableMetadata asset before importing translation workflow state.";
                return false;
            }
            if (document.Rows.Count < 1)
            {
                error = "CSV has no header row.";
                return false;
            }

            string[] header = document.Rows[0];
            if (header.Length < 6 || (header.Length - 3) % 3 != 0 ||
                !string.Equals(header[0], "Key", StringComparison.Ordinal) ||
                !string.Equals(header[1], "SourceRevision", StringComparison.Ordinal) ||
                !string.Equals(header[2], _workspace.AuthoringLocaleCode, StringComparison.Ordinal))
            {
                error = "CSV header must be Key, SourceRevision, AuthoringLocale, followed by locale/status/revision triplets.";
                return false;
            }

            var localeColumns = new List<CsvLocaleColumns>((header.Length - 3) / 3);
            var includedLocales = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 3; index < header.Length; index += 3)
            {
                string localeCode = header[index];
                if (!includedLocales.Add(localeCode))
                {
                    error = "CSV header contains duplicate locale: " + localeCode;
                    return false;
                }
                if (!string.Equals(header[index + 1], localeCode + ".Status", StringComparison.Ordinal) ||
                    !string.Equals(header[index + 2], localeCode + ".TranslatedSourceRevision", StringComparison.Ordinal))
                {
                    error = "CSV workflow columns do not match locale '" + localeCode + "'.";
                    return false;
                }
                LocalizationTableColumn column = FindColumn(localeCode);
                if (column == null || string.Equals(localeCode, _workspace.AuthoringLocaleCode, StringComparison.Ordinal))
                {
                    error = "CSV locale has no target table in the selected table group: " + localeCode;
                    return false;
                }
                localeColumns.Add(new CsvLocaleColumns(column, index, index + 1, index + 2));
            }

            var result = new CsvImportPlan
            {
                RowCount = document.Rows.Count - 1,
                LocaleCount = localeColumns.Count,
            };
            var csvKeys = new HashSet<string>(StringComparer.Ordinal);
            var metadataKeys = new HashSet<string>(StringComparer.Ordinal);
            var pendingStateLocales = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            LocalizationTableColumn authoring = _workspace.Columns[0];

            for (int rowIndex = 1; rowIndex < document.Rows.Count; rowIndex++)
            {
                string[] row = document.Rows[rowIndex];
                if (row.Length != header.Length)
                {
                    error = "CSV row " + (rowIndex + 1) + " has " + row.Length + " fields; expected " + header.Length + ".";
                    return false;
                }
                string key = row[0];
                if (string.IsNullOrEmpty(key) || key.Length > LocalizationCatalogBuilder.MaxKeyChars || !csvKeys.Add(key))
                {
                    error = "CSV row " + (rowIndex + 1) + " has an empty, oversized, or duplicate key.";
                    return false;
                }
                if (!authoring.PrimaryValues.TryGetValue(key, out string currentSource))
                {
                    error = "CSV key does not exist in the Authoring Locale table: " + key;
                    return false;
                }
                if (!string.Equals(row[2], currentSource, StringComparison.Ordinal))
                {
                    error = "Authoring text changed for key '" + key + "'. Export a fresh CSV before importing.";
                    return false;
                }
                if (!int.TryParse(row[1], NumberStyles.None, CultureInfo.InvariantCulture, out int sourceRevision) ||
                    sourceRevision < 0 || sourceRevision != GetSourceRevision(key))
                {
                    error = "SourceRevision conflict for key '" + key + "'. Export a fresh CSV before importing.";
                    return false;
                }

                bool locked = _metadata.IsLocked(key);
                for (int localeIndex = 0; localeIndex < localeColumns.Count; localeIndex++)
                {
                    CsvLocaleColumns csvLocale = localeColumns[localeIndex];
                    string incomingValue = row[csvLocale.ValueIndex] ?? string.Empty;
                    if (incomingValue.Length > LocalizationCatalogBuilder.MaxStringValueChars)
                    {
                        error = "CSV value exceeds the supported length for key '" + key + "'.";
                        return false;
                    }
                    if (!Enum.TryParse(row[csvLocale.StatusIndex], true, out TranslationStatus incomingStatus) ||
                        !Enum.IsDefined(typeof(TranslationStatus), incomingStatus))
                    {
                        error = "CSV has an invalid TranslationStatus for key '" + key + "', locale '" +
                                csvLocale.Column.LocaleCode + "'.";
                        return false;
                    }
                    if (!int.TryParse(
                            row[csvLocale.RevisionIndex],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int incomingTranslatedRevision) ||
                        incomingTranslatedRevision < 0 || incomingTranslatedRevision > sourceRevision)
                    {
                        error = "CSV has an invalid translated source revision for key '" + key + "'.";
                        return false;
                    }

                    bool currentHasEntry = csvLocale.Column.KeyToIndex.ContainsKey(key);
                    string currentValue = csvLocale.Column.PrimaryValues.TryGetValue(key, out string cachedValue)
                        ? cachedValue
                        : string.Empty;
                    bool removeValue = incomingStatus == TranslationStatus.Missing ||
                                       string.IsNullOrWhiteSpace(incomingValue);
                    bool valueChanged = removeValue
                        ? currentHasEntry
                        : !currentHasEntry || !string.Equals(currentValue, incomingValue, StringComparison.Ordinal);
                    TranslationStatus committedStatus = valueChanged
                        ? (removeValue ? TranslationStatus.Missing : TranslationStatus.NeedsReview)
                        : incomingStatus;
                    int committedRevision = valueChanged ? sourceRevision : incomingTranslatedRevision;
                    GetTranslationState(
                        key,
                        csvLocale.Column.LocaleCode,
                        out TranslationStatus currentStatus,
                        out int currentTranslatedRevision);
                    bool statusChanged = currentStatus != committedStatus || currentTranslatedRevision != committedRevision;
                    if (locked && (valueChanged || statusChanged))
                    {
                        error = "Locked key '" + key + "' would be changed. No assets were modified.";
                        return false;
                    }

                    if (valueChanged)
                    {
                        result.ValueChanges.Add(new CsvValueChange(
                            csvLocale.Column,
                            key,
                            incomingValue,
                            removeValue ? CsvValueChangeKind.Remove : CsvValueChangeKind.AddOrUpdate));
                    }
                    if (!statusChanged)
                        continue;
                    result.StatusChanges.Add(new CsvStatusChange(
                        key,
                        csvLocale.Column.LocaleCode,
                        committedStatus,
                        committedRevision));
                    if (!_metadata.Contains(key) && metadataKeys.Add(key))
                        result.MetadataKeysToCreate.Add(key);
                    if (!pendingStateLocales.TryGetValue(key, out HashSet<string> locales))
                    {
                        locales = new HashSet<string>(StringComparer.Ordinal);
                        pendingStateLocales.Add(key, locales);
                    }
                    locales.Add(csvLocale.Column.LocaleCode);
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in pendingStateLocales)
            {
                int newCount = 0;
                foreach (string localeCode in pair.Value)
                {
                    if (!HasTranslationState(pair.Key, localeCode))
                        newCount++;
                }
                if (GetTranslationStatusCount(pair.Key) + newCount > StringTableMetadata.MaxLocaleStatusesPerEntry)
                {
                    error = "CSV would exceed the per-entry locale-status limit for key '" + pair.Key + "'.";
                    return false;
                }
            }

            plan = result;
            return true;
        }

        private void ApplyImportPlan(CsvImportPlan plan)
        {
            var targets = new List<UnityEngine.Object>(_workspace.Columns.Count + 1) { _metadata.Metadata };
            var changedTables = new HashSet<UnityEngine.Object>();
            for (int index = 0; index < plan.ValueChanges.Count; index++)
            {
                if (changedTables.Add(plan.ValueChanges[index].Column.Table))
                    targets.Add(plan.ValueChanges[index].Column.Table);
            }

            bool applied = LocalizationUndoTransaction.TryExecute(
                "Import Localization CSV",
                targets.ToArray(),
                () =>
                {
                    if (!_metadata.CreateEntries(
                            plan.MetadataKeysToCreate,
                            "Create Imported Localization Metadata",
                            out string metadataError))
                    {
                        throw new InvalidOperationException(metadataError);
                    }

                    for (int index = 0; index < plan.ValueChanges.Count; index++)
                    {
                        CsvValueChange change = plan.ValueChanges[index];
                        LocalizationTableColumn column = change.Column;
                        column.Serialized.UpdateIfRequiredOrScript();
                        if (change.Kind == CsvValueChangeKind.Remove)
                        {
                            if (column.KeyToIndex.TryGetValue(change.Key, out int existingIndex))
                                column.Entries.DeleteArrayElementAtIndex(existingIndex);
                        }
                        else if (column.KeyToIndex.TryGetValue(change.Key, out int existingIndex))
                        {
                            column.Entries.GetArrayElementAtIndex(existingIndex)
                                .FindPropertyRelative("Value").stringValue = change.Value;
                        }
                        else
                        {
                            int newIndex = column.Entries.arraySize;
                            column.Entries.InsertArrayElementAtIndex(newIndex);
                            SerializedProperty entry = column.Entries.GetArrayElementAtIndex(newIndex);
                            entry.FindPropertyRelative("Key").stringValue = change.Key;
                            entry.FindPropertyRelative("Value").stringValue = change.Value;
                        }
                        column.Serialized.ApplyModifiedProperties();
                        _workspace.RebuildColumn(column);
                    }

                    _metadata.Update();
                    for (int index = 0; index < plan.StatusChanges.Count; index++)
                    {
                        CsvStatusChange change = plan.StatusChanges[index];
                        if (!LocalizationMetadataIndex.TrySetTranslationState(
                                _metadata.GetEntry(change.Key),
                                change.LocaleCode,
                                change.Status,
                                change.TranslatedSourceRevision,
                                out string statusError))
                        {
                            throw new InvalidOperationException(statusError);
                        }
                    }
                    _metadata.Serialized.ApplyModifiedProperties();
                    _metadata.Rebuild();
                },
                out string error);

            if (!applied)
            {
                EditorUtility.DisplayDialog(
                    "Import Localization CSV",
                    "Import failed and the complete transaction was rolled back.\n\n" + error,
                    "OK");
            }
            _reload();
        }

        private LocalizationTableColumn FindColumn(string localeCode)
        {
            for (int index = 0; index < _workspace.Columns.Count; index++)
            {
                if (string.Equals(_workspace.Columns[index].LocaleCode, localeCode, StringComparison.Ordinal))
                    return _workspace.Columns[index];
            }
            return null;
        }

        private int GetSourceRevision(string key)
        {
            return _metadata.GetEntry(key)?.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
        }

        private void GetTranslationState(
            string key,
            string localeCode,
            out TranslationStatus status,
            out int translatedRevision)
        {
            status = TranslationStatus.Missing;
            translatedRevision = 0;
            SerializedProperty statuses = _metadata.GetEntry(key)?.FindPropertyRelative("LocaleStatuses");
            if (statuses == null || !statuses.isArray)
                return;
            for (int index = 0; index < statuses.arraySize; index++)
            {
                SerializedProperty candidate = statuses.GetArrayElementAtIndex(index);
                if (!string.Equals(
                        candidate.FindPropertyRelative("LocaleCode")?.stringValue,
                        localeCode,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                status = (TranslationStatus)(candidate.FindPropertyRelative("Status")?.enumValueIndex ?? 0);
                translatedRevision = candidate.FindPropertyRelative("TranslatedSourceRevision")?.intValue ?? 0;
                return;
            }
        }

        private int GetTranslationStatusCount(string key)
        {
            SerializedProperty statuses = _metadata.GetEntry(key)?.FindPropertyRelative("LocaleStatuses");
            return statuses != null && statuses.isArray ? statuses.arraySize : 0;
        }

        private bool HasTranslationState(string key, string localeCode)
        {
            SerializedProperty statuses = _metadata.GetEntry(key)?.FindPropertyRelative("LocaleStatuses");
            if (statuses == null || !statuses.isArray)
                return false;
            for (int index = 0; index < statuses.arraySize; index++)
            {
                if (string.Equals(
                        statuses.GetArrayElementAtIndex(index).FindPropertyRelative("LocaleCode")?.stringValue,
                        localeCode,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private enum CsvValueChangeKind : byte
        {
            AddOrUpdate,
            Remove,
        }

        private readonly struct CsvValueChange
        {
            public readonly LocalizationTableColumn Column;
            public readonly string Key;
            public readonly string Value;
            public readonly CsvValueChangeKind Kind;

            public CsvValueChange(
                LocalizationTableColumn column,
                string key,
                string value,
                CsvValueChangeKind kind)
            {
                Column = column;
                Key = key;
                Value = value;
                Kind = kind;
            }
        }

        private readonly struct CsvStatusChange
        {
            public readonly string Key;
            public readonly string LocaleCode;
            public readonly TranslationStatus Status;
            public readonly int TranslatedSourceRevision;

            public CsvStatusChange(
                string key,
                string localeCode,
                TranslationStatus status,
                int translatedSourceRevision)
            {
                Key = key;
                LocaleCode = localeCode;
                Status = status;
                TranslatedSourceRevision = translatedSourceRevision;
            }
        }

        private sealed class CsvImportPlan
        {
            public readonly List<CsvValueChange> ValueChanges = new List<CsvValueChange>();
            public readonly List<CsvStatusChange> StatusChanges = new List<CsvStatusChange>();
            public readonly List<string> MetadataKeysToCreate = new List<string>();
            public int RowCount;
            public int LocaleCount;
        }

        private readonly struct CsvLocaleColumns
        {
            public readonly LocalizationTableColumn Column;
            public readonly int ValueIndex;
            public readonly int StatusIndex;
            public readonly int RevisionIndex;

            public CsvLocaleColumns(
                LocalizationTableColumn column,
                int valueIndex,
                int statusIndex,
                int revisionIndex)
            {
                Column = column;
                ValueIndex = valueIndex;
                StatusIndex = statusIndex;
                RevisionIndex = revisionIndex;
            }
        }
    }
}
#endif
