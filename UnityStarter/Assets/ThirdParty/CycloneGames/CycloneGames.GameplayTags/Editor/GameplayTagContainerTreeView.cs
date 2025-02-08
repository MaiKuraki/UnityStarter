using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

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
            m_ExplicitTagsProperty.arraySize = 0;
            m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
            UpdateIncludedTags();
         }
      }

      protected override void RowGUI(RowGUIArgs args)
      {
         float indent = GetContentIndent(args.item);
         Rect rect = args.rowRect;
         rect.xMin += indent - (hasSearch ? 14 : 0);

         GameplayTagTreeViewItem item = args.item as GameplayTagTreeViewItem;
         using (new EditorGUI.DisabledGroupScope(item.IsIncluded && !item.IsExplicitIncluded))
         {
            s_TempContent.text = hasSearch ? item.Tag.Name : args.label;
            s_TempContent.tooltip = item.Tag.Description;

            EditorGUI.BeginChangeCheck();
            bool added = EditorGUI.ToggleLeft(rect, s_TempContent, item.IsIncluded);

            if (EditorGUI.EndChangeCheck())
            {
               if (added)
               {
                  m_ExplicitTagsProperty.InsertArrayElementAtIndex(0);
                  SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(0);
                  element.stringValue = item.Tag.Name;
               }
               else
               {
                  for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
                  {
                     SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
                     if (string.Equals(element.stringValue, item.Tag.Name, StringComparison.OrdinalIgnoreCase))
                     {
                        m_ExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                        break;
                     }
                  }
               }

               m_ExplicitTagsProperty.serializedObject.ApplyModifiedProperties();
               m_ExplicitTagsProperty.serializedObject.Update();
               UpdateIncludedTags();
            }
         }
      }

      private unsafe void UpdateIncludedTags()
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
            GameplayTagTreeViewItem item = FindItem(tag.RuntimeIndex);

            if (item == null)
            {
               Debug.Log(element.stringValue);
               continue;
            }

            item.IsExplicitIncluded = true;
            item.IsIncluded = true;

            foreach (GameplayTag parentTag in tag.ParentTags)
            {
               GameplayTagTreeViewItem parentItem = FindItem(parentTag.RuntimeIndex);
               if (parentItem == null)
               {
                  continue;
               }

               parentItem.IsIncluded = true;
            }
         }
      }

      private unsafe void ExpandIncludedTagItems()
      {
         for (int i = 0; i < m_ExplicitTagsProperty.arraySize; i++)
         {
            SerializedProperty element = m_ExplicitTagsProperty.GetArrayElementAtIndex(i);
            GameplayTag tag = GameplayTagManager.RequestTag(element.stringValue);
            GameplayTagTreeViewItem item = FindItem(tag.RuntimeIndex);

            if (item == null)
            {
               continue;
            }

            foreach (GameplayTag parentTag in tag.ParentTags)
            {
               GameplayTagTreeViewItem parentItem = FindItem(parentTag.RuntimeIndex);
               if (parentItem == null)
               {
                  continue;
               }

               SetExpanded(parentItem.id, true);
            }
         }
      }
   }
}

