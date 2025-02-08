using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Editor
{
   public class GameplayTagTreeViewItem : TreeViewItem
   {
      public GameplayTag Tag => m_Tag;

      public string DisplayName => Tag.Label;

      public bool IsIncluded { get; set; }

      public bool IsExplicitIncluded { get; set; }

      private GameplayTag m_Tag;

      public GameplayTagTreeViewItem(int id, GameplayTag tag)
         : base(id, tag.HierarchyLevel, tag.Label)
      {
         m_Tag = tag;
      }
   }

   public abstract class GameplayTagTreeViewBase : TreeViewPopupContent.TreeView
   {
      public bool IsEmpty => m_IsEmpty;

      private static Styles s_Styles;
      private SearchField m_SearchField;
      private bool m_IsEmpty;

      public GameplayTagTreeViewBase(TreeViewState treeViewState)
         : base(treeViewState)
      {
         m_SearchField = new SearchField();
         showAlternatingRowBackgrounds = true;

         Reload();
      }

      public override float GetTotalHeight()
      {
         return base.GetTotalHeight() + EditorStyles.toolbar.fixedHeight;
      }

      public override void OnGUI(Rect rect)
      {
         s_Styles ??= new Styles();

         Rect toolbarRect = rect;
         toolbarRect.height = EditorStyles.toolbar.fixedHeight;
         ToolbarGUI(toolbarRect);

         rect.yMin += toolbarRect.height;
         base.OnGUI(rect);
      }

      private void ToolbarGUI(Rect rect)
      {
         GUILayout.BeginArea(rect);
         GUILayout.BeginHorizontal(EditorStyles.toolbar);

         if (ToolbarButton("Expand All"))
         {
            ExpandAll();
         }

         if (ToolbarButton("Collapse All"))
         {
            CollapseAll();
         }

         OnToolbarGUI();

         searchString = m_SearchField.OnToolbarGUI(searchString);

         GUILayout.EndHorizontal();
         GUILayout.EndArea();
      }

      protected virtual void OnToolbarGUI()
      { }

      protected bool ToolbarButton(string text)
      {
         return GUILayout.Button(text, s_Styles.ToolbarButton, GUILayout.ExpandWidth(false));
      }

      protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
      {
         GameplayTagTreeViewItem tagItem = item as GameplayTagTreeViewItem;
         return tagItem.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
      }

      protected override TreeViewItem BuildRoot()
      {
         TreeViewItem root = new(-2, -1, "<Root>");
         m_IsEmpty = true;

         List<TreeViewItem> items = new();

         foreach (GameplayTag tag in GameplayTagManager.GetAllTags())
         {
            bool hideInEditor = (tag.Flags & GameplayTagFlags.HideInEditor) != 0;
            GameplayTag parentIt = tag.ParentTag;
            while (parentIt != GameplayTag.None)
            {
               if ((parentIt.Flags & GameplayTagFlags.HideInEditor) != 0)
               {
                  hideInEditor = true;
                  break;
               }

               parentIt = parentIt.ParentTag;
            }

            if (hideInEditor)
            {
               continue;
            }

            items.Add(new GameplayTagTreeViewItem(tag.RuntimeIndex, tag));
            m_IsEmpty = false;
         }

         SetupParentsAndChildrenFromDepths(root, items);
         return root;
      }

      protected GameplayTagTreeViewItem FindItem(int runtimeTagIndex)
      {
         return FindItem(runtimeTagIndex, rootItem) as GameplayTagTreeViewItem;
      }

      protected class Styles
      {
         public readonly GUIStyle SearchField;
         public readonly GUIStyle ToolbarButton;

         public Styles()
         {
            SearchField = new GUIStyle("SearchTextField");

            ToolbarButton = new GUIStyle(EditorStyles.toolbarButton);
            ToolbarButton.fontSize = 11;
         }
      }
   }
}

