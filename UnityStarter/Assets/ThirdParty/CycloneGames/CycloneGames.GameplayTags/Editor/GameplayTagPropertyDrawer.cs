using System;
using System.Collections.Generic;
using System.Linq;
using CycloneGames.GameplayTags.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Editor
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

            GameplayTag tag = GameplayTagManager.RequestTag(nameProperty.stringValue);

            s_TempContent.text = string.IsNullOrEmpty(tag.Name) || !tag.IsValid ? "Select..." : tag.Name;
            s_TempContent.tooltip = tag.Description;

            if (EditorGUI.DropdownButton(position, s_TempContent, FocusType.Keyboard))
            {
                Action<GameplayTag> onTagSelected = newTag =>
                {
                    nameProperty.stringValue = newTag.Name;
                    property.serializedObject.ApplyModifiedProperties();
                };

                var tagPickerTreeView = new TagPickerTreeView(new TreeViewState(), onTagSelected);
                ShowPopupWindow(tagPickerTreeView, position, 280f);
            }

            EditorGUI.indentLevel = oldIndentLevel;
            EditorGUI.EndProperty();
        }

        private static void ShowPopupWindow<T>(T treeView, Rect rect, float height) where T : TreeView
        {
            var content = new TreeViewPopupContent<T>(treeView, null);
            PopupWindow.Show(rect, content);
        }

        // --- Nested Helper Classes to avoid conflicts ---

        private class TreeViewPopupContent<T> : PopupWindowContent where T : TreeView
        {
            private readonly T m_TreeView;
            private readonly Action m_OnClose;

            public TreeViewPopupContent(T treeView, Action onClose)
            {
                m_TreeView = treeView;
                m_OnClose = onClose;
            }

            public override void OnGUI(Rect rect)
            {
                const int border = 4;
                Rect treeRect = new(border, border, rect.width - border * 2, rect.height - border * 2);
                m_TreeView.OnGUI(treeRect);
            }

            public override void OnClose()
            {
                m_OnClose?.Invoke();
            }
        }

        private class TagPickerTreeView : TreeView
        {
            private readonly Action<GameplayTag> onTagSelected;

            public TagPickerTreeView(TreeViewState state, Action<GameplayTag> onTagSelected) : base(state)
            {
                this.onTagSelected = onTagSelected;
                Reload();
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
                
                GameplayTagManager.InitializeIfNeeded();
                var allTags = GameplayTagManager.GetAllTags().ToArray().OrderBy(t => t.Name).ToList();

                var tagItems = new Dictionary<string, TreeViewItem>();
                int id = 1;

                // Add "None" option
                var noneItem = new TreeViewItem { id = id++, displayName = "None" };
                root.AddChild(noneItem);

                foreach (var tag in allTags)
                {
                    string[] parts = tag.Name.Split('.');
                    string currentPath = "";
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string parentPath = currentPath;
                        currentPath = i == 0 ? parts[i] : $"{currentPath}.{parts[i]}";

                        if (!tagItems.ContainsKey(currentPath))
                        {
                            var newItem = new TreeViewItem { id = id++, displayName = parts[i] };
                            tagItems.Add(currentPath, newItem);

                            if (string.IsNullOrEmpty(parentPath))
                            {
                                root.AddChild(newItem);
                            }
                            else if (tagItems.TryGetValue(parentPath, out var parentItem))
                            {
                                parentItem.AddChild(newItem);
                            }
                        }
                    }
                }
                
                SetupParentsAndChildrenFromDepths(root, root.children);
                return root;
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (selectedIds.Count == 0) return;

                var selectedItem = FindItem(selectedIds[0], rootItem);
                if (selectedItem == null) return;

                if (selectedItem.displayName == "None")
                {
                    onTagSelected?.Invoke(GameplayTag.None);
                }
                else
                {
                    string fullPath = GetFullTagPath(selectedItem);
                    GameplayTag selectedTag = GameplayTagManager.RequestTag(fullPath);
                    onTagSelected?.Invoke(selectedTag);
                }
                
                EditorWindow.GetWindow<PopupWindow>().Close();
            }

            private string GetFullTagPath(TreeViewItem item)
            {
                if (item == null || item.parent == null || item.parent.depth == -1)
                {
                    return item?.displayName ?? "";
                }
                return $"{GetFullTagPath(item.parent)}.{item.displayName}";
            }
        }
    }
}
