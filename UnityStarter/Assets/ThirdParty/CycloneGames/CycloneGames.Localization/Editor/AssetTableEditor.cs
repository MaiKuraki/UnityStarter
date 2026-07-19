#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    public sealed class AssetTableEditor : EditorWindow
    {
        private const float FoldButtonWidth = 54f;
        private const float KeyColumnWidth = 180f;
        private const float ValueColumnWidth = 240f;
        private const float DeleteButtonWidth = 22f;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const float SeparatorHeight = 1f;
        private const float MetadataRowHeight = 110f;
        private const float AssetFieldGap = 2f;
        private const float AssetObjectFieldRatio = 0.45f;

        private static readonly Color MissingColor = new Color(1f, 0.6f, 0.2f, 0.25f);
        private static readonly Color InvalidLocationColor = new Color(0.9f, 0.35f, 0.15f, 0.18f);
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
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic,
                    fontSize = 10,
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
            public AssetTable Table;
            public SerializedObject Serialized;
            public SerializedProperty Entries;
            public readonly Dictionary<string, int> KeyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Locations = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Guids = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, UnityEngine.Object> Objects = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> SearchText = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly HashSet<string> DuplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        [MenuItem("Tools/CycloneGames/Localization/Tables/Asset Table Editor")]
        public static void Open()
        {
            var window = GetWindow<AssetTableEditor>("Asset Table Editor");
            window.minSize = new Vector2(760f, 360f);
        }

        internal static void OpenForTable(string tableId)
        {
            var window = GetWindow<AssetTableEditor>("Asset Table Editor");
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

        private void OnProjectChanged()
        {
            _discoveryDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            if (_discoveryDirty)
                DiscoverTables();

            DrawTableSelector();
            DrawConfigurationErrors();
            if (_selectedTableIndex < 0 || _columns.Count == 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "Select a Table ID, or create an AssetTable asset with Create > CycloneGames > Localization > Asset Table.",
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
            string[] guids = AssetDatabase.FindAssets("t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                var table = AssetDatabase.LoadAssetAtPath<AssetTable>(AssetDatabase.GUIDToAssetPath(guids[index]));
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
            string[] guids = AssetDatabase.FindAssets("t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var table = AssetDatabase.LoadAssetAtPath<AssetTable>(path);
                if (table == null || !string.Equals(table.TableId, tableId, StringComparison.Ordinal))
                    continue;

                string localeCode = table.LocaleId.Code;
                if (!localeCodes.Add(localeCode))
                {
                    _columnError = "Multiple AssetTable assets use tableId '" + tableId + "' and locale '" + localeCode + "'.";
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
            if (string.IsNullOrEmpty(_settingsError) && !_hasAuthoringColumn && string.IsNullOrEmpty(_columnError))
                _columnError = "Table '" + tableId + "' has no AssetTable for Authoring Locale '" + _authoringLocaleCode + "'.";

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
                if (metadata == null || metadata.TableType != TableType.Asset ||
                    !string.Equals(metadata.TableId, tableId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    _metadataError = "Multiple StringTableMetadata assets use tableId '" + tableId + "' and type Asset.";
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
            column.Locations.Clear();
            column.Guids.Clear();
            column.Objects.Clear();
            column.SearchText.Clear();
            column.DuplicateKeys.Clear();
            column.Serialized.UpdateIfRequiredOrScript();
            column.Entries = column.Serialized.FindProperty("entries");
            if (column.Entries == null || !column.Entries.isArray)
                return;

            for (int index = 0; index < column.Entries.arraySize; index++)
            {
                SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
                string key = entry.FindPropertyRelative("Key")?.stringValue ?? string.Empty;
                SerializedProperty asset = entry.FindPropertyRelative("Asset");
                string location = asset?.FindPropertyRelative("m_Location")?.stringValue ?? string.Empty;
                string guid = asset?.FindPropertyRelative("m_GUID")?.stringValue ?? string.Empty;
                if (column.KeyToIndex.ContainsKey(key))
                    column.DuplicateKeys.Add(key);
                column.KeyToIndex[key] = index;
                column.Locations[key] = location;
                column.Guids[key] = guid;

                UnityEngine.Object assetObject = null;
                string assetPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath))
                    assetObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                column.Objects[key] = assetObject;
                column.SearchText[key] = string.Concat(location, "\n", guid, "\n", assetPath);
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
                foreach (string key in column.KeyToIndex.Keys)
                {
                    if (_allKeySet.Add(key))
                        _allKeys.Add(key);
                }
                foreach (string duplicate in column.DuplicateKeys)
                    _duplicateKeys.Add(duplicate);
            }
            _allKeys.Sort(StringComparer.Ordinal);
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
                    if (GUILayout.Button("Metadata", EditorStyles.miniButton))
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
                if (GUILayout.Button("Sync", EditorStyles.toolbarButton))
                    SyncKeysAcrossColumns();
            }
            EditorGUILayout.EndHorizontal();
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
                string key = _allKeys[_rowCache.GetSourceIndex(visibleIndex)];
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
                    DrawAssetCell(_columns[columnIndex], key, cell, locked);
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
                    DrawAssetCell(
                        _columns[0],
                        key,
                        new Rect(deleteX, rowTop, ValueColumnWidth, RowHeight),
                        locked);
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
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y, frozenWidth, HeaderHeight), HeaderBackground);
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

        private void DrawAssetCell(LocaleColumn column, string key, Rect cell, bool locked)
        {
            if (!column.KeyToIndex.TryGetValue(key, out int entryIndex))
            {
                EditorGUI.DrawRect(cell, MissingColor);
                using (new EditorGUI.DisabledScope(!CanEdit || locked))
                {
                    if (GUI.Button(cell, "(fallback)", MissingButton))
                        AddMissingKey(column, key);
                }
                return;
            }

            string currentGuid = column.Guids.TryGetValue(key, out string guid) ? guid : string.Empty;
            string currentLocation = column.Locations.TryGetValue(key, out string location) ? location : string.Empty;
            UnityEngine.Object currentObject = column.Objects.TryGetValue(key, out UnityEngine.Object assetObject)
                ? assetObject
                : null;
            if (string.IsNullOrEmpty(currentLocation))
                EditorGUI.DrawRect(cell, InvalidLocationColor);

            float objectWidth = Mathf.Floor((cell.width - AssetFieldGap) * AssetObjectFieldRatio);
            Rect objectRect = new Rect(cell.x, cell.y, objectWidth, cell.height);
            Rect locationRect = new Rect(
                objectRect.xMax + AssetFieldGap,
                cell.y,
                cell.width - objectWidth - AssetFieldGap,
                cell.height);

            string nextGuid = currentGuid;
            string nextLocation = currentLocation;
            bool changed = false;
            using (new EditorGUI.DisabledScope(!CanEdit || locked))
            {
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object nextObject = EditorGUI.ObjectField(
                    objectRect,
                    currentObject,
                    typeof(UnityEngine.Object),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    changed = true;
                    if (nextObject != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(nextObject);
                        nextGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        // Project paths are not provider-neutral runtime locations. The author supplies the
                        // Resources, Addressables, YooAsset, or custom-provider key in the adjacent field.
                        nextLocation = string.Empty;
                    }
                    else
                    {
                        nextGuid = string.Empty;
                        nextLocation = string.Empty;
                    }
                }

                EditorGUI.BeginChangeCheck();
                string editedLocation = EditorGUI.DelayedTextField(locationRect, nextLocation);
                if (EditorGUI.EndChangeCheck())
                {
                    nextLocation = editedLocation;
                    changed = true;
                }
            }

            if (changed)
                CommitAssetChange(column, entryIndex, key, nextGuid, nextLocation);
        }

        private void CommitAssetChange(
            LocaleColumn column,
            int entryIndex,
            string key,
            string nextGuid,
            string nextLocation)
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
                    if (candidate.Locations.TryGetValue(key, out string translatedLocation) && !string.IsNullOrEmpty(translatedLocation))
                        translatedLocales.Add(candidate.LocaleCode);
                }
            }

            UnityEngine.Object[] targets = _metadata.Metadata != null
                ? new UnityEngine.Object[] { column.Table, _metadata.Metadata }
                : new UnityEngine.Object[] { column.Table };
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Edit Localized Asset",
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
                            !string.IsNullOrEmpty(nextLocation),
                            "Update Translation Status",
                            out workflowError);
                    }
                    if (!workflowUpdated)
                        throw new InvalidOperationException(workflowError);

                    column.Serialized.UpdateIfRequiredOrScript();
                    SerializedProperty asset = column.Entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("Asset");
                    asset.FindPropertyRelative("m_GUID").stringValue = nextGuid ?? string.Empty;
                    asset.FindPropertyRelative("m_Location").stringValue = nextLocation ?? string.Empty;
                    column.Serialized.ApplyModifiedProperties();
                },
                out string error);

            if (!applied)
            {
                EditorUtility.DisplayDialog("Localized Asset", "Asset reference was not changed. " + error, "OK");
                RefreshColumns(_tableIds[_selectedTableIndex]);
                return;
            }

            RebuildColumnCache(column);
            _rowCacheDirty = !string.IsNullOrEmpty(_searchFilter);
        }

        private void AddMissingKey(LocaleColumn column, string key)
        {
            UnityEngine.Object[] targets = _metadata.Metadata != null
                ? new UnityEngine.Object[] { column.Table, _metadata.Metadata }
                : new UnityEngine.Object[] { column.Table };
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Add Localized Asset Entry",
                targets,
                () =>
                {
                    AppendEntry(column, key);
                    if (!string.Equals(column.LocaleCode, _authoringLocaleCode, StringComparison.Ordinal) &&
                        !_metadata.MarkTranslationChanged(key, column.LocaleCode, false, "Update Translation Status", out string workflowError))
                    {
                        throw new InvalidOperationException(workflowError);
                    }
                },
                out string error);
            if (!applied)
                EditorUtility.DisplayDialog("Localized Asset", "Entry was not added. " + error, "OK");
            RefreshColumns(_tableIds[_selectedTableIndex]);
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
                if (_columns[index].SearchText.TryGetValue(key, out string searchText) &&
                    searchText.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawMetadataSubRow(string key, float x, float y, float width)
        {
            GUI.Box(new Rect(x, y, width, MetadataRowHeight - 4f), GUIContent.none, EditorStyles.helpBox);
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
                if (GUI.Button(new Rect(contentX, contentY + 36f, 160f, 18f), "Create Entry Metadata", EditorStyles.miniButton) &&
                    !_metadata.CreateEntry(key, "Create Localization Metadata Entry", out string error))
                {
                    EditorUtility.DisplayDialog("Localization Metadata", error, "OK");
                }
                return;
            }

            SerializedProperty lockedProperty = entry.FindPropertyRelative("Locked");
            bool locked = lockedProperty.boolValue;
            bool nextLocked = EditorGUI.ToggleLeft(new Rect(contentX, contentY, 70f, 18f), "Locked", locked);
            if (nextLocked != locked)
            {
                Undo.RecordObject(_metadata.Metadata, nextLocked ? "Lock Localization Entry" : "Unlock Localization Entry");
                lockedProperty.boolValue = nextLocked;
                _metadata.Serialized.ApplyModifiedProperties();
                locked = nextLocked;
            }

            int sourceRevision = entry.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
            GUI.Label(new Rect(contentX + 82f, contentY + 1f, 190f, 16f), "Source Revision: " + sourceRevision, MetadataLabel);
            GUI.Label(new Rect(contentX + 276f, contentY + 1f, contentWidth - 276f, 16f), BuildStatusSummary(entry), MetadataLabel);

            using (new EditorGUI.DisabledScope(locked))
            {
                SerializedProperty commentProperty = entry.FindPropertyRelative("Comment");
                SerializedProperty tagsProperty = entry.FindPropertyRelative("Tags");
                SerializedProperty screenshotProperty = entry.FindPropertyRelative("Screenshot");
                string comment = commentProperty?.stringValue ?? string.Empty;
                string tags = tagsProperty?.stringValue ?? string.Empty;
                UnityEngine.Object screenshot = screenshotProperty?.objectReferenceValue;

                GUI.Label(new Rect(contentX, contentY + 22f, 60f, 14f), "Comment", MetadataLabel);
                string nextComment = EditorGUI.TextArea(new Rect(contentX, contentY + 36f, contentWidth, 28f), comment);
                GUI.Label(new Rect(contentX, contentY + 70f, 32f, 18f), "Tags", MetadataLabel);
                string nextTags = EditorGUI.TextField(
                    new Rect(contentX + 34f, contentY + 70f, Mathf.Max(80f, contentWidth - 264f), 18f),
                    tags);
                UnityEngine.Object nextScreenshot = EditorGUI.ObjectField(
                    new Rect(contentX + contentWidth - 220f, contentY + 70f, 220f, 18f),
                    screenshot,
                    typeof(Texture2D),
                    false);

                if (!string.Equals(nextComment, comment, StringComparison.Ordinal) ||
                    !string.Equals(nextTags, tags, StringComparison.Ordinal) ||
                    nextScreenshot != screenshot)
                {
                    Undo.RecordObject(_metadata.Metadata, "Edit Localization Metadata");
                    commentProperty.stringValue = nextComment;
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
                EditorUtility.DisplayDialog("Delete Localized Asset Key", "The key is locked. Unlock it before deleting table entries.", "OK");
                return;
            }
            int tableCount = 0;
            int populatedCount = 0;
            for (int index = 0; index < _columns.Count; index++)
            {
                if (!_columns[index].KeyToIndex.ContainsKey(key))
                    continue;
                tableCount++;
                if (_columns[index].Locations.TryGetValue(key, out string location) && !string.IsNullOrEmpty(location))
                    populatedCount++;
            }
            string metadataImpact = _metadata.Contains(key)
                ? "The metadata entry is retained and will be reported as orphaned until handled explicitly."
                : "No metadata entry exists.";
            if (!EditorUtility.DisplayDialog(
                    "Delete Localized Asset Key",
                    "Delete '" + key + "' from " + tableCount + " locale table(s)?\n" +
                    populatedCount + " runtime location(s) will be removed.\n\n" + metadataImpact,
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            if (!LocalizationUndoTransaction.TryExecute(
                    "Delete Localized Asset Key",
                    GetColumnTargets(),
                    () => RemoveKeyFromColumns(key),
                    out string error))
            {
                EditorUtility.DisplayDialog("Delete Localized Asset Key", "Delete failed and was rolled back. " + error, "OK");
            }
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void RequestRenameKey(string oldKey, string newKey)
        {
            newKey = newKey?.Trim();
            if (string.IsNullOrEmpty(newKey) || newKey.Length > LocalizationCatalogBuilder.MaxKeyChars)
            {
                EditorUtility.DisplayDialog("Rename Localized Asset Key", "The new key is empty or exceeds the supported length.", "OK");
                return;
            }
            if (_allKeySet.Contains(newKey))
            {
                EditorUtility.DisplayDialog("Rename Localized Asset Key", "The key already exists: " + newKey, "OK");
                return;
            }
            if (_metadata.IsLocked(oldKey))
            {
                EditorUtility.DisplayDialog("Rename Localized Asset Key", "The key is locked. Unlock it before renaming table entries.", "OK");
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
                    "Rename Localized Asset Key",
                    "Rename '" + oldKey + "' to '" + newKey + "' in " + tableCount + " locale table(s)?\n\n" + metadataImpact,
                    "Rename",
                    "Cancel"))
            {
                return;
            }

            if (!LocalizationUndoTransaction.TryExecute(
                    "Rename Localized Asset Key",
                    GetColumnTargets(),
                    () => RenameKeyInColumns(oldKey, newKey),
                    out string error))
            {
                EditorUtility.DisplayDialog("Rename Localized Asset Key", "Rename failed and was rolled back. " + error, "OK");
            }
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void RemoveKeyFromColumns(string key)
        {
            for (int index = 0; index < _columns.Count; index++)
            {
                LocaleColumn column = _columns[index];
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
            for (int index = 0; index < _columns.Count; index++)
            {
                LocaleColumn column = _columns[index];
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
                    EditorUtility.DisplayDialog("Purge Duplicate Keys", "Duplicate key '" + key + "' is locked. No entries were changed.", "OK");
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
                "Purge Localized Asset Duplicates",
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
            string key = GenerateUniqueKey();
            bool applied = LocalizationUndoTransaction.TryExecute(
                "Add Localized Asset Key",
                new UnityEngine.Object[] { _columns[0].Table },
                () => AppendEntry(_columns[0], key),
                out string error);
            if (!applied)
                EditorUtility.DisplayDialog("Add Localized Asset Key", "Add failed and was rolled back. " + error, "OK");
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private void SyncKeysAcrossColumns()
        {
            for (int keyIndex = 0; keyIndex < _allKeys.Count; keyIndex++)
            {
                string key = _allKeys[keyIndex];
                if (!_metadata.IsLocked(key))
                    continue;
                for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                {
                    if (!_columns[columnIndex].KeyToIndex.ContainsKey(key))
                    {
                        EditorUtility.DisplayDialog(
                            "Sync Localized Asset Keys",
                            "Locked key '" + key + "' is missing from at least one locale. No entries were changed.",
                            "OK");
                        return;
                    }
                }
            }

            if (_missingCount == 0)
                return;
            if (!EditorUtility.DisplayDialog(
                    "Sync Localized Asset Keys",
                    "Add " + _missingCount + " empty AssetRef entries? Sparse fallback cells will become explicit invalid references until configured.",
                    "Sync",
                    "Cancel"))
            {
                return;
            }

            bool applied = LocalizationUndoTransaction.TryExecute(
                "Sync Localized Asset Keys",
                GetColumnTargets(),
                () =>
                {
                    for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                    {
                        LocaleColumn column = _columns[columnIndex];
                        for (int keyIndex = 0; keyIndex < _allKeys.Count; keyIndex++)
                        {
                            if (!column.KeyToIndex.ContainsKey(_allKeys[keyIndex]))
                                AppendEntry(column, _allKeys[keyIndex]);
                        }
                    }
                },
                out string error);
            if (!applied)
                EditorUtility.DisplayDialog("Sync Localized Asset Keys", "Sync failed and was rolled back. " + error, "OK");
            RefreshColumns(_tableIds[_selectedTableIndex]);
        }

        private static void AppendEntry(LocaleColumn column, string key)
        {
            column.Serialized.UpdateIfRequiredOrScript();
            int index = column.Entries.arraySize;
            column.Entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Key").stringValue = key;
            SerializedProperty asset = entry.FindPropertyRelative("Asset");
            asset.FindPropertyRelative("m_GUID").stringValue = string.Empty;
            asset.FindPropertyRelative("m_Location").stringValue = string.Empty;
            column.Serialized.ApplyModifiedProperties();
            column.KeyToIndex[key] = index;
            column.Guids[key] = string.Empty;
            column.Locations[key] = string.Empty;
            column.Objects[key] = null;
            column.SearchText[key] = string.Empty;
        }

        private void CreateMetadataAsset()
        {
            if (_selectedTableIndex < 0 || _columns.Count == 0)
                return;
            string tableId = _tableIds[_selectedTableIndex];
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_columns[0].Table))?.Replace('\\', '/') ?? "Assets";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                directory + "/StringTableMetadata_" + MakeSafeFileName(tableId) + "_Asset.asset");
            var metadata = CreateInstance<StringTableMetadata>();
            var serialized = new SerializedObject(metadata);
            serialized.FindProperty("tableId").stringValue = tableId;
            serialized.FindProperty("tableType").enumValueIndex = (int)TableType.Asset;
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
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                directory + "/AssetTable_" + MakeSafeFileName(tableId) + "_" + MakeSafeFileName(localeCode) + ".asset");
            var table = CreateInstance<AssetTable>();
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

        private LocaleColumn FindColumn(string localeCode)
        {
            for (int index = 0; index < _columns.Count; index++)
            {
                if (string.Equals(_columns[index].LocaleCode, localeCode, StringComparison.Ordinal))
                    return _columns[index];
            }
            return null;
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
                string candidate = "new_asset_key_" + index;
                if (!_allKeySet.Contains(candidate))
                    return candidate;
            }
            return "new_asset_key_" + Guid.NewGuid().ToString("N");
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
