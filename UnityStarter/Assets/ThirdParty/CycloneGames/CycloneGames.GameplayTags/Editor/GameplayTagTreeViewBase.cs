using System;
using System.Collections.Generic;
using System.Text;
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

      private const float ToolbarHeight = 22f;
      private const float CompactSearchWidth = 150f;
      private const float FullSearchWidth = 250f;
      private const float ToolbarSpacing = 4f;
      private const float ToolbarTopPadding = 2f;
      private const float ToolbarContentHeight = 20f;

      private static Styles s_Styles;
      private static readonly StringBuilder s_SourceTooltipBuilder = new(256);
      private SearchField m_SearchField;
      private bool m_IsEmpty;
      private AddNewTagPanel m_AddNewTagPanel;
      private DeleteTagPanel m_DeleteTagPanel;
      private string m_CachedDeleteTooltip;
      private string m_CachedReadOnlyTooltip;
      private GUIContent m_TempDeleteContent;
      private bool m_IsSearching;

      public GameplayTagTreeViewBase(TreeViewState treeViewState)
         : base(treeViewState)
      {
         m_SearchField = new SearchField();
         m_TempDeleteContent = new GUIContent();
         m_CachedDeleteTooltip = "Delete Tag";
         m_CachedReadOnlyTooltip = "Tag cannot be deleted (Read-Only)";
         showAlternatingRowBackgrounds = true;
         rowHeight = 24;

         Reload();
      }

      public override float GetTotalHeight()
      {
         float height = base.GetTotalHeight() + ToolbarHeight;
         if (m_AddNewTagPanel != null)
            height += m_AddNewTagPanel.GetHeight();
         if (m_DeleteTagPanel != null)
            height += m_DeleteTagPanel.GetHeight();
         return height;
      }

      public override void OnGUI(Rect rect)
      {
         s_Styles ??= new Styles();

         Rect toolbarRect = rect;
         toolbarRect.height = ToolbarHeight;
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
         GUI.BeginGroup(rect);

         Rect contentRect = new Rect(0, 0, rect.width, rect.height);
         GUI.DrawTexture(contentRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill);
         EditorGUI.DrawRect(contentRect, EditorGUIUtility.isProSkin 
            ? new Color(0.2f, 0.2f, 0.2f, 1f) 
            : new Color(0.88f, 0.88f, 0.88f, 1f));

         Rect layoutArea = new Rect(ToolbarSpacing, 0, rect.width - ToolbarSpacing * 2, rect.height);

         GUI.BeginGroup(layoutArea);
         DrawCompactToolbar(layoutArea);
         GUI.EndGroup();

         GUI.EndGroup();
      }

      private void DrawCompactToolbar(Rect rect)
      {
         float contentY = ToolbarTopPadding;
         float contentHeight = Mathf.Min(ToolbarContentHeight, Mathf.Max(1f, rect.height - contentY));

         float x = 0;
         float buttonSize = 20f;

         Rect expandRect = new Rect(x, contentY, buttonSize, contentHeight);
         if (GUI.Button(expandRect, s_Styles.ExpandIcon, EditorStyles.miniLabel))
            ExpandAll();

         x += buttonSize + ToolbarSpacing;

         Rect collapseRect = new Rect(x, contentY, buttonSize, contentHeight);
         if (GUI.Button(collapseRect, s_Styles.CollapseIcon, EditorStyles.miniLabel))
            CollapseAll();

         x += buttonSize + ToolbarSpacing;

         Rect addRect = new Rect(rect.xMax - buttonSize, contentY, buttonSize, contentHeight);
         float rightLimit = addRect.xMin - ToolbarSpacing;
         OnToolbarGUI(ref x, rect, rightLimit);

         if (m_AddNewTagPanel == null)
         {
            m_TempDeleteContent.image = s_Styles.AddNewTagIcon;
            m_TempDeleteContent.tooltip = "Add New Tag (Ctrl+Shift+A)";
            if (GUI.Button(addRect, m_TempDeleteContent, EditorStyles.miniLabel))
               CreateAddNewTagPanel();
         }

         float searchWidth = CompactSearchWidth;
         if (rect.width > 600f)
            searchWidth = FullSearchWidth;

         Rect searchRect = new Rect(rect.xMax - searchWidth - buttonSize - ToolbarSpacing, contentY, searchWidth, contentHeight);

         string newSearchString = m_SearchField.OnToolbarGUI(searchRect, searchString);
         if (newSearchString != searchString)
         {
            searchString = newSearchString;
            m_IsSearching = !string.IsNullOrEmpty(searchString);
         }
      }

      private void CreateAddNewTagPanel(string prefillTagName = null)
      {
         m_DeleteTagPanel = null;
         m_AddNewTagPanel = new(prefillTagName);

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

      protected virtual void OnToolbarGUI(ref float x, Rect toolbarRect, float rightLimit)
      { }

      protected void DoTagRowGUI(Rect rect, GameplayTagTreeViewItem item)
      {
         bool isMouseOver = rect.Contains(Event.current.mousePosition);

         Rect deleteButtonRect = rect;
         deleteButtonRect.xMin = deleteButtonRect.xMax - 24;

         Color prevColor = GUI.color;
         GUI.color = item.CanBeDeleted ? prevColor : new Color(1, 1, 1, 0.3f);

         m_TempDeleteContent.image = s_Styles.DeleteTagIcon;
         m_TempDeleteContent.tooltip = item.CanBeDeleted ? m_CachedDeleteTooltip : m_CachedReadOnlyTooltip;

         if (GUI.Button(deleteButtonRect, m_TempDeleteContent, EditorStyles.label) && item.CanBeDeleted)
            CreateDeleteTagPanel(item.Tag);

         GUI.color = prevColor;

         rect.xMax -= 24;

         Rect labelRect = rect;
         labelRect.height = EditorGUIUtility.singleLineHeight;
         labelRect.center = new Vector2(labelRect.center.x, rect.center.y);

         GUI.Label(labelRect, TreeViewGUIUtility.TempContent(m_IsSearching ? item.Tag.Name : item.displayName, item.Tag.Description));

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
               s_SourceTooltipBuilder.Clear();
               s_SourceTooltipBuilder.Append("Sources:\n");
               for (int i = 0; i < item.Tag.Definition.SourceCount; i++)
               {
                  IGameplayTagSource source = item.Tag.Definition.GetSource(i);
                  if (source is not IDeleteTagHandler)
                  {
                     s_SourceTooltipBuilder.Append(source.Name);
                     s_SourceTooltipBuilder.Append(" (Read-Only)");
                  }
                  else
                  {
                     s_SourceTooltipBuilder.Append(source.Name);
                  }

                  s_SourceTooltipBuilder.Append('\n');
               }

               if (s_SourceTooltipBuilder.Length > 0 && s_SourceTooltipBuilder[s_SourceTooltipBuilder.Length - 1] == '\n')
                  s_SourceTooltipBuilder.Length -= 1;

               sourceTooltip = s_SourceTooltipBuilder.ToString();
            }

            GUIContent sourceContent = TreeViewGUIUtility.TempContent(sourceText, sourceTooltip);
            GUI.Label(sourceLabelRect, sourceContent, s_Styles.TagSourceLabel);
         }
      }

      protected bool ToolbarButton(ref float x, Rect toolbarRect, float rightLimit, float width, string text)
      {
         if (x + width > rightLimit)
            return false;

         float contentY = ToolbarTopPadding;
         float contentHeight = Mathf.Min(ToolbarContentHeight, Mathf.Max(1f, toolbarRect.height - contentY));
         Rect rect = new(x, contentY, width, contentHeight);
         x += width + ToolbarSpacing;
         return GUI.Button(rect, text, EditorStyles.toolbarButton);
      }

      protected bool ToolbarButton(ref float x, Rect toolbarRect, float rightLimit, float width, Texture texture, string tooltip = null)
      {
         if (x + width > rightLimit)
            return false;

         float contentY = ToolbarTopPadding;
         float contentHeight = Mathf.Min(ToolbarContentHeight, Mathf.Max(1f, toolbarRect.height - contentY));
         Rect rect = new(x, contentY, width, contentHeight);
         x += width + ToolbarSpacing;
         return GUI.Button(rect, TreeViewGUIUtility.TempContent(texture, tooltip), EditorStyles.toolbarButton);
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

      protected override void ContextClickedItem(int id)
      {
         if (FindItem(id, rootItem) is not GameplayTagTreeViewItem item)
            return;

         GenericMenu menu = new();
         string tagName = item.Tag.Name;

         menu.AddItem(new GUIContent("Copy Tag Name"), false, () =>
         {
            EditorGUIUtility.systemCopyBuffer = tagName;
         });

         menu.AddSeparator("");
         menu.AddItem(new GUIContent("Create Child Tag"), false, () =>
         {
            CreateAddNewTagPanel(tagName + ".");
         });

         if (item.CanBeDeleted)
         {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Tag"), false, () => CreateDeleteTagPanel(item.Tag));
         }

         menu.ShowAsContext();
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
         public readonly Texture ExpandIcon;
         public readonly Texture CollapseIcon;

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
            ExpandIcon = EditorGUIUtility.IconContent("Toolbar Plus").image;
            CollapseIcon = EditorGUIUtility.IconContent("Toolbar Minus").image;
         }
      }
   }
}
