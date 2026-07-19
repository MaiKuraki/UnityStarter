#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class MultiLanguageStringTableEditor : EditorWindow
    {
        private const float FoldButtonWidth = 54f;
        private const float KeyColumnWidth = 180f;
        private const float ValueColumnWidth = 220f;
        private const float DeleteButtonWidth = 22f;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float SeparatorHeight = 1f;
        private const float MetadataRowHeight = 176f;
        private const string ActiveValueControlName = "CycloneGames.Localization.ActiveValue";

        private static readonly GUIContent MetadataAssetButtonContent = new GUIContent(
            "Metadata Asset",
            "Select the table metadata asset. Delete the whole asset only through the Project window with normal version-control safeguards.");
        private static readonly GUIContent RemoveEntryMetadataContent = new GUIContent(
            "Remove Entry...",
            "Remove metadata for this Key only. String values and other metadata entries are not changed.");

        private static readonly Color MissingColor = new Color(1f, 0.6f, 0.2f, 0.25f);
        private static readonly Color DuplicateColor = new Color(0.85f, 0.2f, 0.25f, 0.35f);
        private static readonly Color LockedColor = new Color(0.35f, 0.45f, 0.65f, 0.18f);
        private static readonly Color AlternateRowColor = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color SeparatorColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color HeaderBackground = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color EditorBackground = new Color(0.22f, 0.22f, 0.22f, 1f);

        private static GUIStyle s_header;
        private static GUIStyle s_unregisteredHeader;
        private static GUIStyle s_missingButton;
        private static GUIStyle s_foldButton;
        private static GUIStyle s_metadataLabel;

        [SerializeField] private LocalizationSettings localizationSettings;

        private string[] _tableIds = Array.Empty<string>();
        private GUIContent[] _tableIdContents = Array.Empty<GUIContent>();
        private int _selectedTableIndex = -1;
        private string _requestedTableId;
        private bool _discoveryDirty = true;

        private readonly List<LocaleColumn> _columns = new List<LocaleColumn>();
        private readonly List<LocaleColumn> _unregisteredColumns = new List<LocaleColumn>();
        private readonly HashSet<string> _registeredLocaleCodes = new HashSet<string>(StringComparer.Ordinal);
        private string _unregisteredSummary;
        private readonly List<string> _allKeys = new List<string>();
        private readonly HashSet<string> _allKeySet = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _keysDirty = true;
        private int _missingCount;

        private readonly LocalizationMetadataIndex _metadata = new LocalizationMetadataIndex();
        private string _metadataError;
        private readonly HashSet<string> _expandedKeys = new HashSet<string>(StringComparer.Ordinal);

        private LocalizationSettings _resolvedSettings;
        private string _settingsError;
        private string _authoringLocaleCode;
        private bool _hasAuthoringColumn;
        private string _columnError;

        private string _newLocaleCode = string.Empty;
        private string _searchFilter = string.Empty;
        private bool _showDuplicatesOnly;
        private Vector2 _scrollPosition;
        private readonly LocalizationVisibleRowCache _rowCache = new LocalizationVisibleRowCache();
        private bool _rowCacheDirty = true;
        private LocaleColumn _draftColumn;
        private string _draftKey;
        private string _draftValue;
        private bool _hasValueDraft;
        private bool _focusValueDraft;
        private Rect _draftScreenRect;

        private float FrozenWidth =>
            FoldButtonWidth + KeyColumnWidth + (_hasAuthoringColumn ? ValueColumnWidth : 0f) + DeleteButtonWidth + 4f;

        private float ScrollableWidth => Math.Max(0, _columns.Count - (_hasAuthoringColumn ? 1 : 0)) * ValueColumnWidth;

        private bool CanEdit => string.IsNullOrEmpty(_settingsError) && string.IsNullOrEmpty(_columnError) && _hasAuthoringColumn;

        private static GUIStyle Header => s_header ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            padding = new RectOffset(4, 4, 2, 2),
            alignment = TextAnchor.MiddleLeft,
        };

        private static GUIStyle UnregisteredHeader
        {
            get
            {
                if (s_unregisteredHeader != null)
                    return s_unregisteredHeader;
                s_unregisteredHeader = new GUIStyle(Header);
                s_unregisteredHeader.normal.textColor = new Color(1f, 0.62f, 0.24f, 1f);
                return s_unregisteredHeader;
            }
        }

        private static GUIStyle MissingButton
        {
            get
            {
                if (s_missingButton != null)
                    return s_missingButton;
                s_missingButton = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Italic,
                    fontSize = 10,
                    padding = new RectOffset(6, 4, 1, 1),
                };
                s_missingButton.normal.textColor = new Color(1f, 0.55f, 0.3f, 0.9f);
                return s_missingButton;
            }
        }

        private static GUIStyle FoldButton => s_foldButton ??= new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(2, 2, 0, 0),
        };

        private static GUIStyle MetadataLabel
        {
            get
            {
                if (s_metadataLabel != null)
                    return s_metadataLabel;
                s_metadataLabel = new GUIStyle(EditorStyles.miniLabel);
                s_metadataLabel.normal.textColor = new Color(0.7f, 0.7f, 0.8f);
                return s_metadataLabel;
            }
        }

        private sealed class LocaleColumn
        {
            public string LocaleCode;
            public bool IsRegistered;
            public GUIContent HeaderContent;
            public GUIContent AuthoringHeaderContent;
            public GUIContent RegisterContent;
            public StringTable Table;
            public SerializedObject Serialized;
            public SerializedProperty Entries;
            public readonly Dictionary<string, int> KeyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly HashSet<string> DuplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        private enum KeySortMode : byte
        {
            OrdinalAscending,
            OrdinalDescending,
            NaturalAscending,
        }

        private readonly struct AuthoringEntrySnapshot
        {
            public readonly string Key;
            public readonly string Value;
            public readonly int OriginalIndex;

            public AuthoringEntrySnapshot(string key, string value, int originalIndex)
            {
                Key = key;
                Value = value;
                OriginalIndex = originalIndex;
            }
        }

        private sealed class AuthoringEntryComparer : IComparer<AuthoringEntrySnapshot>
        {
            public static readonly AuthoringEntryComparer OrdinalAscending =
                new AuthoringEntryComparer(KeySortMode.OrdinalAscending);
            public static readonly AuthoringEntryComparer OrdinalDescending =
                new AuthoringEntryComparer(KeySortMode.OrdinalDescending);
            public static readonly AuthoringEntryComparer NaturalAscending =
                new AuthoringEntryComparer(KeySortMode.NaturalAscending);

            private readonly KeySortMode _mode;

            private AuthoringEntryComparer(KeySortMode mode)
            {
                _mode = mode;
            }

            public int Compare(AuthoringEntrySnapshot left, AuthoringEntrySnapshot right)
            {
                int result;
                switch (_mode)
                {
                    case KeySortMode.OrdinalDescending:
                        result = CompareOrdinal(right.Key, left.Key);
                        break;
                    case KeySortMode.NaturalAscending:
                        result = CompareNaturalKeys(left.Key, right.Key);
                        break;
                    default:
                        result = CompareOrdinal(left.Key, right.Key);
                        break;
                }

                return result != 0 ? result : left.OriginalIndex.CompareTo(right.OriginalIndex);
            }

            private static int CompareOrdinal(string left, string right)
            {
                int result = string.CompareOrdinal(left, right);
                return result < 0 ? -1 : result > 0 ? 1 : 0;
            }
        }

        private enum CsvValueChangeKind : byte
        {
            AddOrUpdate,
            Remove,
        }

        private readonly struct CsvValueChange
        {
            public readonly LocaleColumn Column;
            public readonly string Key;
            public readonly string Value;
            public readonly CsvValueChangeKind Kind;

            public CsvValueChange(LocaleColumn column, string key, string value, CsvValueChangeKind kind)
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
            public readonly LocaleColumn Column;
            public readonly int ValueIndex;
            public readonly int StatusIndex;
            public readonly int RevisionIndex;

            public CsvLocaleColumns(LocaleColumn column, int valueIndex, int statusIndex, int revisionIndex)
            {
                Column = column;
                ValueIndex = valueIndex;
                StatusIndex = statusIndex;
                RevisionIndex = revisionIndex;
            }
        }

        [MenuItem("Tools/CycloneGames/Localization/Tables/String Table Editor")]
        public static void Open()
        {
            var window = GetWindow<MultiLanguageStringTableEditor>("String Table Editor");
            window.minSize = new Vector2(760f, 360f);
        }

        internal static void OpenForTable(string tableId)
        {
            var window = GetWindow<MultiLanguageStringTableEditor>("String Table Editor");
            window.minSize = new Vector2(760f, 360f);
            window._requestedTableId = tableId;
            window._discoveryDirty = true;
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _discoveryDirty = true;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnFocus()
        {
            _discoveryDirty = true;
        }

        private void OnLostFocus()
        {
            CommitActiveValueDraft();
        }

        private void OnProjectChanged()
        {
            _discoveryDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            CommitDraftWhenPointerLeavesCell();
            if (_discoveryDirty)
            {
                CommitActiveValueDraft();
                DiscoverTables();
            }

            DrawTableSelector();
            DrawConfigurationErrors();
            if (_selectedTableIndex < 0 || _columns.Count == 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "Select a Table ID, or create a StringTable asset with Create > CycloneGames > Localization > String Table.",
                    MessageType.Info);
                return;
            }

            UpdateSerializedObjects();
            if (_keysDirty)
                RebuildKeys();

            if (_duplicateKeys.Count > 0)
                DrawDuplicateBar();
            if (_unregisteredColumns.Count > 0)
                DrawUnregisteredLocalesBar();
            DrawToolbar();
            DrawTableArea();
            DrawStatusBar();
            ApplySerializedObjects();
        }

        private void DiscoverTables()
        {
            _discoveryDirty = false;
            string selectedId = !string.IsNullOrEmpty(_requestedTableId)
                ? _requestedTableId
                : _selectedTableIndex >= 0 && _selectedTableIndex < _tableIds.Length
                ? _tableIds[_selectedTableIndex]
                : null;
            _requestedTableId = null;

            ResolveSettings();
            string[] guids = AssetDatabase.FindAssets("t:StringTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (table != null && !string.IsNullOrEmpty(table.TableId))
                    ids.Add(table.TableId);
            }

            _tableIds = new string[ids.Count];
            _tableIdContents = new GUIContent[ids.Count];
            int itemIndex = 0;
            foreach (string id in ids)
            {
                _tableIds[itemIndex] = id;
                _tableIdContents[itemIndex] = new GUIContent(id);
                itemIndex++;
            }

            _selectedTableIndex = string.IsNullOrEmpty(selectedId)
                ? (_tableIds.Length > 0 ? 0 : -1)
                : Array.IndexOf(_tableIds, selectedId);
            if (_selectedTableIndex < 0 && _tableIds.Length > 0)
                _selectedTableIndex = 0;
            if (_selectedTableIndex >= 0)
                RefreshColumns(_tableIds[_selectedTableIndex]);
            else
                ClearTableState();
        }

        private void ResolveSettings()
        {
            _resolvedSettings = null;
            _settingsError = null;
            _authoringLocaleCode = null;
            _registeredLocaleCodes.Clear();
            if (!LocalizationEditorSettingsUtility.TryResolve(localizationSettings, out _resolvedSettings, out _settingsError))
                return;
            IReadOnlyList<Locale> availableLocales = _resolvedSettings.AvailableLocales;
            if (availableLocales != null)
            {
                for (int index = 0; index < availableLocales.Count; index++)
                {
                    Locale locale = availableLocales[index];
                    if (locale != null && locale.Id.IsValid)
                        _registeredLocaleCodes.Add(locale.Id.Code);
                }
            }
            if (!LocalizationEditorSettingsUtility.TryResolveAuthoringLocale(_resolvedSettings, out Locale authoringLocale, out _settingsError))
                return;
            _authoringLocaleCode = authoringLocale.Id.Code;
        }

        private void RefreshColumns(string tableId)
        {
            ClearTableState();
            var localeCodes = new HashSet<string>(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:StringTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null || !string.Equals(table.TableId, tableId, StringComparison.Ordinal))
                    continue;

                string localeCode = table.LocaleId.Code;
                if (!localeCodes.Add(localeCode))
                {
                    _columnError = "Multiple StringTable assets use tableId '" + tableId + "' and locale '" + localeCode + "'.";
                    continue;
                }

                var column = new LocaleColumn
                {
                    LocaleCode = localeCode,
                    IsRegistered = _resolvedSettings == null || SettingsContainsLocale(localeCode),
                    Table = table,
                    Serialized = new SerializedObject(table),
                };
                ConfigureColumnPresentation(column);
                column.Entries = column.Serialized.FindProperty("entries");
                RebuildColumnCache(column);
                _columns.Add(column);
                if (!column.IsRegistered)
                    _unregisteredColumns.Add(column);
            }

            _columns.Sort(CompareColumns);
            _unregisteredColumns.Sort(CompareColumns);
            _unregisteredSummary = _unregisteredColumns.Count == 0
                ? null
                : _unregisteredColumns.Count +
                  " locale table asset(s) are not registered in LocalizationSettings. Data is preserved, but these locales are inactive and validation remains blocked.";
            _hasAuthoringColumn = _columns.Count > 0 &&
                                  !string.IsNullOrEmpty(_authoringLocaleCode) &&
                                  string.Equals(_columns[0].LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(_settingsError) && !_hasAuthoringColumn)
            {
                _columnError = "Table '" + tableId + "' has no StringTable for Authoring Locale '" + _authoringLocaleCode + "'.";
            }

            RefreshMetadata(tableId);
            _keysDirty = true;
            _rowCacheDirty = true;
        }

        private void ClearTableState()
        {
            _columns.Clear();
            _unregisteredColumns.Clear();
            _unregisteredSummary = null;
            _allKeys.Clear();
            _allKeySet.Clear();
            _duplicateKeys.Clear();
            _metadata.Bind(null);
            _metadataError = null;
            _columnError = null;
            _hasAuthoringColumn = false;
            _expandedKeys.Clear();
            _keysDirty = true;
            _rowCacheDirty = true;
        }

        private void RefreshMetadata(string tableId)
        {
            StringTableMetadata match = null;
            string[] guids = AssetDatabase.FindAssets("t:StringTableMetadata");
            Array.Sort(guids, CompareAssetGuidByPath);
            for (int index = 0; index < guids.Length; index++)
            {
                var metadata = AssetDatabase.LoadAssetAtPath<StringTableMetadata>(AssetDatabase.GUIDToAssetPath(guids[index]));
                if (metadata == null || metadata.TableType != TableType.String ||
                    !string.Equals(metadata.TableId, tableId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    _metadataError = "Multiple StringTableMetadata assets use tableId '" + tableId + "' and type String.";
                    _metadata.Bind(null);
                    return;
                }
                match = metadata;
            }
            _metadata.Bind(match);
        }

        private void RebuildColumnCache(LocaleColumn column)
        {
            column.KeyToIndex.Clear();
            column.Values.Clear();
            column.DuplicateKeys.Clear();
            column.Serialized.UpdateIfRequiredOrScript();
            column.Entries = column.Serialized.FindProperty("entries");
            if (column.Entries == null || !column.Entries.isArray)
                return;

            for (int index = 0; index < column.Entries.arraySize; index++)
            {
                SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
                string key = entry.FindPropertyRelative("Key")?.stringValue ?? string.Empty;
                string value = entry.FindPropertyRelative("Value")?.stringValue ?? string.Empty;
                if (column.KeyToIndex.ContainsKey(key))
                    column.DuplicateKeys.Add(key);
                column.KeyToIndex[key] = index;
                column.Values[key] = value;
            }
        }

        private void RebuildKeys()
        {
            _keysDirty = false;
            _allKeys.Clear();
            _allKeySet.Clear();
            _duplicateKeys.Clear();
            _missingCount = 0;

            for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
            {
                LocaleColumn column = _columns[columnIndex];
                foreach (string duplicate in column.DuplicateKeys)
                    _duplicateKeys.Add(duplicate);
            }

            if (_hasAuthoringColumn)
            {
                AppendKeysInSerializedOrder(_columns[0]);
                var orphanKeys = new List<string>();
                for (int columnIndex = 1; columnIndex < _columns.Count; columnIndex++)
                {
                    foreach (string key in _columns[columnIndex].KeyToIndex.Keys)
                    {
                        if (_allKeySet.Add(key))
                            orphanKeys.Add(key);
                    }
                }
                orphanKeys.Sort(StringComparer.Ordinal);
                _allKeys.AddRange(orphanKeys);
            }
            else
            {
                for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                {
                    foreach (string key in _columns[columnIndex].KeyToIndex.Keys)
                    {
                        if (_allKeySet.Add(key))
                            _allKeys.Add(key);
                    }
                }
                _allKeys.Sort(StringComparer.Ordinal);
            }

            for (int keyIndex = 0; keyIndex < _allKeys.Count; keyIndex++)
            {
                for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                {
                    if (!_columns[columnIndex].KeyToIndex.ContainsKey(_allKeys[keyIndex]))
                        _missingCount++;
                }
            }

            _rowCacheDirty = true;
        }

        private void AppendKeysInSerializedOrder(LocaleColumn column)
        {
            column.Serialized.UpdateIfRequiredOrScript();
            for (int index = 0; index < column.Entries.arraySize; index++)
            {
                string key = column.Entries.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("Key")?.stringValue;
                if (!string.IsNullOrEmpty(key) && _allKeySet.Add(key))
                    _allKeys.Add(key);
            }
        }

        private void DrawTableSelector()
        {
            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            LocalizationSettings nextSettings = (LocalizationSettings)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Localization Settings",
                    "Optional explicit settings. Required when more than one LocalizationSettings asset exists."),
                localizationSettings,
                typeof(LocalizationSettings),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                localizationSettings = nextSettings;
                _discoveryDirty = true;
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Table ID", GUILayout.Width(52f));
            EditorGUI.BeginChangeCheck();
            _selectedTableIndex = EditorGUILayout.Popup(_selectedTableIndex, _tableIdContents);
            if (EditorGUI.EndChangeCheck() && _selectedTableIndex >= 0)
                RefreshColumns(_tableIds[_selectedTableIndex]);
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(56f)))
                _discoveryDirty = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (_columns.Count > 0)
            {
                var localeSummary = new StringBuilder(96);
                for (int index = 0; index < _columns.Count; index++)
                {
                    if (index > 0)
                        localeSummary.Append(", ");
                    localeSummary.Append(_columns[index].LocaleCode);
                    if (index == 0 && _hasAuthoringColumn)
                        localeSummary.Append(" (authoring)");
                }
                EditorGUILayout.LabelField("Locales (" + _columns.Count + "): " + localeSummary, EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("No locales found", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            if (_selectedTableIndex >= 0 && _columns.Count > 0)
            {
                if (_metadata.Metadata != null)
                {
                    if (GUILayout.Button(MetadataAssetButtonContent, EditorStyles.miniButton))
                    {
                        Selection.activeObject = _metadata.Metadata;
                        EditorGUIUtility.PingObject(_metadata.Metadata);
                    }
                }
                else if (string.IsNullOrEmpty(_metadataError) && GUILayout.Button("+ Metadata", EditorStyles.miniButton))
                {
                    CreateMetadataAsset();
                }
            }

            _newLocaleCode = EditorGUILayout.TextField(
                new GUIContent(string.Empty, "Locale code, for example en or zh-CN."),
                _newLocaleCode,
                GUILayout.Width(72f));
            using (new EditorGUI.DisabledScope(_resolvedSettings == null || _selectedTableIndex < 0))
            {
                if (GUILayout.Button("+ Locale", EditorStyles.miniButton) && !string.IsNullOrWhiteSpace(_newLocaleCode))
                {
                    CreateNewLocaleTable(_newLocaleCode.Trim());
                    _newLocaleCode = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfigurationErrors()
        {
            if (!string.IsNullOrEmpty(_settingsError))
            {
                EditorGUILayout.HelpBox(_settingsError, MessageType.Error);
                if (_resolvedSettings != null && GUILayout.Button("Select Localization Settings", EditorStyles.miniButton))
                {
                    Selection.activeObject = _resolvedSettings;
                    EditorGUIUtility.PingObject(_resolvedSettings);
                }
            }
            if (!string.IsNullOrEmpty(_columnError))
                EditorGUILayout.HelpBox(_columnError, MessageType.Error);
            if (!string.IsNullOrEmpty(_metadataError))
                EditorGUILayout.HelpBox(_metadataError, MessageType.Error);
        }

        private void DrawDuplicateBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(_duplicateKeys.Count + " duplicate key(s) require cleanup.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            bool nextFilter = GUILayout.Toggle(_showDuplicatesOnly, "Filter", EditorStyles.miniButton);
            if (nextFilter != _showDuplicatesOnly)
            {
                _showDuplicatesOnly = nextFilter;
                _rowCacheDirty = true;
            }
            using (new EditorGUI.DisabledScope(!CanEdit))
            {
                if (GUILayout.Button("Purge Duplicates", EditorStyles.miniButton))
                    PurgeDuplicateKeys();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawUnregisteredLocalesBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                _unregisteredSummary,
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            if (_unregisteredColumns.Count == 1)
            {
                LocaleColumn column = _unregisteredColumns[0];
                if (GUILayout.Button(column.RegisterContent, EditorStyles.miniButton))
                    RegisterExistingLocale(column);
                if (GUILayout.Button("Select Table", EditorStyles.miniButton))
                    SelectTableAsset(column);
            }
            else if (GUILayout.Button("Review...", EditorStyles.miniButton))
            {
                ShowUnregisteredLocalesMenu();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ShowUnregisteredLocalesMenu()
        {
            var menu = new GenericMenu();
            for (int index = 0; index < _unregisteredColumns.Count; index++)
            {
                LocaleColumn column = _unregisteredColumns[index];
                menu.AddItem(
                    new GUIContent(column.LocaleCode + "/Register Locale"),
                    false,
                    () => RegisterExistingLocale(column));
                menu.AddItem(
                    new GUIContent(column.LocaleCode + "/Select Table Asset"),
                    false,
                    () => SelectTableAsset(column));
            }
            menu.ShowAsContext();
        }

        private void RegisterExistingLocale(LocaleColumn column)
        {
            if (column == null || _resolvedSettings == null)
                return;
            if (!LocalizationLocaleAssetUtility.TryEnsureRegistered(
                    _resolvedSettings,
                    column.LocaleCode,
                    out _,
                    out _,
                    out string error))
            {
                EditorUtility.DisplayDialog("Register Locale", error, "OK");
                return;
            }

            ResolveSettings();
            _discoveryDirty = true;
            Repaint();
        }

        private static void SelectTableAsset(LocaleColumn column)
        {
            if (column?.Table == null)
                return;
            Selection.activeObject = column.Table;
            EditorGUIUtility.PingObject(column.Table);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            string nextSearch = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
            if (EditorGUI.EndChangeCheck())
            {
                _searchFilter = nextSearch;
                _rowCacheDirty = true;
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!CanEdit))
            {
                if (GUILayout.Button("+ Key", EditorStyles.toolbarButton))
                    AddKeyToAllColumns();
            }
            bool sortBlockedByFilter = !string.IsNullOrEmpty(_searchFilter) || _showDuplicatesOnly;
            bool canSort = CanEdit && _columns.Count > 0 && _columns[0].Entries.arraySize > 1 &&
                           _duplicateKeys.Count == 0 && !sortBlockedByFilter;
            using (new EditorGUI.DisabledScope(!canSort))
            {
                if (GUILayout.Button(new GUIContent("Sort", GetSortTooltip()), EditorStyles.toolbarDropDown))
                    ShowSortMenu();
            }
            using (new EditorGUI.DisabledScope(!CanEdit))
            {
                if (GUILayout.Button("Import", EditorStyles.toolbarButton))
                    ImportCsv();
            }
            using (new EditorGUI.DisabledScope(!_hasAuthoringColumn || _allKeys.Count == 0))
            {
                if (GUILayout.Button("Export", EditorStyles.toolbarButton))
                    ShowExportDialog();
            }
            EditorGUILayout.EndHorizontal();
        }

        private string GetSortTooltip()
        {
            if (!CanEdit)
                return "A valid Authoring Locale table is required.";
            if (_duplicateKeys.Count > 0)
                return "Resolve duplicate keys before sorting.";
            if (!string.IsNullOrEmpty(_searchFilter) || _showDuplicatesOnly)
                return "Clear search and duplicate filters before sorting the complete Authoring Locale table.";
            if (_columns.Count == 0 || _columns[0].Entries.arraySize < 2)
                return "At least two Authoring Locale keys are required.";
            return "Explicitly reorder Authoring Locale keys. Translation content and metadata are unchanged.";
        }

        private void ShowSortMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Key/A to Z (Ordinal)"),
                false,
                () => SortAuthoringEntries(KeySortMode.OrdinalAscending, true));
            menu.AddItem(
                new GUIContent("Key/Z to A (Ordinal)"),
                false,
                () => SortAuthoringEntries(KeySortMode.OrdinalDescending, true));
            menu.AddItem(
                new GUIContent("Key/Natural (A to Z)"),
                false,
                () => SortAuthoringEntries(KeySortMode.NaturalAscending, true));
            menu.ShowAsContext();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.Space(2f);
            var summary = new StringBuilder(160);
            summary.Append("Keys: ").Append(_allKeys.Count).Append("    ").Append(_columns.Count).Append(" locales");
            if (_duplicateKeys.Count > 0)
                summary.Append("  |  ").Append(_duplicateKeys.Count).Append(" duplicate(s)");
            if (_missingCount > 0)
                summary.Append("  |  ").Append(_missingCount).Append(" sparse cell(s)");
            if (_unregisteredColumns.Count > 0)
                summary.Append("  |  ").Append(_unregisteredColumns.Count).Append(" unregistered locale table(s)");
            if (_duplicateKeys.Count == 0 && _missingCount == 0 && _unregisteredColumns.Count == 0)
                summary.Append("  |  Complete");
            if (_metadata.Metadata != null)
                summary.Append("  |  Metadata: ").Append(_metadata.Metadata.name);
            EditorGUILayout.LabelField(summary.ToString(), EditorStyles.miniLabel);
        }

        private void DrawTableArea()
        {
            EnsureRowCache();
            Rect tableRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (tableRect.height < 40f)
                return;

            float frozenWidth = FrozenWidth;
            float scrollableWidth = ScrollableWidth;
            Rect bodyRect = new Rect(
                tableRect.x,
                tableRect.y + HeaderHeight + SeparatorHeight,
                tableRect.width,
                tableRect.height - HeaderHeight - SeparatorHeight);
            float naturalContentWidth = frozenWidth + scrollableWidth;
            LocalizationScrollViewport viewport = LocalizationScrollViewport.CalculateForCurrentSkin(
                bodyRect.width,
                bodyRect.height,
                naturalContentWidth,
                _rowCache.TotalHeight);
            float rightWidth = Mathf.Max(0f, viewport.Width - frozenWidth);

            DrawHeaders(tableRect, frozenWidth, scrollableWidth, rightWidth);
            EditorGUI.DrawRect(
                new Rect(tableRect.x, tableRect.y + HeaderHeight, tableRect.width, SeparatorHeight),
                SeparatorColor);

            Rect contentRect = new Rect(
                0f,
                0f,
                Mathf.Max(naturalContentWidth, viewport.Width),
                Mathf.Max(_rowCache.TotalHeight, viewport.Height));

            _scrollPosition = GUI.BeginScrollView(bodyRect, _scrollPosition, contentRect);
            LocalizationVisibleRange visibleRange = _rowCache.FindVisibleRange(_scrollPosition.y, viewport.Height);
            string deleteKey = null;
            string renameOldKey = null;
            string renameNewKey = null;
            float fixedX = _scrollPosition.x;

            for (int visibleIndex = visibleRange.Start; visibleIndex < visibleRange.EndExclusive; visibleIndex++)
            {
                int keyIndex = _rowCache.GetSourceIndex(visibleIndex);
                string key = _allKeys[keyIndex];
                float rowTop = _rowCache.GetTop(visibleIndex);
                bool expanded = _expandedKeys.Contains(key);
                bool locked = _metadata.IsLocked(key);

                if ((visibleIndex & 1) != 0)
                    EditorGUI.DrawRect(new Rect(0f, rowTop, contentRect.width, RowHeight), AlternateRowColor);
                if (locked)
                    EditorGUI.DrawRect(new Rect(0f, rowTop, contentRect.width, RowHeight), LockedColor);

                int scrollColumnStart = _hasAuthoringColumn ? 1 : 0;
                for (int columnIndex = scrollColumnStart; columnIndex < _columns.Count; columnIndex++)
                {
                    Rect cell = new Rect(
                        frozenWidth + (columnIndex - scrollColumnStart) * ValueColumnWidth,
                        rowTop,
                        ValueColumnWidth,
                        RowHeight);
                    DrawValueCell(_columns[columnIndex], key, cell, locked);
                }

                EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), EditorBackground);
                if ((visibleIndex & 1) != 0)
                    EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), AlternateRowColor);
                if (locked)
                    EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), LockedColor);
                EditorGUI.DrawRect(new Rect(fixedX + frozenWidth - 1f, rowTop, 1f, RowHeight), SeparatorColor);

                if (GUI.Button(
                        new Rect(fixedX + 2f, rowTop + 1f, FoldButtonWidth - 4f, RowHeight - 2f),
                        expanded ? "Hide" : "Details",
                        FoldButton))
                {
                    if (expanded)
                        _expandedKeys.Remove(key);
                    else
                        _expandedKeys.Add(key);
                    _rowCacheDirty = true;
                }

                Rect keyRect = new Rect(fixedX + FoldButtonWidth, rowTop, KeyColumnWidth, RowHeight);
                if (_duplicateKeys.Contains(key))
                    EditorGUI.DrawRect(keyRect, DuplicateColor);
                using (new EditorGUI.DisabledScope(!CanEdit || locked))
                {
                    EditorGUI.BeginChangeCheck();
                    string nextKey = EditorGUI.DelayedTextField(keyRect, key);
                    if (EditorGUI.EndChangeCheck() && !string.Equals(nextKey, key, StringComparison.Ordinal))
                    {
                        renameOldKey = key;
                        renameNewKey = nextKey;
                    }
                }

                float deleteX = fixedX + FoldButtonWidth + KeyColumnWidth;
                if (_hasAuthoringColumn)
                {
                    Rect authoringCell = new Rect(deleteX, rowTop, ValueColumnWidth, RowHeight);
                    DrawValueCell(_columns[0], key, authoringCell, locked);
                    deleteX += ValueColumnWidth;
                }

                using (new EditorGUI.DisabledScope(!CanEdit || locked))
                {
                    if (GUI.Button(new Rect(deleteX + 2f, rowTop, DeleteButtonWidth, RowHeight), "X"))
                        deleteKey = key;
                }

                if (expanded)
                {
                    float metadataTop = rowTop + RowHeight;
                    float visibleWidth = Mathf.Min(naturalContentWidth, viewport.Width);
                    EditorGUI.DrawRect(new Rect(fixedX, metadataTop, visibleWidth, MetadataRowHeight), EditorBackground);
                    DrawMetadataSubRow(
                        key,
                        fixedX + FoldButtonWidth,
                        metadataTop,
                        visibleWidth - FoldButtonWidth);
                }
            }

            GUI.EndScrollView();
            if (deleteKey != null)
                RequestDeleteKey(deleteKey);
            else if (renameOldKey != null)
                RequestRenameKey(renameOldKey, renameNewKey);
        }

        private void DrawHeaders(Rect tableRect, float frozenWidth, float scrollableWidth, float rightWidth)
        {
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y, tableRect.width, HeaderHeight), HeaderBackground);
            Rect frozenHeader = new Rect(tableRect.x, tableRect.y, frozenWidth, HeaderHeight);
            EditorGUI.DrawRect(frozenHeader, HeaderBackground);
            GUI.Label(
                new Rect(tableRect.x, tableRect.y, FoldButtonWidth, HeaderHeight),
                "Details",
                Header);
            GUI.Label(
                new Rect(tableRect.x + FoldButtonWidth, tableRect.y, KeyColumnWidth, HeaderHeight),
                "Key",
                Header);

            float headerX = tableRect.x + FoldButtonWidth + KeyColumnWidth;
            if (_hasAuthoringColumn)
            {
                LocaleColumn authoringColumn = _columns[0];
                if (GUI.Button(
                        new Rect(headerX, tableRect.y, ValueColumnWidth, HeaderHeight),
                        authoringColumn.AuthoringHeaderContent,
                        authoringColumn.IsRegistered ? Header : UnregisteredHeader))
                {
                    SelectTableAsset(authoringColumn);
                }
            }

            if (rightWidth <= 0f)
                return;

            Rect clip = new Rect(tableRect.x + frozenWidth, tableRect.y, rightWidth, HeaderHeight);
            GUI.BeginClip(clip);
            EditorGUI.DrawRect(new Rect(0f, 0f, Mathf.Max(scrollableWidth, rightWidth), HeaderHeight), HeaderBackground);
            int start = _hasAuthoringColumn ? 1 : 0;
            for (int columnIndex = start; columnIndex < _columns.Count; columnIndex++)
            {
                LocaleColumn column = _columns[columnIndex];
                Rect localeRect = new Rect(
                    -_scrollPosition.x + (columnIndex - start) * ValueColumnWidth,
                    0f,
                    ValueColumnWidth,
                    HeaderHeight);
                if (GUI.Button(localeRect, column.HeaderContent, column.IsRegistered ? Header : UnregisteredHeader))
                {
                    SelectTableAsset(column);
                }
            }
            GUI.EndClip();
        }

        private void DrawValueCell(LocaleColumn column, string key, Rect cell, bool locked)
        {
            bool isAuthoring = string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            bool hasEntry = column.KeyToIndex.TryGetValue(key, out _);
            string currentValue = column.Values.TryGetValue(key, out string cached) ? cached : string.Empty;

            if (IsActiveValueDraft(column, key))
            {
                DrawActiveValueDraft(cell, locked);
                return;
            }

            bool effectiveMissing = !hasEntry || (!isAuthoring && string.IsNullOrWhiteSpace(currentValue));
            if (effectiveMissing)
            {
                EditorGUI.DrawRect(cell, MissingColor);
                using (new EditorGUI.DisabledScope(!CanEdit || locked))
                {
                    string label = isAuthoring
                        ? "Missing source - click to edit"
                        : "Fallback - click to override";
                    if (GUI.Button(cell, label, MissingButton))
                        BeginValueDraft(column, key, currentValue);
                }
                return;
            }

            if (CanEdit && !locked && Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                BeginValueDraft(column, key, currentValue);

            if (IsActiveValueDraft(column, key))
            {
                DrawActiveValueDraft(cell, locked);
                return;
            }

            using (new EditorGUI.DisabledScope(!CanEdit || locked))
                EditorGUI.TextField(cell, currentValue);
        }

        private void CommitValueChange(LocaleColumn column, int entryIndex, string key, string nextValue)
        {
            if (_metadata.IsLocked(key))
                return;

            var translatedLocales = new List<string>(_columns.Count);
            if (string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal))
            {
                for (int index = 0; index < _columns.Count; index++)
                {
                    LocaleColumn candidate = _columns[index];
                    if (string.Equals(candidate.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal))
                        continue;
                    if (candidate.Values.TryGetValue(key, out string translation) && !string.IsNullOrWhiteSpace(translation))
                        translatedLocales.Add(candidate.LocaleCode);
                }
            }

            UnityEngine.Object[] targets = _metadata.Metadata != null
                ? new UnityEngine.Object[] { column.Table, _metadata.Metadata }
                : new UnityEngine.Object[] { column.Table };
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Edit Localization Value",
                targets,
                () =>
                {
                    bool workflowUpdated;
                    string workflowError;
                    if (string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal))
                    {
                        workflowUpdated = _metadata.MarkAuthoringChanged(
                            key,
                            translatedLocales,
                            "Update Localization Source Revision",
                            out workflowError);
                    }
                    else
                    {
                        workflowUpdated = _metadata.MarkTranslationChanged(
                            key,
                            column.LocaleCode,
                            !string.IsNullOrWhiteSpace(nextValue),
                            "Update Translation Status",
                            out workflowError);
                    }

                    if (!workflowUpdated)
                        throw new InvalidOperationException(workflowError);

                    column.Serialized.UpdateIfRequiredOrScript();
                    SerializedProperty value = column.Entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("Value");
                    value.stringValue = nextValue;
                    column.Serialized.ApplyModifiedProperties();
                },
                out string error);

            if (!applied)
            {
                EditorUtility.DisplayDialog("Localization Edit", "Value was not changed. " + error, "OK");
                RefreshColumns(_tableIds[_selectedTableIndex]);
                return;
            }

            column.Values[key] = nextValue;
            _rowCacheDirty = !string.IsNullOrEmpty(_searchFilter);
        }

        private void AddMissingKey(LocaleColumn column, string key, string value)
        {
            if (_metadata.IsLocked(key))
                return;

            bool isAuthoring = string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            List<string> translatedLocales = isAuthoring
                ? CollectPopulatedTranslationLocales(key)
                : null;

            UnityEngine.Object[] targets = _metadata.Metadata != null
                ? new UnityEngine.Object[] { column.Table, _metadata.Metadata }
                : new UnityEngine.Object[] { column.Table };
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Add Localization Entry",
                targets,
                () =>
                {
                    column.Serialized.UpdateIfRequiredOrScript();
                    int index = column.Entries.arraySize;
                    column.Entries.InsertArrayElementAtIndex(index);
                    SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("Key").stringValue = key;
                    entry.FindPropertyRelative("Value").stringValue = value;
                    string workflowError;
                    bool workflowUpdated;
                    if (isAuthoring)
                    {
                        workflowUpdated = _metadata.MarkAuthoringChanged(
                            key,
                            translatedLocales,
                            "Update Localization Source Revision",
                            out workflowError);
                    }
                    else
                    {
                        workflowUpdated = _metadata.MarkTranslationChanged(
                            key,
                            column.LocaleCode,
                            true,
                            "Update Translation Status",
                            out workflowError);
                    }
                    if (!workflowUpdated)
                    {
                        throw new InvalidOperationException(workflowError);
                    }
                    column.Serialized.ApplyModifiedProperties();
                },
                out string error);

            if (!applied)
            {
                EditorUtility.DisplayDialog("Localization Edit", "Entry was not added. " + error, "OK");
                RefreshColumns(_tableIds[_selectedTableIndex]);
                return;
            }

            RebuildColumnCache(column);
            _keysDirty = true;
        }

        private void RemoveTranslationOverride(LocaleColumn column, int entryIndex, string key)
        {
            UnityEngine.Object[] targets = _metadata.Metadata != null
                ? new UnityEngine.Object[] { column.Table, _metadata.Metadata }
                : new UnityEngine.Object[] { column.Table };
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Restore Localization Fallback",
                targets,
                () =>
                {
                    if (!_metadata.MarkTranslationChanged(
                            key,
                            column.LocaleCode,
                            false,
                            "Restore Localization Fallback",
                            out string workflowError))
                    {
                        throw new InvalidOperationException(workflowError);
                    }

                    column.Serialized.UpdateIfRequiredOrScript();
                    column.Entries.DeleteArrayElementAtIndex(entryIndex);
                    column.Serialized.ApplyModifiedProperties();
                },
                out string error);
            if (!applied)
            {
                EditorUtility.DisplayDialog("Localization Edit", "Fallback was not restored. " + error, "OK");
                RefreshColumns(_tableIds[_selectedTableIndex]);
                return;
            }

            RebuildColumnCache(column);
            _keysDirty = true;
        }

        private List<string> CollectPopulatedTranslationLocales(string key)
        {
            var translatedLocales = new List<string>(_columns.Count);
            for (int index = 0; index < _columns.Count; index++)
            {
                LocaleColumn candidate = _columns[index];
                if (string.Equals(candidate.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal))
                    continue;
                if (candidate.Values.TryGetValue(key, out string translation) &&
                    !string.IsNullOrWhiteSpace(translation))
                {
                    translatedLocales.Add(candidate.LocaleCode);
                }
            }
            return translatedLocales;
        }

        private void BeginValueDraft(LocaleColumn column, string key, string value)
        {
            if (IsActiveValueDraft(column, key))
                return;

            CommitActiveValueDraft();
            _draftColumn = column;
            _draftKey = key;
            _draftValue = value ?? string.Empty;
            _hasValueDraft = true;
            _focusValueDraft = true;
            Repaint();
        }

        private bool IsActiveValueDraft(LocaleColumn column, string key)
        {
            return _hasValueDraft && ReferenceEquals(_draftColumn, column) &&
                   string.Equals(_draftKey, key, StringComparison.Ordinal);
        }

        private void DrawActiveValueDraft(Rect cell, bool locked)
        {
            Vector2 screenPosition = GUIUtility.GUIToScreenPoint(cell.position);
            _draftScreenRect = new Rect(screenPosition, cell.size);
            GUI.SetNextControlName(ActiveValueControlName);
            using (new EditorGUI.DisabledScope(!CanEdit || locked))
            {
                EditorGUI.BeginChangeCheck();
                string nextValue = EditorGUI.TextField(cell, _draftValue ?? string.Empty);
                if (EditorGUI.EndChangeCheck())
                    _draftValue = nextValue;
            }

            if (_focusValueDraft)
            {
                GUI.FocusControl(ActiveValueControlName);
                EditorGUI.FocusTextInControl(ActiveValueControlName);
                _focusValueDraft = false;
            }

            Event current = Event.current;
            if (current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) &&
                string.Equals(GUI.GetNameOfFocusedControl(), ActiveValueControlName, StringComparison.Ordinal))
            {
                current.Use();
                GUI.FocusControl(null);
                CommitActiveValueDraft();
            }
        }

        private void CommitDraftWhenPointerLeavesCell()
        {
            if (!_hasValueDraft || _focusValueDraft || Event.current.type != EventType.MouseDown)
                return;

            Vector2 screenPoint = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            if (!_draftScreenRect.Contains(screenPoint))
                CommitActiveValueDraft();
        }

        private void CommitActiveValueDraft()
        {
            if (!_hasValueDraft)
                return;

            LocaleColumn column = _draftColumn;
            string key = _draftKey;
            string value = _draftValue ?? string.Empty;
            _draftColumn = null;
            _draftKey = null;
            _draftValue = null;
            _hasValueDraft = false;
            _focusValueDraft = false;
            _draftScreenRect = default;

            if (column == null || string.IsNullOrEmpty(key) || _metadata.IsLocked(key))
                return;

            bool isAuthoring = string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            bool hasEntry = column.KeyToIndex.TryGetValue(key, out int entryIndex);
            if (!isAuthoring && string.IsNullOrWhiteSpace(value))
            {
                if (hasEntry)
                    RemoveTranslationOverride(column, entryIndex, key);
                return;
            }

            if (hasEntry)
            {
                string currentValue = column.Values.TryGetValue(key, out string cached) ? cached : string.Empty;
                if (!string.Equals(currentValue, value, StringComparison.Ordinal))
                    CommitValueChange(column, entryIndex, key, value);
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
                AddMissingKey(column, key, value);
        }

        private void EnsureRowCache()
        {
            if (!_rowCacheDirty)
                return;

            _rowCacheDirty = false;
            _rowCache.Rebuild(
                _allKeys.Count,
                keyIndex => MatchesFilter(_allKeys[keyIndex]),
                keyIndex => RowHeight + (_expandedKeys.Contains(_allKeys[keyIndex]) ? MetadataRowHeight : 0f));
        }

        private bool MatchesFilter(string key)
        {
            if (_showDuplicatesOnly && !_duplicateKeys.Contains(key))
                return false;
            if (string.IsNullOrEmpty(_searchFilter))
                return true;
            if (key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            for (int index = 0; index < _columns.Count; index++)
            {
                if (_columns[index].Values.TryGetValue(key, out string value) &&
                    value.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawMetadataSubRow(string key, float x, float y, float width)
        {
            Rect box = new Rect(x, y, width, MetadataRowHeight - 4f);
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);
            float contentX = x + 4f;
            float contentY = y + 4f;
            float contentWidth = Mathf.Max(0f, width - 8f);

            if (_metadata.Metadata == null)
            {
                GUI.Label(new Rect(contentX, contentY, 260f, 16f), "No metadata asset is assigned to this table.", EditorStyles.miniLabel);
                if (string.IsNullOrEmpty(_metadataError) &&
                    GUI.Button(new Rect(contentX, contentY + 20f, 150f, 18f), "Create Metadata Asset", EditorStyles.miniButton))
                {
                    CreateMetadataAsset();
                }
                return;
            }

            SerializedProperty entry = _metadata.GetEntry(key);
            if (entry == null)
            {
                GUI.Label(
                    new Rect(contentX, contentY, contentWidth, 32f),
                    "No metadata entry exists. Expanding a row never mutates metadata.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUI.Button(new Rect(contentX, contentY + 36f, 160f, 18f), "Create Entry Metadata", EditorStyles.miniButton))
                {
                    if (!_metadata.CreateEntry(key, "Create Localization Metadata Entry", out string error))
                        EditorUtility.DisplayDialog("Localization Metadata", error, "OK");
                }
                return;
            }

            SerializedProperty lockedProperty = entry.FindPropertyRelative("Locked");
            bool locked = lockedProperty.boolValue;
            bool nextLocked = EditorGUI.ToggleLeft(
                new Rect(contentX, contentY, 70f, 18f),
                "Locked",
                locked);
            if (nextLocked != locked)
            {
                Undo.RecordObject(_metadata.Metadata, nextLocked ? "Lock Localization Entry" : "Unlock Localization Entry");
                lockedProperty.boolValue = nextLocked;
                _metadata.Serialized.ApplyModifiedProperties();
                locked = nextLocked;
            }

            int sourceRevision = entry.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
            GUI.Label(
                new Rect(contentX + 82f, contentY + 1f, 190f, 16f),
                "Source Revision: " + sourceRevision,
                MetadataLabel);

            const float removeButtonWidth = 126f;
            using (new EditorGUI.DisabledScope(locked || !CanEdit))
            {
                if (GUI.Button(
                        new Rect(
                            contentX + Mathf.Max(0f, contentWidth - removeButtonWidth),
                            contentY,
                            Mathf.Min(removeButtonWidth, contentWidth),
                            18f),
                        RemoveEntryMetadataContent,
                        EditorStyles.miniButton))
                {
                    if (EditorUtility.DisplayDialog(
                            "Remove Entry Metadata",
                            "Remove metadata for key '" + key + "'?\n\n" +
                            "This removes its lock, source revision, translation statuses, comment, maximum length, tags, and screenshot reference. " +
                            "String values and metadata for other keys remain unchanged. If metadata is recreated later, its source revision starts at 0.",
                            "Remove",
                            "Cancel"))
                    {
                        if (!_metadata.RemoveEntry(key, "Remove Localization Metadata Entry", out string error))
                            EditorUtility.DisplayDialog("Remove Entry Metadata", error, "OK");
                        else
                            Repaint();
                    }
                    return;
                }
            }

            GUI.Label(
                new Rect(contentX, contentY + 22f, contentWidth, 16f),
                BuildStatusSummary(entry),
                MetadataLabel);

            using (new EditorGUI.DisabledScope(locked))
            {
                string comment = entry.FindPropertyRelative("Comment")?.stringValue ?? string.Empty;
                GUI.Label(new Rect(contentX, contentY + 44f, 60f, 14f), "Comment", MetadataLabel);
                string nextComment = EditorGUI.TextArea(
                    new Rect(contentX, contentY + 58f, contentWidth, 40f),
                    comment);

                SerializedProperty maxLengthProperty = entry.FindPropertyRelative("MaxLength");
                SerializedProperty tagsProperty = entry.FindPropertyRelative("Tags");
                SerializedProperty screenshotProperty = entry.FindPropertyRelative("Screenshot");
                int maxLength = maxLengthProperty?.intValue ?? 0;
                string tags = tagsProperty?.stringValue ?? string.Empty;
                float propertiesY = contentY + 104f;
                GUI.Label(new Rect(contentX, propertiesY, 68f, 18f), "Max Length", MetadataLabel);
                int nextMaxLength = EditorGUI.IntField(new Rect(contentX + 70f, propertiesY, 58f, 18f), maxLength);
                GUI.Label(new Rect(contentX + 142f, propertiesY, 32f, 18f), "Tags", MetadataLabel);
                string nextTags = EditorGUI.TextField(
                    new Rect(contentX + 176f, propertiesY, Mathf.Max(0f, contentWidth - 176f), 18f),
                    tags);

                UnityEngine.Object screenshot = screenshotProperty?.objectReferenceValue;
                float screenshotY = contentY + 130f;
                GUI.Label(new Rect(contentX, screenshotY, 68f, 18f), "Screenshot", MetadataLabel);
                UnityEngine.Object nextScreenshot = EditorGUI.ObjectField(
                    new Rect(contentX + 70f, screenshotY, Mathf.Max(0f, contentWidth - 70f), 18f),
                    screenshot,
                    typeof(Texture2D),
                    false);

                if (!string.Equals(nextComment, comment, StringComparison.Ordinal) ||
                    nextMaxLength != maxLength ||
                    !string.Equals(nextTags, tags, StringComparison.Ordinal) ||
                    nextScreenshot != screenshot)
                {
                    Undo.RecordObject(_metadata.Metadata, "Edit Localization Metadata");
                    entry.FindPropertyRelative("Comment").stringValue = nextComment;
                    maxLengthProperty.intValue = Math.Max(0, nextMaxLength);
                    tagsProperty.stringValue = nextTags;
                    if (screenshotProperty != null)
                        screenshotProperty.objectReferenceValue = nextScreenshot;
                    _metadata.Serialized.ApplyModifiedProperties();
                }
            }
        }

        private static string BuildStatusSummary(SerializedProperty entry)
        {
            SerializedProperty statuses = entry.FindPropertyRelative("LocaleStatuses");
            if (statuses == null || !statuses.isArray || statuses.arraySize == 0)
                return "No translation status records";

            int draft = 0;
            int review = 0;
            int approved = 0;
            int stale = 0;
            for (int index = 0; index < statuses.arraySize; index++)
            {
                int status = statuses.GetArrayElementAtIndex(index).FindPropertyRelative("Status")?.enumValueIndex ?? 0;
                if (status == (int)TranslationStatus.Draft) draft++;
                else if (status == (int)TranslationStatus.NeedsReview) review++;
                else if (status == (int)TranslationStatus.Approved) approved++;
                else if (status == (int)TranslationStatus.Stale) stale++;
            }
            return "Status — Draft: " + draft + "  Review: " + review + "  Approved: " + approved + "  Stale: " + stale;
        }

        private void RequestDeleteKey(string key)
        {
            if (_metadata.IsLocked(key))
            {
                EditorUtility.DisplayDialog("Delete Localization Key", "The key is locked. Unlock it before deleting table entries.", "OK");
                return;
            }

            int tableCount = 0;
            int populatedCount = 0;
            for (int index = 0; index < _columns.Count; index++)
            {
                if (!_columns[index].KeyToIndex.ContainsKey(key))
                    continue;
                tableCount++;
                if (_columns[index].Values.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                    populatedCount++;
            }

            string metadataImpact = _metadata.Contains(key)
                ? "The metadata entry is retained and will be reported as orphaned until handled explicitly."
                : "No metadata entry exists.";
            if (!EditorUtility.DisplayDialog(
                    "Delete Localization Key",
                    "Delete '" + key + "' from " + tableCount + " locale table(s)?\n" +
                    populatedCount + " populated value(s) will be removed.\n\n" + metadataImpact,
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            UnityEngine.Object[] targets = GetColumnTargets();
            if (!LocalizationUndoTransaction.TryExecute(
                    "Delete Localization Key",
                    targets,
                    () => RemoveKeyFromColumns(key),
                    out string error))
            {
                EditorUtility.DisplayDialog("Delete Localization Key", "Delete failed and was rolled back. " + error, "OK");
            }
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void RequestRenameKey(string oldKey, string newKey)
        {
            newKey = newKey?.Trim();
            if (string.IsNullOrEmpty(newKey) || newKey.Length > LocalizationCatalogBuilder.MaxKeyChars)
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The new key is empty or exceeds the supported length.", "OK");
                return;
            }
            if (_allKeySet.Contains(newKey))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The key already exists: " + newKey, "OK");
                return;
            }
            if (_metadata.IsLocked(oldKey))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The key is locked. Unlock it before renaming table entries.", "OK");
                return;
            }

            int tableCount = 0;
            for (int index = 0; index < _columns.Count; index++)
            {
                if (_columns[index].KeyToIndex.ContainsKey(oldKey))
                    tableCount++;
            }
            string metadataImpact = _metadata.Contains(oldKey)
                ? "Metadata remains under the original key and must be migrated explicitly."
                : "No metadata entry exists.";
            if (!EditorUtility.DisplayDialog(
                    "Rename Localization Key",
                    "Rename '" + oldKey + "' to '" + newKey + "' in " + tableCount + " locale table(s)?\n\n" + metadataImpact,
                    "Rename",
                    "Cancel"))
            {
                return;
            }

            if (!LocalizationUndoTransaction.TryExecute(
                    "Rename Localization Key",
                    GetColumnTargets(),
                    () => RenameKeyInColumns(oldKey, newKey),
                    out string error))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "Rename failed and was rolled back. " + error, "OK");
            }
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void RemoveKeyFromColumns(string key)
        {
            for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
            {
                LocaleColumn column = _columns[columnIndex];
                column.Serialized.UpdateIfRequiredOrScript();
                if (!column.KeyToIndex.TryGetValue(key, out int entryIndex))
                    continue;
                column.Entries.DeleteArrayElementAtIndex(entryIndex);
                column.Serialized.ApplyModifiedProperties();
                RebuildColumnCache(column);
            }
        }

        private void RenameKeyInColumns(string oldKey, string newKey)
        {
            for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
            {
                LocaleColumn column = _columns[columnIndex];
                column.Serialized.UpdateIfRequiredOrScript();
                if (!column.KeyToIndex.TryGetValue(oldKey, out int entryIndex))
                    continue;
                column.Entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("Key").stringValue = newKey;
                column.Serialized.ApplyModifiedProperties();
                RebuildColumnCache(column);
            }
        }

        private void PurgeDuplicateKeys()
        {
            foreach (string key in _duplicateKeys)
            {
                if (_metadata.IsLocked(key))
                {
                    EditorUtility.DisplayDialog(
                        "Purge Duplicate Keys",
                        "Duplicate key '" + key + "' is locked. No entries were changed.",
                        "OK");
                    return;
                }
            }
            if (!EditorUtility.DisplayDialog(
                    "Purge Duplicate Keys",
                    "Remove duplicate occurrences while keeping the last occurrence in each locale table?",
                    "Purge",
                    "Cancel"))
            {
                return;
            }

            bool applied = LocalizationUndoTransaction.TryExecute(
                "Purge Localization Duplicates",
                GetColumnTargets(),
                () =>
                {
                    for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                    {
                        LocaleColumn column = _columns[columnIndex];
                        if (column.DuplicateKeys.Count == 0)
                            continue;
                        column.Serialized.UpdateIfRequiredOrScript();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        for (int entryIndex = column.Entries.arraySize - 1; entryIndex >= 0; entryIndex--)
                        {
                            string key = column.Entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("Key").stringValue;
                            if (!seen.Add(key))
                                column.Entries.DeleteArrayElementAtIndex(entryIndex);
                        }
                        column.Serialized.ApplyModifiedProperties();
                    }
                },
                out string error);
            if (!applied)
                EditorUtility.DisplayDialog("Purge Duplicate Keys", "Purge failed and was rolled back. " + error, "OK");
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void AddKeyToAllColumns()
        {
            CommitActiveValueDraft();
            string key = GenerateUniqueKey();
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Add Localization Key",
                new UnityEngine.Object[] { _columns[0].Table },
                () => AppendEntry(_columns[0], key, string.Empty),
                out string error);
            if (!applied)
                EditorUtility.DisplayDialog("Add Localization Key", "Add failed and was rolled back. " + error, "OK");
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private bool SortAuthoringEntries(KeySortMode mode, bool showConfirmation)
        {
            CommitActiveValueDraft();
            if (!CanEdit || _columns.Count == 0 || _columns[0].Entries.arraySize < 2)
                return false;

            if (_duplicateKeys.Count > 0 || !string.IsNullOrEmpty(_searchFilter) || _showDuplicatesOnly)
            {
                if (showConfirmation)
                {
                    EditorUtility.DisplayDialog(
                        "Sort Authoring Keys",
                        "Resolve duplicate keys and clear all filters before sorting the complete Authoring Locale table.",
                        "OK");
                }
                return false;
            }

            LocaleColumn authoring = _columns[0];
            authoring.Serialized.UpdateIfRequiredOrScript();
            int count = authoring.Entries.arraySize;
            var original = new AuthoringEntrySnapshot[count];
            for (int index = 0; index < count; index++)
            {
                SerializedProperty entry = authoring.Entries.GetArrayElementAtIndex(index);
                original[index] = new AuthoringEntrySnapshot(
                    entry.FindPropertyRelative("Key").stringValue,
                    entry.FindPropertyRelative("Value").stringValue,
                    index);
            }

            var sorted = (AuthoringEntrySnapshot[])original.Clone();
            Array.Sort(sorted, GetAuthoringEntryComparer(mode));
            bool changed = false;
            for (int index = 0; index < count; index++)
            {
                if (sorted[index].OriginalIndex == index)
                    continue;
                changed = true;
                break;
            }

            if (!changed)
            {
                if (showConfirmation)
                    EditorUtility.DisplayDialog("Sort Authoring Keys", "The Authoring Locale keys already use this order.", "OK");
                return false;
            }

            if (showConfirmation && !EditorUtility.DisplayDialog(
                    "Sort Authoring Keys",
                    BuildSortPreview(original, sorted, mode),
                    "Sort",
                    "Cancel"))
            {
                return false;
            }

            bool applied = LocalizationUndoTransaction.TryExecute(
                "Sort Localization Keys",
                new UnityEngine.Object[] { authoring.Table },
                () =>
                {
                    authoring.Serialized.UpdateIfRequiredOrScript();
                    for (int index = 0; index < sorted.Length; index++)
                    {
                        SerializedProperty entry = authoring.Entries.GetArrayElementAtIndex(index);
                        entry.FindPropertyRelative("Key").stringValue = sorted[index].Key;
                        entry.FindPropertyRelative("Value").stringValue = sorted[index].Value;
                    }
                    authoring.Serialized.ApplyModifiedProperties();
                },
                out string error);
            if (!applied)
            {
                if (showConfirmation)
                    EditorUtility.DisplayDialog("Sort Authoring Keys", "Sort failed and was rolled back. " + error, "OK");
                return false;
            }

            RefreshColumns(_tableIds[_selectedTableIndex]);
            Repaint();
            return true;
        }

        private static IComparer<AuthoringEntrySnapshot> GetAuthoringEntryComparer(KeySortMode mode)
        {
            switch (mode)
            {
                case KeySortMode.OrdinalDescending:
                    return AuthoringEntryComparer.OrdinalDescending;
                case KeySortMode.NaturalAscending:
                    return AuthoringEntryComparer.NaturalAscending;
                default:
                    return AuthoringEntryComparer.OrdinalAscending;
            }
        }

        private static string BuildSortPreview(
            AuthoringEntrySnapshot[] original,
            AuthoringEntrySnapshot[] sorted,
            KeySortMode mode)
        {
            var preview = new StringBuilder(320);
            preview.Append("Reorder ").Append(sorted.Length).Append(" Authoring Locale keys using ")
                .Append(GetSortModeLabel(mode)).Append("?\n\n")
                .Append("Current first: ").Append(TruncateSortPreviewKey(original[0].Key)).Append('\n')
                .Append("New first: ").Append(TruncateSortPreviewKey(sorted[0].Key)).Append('\n')
                .Append("Current last: ").Append(TruncateSortPreviewKey(original[original.Length - 1].Key)).Append('\n')
                .Append("New last: ").Append(TruncateSortPreviewKey(sorted[sorted.Length - 1].Key)).Append("\n\n")
                .Append("Only serialized Authoring Locale entry order changes. Values, target locales, metadata, and revisions remain unchanged. The operation is one Undo step.");
            return preview.ToString();
        }

        private static string GetSortModeLabel(KeySortMode mode)
        {
            switch (mode)
            {
                case KeySortMode.OrdinalDescending:
                    return "ordinal Z to A";
                case KeySortMode.NaturalAscending:
                    return "natural A to Z";
                default:
                    return "ordinal A to Z";
            }
        }

        private static string TruncateSortPreviewKey(string key)
        {
            const int maxLength = 80;
            if (string.IsNullOrEmpty(key) || key.Length <= maxLength)
                return key ?? string.Empty;
            return key.Substring(0, maxLength - 3) + "...";
        }

        private static int CompareNaturalKeys(string left, string right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                char leftCharacter = left[leftIndex];
                char rightCharacter = right[rightIndex];
                if (IsAsciiDigit(leftCharacter) && IsAsciiDigit(rightCharacter))
                {
                    int leftRunStart = leftIndex;
                    int rightRunStart = rightIndex;
                    while (leftIndex < left.Length && IsAsciiDigit(left[leftIndex]))
                        leftIndex++;
                    while (rightIndex < right.Length && IsAsciiDigit(right[rightIndex]))
                        rightIndex++;

                    int leftSignificant = leftRunStart;
                    int rightSignificant = rightRunStart;
                    while (leftSignificant < leftIndex && left[leftSignificant] == '0')
                        leftSignificant++;
                    while (rightSignificant < rightIndex && right[rightSignificant] == '0')
                        rightSignificant++;

                    int leftSignificantLength = leftIndex - leftSignificant;
                    int rightSignificantLength = rightIndex - rightSignificant;
                    if (leftSignificantLength != rightSignificantLength)
                        return leftSignificantLength < rightSignificantLength ? -1 : 1;
                    for (int digitIndex = 0; digitIndex < leftSignificantLength; digitIndex++)
                    {
                        char leftDigit = left[leftSignificant + digitIndex];
                        char rightDigit = right[rightSignificant + digitIndex];
                        if (leftDigit != rightDigit)
                            return leftDigit < rightDigit ? -1 : 1;
                    }

                    int leftRunLength = leftIndex - leftRunStart;
                    int rightRunLength = rightIndex - rightRunStart;
                    if (leftRunLength != rightRunLength)
                        return leftRunLength < rightRunLength ? -1 : 1;
                    continue;
                }

                if (leftCharacter != rightCharacter)
                    return leftCharacter < rightCharacter ? -1 : 1;
                leftIndex++;
                rightIndex++;
            }

            if (leftIndex == left.Length && rightIndex == right.Length)
                return 0;
            return leftIndex == left.Length ? -1 : 1;
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static void AppendEntry(LocaleColumn column, string key, string value)
        {
            column.Serialized.UpdateIfRequiredOrScript();
            int index = column.Entries.arraySize;
            column.Entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Key").stringValue = key;
            entry.FindPropertyRelative("Value").stringValue = value ?? string.Empty;
            column.Serialized.ApplyModifiedProperties();
            column.KeyToIndex[key] = index;
            column.Values[key] = value ?? string.Empty;
        }

        private void CreateMetadataAsset()
        {
            if (_selectedTableIndex < 0 || _columns.Count == 0)
                return;

            string tableId = _tableIds[_selectedTableIndex];
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_columns[0].Table))?.Replace('\\', '/') ?? "Assets";
            string fileName = "StringTableMetadata_" + MakeSafeFileName(tableId) + "_String.asset";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + fileName);
            var metadata = CreateInstance<StringTableMetadata>();
            var serialized = new SerializedObject(metadata);
            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("tableType").enumValueIndex = (int)TableType.String;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(metadata, assetPath);
            AssetDatabase.SaveAssetIfDirty(metadata);
            Selection.activeObject = metadata;
            EditorGUIUtility.PingObject(metadata);
            RefreshMetadata(tableId);
        }

        private void CreateNewLocaleTable(string localeCode)
        {
            string requestedLocaleCode = localeCode;
            if (!LocalizationLocaleCodeUtility.TryCanonicalize(localeCode, out localeCode))
            {
                EditorUtility.DisplayDialog("Create Locale Table", "Locale code is not a supported BCP 47 tag: " + requestedLocaleCode, "OK");
                return;
            }
            LocaleColumn existingColumn = FindColumn(localeCode);
            bool wasRegistered = SettingsContainsLocale(localeCode);
            if (!wasRegistered)
            {
                if (!EditorUtility.DisplayDialog(
                        "Register Locale",
                        "Locale '" + localeCode + "' is not registered in LocalizationSettings. " +
                        "Create or reuse its Locale asset, register it, and continue?",
                        "Register and Continue",
                        "Cancel"))
                {
                    return;
                }
                if (!LocalizationLocaleAssetUtility.TryEnsureRegistered(
                        _resolvedSettings,
                        localeCode,
                        out _,
                        out _,
                        out string localeError))
                {
                    EditorUtility.DisplayDialog("Create Locale Table", localeError, "OK");
                    return;
                }
                ResolveSettings();
                if (!string.IsNullOrEmpty(_settingsError))
                {
                    EditorUtility.DisplayDialog("Create Locale Table", _settingsError, "OK");
                    return;
                }
            }
            if (existingColumn != null)
            {
                SelectTableAsset(existingColumn);
                _discoveryDirty = true;
                if (wasRegistered)
                    EditorUtility.DisplayDialog("Create Locale Table", "A table already exists for locale: " + localeCode, "OK");
                return;
            }

            if (TryAssignLocaleToSingleUnconfiguredTable(localeCode))
            {
                _discoveryDirty = true;
                return;
            }

            string tableId = _tableIds[_selectedTableIndex];
            string directory = _columns.Count > 0
                ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(_columns[0].Table))?.Replace('\\', '/')
                : "Assets";
            string fileName = "StringTable_" + MakeSafeFileName(tableId) + "_" + MakeSafeFileName(localeCode) + ".asset";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + fileName);
            var table = CreateInstance<StringTable>();
            var serialized = new SerializedObject(table);
            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("localeCode").stringValue = localeCode;
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(table, assetPath);
            AssetDatabase.SaveAssetIfDirty(table);
            Selection.activeObject = table;
            EditorGUIUtility.PingObject(table);
            _discoveryDirty = true;
        }

        private bool SettingsContainsLocale(string localeCode)
        {
            return _registeredLocaleCodes.Contains(localeCode);
        }

        private static void ConfigureColumnPresentation(LocaleColumn column)
        {
            column.HeaderContent = new GUIContent(
                column.IsRegistered ? column.LocaleCode : column.LocaleCode + "  [Not in Settings]",
                column.IsRegistered
                    ? "Select this locale table asset."
                    : "This table asset is preserved but its locale is not registered in LocalizationSettings.");
            column.AuthoringHeaderContent = new GUIContent(
                column.IsRegistered
                    ? column.LocaleCode + "  [Authoring]"
                    : column.LocaleCode + "  [Authoring, Not in Settings]",
                column.IsRegistered
                    ? "Select the Authoring Locale table asset."
                    : "The Authoring Locale table is preserved, but the locale is not registered in LocalizationSettings.");
            column.RegisterContent = new GUIContent(
                "Register " + column.LocaleCode,
                "Register the existing Locale asset in LocalizationSettings without changing table content.");
        }

        private bool TryAssignLocaleToSingleUnconfiguredTable(string localeCode)
        {
            LocaleColumn unconfigured = null;
            for (int index = 0; index < _columns.Count; index++)
            {
                if (LocalizationLocaleCodeUtility.IsWellFormed(_columns[index].LocaleCode))
                    continue;
                if (unconfigured != null)
                    return false;
                unconfigured = _columns[index];
            }
            if (unconfigured == null)
                return false;

            Undo.RecordObject(unconfigured.Table, "Assign Localization Table Locale");
            var serialized = new SerializedObject(unconfigured.Table);
            serialized.FindProperty("localeCode").stringValue = localeCode;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(unconfigured.Table);
            return true;
        }

        private void ShowExportDialog()
        {
            int allKeyCount = CountExportableAuthoringKeys(false);
            bool hasActiveFilter = !string.IsNullOrEmpty(_searchFilter) || _showDuplicatesOnly;
            int filteredKeyCount = hasActiveFilter ? CountExportableAuthoringKeys(true) : 0;
            var targetLocales = new LocalizationCsvExportLanguageOption[Math.Max(0, _columns.Count - 1)];
            for (int index = 1; index < _columns.Count; index++)
            {
                targetLocales[index - 1] = new LocalizationCsvExportLanguageOption(
                    _columns[index].LocaleCode,
                    _columns[index].IsRegistered);
            }

            LocalizationCsvExportDialog.Open(
                this,
                _tableIds[_selectedTableIndex],
                allKeyCount,
                hasActiveFilter,
                filteredKeyCount,
                targetLocales,
                selection =>
                {
                    if (this != null)
                    {
                        ExportCsv(
                            selection.TargetColumnIndex,
                            selection.FilteredOnly,
                            selection.Encoding,
                            selection.RegisteredLocalesOnly);
                    }
                });
        }

        private int CountExportableAuthoringKeys(bool filteredOnly)
        {
            if (!_hasAuthoringColumn || _columns.Count == 0)
                return 0;

            int count = 0;
            for (int index = 0; index < _allKeys.Count; index++)
            {
                string key = _allKeys[index];
                if (_columns[0].KeyToIndex.ContainsKey(key) && (!filteredOnly || MatchesFilter(key)))
                    count++;
            }
            return count;
        }

        private void ExportCsv(
            int? targetColumnIndex,
            bool filteredOnly,
            LocalizationCsvEncoding encoding,
            bool registeredLocalesOnly)
        {
            if (!_hasAuthoringColumn)
                return;

            string path = EditorUtility.SaveFilePanel(
                encoding == LocalizationCsvEncoding.Utf8WithBom
                    ? "Export Localization for Spreadsheet"
                    : "Export Localization for Automation",
                string.Empty,
                _tableIds[_selectedTableIndex] + ".csv",
                "csv");
            if (string.IsNullOrEmpty(path))
                return;

            List<int> exportColumns = BuildExportColumnIndices(targetColumnIndex, registeredLocalesOnly);

            int csvColumnCount = 3 + exportColumns.Count * 3;
            if (csvColumnCount > LocalizationCsvLimits.Default.MaxColumns)
            {
                EditorUtility.DisplayDialog("Export Localization CSV", "Selected locales exceed the CSV column limit.", "OK");
                return;
            }

            var keys = new List<string>(_columns[0].KeyToIndex.Count);
            for (int index = 0; index < _allKeys.Count; index++)
            {
                string key = _allKeys[index];
                if (_columns[0].KeyToIndex.ContainsKey(key) && (!filteredOnly || MatchesFilter(key)))
                    keys.Add(key);
            }
            if (keys.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Export Localization CSV",
                    filteredOnly
                        ? "No authoring keys match the current search/filter."
                        : "The Authoring Locale table has no keys.",
                    "OK");
                return;
            }
            var rows = new List<string[]>(keys.Count + 1);
            var header = new string[csvColumnCount];
            header[0] = "Key";
            header[1] = "SourceRevision";
            header[2] = _authoringLocaleCode;
            int headerIndex = 3;
            for (int index = 0; index < exportColumns.Count; index++)
            {
                string localeCode = _columns[exportColumns[index]].LocaleCode;
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
                row[2] = _columns[0].Values.TryGetValue(key, out string source) ? source : string.Empty;
                int cellIndex = 3;
                for (int columnIndex = 0; columnIndex < exportColumns.Count; columnIndex++)
                {
                    LocaleColumn column = _columns[exportColumns[columnIndex]];
                    bool hasValue = column.Values.TryGetValue(key, out string value) &&
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

        private List<int> BuildExportColumnIndices(int? targetColumnIndex, bool registeredLocalesOnly)
        {
            var exportColumns = new List<int>(_columns.Count);
            if (targetColumnIndex.HasValue)
            {
                exportColumns.Add(targetColumnIndex.Value);
            }
            else
            {
                for (int index = 1; index < _columns.Count; index++)
                {
                    if (!registeredLocalesOnly || _columns[index].IsRegistered)
                        exportColumns.Add(index);
                }
            }
            return exportColumns;
        }

        private void ImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("Import Localization CSV", string.Empty, "csv");
            if (string.IsNullOrEmpty(path))
                return;
            if (!LocalizationCsv.TryReadFile(path, LocalizationCsvLimits.Default, out LocalizationCsvDocument document, out string readError))
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

        private bool TryBuildImportPlan(
            LocalizationCsvDocument document,
            out CsvImportPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (!_hasAuthoringColumn)
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
                !string.Equals(header[2], _authoringLocaleCode, StringComparison.Ordinal))
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
                LocaleColumn column = FindColumn(localeCode);
                if (column == null || string.Equals(localeCode, _authoringLocaleCode, StringComparison.Ordinal))
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
                if (!_columns[0].Values.TryGetValue(key, out string currentSource))
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
                        error = "CSV has an invalid TranslationStatus for key '" + key + "', locale '" + csvLocale.Column.LocaleCode + "'.";
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
                    string currentValue = csvLocale.Column.Values.TryGetValue(key, out string cachedValue)
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
                    if (statusChanged)
                    {
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
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in pendingStateLocales)
            {
                int existingCount = GetTranslationStatusCount(pair.Key);
                int newCount = 0;
                foreach (string localeCode in pair.Value)
                {
                    if (!HasTranslationState(pair.Key, localeCode))
                        newCount++;
                }
                if (existingCount + newCount > StringTableMetadata.MaxLocaleStatusesPerEntry)
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
            var targets = new List<UnityEngine.Object>(_columns.Count + 1) { _metadata.Metadata };
            var changedTables = new HashSet<StringTable>();
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
                        LocaleColumn column = change.Column;
                        column.Serialized.UpdateIfRequiredOrScript();
                        if (change.Kind == CsvValueChangeKind.Remove)
                        {
                            if (column.KeyToIndex.TryGetValue(change.Key, out int entryIndex))
                                column.Entries.DeleteArrayElementAtIndex(entryIndex);
                        }
                        else if (column.KeyToIndex.TryGetValue(change.Key, out int entryIndex))
                        {
                            column.Entries.GetArrayElementAtIndex(entryIndex)
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
                        RebuildColumnCache(column);
                    }

                    _metadata.Update();
                    for (int index = 0; index < plan.StatusChanges.Count; index++)
                    {
                        CsvStatusChange change = plan.StatusChanges[index];
                        SerializedProperty entry = _metadata.GetEntry(change.Key);
                        if (!LocalizationMetadataIndex.TrySetTranslationState(
                                entry,
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
            RefreshColumns(_tableIds[_selectedTableIndex]);
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
            SerializedProperty entry = _metadata.GetEntry(key);
            SerializedProperty statuses = entry?.FindPropertyRelative("LocaleStatuses");
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

        private LocaleColumn FindColumn(string localeCode)
        {
            for (int index = 0; index < _columns.Count; index++)
            {
                if (string.Equals(_columns[index].LocaleCode, localeCode, StringComparison.Ordinal))
                    return _columns[index];
            }
            return null;
        }

        private UnityEngine.Object[] GetColumnTargets()
        {
            var targets = new UnityEngine.Object[_columns.Count];
            for (int index = 0; index < _columns.Count; index++)
                targets[index] = _columns[index].Table;
            return targets;
        }

        private void UpdateSerializedObjects()
        {
            for (int index = 0; index < _columns.Count; index++)
                _columns[index].Serialized.UpdateIfRequiredOrScript();
            _metadata.Update();
        }

        private void ApplySerializedObjects()
        {
            for (int index = 0; index < _columns.Count; index++)
                _columns[index].Serialized.ApplyModifiedProperties();
            _metadata.Serialized?.ApplyModifiedProperties();
        }

        private string GenerateUniqueKey()
        {
            for (int index = 0; index < 10_000; index++)
            {
                string candidate = "new_key_" + index;
                if (!_allKeySet.Contains(candidate))
                    return candidate;
            }
            return "new_key_" + Guid.NewGuid().ToString("N");
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Localization";
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                builder.Append(Array.IndexOf(invalid, character) >= 0 || character == '/' || character == '\\' ? '_' : character);
            }
            return builder.ToString();
        }

        private int CompareColumns(LocaleColumn left, LocaleColumn right)
        {
            bool leftAuthoring = string.Equals(left.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            bool rightAuthoring = string.Equals(right.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal);
            if (leftAuthoring != rightAuthoring)
                return leftAuthoring ? -1 : 1;
            if (left.IsRegistered != right.IsRegistered)
                return left.IsRegistered ? -1 : 1;
            return string.CompareOrdinal(left.LocaleCode, right.LocaleCode);
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            return string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(leftGuid),
                AssetDatabase.GUIDToAssetPath(rightGuid));
        }
    }
}
#endif
