using System.Collections.Generic;
using System.Linq;
using CycloneGames.GameplayTags.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagTreeView : TreeView
    {
        public GameplayTagTreeView(TreeViewState state) : base(state)
        {
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>();
            
            GameplayTagManager.InitializeIfNeeded();
            var allTags = GameplayTagManager.GetAllTags().ToArray().OrderBy(t => t.Name).ToList();

            var tagItems = new Dictionary<string, TreeViewItem>();
            int id = 1;

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
                        // Set the depth explicitly to control the indentation.
                        var newItem = new TreeViewItem { id = id++, displayName = parts[i], depth = i };
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
            
            // We are manually setting up children, but we still need to call this
            // to ensure the TreeView's internal state (like parent pointers) is correctly built from the depth information.
            SetupParentsAndChildrenFromDepths(root, root.children.ToList());
            return root;
        }

        protected override void ContextClickedItem(int id)
        {
            var item = FindItem(id, rootItem);
            if (item == null) return;

            GenericMenu menu = new GenericMenu();
            string fullPath = GetFullTagPath(item);

            menu.AddItem(new GUIContent("Copy Tag Name"), false, () => {
                EditorGUIUtility.systemCopyBuffer = fullPath;
            });

            string staticAccessorPath = $"AllGameplayTags.{fullPath.Replace('.', '.')}.Get()";
            menu.AddItem(new GUIContent("Copy Static Accessor Path"), false, () => {
                EditorGUIUtility.systemCopyBuffer = staticAccessorPath;
            });

            menu.ShowAsContext();
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
