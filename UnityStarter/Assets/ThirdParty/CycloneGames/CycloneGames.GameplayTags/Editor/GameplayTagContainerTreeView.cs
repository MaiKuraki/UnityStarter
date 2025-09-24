using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor 
{
    public class GameplayTagContainerTreeView : GameplayTagTreeViewBase
    {
        private static GUIContent s_TempContent = new();
        private SerializedProperty m_ExplicitTagsProperty;

        public GameplayTagContainerTreeView(TreeViewState treeViewState, SerializedProperty explicitTagsProperty)
            : base(treeViewState)
        {
            m_ExplicitTagsProperty = explicitTagsProperty;
            m_ExplicitTagsProperty.serializedObject.Update();
            ExpandIncludedTagItems();
            UpdateIncludedTags();
        }

        protected override void OnToolbarGUI()
        {
            if (ToolbarButton("Clear All"))
            {
                m_ExplicitTagsProperty.ClearArray();
                m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
                UpdateIncludedTags();
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            float indent = GetContentIndent(args.item);
            Rect rect = args.rowRect;
            rect.xMin += indent + 2 - (hasSearch ? 14 : 0);

            GameplayTagTreeViewItem item = args.item as GameplayTagTreeViewItem;
            bool added;

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = item.IsIncluded && !item.IsExplicitIncluded;
            added = EditorGUI.Toggle(rect, s_TempContent, item.IsIncluded);
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck())
            {
                if (added)
                    AddTag(item.Tag);
                else
                    RemoveTag(item.Tag);
            }

            Rect baseRowRect = rect;
            baseRowRect.xMin += 18;
            DoTagRowGUI(baseRowRect, item);
        }

        protected override void OnTagAdded(GameplayTag tag)
        {
            AddTag(tag);
        }

        private void AddTag(GameplayTag tag)
        {
            if (!tag.IsValid)
                return;

            for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
            {
                SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                if (string.Equals(element.stringValue, tag.Name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            m_ExplicitTagsProperty.InsertArrayElementAtIndex(0);
            SerializedProperty element1 = m_ExplicitTagsProperty.GetArrayElementAtIndex(0);
            element1.stringValue = tag.Name;

            m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
            m_ExplicitTagsProperty.serializedObject.Update();
            UpdateIncludedTags();
        }

        private void RemoveTag(GameplayTag tag)
        {
            for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
            {
                SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                if (string.Equals(element.stringValue, tag.Name, StringComparison.OrdinalIgnoreCase))
                {
                    m_ExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
            m_ExplicitTagsProperty.serializedObject.Update();
            UpdateIncludedTags();
        }

        protected override void OnTagDeleted(GameplayTag tag)
        {
            for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
            {
                SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                if (string.Equals(element.stringValue, tag.Name, StringComparison.OrdinalIgnoreCase))
                {
                    m_ExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
            m_ExplicitTagsProperty.serializedObject.Update();
            UpdateIncludedTags();
        }

        private void UpdateIncludedTags()
        {
            foreach (TreeViewItem row in GetRows())
            {
                if (row is GameplayTagTreeViewItem item)
                {
                    item.IsExplicitIncluded = false;
                    item.IsIncluded = false;
                }
            }

            for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
            {
                SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue);
                if (!tag.IsValid)
                    continue;

                GameplayTagTreeViewItem item = FindItem(tag.RuntimeIndex);

                if (item == null)
                    continue;

                item.IsExplicitIncluded = true;
                item.IsIncluded = true;

                foreach (GameplayTag parentTag in tag.ParentTags)
                {
                    GameplayTagTreeViewItem parentItem = FindItem(parentTag.RuntimeIndex);
                    if (parentItem == null)
                        continue;

                    parentItem.IsIncluded = true;
                }
            }
        }

        private void ExpandIncludedTagItems()
        {
            for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
            {
                SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue);
                if (!tag.IsValid)
                    continue;

                GameplayTagTreeViewItem item = FindItem(tag.RuntimeIndex);
                if (item == null)
                    continue;

                foreach (GameplayTag parentTag in tag.ParentTags)
                {
                    GameplayTagTreeViewItem parentItem = FindItem(parentTag.RuntimeIndex);
                    if (parentItem == null)
                        continue;

                    SetExpanded(parentItem.id, true);
                }
            }
        }
    }
}
