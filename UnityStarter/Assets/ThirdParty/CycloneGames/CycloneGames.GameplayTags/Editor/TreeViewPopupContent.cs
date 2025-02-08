using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace CycloneGames.GameplayTags.Editor
{
   public static class TreeViewMethodExtensions
   {
      public static void ShowPopupWindow(this TreeViewPopupContent.TreeView treeView, Rect activatorRect, float maxHeight)
      {
         TreeViewPopupContent treeViewPopupContent = new(activatorRect.width, maxHeight, treeView);
         PopupWindow.Show(activatorRect, treeViewPopupContent);
      }
   }

   public class TreeViewPopupContent : PopupWindowContent
   {
      public abstract class TreeView : UnityEditor.IMGUI.Controls.TreeView
      {
         public TreeView(TreeViewState state) : base(state)
         {
         }

         public virtual float GetTotalHeight()
         {
            return totalHeight;
         }
      }

      private TreeView m_TreeView;
      private float m_Width;
      private float m_MaxHeight;

      public TreeViewPopupContent(float width, float maxHeight, TreeView tagTreeView)
      {
         m_Width = width;
         m_MaxHeight = maxHeight;
         m_TreeView = tagTreeView;
      }

      public override void OnGUI(Rect rect)
      {
         m_TreeView.OnGUI(rect);
      }

      public override Vector2 GetWindowSize()
      {
         return new Vector2(m_Width, m_MaxHeight);
      }
   }
}

