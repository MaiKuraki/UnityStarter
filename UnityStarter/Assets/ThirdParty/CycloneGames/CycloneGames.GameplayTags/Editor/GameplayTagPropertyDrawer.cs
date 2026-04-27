using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public class GameplayTagPropertyDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_TempContent = new();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            int oldIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            SerializedProperty nameProperty = property.FindPropertyRelative("m_Name");

            if (nameProperty == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Invalid Tag Property"));
                EditorGUI.EndProperty();
                return;
            }

            GameplayTag tag = GameplayTagManager.RequestTag(nameProperty.stringValue, false);

            bool hasValue = !string.IsNullOrEmpty(nameProperty.stringValue);
            bool isValid = hasValue && tag.IsValid;

            if (!hasValue)
                s_TempContent.text = "None";
            else if (!isValid)
                s_TempContent.text = nameProperty.stringValue + " (Invalid)";
            else
                s_TempContent.text = tag.Name;

            s_TempContent.tooltip = isValid ? tag.Description : null;

            // Draw clear button when a tag is selected
            Rect clearRect = default;
            if (hasValue)
            {
                clearRect = new Rect(position.xMax - 18, position.y, 18, position.height);
                position.width -= 20;
            }

            if (EditorGUI.DropdownButton(position, s_TempContent, FocusType.Keyboard))
            {
                Action<GameplayTag> onTagSelected = newTag =>
                {
                    nameProperty.stringValue = newTag.IsNone ? null : newTag.Name;
                    property.serializedObject.ApplyModifiedProperties();
                };

                var tagPickerTreeView = new TagPickerTreeView(new TreeViewState(), onTagSelected);
                var content = new TagPickerPopup(tagPickerTreeView, position.width);
                PopupWindow.Show(position, content);
            }

            if (hasValue)
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
                ReadOnlySpan<GameplayTag> allTags = GameplayTagManager.GetAllTags();

                var tagItems = new Dictionary<string, TreeViewItem>();
                int id = 1;

                // "None" option at top
                int noneId = id++;
                var noneItem = new TreeViewItem { id = noneId, displayName = "(None)", depth = 0 };
                m_IdToTagPath[noneId] = null;

                List<TreeViewItem> flatItems = new() { noneItem };

                for (int t = 0; t < allTags.Length; t++)
                {
                    GameplayTag tag = allTags[t];
                    string[] parts = tag.Name.Split('.');
                    string currentPath = "";

                    for (int i = 0; i < parts.Length; i++)
                    {
                        currentPath = i == 0 ? parts[i] : $"{currentPath}.{parts[i]}";

                        if (!tagItems.ContainsKey(currentPath))
                        {
                            int itemId = id++;
                            var newItem = new TreeViewItem { id = itemId, displayName = parts[i], depth = i + 1 };
                            tagItems[currentPath] = newItem;
                            m_IdToTagPath[itemId] = currentPath;
                            flatItems.Add(newItem);
                        }
                    }
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
                    GameplayTag selectedTag = GameplayTagManager.RequestTag(path);
                    onTagSelected?.Invoke(selectedTag);
                }

                closeRequested?.Invoke();
            }
        }
    }
}
