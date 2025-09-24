using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   public class GameplayTagTreeViewItem : TreeViewItem
   {
      public GameplayTag Tag => m_Tag;

      public string DisplayName => Tag.Label;

      public bool IsIncluded { get; set; }

      public bool IsExplicitIncluded { get; set; }

      public bool CanBeDeleted { get; set; }

      private GameplayTag m_Tag;

      public GameplayTagTreeViewItem(int id, GameplayTag tag)
         : base(id, tag.HierarchyLevel, tag.Label)
      {
         m_Tag = tag;

         foreach (IGameplayTagSource source in tag.Definition.GetAllSources())
         {
            if (source is IDeleteTagHandler)
               CanBeDeleted = true;
         }
      }
   }

   internal class TreeViewGUIUtility
   {
      private static GUIContent s_TempContent;

      public static GUIContent TempContent(string text, string tooltip = null)
      {
         s_TempContent ??= new GUIContent();
         s_TempContent.text = text;
         s_TempContent.tooltip = tooltip;
         s_TempContent.image = null;
         return s_TempContent;
      }

      public static GUIContent TempContent(Texture image, string tooltip = null)
      {
         s_TempContent ??= new GUIContent();
         s_TempContent.image = image;
         s_TempContent.tooltip = tooltip;
         s_TempContent.text = null;
         return s_TempContent;
      }

      public static GUIContent TempContent(string text, Texture image, string tooltip = null)
      {
         s_TempContent ??= new GUIContent();
         s_TempContent.text = text;
         s_TempContent.image = image;
         s_TempContent.tooltip = tooltip;
         return s_TempContent;
      }
   }

   public abstract class GameplayTagTreeViewBase : TreeViewPopupContent.TreeView
   {
      public bool IsEmpty => m_IsEmpty;

      private static Styles s_Styles;
      private SearchField m_SearchField;
      private bool m_IsEmpty;
      private AddNewTagPanel m_AddNewTagPanel;
      private DeleteTagPanel m_DeleteTagPanel;

      public GameplayTagTreeViewBase(TreeViewState treeViewState)
         : base(treeViewState)
      {
         m_SearchField = new SearchField();
         showAlternatingRowBackgrounds = true;
         rowHeight = 24;

         Reload();
      }

      public override float GetTotalHeight()
      {
         return base.GetTotalHeight() + EditorStyles.toolbar.fixedHeight * 2;
      }

      public override void OnGUI(Rect rect)
      {
         rect.height = Mathf.Min(rect.height, GetTotalHeight());

         s_Styles ??= new Styles();

         Rect toolbarRect = rect;
         toolbarRect.height = EditorStyles.toolbar.fixedHeight * 2;
         ToolbarGUI(toolbarRect);

         rect.yMin += toolbarRect.height;

         if (m_AddNewTagPanel != null)
         {
            Rect panelRect = rect;
            panelRect.height = m_AddNewTagPanel.GetHeight();
            rect.yMin += panelRect.height;

            m_AddNewTagPanel.OnGUI(panelRect);
         }
         if (m_DeleteTagPanel != null)
         {
            Rect panelRect = rect;
            panelRect.height = m_DeleteTagPanel.GetHeight();
            rect.yMin += panelRect.height;

            m_DeleteTagPanel.OnGUI(panelRect);
         }

         base.OnGUI(rect);
      }

      private void ToolbarGUI(Rect rect)
      {
         Rect topRect = rect;
         rect.height = EditorStyles.toolbar.fixedHeight;
         Rect bottomRect = rect;
         bottomRect.y += rect.height;
         rect.height = EditorStyles.toolbar.fixedHeight;

         GUILayout.BeginArea(topRect);
         GUILayout.BeginHorizontal(EditorStyles.toolbar);

         if (ToolbarButton("Expand All"))
            ExpandAll();

         if (ToolbarButton("Collapse All"))
            CollapseAll();

         OnToolbarGUI();

         GUILayout.FlexibleSpace();

         if (m_AddNewTagPanel == null && ToolbarButton(s_Styles.AddNewTagIcon, "Add New Tag"))
            CreateAddNewTagPanel();

         GUILayout.EndHorizontal();
         GUILayout.EndArea();

         GUILayout.BeginArea(bottomRect);
         GUILayout.BeginHorizontal(EditorStyles.toolbar);

         GUILayout.Space(4);

         searchString = m_SearchField.OnToolbarGUI(searchString);

         GUILayout.Space(4);

         GUILayout.EndHorizontal();
         GUILayout.EndArea();
      }

      private void CreateAddNewTagPanel()
      {
         m_DeleteTagPanel = null;

         m_AddNewTagPanel = new();

         m_AddNewTagPanel.OnClose += () =>
         {
            m_AddNewTagPanel = null;
         };

         m_AddNewTagPanel.OnTagAdded += (tag) =>
         {
            Reload();
            OnTagAdded(tag);
         };
      }

      private void CreateDeleteTagPanel(GameplayTag tag)
      {
         m_AddNewTagPanel = null;
         m_DeleteTagPanel = new(tag);

         m_DeleteTagPanel.OnClose += () =>
         {
            m_DeleteTagPanel = null;
         };

         m_DeleteTagPanel.OnTagDeleted += () =>
         {
            Reload();
            OnTagDeleted(tag);
         };
      }

      protected virtual void OnTagDeleted(GameplayTag tag)
      { }

      protected virtual void OnTagAdded(GameplayTag tag)
      { }

      protected virtual void OnToolbarGUI()
      { }

      protected void DoTagRowGUI(Rect rect, GameplayTagTreeViewItem item)
      {
         bool isMouseOver = rect.Contains(Event.current.mousePosition);

         Rect deleteButtonRect = rect;
         deleteButtonRect.xMin = deleteButtonRect.xMax - 24;

         Color prevColor = GUI.color;
         GUI.color = item.CanBeDeleted ? prevColor : new Color(1, 1, 1, 0.3f);
         string tooltip = item.CanBeDeleted ? "Delete Tag" : "Tag cannot be deleted (Read-Only)";

         GUIContent deleteButtonContent = TreeViewGUIUtility.TempContent(s_Styles.DeleteTagIcon, tooltip);
         if (GUI.Button(deleteButtonRect, deleteButtonContent, EditorStyles.label) && item.CanBeDeleted)
            CreateDeleteTagPanel(item.Tag);

         GUI.color = prevColor;

         rect.xMax -= 24;

         Rect labelRect = rect;
         labelRect.height = EditorGUIUtility.singleLineHeight;
         labelRect.center = new Vector2(labelRect.center.x, rect.center.y);

         GUI.Label(labelRect, TreeViewGUIUtility.TempContent(hasSearch ? item.Tag.Name : item.displayName, item.Tag.Description));

         if (item.Tag.Definition.SourceCount > 0)
         {
            Rect sourceLabelRect = rect;
            sourceLabelRect.height = EditorGUIUtility.singleLineHeight;
            sourceLabelRect.center = new Vector2(sourceLabelRect.center.x, rect.center.y);

            string sourceText;

            if (item.Tag.Definition.SourceCount == 1)
               sourceText = item.Tag.Definition.GetSource(0).Name;
            else
               sourceText = "(Multiple Sources)";

            string sourceTooltip = string.Empty;

            if (isMouseOver)
            {
               sourceTooltip = "Sources:\n";
               for (int i = 0; i < item.Tag.Definition.SourceCount; i++)
               {
                  IGameplayTagSource source = item.Tag.Definition.GetSource(i);
                  if (source is not IDeleteTagHandler)
                     sourceTooltip += $"{source.Name} (Read-Only)\n";
                  else
                     sourceTooltip += $"{source.Name}\n";
               }

               sourceTooltip = sourceTooltip.TrimEnd();
            }

            GUIContent sourceContent = TreeViewGUIUtility.TempContent(sourceText, sourceTooltip);
            GUI.Label(sourceLabelRect, sourceContent, s_Styles.TagSourceLabel);
         }
      }

      protected bool ToolbarButton(string text)
      {
         return GUILayout.Button(text, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
      }

      protected bool ToolbarButton(Texture texture, string tooltip = null)
      {
         return GUILayout.Button(TreeViewGUIUtility.TempContent(texture, tooltip), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
      }

      protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
      {
         GameplayTagTreeViewItem tagItem = item as GameplayTagTreeViewItem;
         bool nameMatches = tagItem.Tag.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

         if (nameMatches)
            return true;

         for (int i = 0; i < tagItem.Tag.Definition.SourceCount; i++)
         {
            IGameplayTagSource source = tagItem.Tag.Definition.GetSource(i);
            if (source.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
               return true;
         }

         return false;
      }

      protected override TreeViewItem BuildRoot()
      {
         TreeViewItem root = new(-2, -1, "<Root>");
         m_IsEmpty = true;

         List<TreeViewItem> items = new();

         foreach (GameplayTag tag in GameplayTagManager.GetAllTags())
         {
            if (tag.Name.StartsWith("Test.") || tag.Name.Equals("Test"))
               continue;

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
         public readonly GUIStyle TagSourceLabel;
         public readonly Texture AddNewTagIcon;
         public readonly Texture DeleteTagIcon;

         public Styles()
         {
            SearchField = new GUIStyle("SearchTextField");

            TagSourceLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
               alignment = TextAnchor.MiddleRight,
               fontSize = 10,
               padding = new RectOffset(0, 4, 0, 0)
            };

            AddNewTagIcon = EditorGUIUtility.IconContent("Toolbar Plus").image;
            DeleteTagIcon = EditorGUIUtility.IconContent("Toolbar Minus").image;
         }
      }
   }
}
