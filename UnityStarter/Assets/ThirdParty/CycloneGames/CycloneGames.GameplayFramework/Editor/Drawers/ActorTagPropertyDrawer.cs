using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom PropertyDrawer for [ActorTag(typeof(ConstantsClass))].
    /// Displays a dropdown button that opens a searchable popup window with all tags
    /// defined as public const string fields in the specified constants class.
    /// </summary>
    [CustomPropertyDrawer(typeof(ActorTagAttribute))]
    public class ActorTagPropertyDrawer : PropertyDrawer
    {
        /// <summary>
        /// Caches the reflected constant data per type to avoid repeated reflection.
        /// Automatically cleared on domain reload (script compilation).
        /// </summary>
        private static readonly Dictionary<Type, CachedConstantData> s_constantsCache = new();

        private static readonly GUIContent s_TempContent = new();

        private class CachedConstantData
        {
            public readonly string[] DisplayNames;
            public readonly string[] Values;
            public readonly Dictionary<string, string> ValueToDisplayMap;

            public CachedConstantData(List<FieldInfo> fields)
            {
                DisplayNames = fields.Select(f => f.Name).ToArray();
                Values = fields.Select(f => (string)f.GetValue(null)).ToArray();

                ValueToDisplayMap = new Dictionary<string, string>(Values.Length);
                for (int i = 0; i < Values.Length; i++)
                {
                    if (!ValueToDisplayMap.ContainsKey(Values[i]))
                        ValueToDisplayMap.Add(Values[i], DisplayNames[i]);
                }
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [ActorTag] with string fields only.");
                return;
            }

            var attrib = attribute as ActorTagAttribute;

            // If no constants type specified, draw as a normal string field
            if (attrib?.ConstantsType == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            CachedConstantData data = GetOrCacheConstants(attrib.ConstantsType);
            if (data == null || data.Values.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, $"No const strings in {attrib.ConstantsType.Name}.");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            string currentValue = property.stringValue;
            bool hasValue = !string.IsNullOrEmpty(currentValue);
            bool isValid = hasValue && data.ValueToDisplayMap.ContainsKey(currentValue);

            // Determine button text
            if (!hasValue)
                s_TempContent.text = "(None)";
            else if (!isValid)
                s_TempContent.text = $"{currentValue} (Invalid)";
            else
                s_TempContent.text = data.ValueToDisplayMap[currentValue];

            s_TempContent.tooltip = isValid ? currentValue : null;

            // Clear button when a tag is selected
            Rect clearRect = default;
            if (hasValue)
            {
                clearRect = new Rect(position.xMax - 18, position.y, 18, position.height);
                position.width -= 20;
            }

            // Prefix label
            position = EditorGUI.PrefixLabel(position, label);

            // Draw the dropdown button
            if (EditorGUI.DropdownButton(position, s_TempContent, FocusType.Keyboard))
            {
                var picker = new ActorTagPickerPopup(data, property, position.width);
                PopupWindow.Show(position, picker);
            }

            // Draw clear (×) button
            if (hasValue)
            {
                Color prev = GUI.color;
                if (!isValid) GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUI.Button(clearRect, "\u00D7", EditorStyles.miniLabel))
                {
                    property.stringValue = "";
                    property.serializedObject.ApplyModifiedProperties();
                }
                GUI.color = prev;
            }

            EditorGUI.EndProperty();
        }

        private static CachedConstantData GetOrCacheConstants(Type type)
        {
            if (s_constantsCache.TryGetValue(type, out var cached))
                return cached;

            var fields = type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();

            var data = new CachedConstantData(fields);
            s_constantsCache[type] = data;
            return data;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Searchable Popup Window (inspired by GameplayTagPropertyDrawer)
        // ─────────────────────────────────────────────────────────────────────
        private class ActorTagPickerPopup : PopupWindowContent
        {
            private readonly ActorTagPickerTreeView m_TreeView;
            private readonly SearchField m_SearchField;
            private readonly float m_Width;

            public ActorTagPickerPopup(CachedConstantData data, SerializedProperty property, float width)
            {
                m_SearchField = new SearchField();
                m_Width = Mathf.Max(width, 220f);
                m_TreeView = new ActorTagPickerTreeView(
                    new TreeViewState(), data,
                    selectedValue =>
                    {
                        property.stringValue = selectedValue ?? "";
                        property.serializedObject.ApplyModifiedProperties();
                    });
                m_TreeView.closeRequested = () => editorWindow?.Close();
            }

            public override Vector2 GetWindowSize()
            {
                const float rowHeight = 20f;
                float rows = m_TreeView.VisibleRowCount;
                float treeHeight = rows * rowHeight;
                // Search bar (20) + padding (8) + tree content, capped at 300
                float totalHeight = 28f + Mathf.Min(treeHeight, 272f);
                return new Vector2(m_Width, totalHeight);
            }

            public override void OnGUI(Rect rect)
            {
                const float searchHeight = 20f;
                const float padding = 4f;

                Rect searchRect = new(rect.x + padding, rect.y + padding,
                    rect.width - padding * 2, searchHeight);
                string newSearch = m_SearchField.OnGUI(searchRect, m_TreeView.searchString);
                if (newSearch != m_TreeView.searchString)
                    m_TreeView.searchString = newSearch;

                Rect treeRect = new(rect.x + padding, searchRect.yMax + padding,
                    rect.width - padding * 2, rect.height - searchHeight - padding * 3);
                m_TreeView.OnGUI(treeRect);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Flat TreeView for tag picking (scrollable, searchable)
        // ─────────────────────────────────────────────────────────────────────
        private class ActorTagPickerTreeView : TreeView
        {
            private readonly CachedConstantData m_Data;
            private readonly Action<string> m_OnSelected;
            private readonly Dictionary<int, int> m_IdToIndex = new(); // id -> index in data (-1 = None)
            public Action closeRequested;

            public int VisibleRowCount => GetRows()?.Count ?? (m_Data.Values.Length + 1);

            public ActorTagPickerTreeView(TreeViewState state, CachedConstantData data,
                Action<string> onSelected) : base(state)
            {
                m_Data = data;
                m_OnSelected = onSelected;
                showAlternatingRowBackgrounds = true;
                Reload();
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
                var items = new List<TreeViewItem>();
                m_IdToIndex.Clear();

                // (None) option
                int noneId = 1;
                items.Add(new TreeViewItem { id = noneId, depth = 0, displayName = "(None)" });
                m_IdToIndex[noneId] = -1;

                for (int i = 0; i < m_Data.DisplayNames.Length; i++)
                {
                    int itemId = i + 2;
                    string display = $"{m_Data.DisplayNames[i]}    \u2192 \"{m_Data.Values[i]}\"";
                    items.Add(new TreeViewItem { id = itemId, depth = 0, displayName = display });
                    m_IdToIndex[itemId] = i;
                }

                SetupParentsAndChildrenFromDepths(root, items);
                return root;
            }

            protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
            {
                if (!m_IdToIndex.TryGetValue(item.id, out int index))
                    return false;
                if (index < 0) // (None)
                    return "none".Contains(search, StringComparison.OrdinalIgnoreCase);

                return m_Data.DisplayNames[index].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || m_Data.Values[index].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            protected override void SingleClickedItem(int id)
            {
                SelectById(id);
            }

            protected override void KeyEvent()
            {
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    var selection = GetSelection();
                    if (selection.Count > 0)
                        SelectById(selection[0]);
                }
            }

            private void SelectById(int id)
            {
                if (!m_IdToIndex.TryGetValue(id, out int index))
                    return;

                m_OnSelected?.Invoke(index < 0 ? null : m_Data.Values[index]);
                closeRequested?.Invoke();
            }
        }
    }
}
