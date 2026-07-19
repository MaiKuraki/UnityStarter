#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    internal enum LocalizationTableKind : byte
    {
        String,
        Asset,
    }

    internal sealed class LocalizationTableColumn
    {
        public string LocaleCode;
        public UnityEngine.Object Table;
        public SerializedObject Serialized;
        public SerializedProperty Entries;
        public readonly Dictionary<string, int> KeyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> PrimaryValues = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Guids = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, UnityEngine.Object> Objects = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> SearchText = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly HashSet<string> DuplicateKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Cold-path discovery and content snapshots shared by the two localization table windows.
    /// </summary>
    internal sealed class LocalizationTableWorkspace
    {
        public readonly List<LocalizationTableColumn> Columns = new List<LocalizationTableColumn>();
        public readonly List<string> Keys = new List<string>();
        public readonly HashSet<string> KeySet = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> DuplicateKeys = new HashSet<string>(StringComparer.Ordinal);

        public LocalizationTableKind Kind { get; private set; }
        public string TableId { get; private set; }
        public string AuthoringLocaleCode { get; private set; }
        public string Error { get; private set; }
        public int MissingCount { get; private set; }

        public bool HasAuthoringColumn =>
            Columns.Count > 0 &&
            !string.IsNullOrEmpty(AuthoringLocaleCode) &&
            string.Equals(Columns[0].LocaleCode, AuthoringLocaleCode, StringComparison.Ordinal);

        public static void DiscoverTableIds(
            LocalizationTableKind kind,
            out string[] tableIds,
            out GUIContent[] contents)
        {
            string[] guids = AssetDatabase.FindAssets(kind == LocalizationTableKind.String ? "t:StringTable" : "t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                UnityEngine.Object table = LoadTable(kind, path);
                string tableId = GetTableId(kind, table);
                if (!string.IsNullOrEmpty(tableId))
                    ids.Add(tableId);
            }

            tableIds = new string[ids.Count];
            contents = new GUIContent[ids.Count];
            int itemIndex = 0;
            foreach (string id in ids)
            {
                tableIds[itemIndex] = id;
                contents[itemIndex] = new GUIContent(id);
                itemIndex++;
            }
        }

        public void Reload(LocalizationTableKind kind, string tableId, string authoringLocaleCode)
        {
            Clear();
            Kind = kind;
            TableId = tableId;
            AuthoringLocaleCode = authoringLocaleCode;

            string[] guids = AssetDatabase.FindAssets(kind == LocalizationTableKind.String ? "t:StringTable" : "t:AssetTable");
            Array.Sort(guids, CompareAssetGuidByPath);
            var localeCodes = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                UnityEngine.Object table = LoadTable(kind, path);
                if (table == null || !string.Equals(GetTableId(kind, table), tableId, StringComparison.Ordinal))
                    continue;

                string localeCode = GetLocaleCode(kind, table);
                if (!localeCodes.Add(localeCode))
                {
                    Error = "Multiple " + GetTypeLabel(kind) + " assets use tableId '" + tableId +
                            "' and locale '" + localeCode + "'.";
                    continue;
                }

                var column = new LocalizationTableColumn
                {
                    LocaleCode = localeCode,
                    Table = table,
                    Serialized = new SerializedObject(table),
                };
                RebuildColumn(column);
                Columns.Add(column);
            }

            Columns.Sort(CompareColumns);
            if (!HasAuthoringColumn && string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(authoringLocaleCode))
            {
                Error = "Table '" + tableId + "' has no " + GetTypeLabel(kind) +
                        " for Authoring Locale '" + authoringLocaleCode + "'.";
            }
            RebuildKeyIndex();
        }

        public void Clear()
        {
            Columns.Clear();
            Keys.Clear();
            KeySet.Clear();
            DuplicateKeys.Clear();
            MissingCount = 0;
            Error = null;
        }

        public void UpdateSerializedObjects()
        {
            for (int index = 0; index < Columns.Count; index++)
                Columns[index].Serialized.UpdateIfRequiredOrScript();
        }

        public void ApplySerializedObjects()
        {
            for (int index = 0; index < Columns.Count; index++)
                Columns[index].Serialized.ApplyModifiedProperties();
        }

        public void RebuildColumn(LocalizationTableColumn column)
        {
            column.KeyToIndex.Clear();
            column.PrimaryValues.Clear();
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
                if (column.KeyToIndex.ContainsKey(key))
                    column.DuplicateKeys.Add(key);
                column.KeyToIndex[key] = index;

                if (Kind == LocalizationTableKind.String)
                {
                    string value = entry.FindPropertyRelative("Value")?.stringValue ?? string.Empty;
                    column.PrimaryValues[key] = value;
                    column.SearchText[key] = value;
                    continue;
                }

                SerializedProperty asset = entry.FindPropertyRelative("Asset");
                string location = asset?.FindPropertyRelative("m_Location")?.stringValue ?? string.Empty;
                string guid = asset?.FindPropertyRelative("m_GUID")?.stringValue ?? string.Empty;
                column.PrimaryValues[key] = location;
                column.Guids[key] = guid;
                string assetPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object assetObject = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                column.Objects[key] = assetObject;
                column.SearchText[key] = string.Concat(location, "\n", guid, "\n", assetPath);
            }
        }

        public void RebuildKeyIndex()
        {
            Keys.Clear();
            KeySet.Clear();
            DuplicateKeys.Clear();
            MissingCount = 0;
            for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
            {
                LocalizationTableColumn column = Columns[columnIndex];
                foreach (string key in column.KeyToIndex.Keys)
                {
                    if (KeySet.Add(key))
                        Keys.Add(key);
                }
                foreach (string duplicate in column.DuplicateKeys)
                    DuplicateKeys.Add(duplicate);
            }
            Keys.Sort(StringComparer.Ordinal);
            for (int keyIndex = 0; keyIndex < Keys.Count; keyIndex++)
            {
                for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
                {
                    if (!Columns[columnIndex].KeyToIndex.ContainsKey(Keys[keyIndex]))
                        MissingCount++;
                }
            }
        }

        public void AppendEmpty(LocalizationTableColumn column, string key)
        {
            column.Serialized.UpdateIfRequiredOrScript();
            int index = column.Entries.arraySize;
            column.Entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = column.Entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Key").stringValue = key;
            if (Kind == LocalizationTableKind.String)
            {
                entry.FindPropertyRelative("Value").stringValue = string.Empty;
            }
            else
            {
                SerializedProperty asset = entry.FindPropertyRelative("Asset");
                asset.FindPropertyRelative("m_GUID").stringValue = string.Empty;
                asset.FindPropertyRelative("m_Location").stringValue = string.Empty;
            }
            column.Serialized.ApplyModifiedProperties();
            RebuildColumn(column);
        }

        public void RemoveKey(string key)
        {
            for (int index = 0; index < Columns.Count; index++)
            {
                LocalizationTableColumn column = Columns[index];
                column.Serialized.UpdateIfRequiredOrScript();
                if (!column.KeyToIndex.TryGetValue(key, out int entryIndex))
                    continue;
                column.Entries.DeleteArrayElementAtIndex(entryIndex);
                column.Serialized.ApplyModifiedProperties();
                RebuildColumn(column);
            }
            RebuildKeyIndex();
        }

        public void RenameKey(string oldKey, string newKey)
        {
            for (int index = 0; index < Columns.Count; index++)
            {
                LocalizationTableColumn column = Columns[index];
                column.Serialized.UpdateIfRequiredOrScript();
                if (!column.KeyToIndex.TryGetValue(oldKey, out int entryIndex))
                    continue;
                column.Entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("Key").stringValue = newKey;
                column.Serialized.ApplyModifiedProperties();
                RebuildColumn(column);
            }
            RebuildKeyIndex();
        }

        public void PurgeDuplicates()
        {
            for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
            {
                LocalizationTableColumn column = Columns[columnIndex];
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
                RebuildColumn(column);
            }
            RebuildKeyIndex();
        }

        public void SyncKeys()
        {
            for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
            {
                LocalizationTableColumn column = Columns[columnIndex];
                for (int keyIndex = 0; keyIndex < Keys.Count; keyIndex++)
                {
                    if (!column.KeyToIndex.ContainsKey(Keys[keyIndex]))
                        AppendEmpty(column, Keys[keyIndex]);
                }
            }
            RebuildKeyIndex();
        }

        public string GenerateUniqueKey()
        {
            string prefix = Kind == LocalizationTableKind.String ? "new_key_" : "new_asset_key_";
            for (int index = 0; index < 10_000; index++)
            {
                string candidate = prefix + index;
                if (!KeySet.Contains(candidate))
                    return candidate;
            }
            return prefix + Guid.NewGuid().ToString("N");
        }

        public UnityEngine.Object[] GetTableTargets()
        {
            var targets = new UnityEngine.Object[Columns.Count];
            for (int index = 0; index < Columns.Count; index++)
                targets[index] = Columns[index].Table;
            return targets;
        }

        public int CountPopulated(string key)
        {
            int count = 0;
            for (int index = 0; index < Columns.Count; index++)
            {
                if (Columns[index].PrimaryValues.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                    count++;
            }
            return count;
        }

        private int CompareColumns(LocalizationTableColumn left, LocalizationTableColumn right)
        {
            bool leftAuthoring = string.Equals(left.LocaleCode, AuthoringLocaleCode, StringComparison.Ordinal);
            bool rightAuthoring = string.Equals(right.LocaleCode, AuthoringLocaleCode, StringComparison.Ordinal);
            if (leftAuthoring != rightAuthoring)
                return leftAuthoring ? -1 : 1;
            return string.CompareOrdinal(left.LocaleCode, right.LocaleCode);
        }

        private static UnityEngine.Object LoadTable(LocalizationTableKind kind, string path)
        {
            return kind == LocalizationTableKind.String
                ? (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<StringTable>(path)
                : AssetDatabase.LoadAssetAtPath<AssetTable>(path);
        }

        private static string GetTableId(LocalizationTableKind kind, UnityEngine.Object table)
        {
            return kind == LocalizationTableKind.String
                ? (table as StringTable)?.TableId
                : (table as AssetTable)?.TableId;
        }

        private static string GetLocaleCode(LocalizationTableKind kind, UnityEngine.Object table)
        {
            return kind == LocalizationTableKind.String
                ? (table as StringTable)?.LocaleId.Code
                : (table as AssetTable)?.LocaleId.Code;
        }

        private static string GetTypeLabel(LocalizationTableKind kind)
        {
            return kind == LocalizationTableKind.String ? "StringTable" : "AssetTable";
        }

        private static int CompareAssetGuidByPath(string leftGuid, string rightGuid)
        {
            return string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(leftGuid),
                AssetDatabase.GUIDToAssetPath(rightGuid));
        }
    }

    internal sealed class LocalizationGridCallbacks
    {
        public Func<string, bool> IncludeKey;
        public Func<string, bool> IsLocked;
        public Action<int, string, Rect, bool> DrawCell;
        public Action<string, Rect> DrawMetadata;
        public Action<int> SelectColumn;
    }

    internal sealed class LocalizationGridState
    {
        private readonly Func<int, bool> _includeRow;
        private readonly Func<int, float> _getRowHeight;
        private LocalizationTableWorkspace _workspace;
        private LocalizationGridCallbacks _callbacks;
        private float _metadataHeight;

        public readonly LocalizationVisibleRowCache Rows = new LocalizationVisibleRowCache();
        public readonly HashSet<string> ExpandedKeys = new HashSet<string>(StringComparer.Ordinal);
        public Vector2 ScrollPosition;
        public bool Dirty = true;

        public LocalizationGridState()
        {
            _includeRow = IncludeRow;
            _getRowHeight = GetRowHeight;
        }

        public void EnsureRows(
            LocalizationTableWorkspace workspace,
            LocalizationGridCallbacks callbacks,
            float metadataHeight)
        {
            if (!Dirty)
                return;
            Dirty = false;
            _workspace = workspace;
            _callbacks = callbacks;
            _metadataHeight = metadataHeight;
            Rows.Rebuild(workspace.Keys.Count, _includeRow, _getRowHeight);
        }

        public void Reset()
        {
            ExpandedKeys.Clear();
            ScrollPosition = Vector2.zero;
            Dirty = true;
        }

        private bool IncludeRow(int index)
        {
            return _callbacks.IncludeKey == null || _callbacks.IncludeKey(_workspace.Keys[index]);
        }

        private float GetRowHeight(int index)
        {
            return LocalizationTableGrid.RowHeight +
                   (ExpandedKeys.Contains(_workspace.Keys[index]) ? _metadataHeight : 0f);
        }
    }

    internal readonly struct LocalizationGridAction
    {
        public readonly string DeleteKey;
        public readonly string RenameOldKey;
        public readonly string RenameNewKey;

        public LocalizationGridAction(string deleteKey, string renameOldKey, string renameNewKey)
        {
            DeleteKey = deleteKey;
            RenameOldKey = renameOldKey;
            RenameNewKey = renameNewKey;
        }
    }

    internal static class LocalizationTableGrid
    {
        public const float RowHeight = 20f;
        private const float FoldButtonWidth = 18f;
        private const float KeyColumnWidth = 180f;
        private const float DeleteButtonWidth = 22f;
        private const float HeaderHeight = 22f;
        private const float SeparatorHeight = 1f;

        private static readonly Color DuplicateColor = new Color(0.85f, 0.2f, 0.25f, 0.35f);
        private static readonly Color LockedColor = new Color(0.35f, 0.45f, 0.65f, 0.18f);
        private static readonly Color AlternateColor = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color SeparatorColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color HeaderColor = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);

        private static GUIStyle s_header;
        private static GUIStyle s_foldButton;

        private static GUIStyle Header => s_header ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            padding = new RectOffset(4, 4, 2, 2),
            alignment = TextAnchor.MiddleLeft,
        };

        private static GUIStyle FoldButton => s_foldButton ??= new GUIStyle(EditorStyles.label)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
        };

        public static LocalizationGridAction Draw(
            LocalizationTableWorkspace workspace,
            LocalizationGridState state,
            LocalizationGridCallbacks callbacks,
            bool canEdit,
            float valueColumnWidth,
            float metadataHeight)
        {
            state.EnsureRows(workspace, callbacks, metadataHeight);
            Rect tableRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (tableRect.height < 40f)
                return default;

            bool hasAuthoring = workspace.HasAuthoringColumn;
            float frozenWidth = FoldButtonWidth + KeyColumnWidth +
                                (hasAuthoring ? valueColumnWidth : 0f) + DeleteButtonWidth + 4f;
            int scrollingStart = hasAuthoring ? 1 : 0;
            float scrollableWidth = Math.Max(0, workspace.Columns.Count - scrollingStart) * valueColumnWidth;
            float rightWidth = Mathf.Max(0f, tableRect.width - frozenWidth);
            float viewportHeight = tableRect.height - HeaderHeight - SeparatorHeight;

            DrawHeaders(workspace, state, callbacks, tableRect, frozenWidth, scrollableWidth, rightWidth, valueColumnWidth);
            EditorGUI.DrawRect(
                new Rect(tableRect.x, tableRect.y + HeaderHeight, tableRect.width, SeparatorHeight),
                SeparatorColor);

            Rect bodyRect = new Rect(
                tableRect.x,
                tableRect.y + HeaderHeight + SeparatorHeight,
                tableRect.width,
                viewportHeight);
            Rect contentRect = new Rect(
                0f,
                0f,
                frozenWidth + Mathf.Max(scrollableWidth, rightWidth),
                Mathf.Max(state.Rows.TotalHeight, viewportHeight));
            state.ScrollPosition = GUI.BeginScrollView(bodyRect, state.ScrollPosition, contentRect);

            LocalizationVisibleRange range = state.Rows.FindVisibleRange(state.ScrollPosition.y, viewportHeight);
            string deleteKey = null;
            string renameOld = null;
            string renameNew = null;
            float fixedX = state.ScrollPosition.x;

            for (int visibleIndex = range.Start; visibleIndex < range.EndExclusive; visibleIndex++)
            {
                string key = workspace.Keys[state.Rows.GetSourceIndex(visibleIndex)];
                float rowTop = state.Rows.GetTop(visibleIndex);
                bool expanded = state.ExpandedKeys.Contains(key);
                bool locked = callbacks.IsLocked != null && callbacks.IsLocked(key);
                if ((visibleIndex & 1) != 0)
                    EditorGUI.DrawRect(new Rect(0f, rowTop, contentRect.width, RowHeight), AlternateColor);
                if (locked)
                    EditorGUI.DrawRect(new Rect(0f, rowTop, contentRect.width, RowHeight), LockedColor);

                for (int columnIndex = scrollingStart; columnIndex < workspace.Columns.Count; columnIndex++)
                {
                    callbacks.DrawCell?.Invoke(
                        columnIndex,
                        key,
                        new Rect(
                            frozenWidth + (columnIndex - scrollingStart) * valueColumnWidth,
                            rowTop,
                            valueColumnWidth,
                            RowHeight),
                        locked);
                }

                EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), BackgroundColor);
                if ((visibleIndex & 1) != 0)
                    EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), AlternateColor);
                if (locked)
                    EditorGUI.DrawRect(new Rect(fixedX, rowTop, frozenWidth, RowHeight), LockedColor);
                EditorGUI.DrawRect(new Rect(fixedX + frozenWidth - 1f, rowTop, 1f, RowHeight), SeparatorColor);

                if (GUI.Button(new Rect(fixedX, rowTop, FoldButtonWidth, RowHeight), expanded ? "-" : "+", FoldButton))
                {
                    if (expanded)
                        state.ExpandedKeys.Remove(key);
                    else
                        state.ExpandedKeys.Add(key);
                    state.Dirty = true;
                }

                Rect keyRect = new Rect(fixedX + FoldButtonWidth, rowTop, KeyColumnWidth, RowHeight);
                if (workspace.DuplicateKeys.Contains(key))
                    EditorGUI.DrawRect(keyRect, DuplicateColor);
                using (new EditorGUI.DisabledScope(!canEdit || locked))
                {
                    EditorGUI.BeginChangeCheck();
                    string nextKey = EditorGUI.DelayedTextField(keyRect, key);
                    if (EditorGUI.EndChangeCheck() && !string.Equals(nextKey, key, StringComparison.Ordinal))
                    {
                        renameOld = key;
                        renameNew = nextKey;
                    }
                }

                float deleteX = fixedX + FoldButtonWidth + KeyColumnWidth;
                if (hasAuthoring)
                {
                    callbacks.DrawCell?.Invoke(
                        0,
                        key,
                        new Rect(deleteX, rowTop, valueColumnWidth, RowHeight),
                        locked);
                    deleteX += valueColumnWidth;
                }
                using (new EditorGUI.DisabledScope(!canEdit || locked))
                {
                    if (GUI.Button(new Rect(deleteX + 2f, rowTop, DeleteButtonWidth, RowHeight), "X"))
                        deleteKey = key;
                }

                if (expanded && callbacks.DrawMetadata != null)
                {
                    float metadataTop = rowTop + RowHeight;
                    float visibleWidth = Mathf.Min(frozenWidth + scrollableWidth, bodyRect.width);
                    EditorGUI.DrawRect(new Rect(fixedX, metadataTop, visibleWidth, metadataHeight), BackgroundColor);
                    callbacks.DrawMetadata(
                        key,
                        new Rect(
                            fixedX + FoldButtonWidth,
                            metadataTop,
                            visibleWidth - FoldButtonWidth,
                            metadataHeight));
                }
            }

            GUI.EndScrollView();
            return new LocalizationGridAction(deleteKey, renameOld, renameNew);
        }

        private static void DrawHeaders(
            LocalizationTableWorkspace workspace,
            LocalizationGridState state,
            LocalizationGridCallbacks callbacks,
            Rect tableRect,
            float frozenWidth,
            float scrollableWidth,
            float rightWidth,
            float valueColumnWidth)
        {
            EditorGUI.DrawRect(new Rect(tableRect.x, tableRect.y, frozenWidth, HeaderHeight), HeaderColor);
            GUI.Label(
                new Rect(tableRect.x + FoldButtonWidth, tableRect.y, KeyColumnWidth, HeaderHeight),
                "Key",
                Header);
            if (workspace.HasAuthoringColumn && GUI.Button(
                    new Rect(
                        tableRect.x + FoldButtonWidth + KeyColumnWidth,
                        tableRect.y,
                        valueColumnWidth,
                        HeaderHeight),
                    workspace.Columns[0].LocaleCode + "  [Authoring]",
                    Header))
            {
                callbacks.SelectColumn?.Invoke(0);
            }

            if (rightWidth <= 0f)
                return;
            Rect clip = new Rect(tableRect.x + frozenWidth, tableRect.y, rightWidth, HeaderHeight);
            GUI.BeginClip(clip);
            EditorGUI.DrawRect(new Rect(0f, 0f, Mathf.Max(scrollableWidth, rightWidth), HeaderHeight), HeaderColor);
            int start = workspace.HasAuthoringColumn ? 1 : 0;
            for (int columnIndex = start; columnIndex < workspace.Columns.Count; columnIndex++)
            {
                if (GUI.Button(
                        new Rect(
                            -state.ScrollPosition.x + (columnIndex - start) * valueColumnWidth,
                            0f,
                            valueColumnWidth,
                            HeaderHeight),
                        workspace.Columns[columnIndex].LocaleCode,
                        Header))
                {
                    callbacks.SelectColumn?.Invoke(columnIndex);
                }
            }
            GUI.EndClip();
        }
    }

    internal static class LocalizationTableMutationWorkflow
    {
        public static bool TryAddAuthoringKey(
            LocalizationTableWorkspace workspace,
            out string changedKey)
        {
            changedKey = null;
            if (!workspace.HasAuthoringColumn)
                return false;
            string key = workspace.GenerateUniqueKey();
            if (!LocalizationUndoTransaction.TryExecute(
                    "Add Localization Key",
                    new[] { workspace.Columns[0].Table },
                    () => workspace.AppendEmpty(workspace.Columns[0], key),
                    out string error))
            {
                EditorUtility.DisplayDialog("Add Localization Key", "Add failed and was rolled back. " + error, "OK");
                return false;
            }
            workspace.RebuildKeyIndex();
            changedKey = key;
            return true;
        }

        public static bool TryDelete(
            LocalizationTableWorkspace workspace,
            LocalizationMetadataIndex metadata,
            string key)
        {
            if (metadata.IsLocked(key))
            {
                EditorUtility.DisplayDialog("Delete Localization Key", "The key is locked. Unlock it before deleting table entries.", "OK");
                return false;
            }
            int tableCount = 0;
            for (int index = 0; index < workspace.Columns.Count; index++)
            {
                if (workspace.Columns[index].KeyToIndex.ContainsKey(key))
                    tableCount++;
            }
            string metadataImpact = metadata.Contains(key)
                ? "The metadata entry is retained and will be reported as orphaned until handled explicitly."
                : "No metadata entry exists.";
            if (!EditorUtility.DisplayDialog(
                    "Delete Localization Key",
                    "Delete '" + key + "' from " + tableCount + " locale table(s)?\n" +
                    workspace.CountPopulated(key) + " populated cell(s) will be removed.\n\n" + metadataImpact,
                    "Delete",
                    "Cancel"))
            {
                return false;
            }

            if (!LocalizationUndoTransaction.TryExecute(
                    "Delete Localization Key",
                    workspace.GetTableTargets(),
                    () => workspace.RemoveKey(key),
                    out string error))
            {
                EditorUtility.DisplayDialog("Delete Localization Key", "Delete failed and was rolled back. " + error, "OK");
                return false;
            }
            return true;
        }

        public static bool TryRename(
            LocalizationTableWorkspace workspace,
            LocalizationMetadataIndex metadata,
            string oldKey,
            string newKey)
        {
            newKey = newKey?.Trim();
            if (string.IsNullOrEmpty(newKey) || newKey.Length > LocalizationCatalogBuilder.MaxKeyChars)
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The new key is empty or exceeds the supported length.", "OK");
                return false;
            }
            if (workspace.KeySet.Contains(newKey))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The key already exists: " + newKey, "OK");
                return false;
            }
            if (metadata.IsLocked(oldKey))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "The key is locked. Unlock it before renaming table entries.", "OK");
                return false;
            }

            int tableCount = 0;
            for (int index = 0; index < workspace.Columns.Count; index++)
            {
                if (workspace.Columns[index].KeyToIndex.ContainsKey(oldKey))
                    tableCount++;
            }
            string metadataImpact = metadata.Contains(oldKey)
                ? "Metadata remains under the original key and must be migrated explicitly."
                : "No metadata entry exists.";
            if (!EditorUtility.DisplayDialog(
                    "Rename Localization Key",
                    "Rename '" + oldKey + "' to '" + newKey + "' in " + tableCount + " locale table(s)?\n\n" + metadataImpact,
                    "Rename",
                    "Cancel"))
            {
                return false;
            }

            if (!LocalizationUndoTransaction.TryExecute(
                    "Rename Localization Key",
                    workspace.GetTableTargets(),
                    () => workspace.RenameKey(oldKey, newKey),
                    out string error))
            {
                EditorUtility.DisplayDialog("Rename Localization Key", "Rename failed and was rolled back. " + error, "OK");
                return false;
            }
            return true;
        }

        public static bool TryPurgeDuplicates(
            LocalizationTableWorkspace workspace,
            LocalizationMetadataIndex metadata)
        {
            foreach (string key in workspace.DuplicateKeys)
            {
                if (!metadata.IsLocked(key))
                    continue;
                EditorUtility.DisplayDialog(
                    "Purge Duplicate Keys",
                    "Duplicate key '" + key + "' is locked. No entries were changed.",
                    "OK");
                return false;
            }
            if (!EditorUtility.DisplayDialog(
                    "Purge Duplicate Keys",
                    "Remove duplicate occurrences while keeping the last occurrence in each locale table?",
                    "Purge",
                    "Cancel"))
            {
                return false;
            }
            if (!LocalizationUndoTransaction.TryExecute(
                    "Purge Localization Duplicates",
                    workspace.GetTableTargets(),
                    workspace.PurgeDuplicates,
                    out string error))
            {
                EditorUtility.DisplayDialog("Purge Duplicate Keys", "Purge failed and was rolled back. " + error, "OK");
                return false;
            }
            return true;
        }

        public static bool TrySyncKeys(
            LocalizationTableWorkspace workspace,
            LocalizationMetadataIndex metadata)
        {
            for (int keyIndex = 0; keyIndex < workspace.Keys.Count; keyIndex++)
            {
                string key = workspace.Keys[keyIndex];
                if (!metadata.IsLocked(key))
                    continue;
                for (int columnIndex = 0; columnIndex < workspace.Columns.Count; columnIndex++)
                {
                    if (workspace.Columns[columnIndex].KeyToIndex.ContainsKey(key))
                        continue;
                    EditorUtility.DisplayDialog(
                        "Sync Localization Keys",
                        "Locked key '" + key + "' is missing from at least one locale. No entries were changed.",
                        "OK");
                    return false;
                }
            }
            if (workspace.MissingCount == 0)
                return false;

            string consequence = workspace.Kind == LocalizationTableKind.String
                ? "Sparse fallback cells will become explicit empty values."
                : "Sparse fallback cells will become explicit invalid AssetRef values until configured.";
            if (!EditorUtility.DisplayDialog(
                    "Sync Localization Keys",
                    "Add " + workspace.MissingCount + " explicit empty entries? " + consequence,
                    "Sync",
                    "Cancel"))
            {
                return false;
            }
            if (!LocalizationUndoTransaction.TryExecute(
                    "Sync Localization Keys",
                    workspace.GetTableTargets(),
                    workspace.SyncKeys,
                    out string error))
            {
                EditorUtility.DisplayDialog("Sync Localization Keys", "Sync failed and was rolled back. " + error, "OK");
                return false;
            }
            return true;
        }
    }

    internal static class LocalizationMetadataAuthoringGUI
    {
        private static GUIStyle s_label;

        private static GUIStyle Label
        {
            get
            {
                if (s_label != null)
                    return s_label;
                s_label = new GUIStyle(EditorStyles.miniLabel);
                s_label.normal.textColor = new Color(0.7f, 0.7f, 0.8f);
                return s_label;
            }
        }

        public static void Draw(
            LocalizationMetadataIndex metadata,
            string metadataError,
            string key,
            Rect rect,
            bool showMaxLength,
            Action createMetadataAsset)
        {
            GUI.Box(new Rect(rect.x, rect.y, rect.width, rect.height - 4f), GUIContent.none, EditorStyles.helpBox);
            float x = rect.x + 4f;
            float y = rect.y + 4f;
            float width = Mathf.Max(0f, rect.width - 8f);
            if (metadata.Metadata == null)
            {
                GUI.Label(new Rect(x, y, 280f, 16f), "No metadata asset is assigned to this table.", EditorStyles.miniLabel);
                if (string.IsNullOrEmpty(metadataError) && createMetadataAsset != null &&
                    GUI.Button(new Rect(x, y + 20f, 150f, 18f), "Create Metadata Asset", EditorStyles.miniButton))
                {
                    createMetadataAsset();
                }
                return;
            }

            SerializedProperty entry = metadata.GetEntry(key);
            if (entry == null)
            {
                GUI.Label(
                    new Rect(x, y, width, 32f),
                    "No metadata entry exists. Expanding a row never mutates metadata.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUI.Button(new Rect(x, y + 36f, 160f, 18f), "Create Entry Metadata", EditorStyles.miniButton) &&
                    !metadata.CreateEntry(key, "Create Localization Metadata Entry", out string error))
                {
                    EditorUtility.DisplayDialog("Localization Metadata", error, "OK");
                }
                return;
            }

            SerializedProperty lockedProperty = entry.FindPropertyRelative("Locked");
            bool locked = lockedProperty.boolValue;
            bool nextLocked = EditorGUI.ToggleLeft(new Rect(x, y, 70f, 18f), "Locked", locked);
            if (nextLocked != locked)
            {
                Undo.RecordObject(metadata.Metadata, nextLocked ? "Lock Localization Entry" : "Unlock Localization Entry");
                lockedProperty.boolValue = nextLocked;
                metadata.Serialized.ApplyModifiedProperties();
                locked = nextLocked;
            }

            int sourceRevision = entry.FindPropertyRelative("SourceRevision")?.intValue ?? 0;
            GUI.Label(new Rect(x + 82f, y + 1f, 190f, 16f), "Source Revision: " + sourceRevision, Label);
            GUI.Label(new Rect(x + 276f, y + 1f, width - 276f, 16f), BuildStatusSummary(entry), Label);
            using (new EditorGUI.DisabledScope(locked))
            {
                SerializedProperty commentProperty = entry.FindPropertyRelative("Comment");
                SerializedProperty maxLengthProperty = entry.FindPropertyRelative("MaxLength");
                SerializedProperty tagsProperty = entry.FindPropertyRelative("Tags");
                SerializedProperty screenshotProperty = entry.FindPropertyRelative("Screenshot");
                string comment = commentProperty?.stringValue ?? string.Empty;
                string tags = tagsProperty?.stringValue ?? string.Empty;
                int maxLength = maxLengthProperty?.intValue ?? 0;
                UnityEngine.Object screenshot = screenshotProperty?.objectReferenceValue;

                GUI.Label(new Rect(x, y + 22f, 60f, 14f), "Comment", Label);
                string nextComment = EditorGUI.TextArea(
                    new Rect(x, y + 36f, width, showMaxLength ? 34f : 28f),
                    comment);
                float bottomY = y + (showMaxLength ? 76f : 70f);
                float cursor = x;
                int nextMaxLength = maxLength;
                if (showMaxLength)
                {
                    GUI.Label(new Rect(cursor, bottomY, 64f, 18f), "Max Length", Label);
                    nextMaxLength = EditorGUI.IntField(new Rect(cursor + 66f, bottomY, 54f, 18f), maxLength);
                    cursor += 130f;
                }
                GUI.Label(new Rect(cursor, bottomY, 32f, 18f), "Tags", Label);
                string nextTags = EditorGUI.TextField(
                    new Rect(cursor + 34f, bottomY, Mathf.Max(80f, x + width - 220f - cursor - 38f), 18f),
                    tags);
                UnityEngine.Object nextScreenshot = EditorGUI.ObjectField(
                    new Rect(x + width - 220f, bottomY, 220f, 18f),
                    screenshot,
                    typeof(Texture2D),
                    false);

                if (!string.Equals(nextComment, comment, StringComparison.Ordinal) ||
                    nextMaxLength != maxLength ||
                    !string.Equals(nextTags, tags, StringComparison.Ordinal) ||
                    nextScreenshot != screenshot)
                {
                    Undo.RecordObject(metadata.Metadata, "Edit Localization Metadata");
                    commentProperty.stringValue = nextComment;
                    if (showMaxLength && maxLengthProperty != null)
                        maxLengthProperty.intValue = Math.Max(0, nextMaxLength);
                    tagsProperty.stringValue = nextTags;
                    if (screenshotProperty != null)
                        screenshotProperty.objectReferenceValue = nextScreenshot;
                    metadata.Serialized.ApplyModifiedProperties();
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
    }
}
#endif
