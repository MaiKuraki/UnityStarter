using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeView = UnityEditor.IMGUI.Controls.TreeView;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    [CustomPropertyDrawer(typeof(SerializableGameplayTag))]
    public class GameplayTagPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_TempContent = new();
        private static readonly GUIContent s_InvalidPropertyContent = new("Invalid Tag Property");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            int oldIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            SerializedProperty nameProperty = property.FindPropertyRelative("tagName");

            if (nameProperty == null)
            {
                EditorGUI.LabelField(position, label, s_InvalidPropertyContent);
                EditorGUI.indentLevel = oldIndentLevel;
                EditorGUI.EndProperty();
                return;
            }

            bool hasMixedValues = HasMixedTagValues(nameProperty);
            GameplayTag tag = hasMixedValues
                ? GameplayTag.None
                : GameplayTagManager.Request(nameProperty.stringValue, false);

            bool hasValue = !hasMixedValues && !string.IsNullOrEmpty(nameProperty.stringValue);
            bool isValid = hasValue && tag.IsValid;

            if (hasMixedValues)
                s_TempContent.text = "Mixed Values";
            else if (!hasValue)
                s_TempContent.text = "None";
            else if (!isValid)
                s_TempContent.text = nameProperty.stringValue + " (Invalid)";
            else
                s_TempContent.text = tag.Name;

            s_TempContent.tooltip = hasMixedValues
                ? "The selected objects contain different gameplay tags. Choose a tag to assign it to all selected objects."
                : isValid
                ? tag.Description
                : hasValue
                    ? "This tag is not registered. Open the GameplayTag Validation Window or clear the field."
                    : null;

            // Draw clear button when a tag is selected
            Rect clearRect = default;
            if (hasValue || hasMixedValues)
            {
                clearRect = new Rect(position.xMax - 18, position.y, 18, position.height);
                position.width -= 20;
            }

            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = hasMixedValues;
            bool openPicker = EditorGUI.DropdownButton(position, s_TempContent, FocusType.Keyboard);
            EditorGUI.showMixedValue = previousMixedValue;
            if (openPicker)
            {
                // Runs once per picker selection, not per frame; the delegate captures the property being
                // edited, so there is no static form for it.
#pragma warning disable CG0046
                Action<GameplayTag> onTagSelected = newTag =>
                {
                    nameProperty.stringValue = newTag.IsNone ? null : newTag.Name;
                    property.serializedObject.ApplyModifiedProperties();
                };
#pragma warning restore CG0046

                var tagPickerTreeView = new TagPickerTreeView(new TreeViewState(), onTagSelected);
                var content = new TagPickerPopup(tagPickerTreeView, position.width);
                PopupWindow.Show(position, content);
            }

            if (hasValue || hasMixedValues)
            {
                Color prev = GUI.color;
                if (!isValid) GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUI.Button(clearRect, "\u00D7", EditorStyles.miniLabel))
                {
                    nameProperty.stringValue = null;
                    property.serializedObject.ApplyModifiedProperties();
                }
                GUI.color = prev;
            }

            EditorGUI.indentLevel = oldIndentLevel;
            EditorGUI.EndProperty();
        }

        internal static bool HasMixedTagValues(SerializedProperty nameProperty)
        {
            return nameProperty != null && nameProperty.hasMultipleDifferentValues;
        }

        private class TagPickerPopup : PopupWindowContent
        {
            private readonly TagPickerTreeView m_TreeView;
            private readonly SearchField m_SearchField;
            private readonly float m_Width;

            public TagPickerPopup(TagPickerTreeView treeView, float width)
            {
                m_TreeView = treeView;
                m_TreeView.closeRequested = () => editorWindow?.Close();
                m_SearchField = new SearchField();
                m_Width = Mathf.Max(width, 200f);
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(m_Width, 300f);
            }

            public override void OnGUI(Rect rect)
            {
                const float searchHeight = 20f;
                const float padding = 4f;

                Rect searchRect = new(rect.x + padding, rect.y + padding, rect.width - padding * 2, searchHeight);
                string newSearch = m_SearchField.OnGUI(searchRect, m_TreeView.searchString);
                if (newSearch != m_TreeView.searchString)
                    m_TreeView.searchString = newSearch;

                Rect treeRect = new(rect.x + padding, searchRect.yMax + padding, rect.width - padding * 2, rect.height - searchHeight - padding * 3);
                m_TreeView.OnGUI(treeRect);
            }
        }

        private class TagPickerTreeView : TreeView
        {
            private readonly Action<GameplayTag> onTagSelected;
            private readonly Dictionary<int, string> m_IdToTagPath = new();
            public Action closeRequested;

            public TagPickerTreeView(TreeViewState state, Action<GameplayTag> onTagSelected) : base(state)
            {
                this.onTagSelected = onTagSelected;
                showAlternatingRowBackgrounds = true;
                Reload();
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };

                GameplayTagManager.InitializeIfNeeded();
                TagDataSnapshot snapshot = GameplayTagManager.Snapshot;

                // The "(None)" row is the explicit clear choice. Item ids are the runtime index plus one,
                // so the mapping stays valid without a second dictionary.
                int noneId = 1;
                var noneItem = new TreeViewItem { id = noneId, displayName = "(None)", depth = 0 };
                m_IdToTagPath[noneId] = null;

                List<TreeViewItem> flatItems = new(snapshot.TagCount + 1) { noneItem };
                var itemByIndex = new Dictionary<int, TreeViewItem>(snapshot.TagCount);

                // Runtime indices ascend with parents before descendants and the snapshot already stores
                // the parent chain as a compressed row, so the tree builds in one pass. The previous
                // implementation split and re-interpolated every tag name to rediscover that hierarchy -
                // O(N*depth) string allocations on every popup open for information the snapshot holds.
                for (int runtimeIndex = 1; runtimeIndex < snapshot.TotalTagCount; runtimeIndex++)
                {
                    string name = snapshot.GetName(runtimeIndex);
                    int lastDot = name.LastIndexOf('.');
                    string label = lastDot < 0 ? name : name.Substring(lastDot + 1);

                    int parentIndex = snapshot.GetParentIndex(runtimeIndex);
                    int depth = parentIndex == 0 ? 0 : itemByIndex[parentIndex].depth + 1;

                    int itemId = runtimeIndex + 1;
                    var item = new TreeViewItem { id = itemId, displayName = label, depth = depth };
                    itemByIndex[runtimeIndex] = item;
                    m_IdToTagPath[itemId] = name;
                    flatItems.Add(item);
                }

                SetupParentsAndChildrenFromDepths(root, flatItems);
                return root;
            }

            protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
            {
                if (m_IdToTagPath.TryGetValue(item.id, out string path) && path != null)
                    return path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            }

            protected override void SingleClickedItem(int id)
            {
                SelectTagById(id);
            }

            protected override void KeyEvent()
            {
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    var selection = GetSelection();
                    if (selection.Count > 0)
                        SelectTagById(selection[0]);
                }
            }

            private void SelectTagById(int id)
            {
                if (!m_IdToTagPath.TryGetValue(id, out string path))
                    return;

                if (path == null)
                {
                    onTagSelected?.Invoke(GameplayTag.None);
                }
                else
                {
                    GameplayTag selectedTag = GameplayTagManager.Request(path);
                    onTagSelected?.Invoke(selectedTag);
                }

                closeRequested?.Invoke();
            }
        }
    }
}
