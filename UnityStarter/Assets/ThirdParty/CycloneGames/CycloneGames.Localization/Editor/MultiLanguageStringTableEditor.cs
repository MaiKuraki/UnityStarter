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
    public sealed class MultiLanguageStringTableEditor : EditorWindow
    {
        [MenuItem("Tools/CycloneGames/Localization/String Table Editor")]
        public static void Open()
        {
            var w = GetWindow<MultiLanguageStringTableEditor>("String Table Editor");
            w.minSize = new Vector2(600, 300);
        }

        // ── Discovery ───────────────────────────────────────────
        private string[] _tableIds = Array.Empty<string>();
        private GUIContent[] _tableIdContents = Array.Empty<GUIContent>();
        private int _selectedTableIdx = -1;
        private bool _discoveryDirty = true;

        // ── Columns ─────────────────────────────────────────────
        private readonly List<LocaleColumn> _columns = new();
        private readonly List<string> _allKeys = new();
        private readonly HashSet<string> _allKeysSet = new(StringComparer.Ordinal);
        private bool _keysDirty = true;
        private int _missingCount;

        // ── Duplicates ──────────────────────────────────────────
        private readonly HashSet<string> _duplicateKeys = new(StringComparer.Ordinal);
        private int _duplicateCount;
        private bool _showDupesOnly;

        // ── Metadata ────────────────────────────────────────────
        private StringTableMetadata _metadata;
        private SerializedObject _metadataSO;
        private SerializedProperty _metaEntriesProp;
        private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);

        // ── Create locale ───────────────────────────────────────
        private string _newLocaleCode = string.Empty;

        // ── UI state ────────────────────────────────────────────
        private Vector2 _scrollPos;
        private string _searchFilter = string.Empty;

        // ── Filtered keys (cached per frame) ────────────────────
        private readonly List<int> _visibleKeyIndices = new();

        // ── Layout constants ────────────────────────────────────
        private const float FoldBtnW   = 18f;
        private const float KeyColW    = 180f;
        private const float ValColW    = 180f;   // fixed width per locale column
        private const float DelBtnW    = 22f;
        private const float RowH       = 20f;
        private const float HeaderH    = 22f;
        private const float SepH       = 1f;
        private const float MetaRowH   = 90f;    // approximate height for metadata sub-row

        // Frozen width = foldout + key + delete
        private float FrozenW => FoldBtnW + KeyColW + DelBtnW + 4f;
        // Scrollable width = all locale columns
        private float ScrollableW => _columns.Count * ValColW;

        // ── Colors ──────────────────────────────────────────────
        private static readonly Color MissingColor   = new(1f, 0.6f, 0.2f, 0.25f);
        private static readonly Color DuplicateColor = new(0.85f, 0.2f, 0.25f, 0.35f);
        private static readonly Color AltRowColor    = new(1f, 1f, 1f, 0.025f);
        private static readonly Color SepColor       = new(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color HeaderBg       = new(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color EditorBg       = new(0.22f, 0.22f, 0.22f, 1f);

        // ── Styles (lazy) ───────────────────────────────────────
        private static GUIStyle s_Header, s_MissBtn, s_FoldBtn, s_MetaLbl;

        private static GUIStyle Header => s_Header ??= new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 11, padding = new RectOffset(4, 4, 2, 2), alignment = TextAnchor.MiddleLeft };

        private static GUIStyle MissBtn
        {
            get
            {
                if (s_MissBtn != null) return s_MissBtn;
                s_MissBtn = new GUIStyle(EditorStyles.miniButton)
                { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, fontSize = 10 };
                s_MissBtn.normal.textColor = new Color(1f, 0.55f, 0.3f, 0.9f);
                return s_MissBtn;
            }
        }

        private static GUIStyle FoldBtn => s_FoldBtn ??= new GUIStyle(EditorStyles.label)
        { fontSize = 9, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };

        private static GUIStyle MetaLbl
        {
            get
            {
                if (s_MetaLbl != null) return s_MetaLbl;
                s_MetaLbl = new GUIStyle(EditorStyles.miniLabel);
                s_MetaLbl.normal.textColor = new Color(0.7f, 0.7f, 0.8f);
                return s_MetaLbl;
            }
        }

        private struct LocaleColumn
        {
            public string LocaleCode;
            public StringTable Table;
            public SerializedObject Serialized;
            public SerializedProperty EntriesProp;
            public Dictionary<string, int> KeyToIndex;
            public HashSet<string> DuplicateKeysInColumn;
        }

        // ═════════════════════════════════════════════════════════
        // Lifecycle
        // ═════════════════════════════════════════════════════════
        private void OnEnable() => _discoveryDirty = true;
        private void OnFocus() => _discoveryDirty = true;

        private void OnGUI()
        {
            if (_discoveryDirty) DiscoverTables();

            DrawTableSelector();

            if (_selectedTableIdx < 0 || _columns.Count == 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "Select a Table ID above, or create your first StringTable:\n" +
                    "Right-click in Project → Create → CycloneGames → Localization → String Table",
                    MessageType.Info);
                return;
            }

            UpdateAllSerialized();
            if (_keysDirty) RebuildKeys();

            if (_duplicateCount > 0) DrawDuplicateBar();
            DrawToolbar();
            DrawTableArea();
            DrawStatusBar();

            ApplyAllSerialized();
        }

        // ═════════════════════════════════════════════════════════
        // Discovery
        // ═════════════════════════════════════════════════════════
        private void DiscoverTables()
        {
            _discoveryDirty = false;
            var guids = AssetDatabase.FindAssets("t:StringTable");
            var idSet = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var g in guids)
            {
                var t = AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null && !string.IsNullOrEmpty(t.TableId)) idSet.Add(t.TableId);
            }
            _tableIds = new string[idSet.Count];
            _tableIdContents = new GUIContent[idSet.Count];
            int i = 0;
            foreach (var id in idSet)
            {
                _tableIds[i] = id;
                _tableIdContents[i] = new GUIContent(id);
                i++;
            }
            if (_selectedTableIdx >= _tableIds.Length)
                _selectedTableIdx = _tableIds.Length > 0 ? 0 : -1;
            if (_selectedTableIdx >= 0)
                RefreshColumnsForTable(_tableIds[_selectedTableIdx]);
        }

        private void RefreshColumnsForTable(string tableId)
        {
            _columns.Clear();
            _keysDirty = true;
            _expandedKeys.Clear();
            foreach (var g in AssetDatabase.FindAssets("t:StringTable"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null || table.TableId != tableId) continue;
                var so = new SerializedObject(table);
                var entries = so.FindProperty("entries");
                var map = new Dictionary<string, int>(entries.arraySize, StringComparer.Ordinal);
                var dupes = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < entries.arraySize; j++)
                {
                    string k = entries.GetArrayElementAtIndex(j).FindPropertyRelative("Key").stringValue;
                    if (map.ContainsKey(k)) dupes.Add(k);
                    map[k] = j;
                }
                _columns.Add(new LocaleColumn
                {
                    LocaleCode = table.LocaleId.Code,
                    Table = table, Serialized = so, EntriesProp = entries,
                    KeyToIndex = map, DuplicateKeysInColumn = dupes,
                });
            }
            _columns.Sort((a, b) => string.Compare(a.LocaleCode, b.LocaleCode, StringComparison.Ordinal));
            RefreshMetadata(tableId);
        }

        private void RefreshMetadata(string tableId)
        {
            _metadata = null; _metadataSO = null; _metaEntriesProp = null;
            foreach (var g in AssetDatabase.FindAssets("t:StringTableMetadata"))
            {
                var m = AssetDatabase.LoadAssetAtPath<StringTableMetadata>(AssetDatabase.GUIDToAssetPath(g));
                if (m != null && m.TableId == tableId && m.TableType == TableType.String)
                {
                    _metadata = m;
                    _metadataSO = new SerializedObject(m);
                    _metaEntriesProp = _metadataSO.FindProperty("entries");
                    break;
                }
            }
        }

        // ═════════════════════════════════════════════════════════
        // Key management
        // ═════════════════════════════════════════════════════════
        private void RebuildKeys()
        {
            _keysDirty = false;
            _allKeys.Clear(); _allKeysSet.Clear();
            _missingCount = 0;
            _duplicateKeys.Clear(); _duplicateCount = 0;
            for (int c = 0; c < _columns.Count; c++)
            {
                foreach (var key in _columns[c].KeyToIndex.Keys)
                    if (_allKeysSet.Add(key)) _allKeys.Add(key);
                foreach (var dk in _columns[c].DuplicateKeysInColumn)
                    _duplicateKeys.Add(dk);
            }
            _duplicateCount = _duplicateKeys.Count;
            for (int k = 0; k < _allKeys.Count; k++)
                for (int c = 0; c < _columns.Count; c++)
                    if (!_columns[c].KeyToIndex.ContainsKey(_allKeys[k]))
                        _missingCount++;
        }

        // ═════════════════════════════════════════════════════════
        // Drawing — top bars
        // ═════════════════════════════════════════════════════════
        private void DrawTableSelector()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Table ID", GUILayout.Width(52));
            EditorGUI.BeginChangeCheck();
            _selectedTableIdx = EditorGUILayout.Popup(_selectedTableIdx, _tableIdContents);
            if (EditorGUI.EndChangeCheck() && _selectedTableIdx >= 0)
                RefreshColumnsForTable(_tableIds[_selectedTableIdx]);
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(56)))
                _discoveryDirty = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (_columns.Count > 0)
            {
                var sb = new StringBuilder(64);
                for (int i = 0; i < _columns.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(_columns[i].LocaleCode); }
                EditorGUILayout.LabelField($"Locales ({_columns.Count}): {sb}", EditorStyles.miniLabel);
            }
            else EditorGUILayout.LabelField("No locales found", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (_selectedTableIdx >= 0 && _columns.Count > 0)
            {
                if (_metadata != null)
                { if (GUILayout.Button("Metadata ✓", EditorStyles.miniButton)) { EditorGUIUtility.PingObject(_metadata); Selection.activeObject = _metadata; } }
                else
                { if (GUILayout.Button("+ Metadata", EditorStyles.miniButton)) CreateMetadataAsset(); }
            }
            _newLocaleCode = EditorGUILayout.TextField(_newLocaleCode, GUILayout.Width(60));
            if (GUILayout.Button("+ Locale", EditorStyles.miniButton))
            { if (!string.IsNullOrWhiteSpace(_newLocaleCode)) { CreateNewLocaleTable(_newLocaleCode.Trim()); _newLocaleCode = string.Empty; } }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private void DrawDuplicateBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"⚠ {_duplicateCount} duplicate key(s) — rename or remove.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            _showDupesOnly = GUILayout.Toggle(_showDupesOnly, "Filter", EditorStyles.miniButton);
            if (GUILayout.Button("Purge Dupes", EditorStyles.miniButton)) PurgeDuplicateKeys();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Key", EditorStyles.toolbarButton)) AddKeyToAllColumns();
            if (GUILayout.Button("Sync", EditorStyles.toolbarButton)) SyncKeysAcrossColumns();
            if (GUILayout.Button("Import", EditorStyles.toolbarButton)) ImportMultiLanguageCSV();
            if (GUILayout.Button("Export", EditorStyles.toolbarButton)) ExportMultiLanguageCSV();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.Space(2);
            var sb = new StringBuilder(128);
            sb.Append($"Keys: {_allKeys.Count}  ×  {_columns.Count} locales");
            if (_duplicateCount > 0) sb.Append($"  |  ⚠ {_duplicateCount} dupe(s)");
            if (_missingCount > 0)   sb.Append($"  |  ⚠ {_missingCount} missing");
            if (_duplicateCount == 0 && _missingCount == 0) sb.Append("  |  ✓ All good");
            if (_metadata != null) sb.Append($"  |  Metadata: {_metadata.name}");
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
        }

        // ═════════════════════════════════════════════════════════
        // Drawing — table area (pinned key + scrollable locales)
        // ═════════════════════════════════════════════════════════
        private void DrawTableArea()
        {
            // Build visible keys list
            _visibleKeyIndices.Clear();
            bool hasFilter = !string.IsNullOrEmpty(_searchFilter);
            for (int k = 0; k < _allKeys.Count; k++)
            {
                if (_showDupesOnly && !_duplicateKeys.Contains(_allKeys[k])) continue;
                if (hasFilter && !MatchesFilter(_allKeys[k])) continue;
                _visibleKeyIndices.Add(k);
            }

            // Calculate content height (rows + expanded metadata)
            float contentH = 0f;
            for (int v = 0; v < _visibleKeyIndices.Count; v++)
            {
                contentH += RowH;
                if (_expandedKeys.Contains(_allKeys[_visibleKeyIndices[v]]))
                    contentH += MetaRowH;
            }

            // Available rect for the table
            Rect tableRect = GUILayoutUtility.GetRect(1, 1,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (tableRect.height < 40f) return;

            float frozenW = FrozenW;
            float scrollW = ScrollableW;
            float rightW  = Mathf.Max(0f, tableRect.width - frozenW);
            float viewH   = tableRect.height - HeaderH - SepH;

            // ── Header row ──
            // Frozen header (Key) — always visible at left
            Rect frozenHdrR = new(tableRect.x, tableRect.y, frozenW, HeaderH);
            EditorGUI.DrawRect(frozenHdrR, HeaderBg);
            GUI.Label(new Rect(tableRect.x + FoldBtnW, tableRect.y, KeyColW, HeaderH), "Key", Header);

            // Scrollable header (locale labels) — clipped, synced with body H-scroll
            if (rightW > 0)
            {
                Rect scrollHdrClip = new(tableRect.x + frozenW, tableRect.y, rightW, HeaderH);
                GUI.BeginClip(scrollHdrClip);
                EditorGUI.DrawRect(new Rect(0, 0, Mathf.Max(scrollW, rightW), HeaderH), HeaderBg);
                for (int c = 0; c < _columns.Count; c++)
                {
                    Rect lr = new(-_scrollPos.x + c * ValColW, 0, ValColW, HeaderH);
                    if (GUI.Button(lr, _columns[c].LocaleCode, Header))
                    {
                        EditorGUIUtility.PingObject(_columns[c].Table);
                        Selection.activeObject = _columns[c].Table;
                    }
                }
                GUI.EndClip();
            }

            // Separator
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y + HeaderH, tableRect.width, SepH), SepColor);

            // ── Body (single scroll view, frozen columns offset by scrollPos.x) ──
            Rect bodyRect = new(tableRect.x, tableRect.y + HeaderH + SepH, tableRect.width, viewH);
            Rect bodyContentRect = new(0, 0, frozenW + Mathf.Max(scrollW, rightW), contentH);

            _scrollPos = GUI.BeginScrollView(bodyRect, _scrollPos, bodyContentRect);

            string deleteKey = null;
            float y = 0;
            float fx = _scrollPos.x; // offset to cancel horizontal scroll for frozen columns

            for (int v = 0; v < _visibleKeyIndices.Count; v++)
            {
                int ki = _visibleKeyIndices[v];
                string key = _allKeys[ki];
                bool isDupe = _duplicateKeys.Contains(key);
                bool isExpanded = _expandedKeys.Contains(key);

                // Alternate row background (full content width)
                if (v % 2 == 1)
                    EditorGUI.DrawRect(new Rect(0, y, bodyContentRect.width, RowH), AltRowColor);

                // ── Scrollable locale columns (drawn FIRST) ──
                for (int c = 0; c < _columns.Count; c++)
                {
                    Rect cellR = new(frozenW + c * ValColW, y, ValColW, RowH);
                    var col = _columns[c];
                    if (col.KeyToIndex.TryGetValue(key, out int idx))
                    {
                        var vp = col.EntriesProp.GetArrayElementAtIndex(idx).FindPropertyRelative("Value");
                        vp.stringValue = EditorGUI.TextField(cellR, vp.stringValue);
                    }
                    else
                    {
                        EditorGUI.DrawRect(cellR, MissingColor);
                        if (GUI.Button(cellR, "(missing)", MissBtn))
                        { AddKeyToColumn(c, key); _keysDirty = true; }
                    }
                }

                // ── Frozen columns (drawn SECOND with solid bg to cover scrolled content) ──
                EditorGUI.DrawRect(new Rect(fx, y, frozenW, RowH), EditorBg);
                if (v % 2 == 1) EditorGUI.DrawRect(new Rect(fx, y, frozenW, RowH), AltRowColor);
                EditorGUI.DrawRect(new Rect(fx + frozenW - 1f, y, 1f, RowH), SepColor);

                if (GUI.Button(new Rect(fx, y, FoldBtnW, RowH), isExpanded ? "▼" : "▶", FoldBtn))
                { if (isExpanded) _expandedKeys.Remove(key); else _expandedKeys.Add(key); }

                Rect keyR = new(fx + FoldBtnW, y, KeyColW, RowH);
                if (isDupe) EditorGUI.DrawRect(keyR, DuplicateColor);
                EditorGUI.BeginChangeCheck();
                string nk = EditorGUI.TextField(keyR, key);
                if (EditorGUI.EndChangeCheck() && nk != key && !string.IsNullOrEmpty(nk))
                    RenameKeyInAllColumns(key, nk);

                if (GUI.Button(new Rect(fx + FoldBtnW + KeyColW + 2f, y, DelBtnW, RowH), "✕"))
                    deleteKey = key;

                y += RowH;

                // ── Metadata sub-row (pinned horizontally via fx offset) ──
                if (isExpanded)
                {
                    float visibleW = Mathf.Min(frozenW + scrollW, bodyRect.width);
                    EditorGUI.DrawRect(new Rect(fx, y, visibleW, MetaRowH), EditorBg);
                    float metaX = fx + FoldBtnW;
                    float metaW = visibleW - FoldBtnW;
                    DrawMetadataSubRow(key, metaX, y, metaW);
                    y += MetaRowH;
                }
            }

            GUI.EndScrollView();

            if (deleteKey != null) RemoveKeyFromAllColumns(deleteKey);
        }

        private void DrawMetadataSubRow(string key, float x, float y, float w)
        {
            Rect box = new(x, y, w, MetaRowH - 4f);
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

            float pad = 4f;
            float cx = x + pad;
            float cy = y + pad;
            float cw = w - pad * 2;

            if (_metadataSO == null)
            {
                GUI.Label(new Rect(cx, cy, 200, 16), "No metadata asset.", EditorStyles.miniLabel);
                if (GUI.Button(new Rect(cx, cy + 18, 140, 18), "Create Metadata", EditorStyles.miniButton))
                    CreateMetadataAsset();
                return;
            }

            _metadataSO.Update();
            var entry = EnsureMetaEntry(key);

            // Comment label + text area
            GUI.Label(new Rect(cx, cy, 60, 14), "Comment", MetaLbl);
            var commentProp = entry.FindPropertyRelative("Comment");
            Rect taR = new(cx, cy + 14, cw, 34);
            commentProp.stringValue = EditorGUI.TextArea(taR, commentProp.stringValue);

            // MaxLength + Locked + Tags
            float row2Y = cy + 52;
            GUI.Label(new Rect(cx, row2Y, 62, 16), "Max Length", MetaLbl);
            var maxLenProp = entry.FindPropertyRelative("MaxLength");
            maxLenProp.intValue = EditorGUI.IntField(new Rect(cx + 62, row2Y, 44, 16), maxLenProp.intValue);

            var lockedProp = entry.FindPropertyRelative("Locked");
            lockedProp.boolValue = EditorGUI.ToggleLeft(new Rect(cx + 116, row2Y, 60, 16), "Locked", lockedProp.boolValue);

            GUI.Label(new Rect(cx + 184, row2Y, 30, 16), "Tags", MetaLbl);
            var tagsProp = entry.FindPropertyRelative("Tags");
            tagsProp.stringValue = EditorGUI.TextField(new Rect(cx + 216, row2Y, cw - 216, 16), tagsProp.stringValue);

            _metadataSO.ApplyModifiedProperties();
        }

        private bool MatchesFilter(string key)
        {
            if (key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int c = 0; c < _columns.Count; c++)
            {
                if (!_columns[c].KeyToIndex.TryGetValue(key, out int idx)) continue;
                string val = _columns[c].EntriesProp.GetArrayElementAtIndex(idx).FindPropertyRelative("Value").stringValue;
                if (val.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // ═════════════════════════════════════════════════════════
        // Metadata helpers
        // ═════════════════════════════════════════════════════════
        private int FindMetaEntryIndex(string key)
        {
            if (_metaEntriesProp == null) return -1;
            for (int i = 0; i < _metaEntriesProp.arraySize; i++)
                if (string.Equals(_metaEntriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("Key").stringValue, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private SerializedProperty EnsureMetaEntry(string key)
        {
            int idx = FindMetaEntryIndex(key);
            if (idx >= 0) return _metaEntriesProp.GetArrayElementAtIndex(idx);
            idx = _metaEntriesProp.arraySize;
            _metaEntriesProp.InsertArrayElementAtIndex(idx);
            var entry = _metaEntriesProp.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("Key").stringValue = key;
            entry.FindPropertyRelative("Comment").stringValue = string.Empty;
            entry.FindPropertyRelative("MaxLength").intValue = 0;
            entry.FindPropertyRelative("Locked").boolValue = false;
            entry.FindPropertyRelative("Tags").stringValue = string.Empty;
            return entry;
        }

        private void RemoveMetaEntry(string key)
        {
            if (_metaEntriesProp == null) return;
            int idx = FindMetaEntryIndex(key);
            if (idx < 0) return;
            _metaEntriesProp.DeleteArrayElementAtIndex(idx);
            _metadataSO.ApplyModifiedProperties();
        }

        private void RenameMetaEntry(string oldKey, string newKey)
        {
            if (_metaEntriesProp == null) return;
            int idx = FindMetaEntryIndex(oldKey);
            if (idx < 0) return;
            _metaEntriesProp.GetArrayElementAtIndex(idx).FindPropertyRelative("Key").stringValue = newKey;
            _metadataSO.ApplyModifiedProperties();
        }

        private void CreateMetadataAsset()
        {
            if (_selectedTableIdx < 0) return;
            string tableId = _tableIds[_selectedTableIdx];
            string dir = "Assets";
            if (_columns.Count > 0) { string p = AssetDatabase.GetAssetPath(_columns[0].Table); if (!string.IsNullOrEmpty(p)) dir = Path.GetDirectoryName(p); }
            string assetPath = Path.Combine(dir, $"StringTableMetadata_{tableId}_String.asset").Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<StringTableMetadata>(assetPath) != null) { Debug.LogWarning($"[Localization] Metadata already exists at {assetPath}"); return; }
            var meta = CreateInstance<StringTableMetadata>();
            var so = new SerializedObject(meta);
            so.FindProperty("tableId").stringValue = tableId;
            so.FindProperty("tableType").enumValueIndex = (int)TableType.String;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(meta, assetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(meta);
            RefreshMetadata(tableId);
        }

        // ═════════════════════════════════════════════════════════
        // Create locale
        // ═════════════════════════════════════════════════════════
        private void CreateNewLocaleTable(string localeCode)
        {
            for (int c = 0; c < _columns.Count; c++)
                if (string.Equals(_columns[c].LocaleCode, localeCode, StringComparison.Ordinal))
                { Debug.LogWarning($"[Localization] Locale \"{localeCode}\" already exists."); return; }

            string tableId = _selectedTableIdx >= 0 ? _tableIds[_selectedTableIdx] : "default";
            string dir = "Assets";
            if (_columns.Count > 0) { string p = AssetDatabase.GetAssetPath(_columns[0].Table); if (!string.IsNullOrEmpty(p)) dir = Path.GetDirectoryName(p); }
            string assetPath = Path.Combine(dir, $"StringTable_{tableId}_{localeCode}.asset").Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<StringTable>(assetPath) != null) { Debug.LogWarning($"[Localization] Already exists: {assetPath}"); return; }

            var newTable = CreateInstance<StringTable>();
            var so = new SerializedObject(newTable);
            so.FindProperty("tableId").stringValue = tableId;
            so.FindProperty("localeCode").stringValue = localeCode;
            var entries = so.FindProperty("entries");
            entries.ClearArray();
            for (int k = 0; k < _allKeys.Count; k++)
            {
                entries.InsertArrayElementAtIndex(k);
                var e = entries.GetArrayElementAtIndex(k);
                e.FindPropertyRelative("Key").stringValue = _allKeys[k];
                e.FindPropertyRelative("Value").stringValue = string.Empty;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(newTable, assetPath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newTable);
            _discoveryDirty = true;
        }

        // ═════════════════════════════════════════════════════════
        // Mutations
        // ═════════════════════════════════════════════════════════
        private void PurgeDuplicateKeys()
        {
            if (_duplicateCount == 0) return;
            if (!EditorUtility.DisplayDialog("Purge Duplicate Keys",
                $"Remove {_duplicateCount} duplicate key(s)?\nKeeps the LAST occurrence. Supports Undo.", "Purge", "Cancel")) return;
            int removed = 0;
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                if (col.DuplicateKeysInColumn.Count == 0) continue;
                Undo.RecordObject(col.Table, "Purge Duplicate Keys");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int j = col.EntriesProp.arraySize - 1; j >= 0; j--)
                {
                    string k = col.EntriesProp.GetArrayElementAtIndex(j).FindPropertyRelative("Key").stringValue;
                    if (!seen.Add(k)) { col.EntriesProp.DeleteArrayElementAtIndex(j); removed++; }
                }
                col.Serialized.ApplyModifiedProperties();
                RebuildColumnKeyMap(c);
            }
            _keysDirty = true;
        }

        private void AddKeyToAllColumns()
        {
            string newKey = GenerateUniqueKey();
            for (int c = 0; c < _columns.Count; c++) AddKeyToColumn(c, newKey);
            _allKeys.Add(newKey); _allKeysSet.Add(newKey); _keysDirty = true;
        }

        private void AddKeyToColumn(int colIdx, string key)
        {
            var col = _columns[colIdx];
            if (col.KeyToIndex.ContainsKey(key)) return;
            int idx = col.EntriesProp.arraySize;
            col.EntriesProp.InsertArrayElementAtIndex(idx);
            var e = col.EntriesProp.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("Key").stringValue = key;
            e.FindPropertyRelative("Value").stringValue = string.Empty;
            col.KeyToIndex[key] = idx;
        }

        private void RemoveKeyFromAllColumns(string key)
        {
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                if (!col.KeyToIndex.TryGetValue(key, out int idx)) continue;
                col.EntriesProp.DeleteArrayElementAtIndex(idx);
                col.KeyToIndex.Remove(key);
                RebuildColumnKeyMap(c);
            }
            _allKeysSet.Remove(key); _allKeys.Remove(key);
            _expandedKeys.Remove(key); RemoveMetaEntry(key);
            _keysDirty = true;
        }

        private void RenameKeyInAllColumns(string oldKey, string newKey)
        {
            if (_allKeysSet.Contains(newKey)) { Debug.LogWarning($"[Localization] Key \"{newKey}\" exists."); return; }
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                if (!col.KeyToIndex.TryGetValue(oldKey, out int idx)) continue;
                col.EntriesProp.GetArrayElementAtIndex(idx).FindPropertyRelative("Key").stringValue = newKey;
                col.KeyToIndex.Remove(oldKey); col.KeyToIndex[newKey] = idx;
            }
            int ki = _allKeys.IndexOf(oldKey);
            if (ki >= 0) _allKeys[ki] = newKey;
            _allKeysSet.Remove(oldKey); _allKeysSet.Add(newKey);
            if (_expandedKeys.Remove(oldKey)) _expandedKeys.Add(newKey);
            RenameMetaEntry(oldKey, newKey);
        }

        private void SyncKeysAcrossColumns()
        {
            RebuildKeys();
            int added = 0;
            for (int c = 0; c < _columns.Count; c++)
                for (int k = 0; k < _allKeys.Count; k++)
                    if (!_columns[c].KeyToIndex.ContainsKey(_allKeys[k])) { AddKeyToColumn(c, _allKeys[k]); added++; }
            _keysDirty = true; ApplyAllSerialized();
            Debug.Log(added > 0 ? $"[Localization] Synced: added {added} entries." : "[Localization] All synced.");
        }

        private void RebuildColumnKeyMap(int colIdx)
        {
            var col = _columns[colIdx];
            col.KeyToIndex.Clear(); col.DuplicateKeysInColumn.Clear();
            for (int j = 0; j < col.EntriesProp.arraySize; j++)
            {
                string k = col.EntriesProp.GetArrayElementAtIndex(j).FindPropertyRelative("Key").stringValue;
                if (col.KeyToIndex.ContainsKey(k)) col.DuplicateKeysInColumn.Add(k);
                col.KeyToIndex[k] = j;
            }
            _columns[colIdx] = col;
        }

        // ═════════════════════════════════════════════════════════
        // CSV
        // ═════════════════════════════════════════════════════════
        private void ExportMultiLanguageCSV()
        {
            if (_columns.Count == 0 || _allKeys.Count == 0) return;
            string path = EditorUtility.SaveFilePanel("Export CSV", "", _tableIds[_selectedTableIdx], "csv");
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder(4096);
            sb.Append("Key");
            for (int c = 0; c < _columns.Count; c++) sb.Append(',').Append(EscapeCSV(_columns[c].LocaleCode));
            sb.AppendLine();
            for (int k = 0; k < _allKeys.Count; k++)
            {
                sb.Append(EscapeCSV(_allKeys[k]));
                for (int c = 0; c < _columns.Count; c++)
                {
                    sb.Append(',');
                    if (_columns[c].KeyToIndex.TryGetValue(_allKeys[k], out int idx))
                        sb.Append(EscapeCSV(_columns[c].EntriesProp.GetArrayElementAtIndex(idx).FindPropertyRelative("Value").stringValue));
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private void ImportMultiLanguageCSV()
        {
            string path = EditorUtility.OpenFilePanel("Import CSV", "", "csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2) return;
            var header = ParseCSVLine(lines[0]);
            if (header.Count < 2 || !string.Equals(header[0], "Key", StringComparison.OrdinalIgnoreCase)) return;
            var colMap = new int[header.Count];
            colMap[0] = -1;
            for (int h = 1; h < header.Count; h++)
            {
                colMap[h] = -1;
                for (int c = 0; c < _columns.Count; c++)
                    if (string.Equals(_columns[c].LocaleCode, header[h], StringComparison.OrdinalIgnoreCase)) { colMap[h] = c; break; }
            }
            for (int c = 0; c < _columns.Count; c++) { _columns[c].EntriesProp.ClearArray(); _columns[c].KeyToIndex.Clear(); }
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var fields = ParseCSVLine(lines[i]);
                if (fields.Count < 1 || string.IsNullOrEmpty(fields[0])) continue;
                for (int h = 1; h < header.Count && h < fields.Count; h++)
                {
                    int ci = colMap[h]; if (ci < 0) continue;
                    var col = _columns[ci]; int ei = col.EntriesProp.arraySize;
                    col.EntriesProp.InsertArrayElementAtIndex(ei);
                    var e = col.EntriesProp.GetArrayElementAtIndex(ei);
                    e.FindPropertyRelative("Key").stringValue = fields[0];
                    e.FindPropertyRelative("Value").stringValue = fields[h];
                    col.KeyToIndex[fields[0]] = ei;
                }
            }
            ApplyAllSerialized(); _keysDirty = true;
            for (int c = 0; c < _columns.Count; c++) RebuildColumnKeyMap(c);
        }

        // ═════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════
        private void UpdateAllSerialized()
        {
            for (int c = 0; c < _columns.Count; c++) _columns[c].Serialized.Update();
            if (_metadata == null) { _metadataSO = null; _metaEntriesProp = null; return; }
            _metadataSO?.Update();
        }
        private void ApplyAllSerialized()
        {
            for (int c = 0; c < _columns.Count; c++) _columns[c].Serialized.ApplyModifiedProperties();
            if (_metadata == null) { _metadataSO = null; _metaEntriesProp = null; return; }
            _metadataSO?.ApplyModifiedProperties();
        }
        private string GenerateUniqueKey() { for (int n = 0; n < 10000; n++) { string c = $"new_key_{n}"; if (!_allKeysSet.Contains(c)) return c; } return $"new_key_{Guid.NewGuid():N}"; }
        private static string EscapeCSV(string v) { if (string.IsNullOrEmpty(v)) return v; if (v.Contains(',') || v.Contains('"') || v.Contains('\n')) return "\"" + v.Replace("\"", "\"\"") + "\""; return v; }
        private static List<string> ParseCSVLine(string line)
        {
            var fields = new List<string>(8); var sb = new StringBuilder(64); bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else inQ = false; } else sb.Append(c); }
                else { if (c == '"') inQ = true; else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
            }
            fields.Add(sb.ToString()); return fields;
        }
    }
}
#endif
