using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   internal class DeleteTagPanel
   {

      public event Action OnClose;
      public event Action OnTagDeleted;

      private bool HasError => !string.IsNullOrEmpty(m_ValidationError);

      private readonly GameplayTag m_TagToDelete;
      private readonly IGameplayTagSource[] m_SourceFileOptions;
      private readonly string[] m_SourceFileNameOptions;

      private int m_SelectedSourceFileIndex;
      private string m_ValidationError;

      private GUIStyle m_PanelStyle;
      private GUIStyle m_PanelTitleStyle;

      public DeleteTagPanel(GameplayTag tag)
      {
         m_TagToDelete = tag;

         int deletableSourceCount = 0;
         for (int i = 0; i < tag.Definition.SourceCount; i++)
         {
            if (tag.Definition.GetSource(i) is IDeleteTagHandler)
               deletableSourceCount++;
         }

         m_SourceFileNameOptions = new string[deletableSourceCount];
         m_SourceFileOptions = new IGameplayTagSource[deletableSourceCount];
         int destinationIndex = 0;
         for (int i = 0; i < tag.Definition.SourceCount; i++)
         {
            IGameplayTagSource source = tag.Definition.GetSource(i);
            if (source is not IDeleteTagHandler)
               continue;

            m_SourceFileNameOptions[destinationIndex] = source.Name;
            m_SourceFileOptions[destinationIndex] = source;
            destinationIndex++;
         }

         m_SelectedSourceFileIndex = 0;

         m_PanelStyle = new GUIStyle(EditorStyles.toolbar)
         {
            fixedHeight = 0,
            padding = new RectOffset(32, 32, 0, 0)
         };

         m_PanelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
         {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(0, 0, 4, 4)
         };
      }

      public void OnGUI(Rect rect)
      {
         GUILayout.BeginArea(rect, m_PanelStyle);
         GUILayout.FlexibleSpace();

         GUILayout.Label("Delete Tag", m_PanelTitleStyle);

         float previousLabelWidth = EditorGUIUtility.labelWidth;
         EditorGUIUtility.labelWidth = 60;

         EditorGUILayout.TextField("Tag", m_TagToDelete.Name);

         EditorGUI.BeginChangeCheck();
         m_SelectedSourceFileIndex = EditorGUILayout.Popup("From", m_SelectedSourceFileIndex, m_SourceFileNameOptions);

         EditorGUIUtility.labelWidth = previousLabelWidth;

         if (HasError)
            EditorGUILayout.HelpBox(m_ValidationError, MessageType.Error);

         GUILayout.Space(10);

         GUILayout.BeginHorizontal();
         GUILayout.FlexibleSpace();

         if (GUILayout.Button("Delete"))
         {
            ValidateFields();

            if (!HasError)
            {
               try
               {
                  IDeleteTagHandler source = GetSelectedFileTagSource();
                  source.DeleteTag(m_TagToDelete.Name);

                  GameplayTagManager.ReloadTags();

                  OnTagDeleted?.Invoke();
                  OnClose?.Invoke();
               }
               catch (Exception e)
               {
                  m_ValidationError = $"Failed to delete tag: {e.Message}";
               }
            }
         }

         if (GUILayout.Button("Cancel"))
            OnClose?.Invoke();

         GUILayout.FlexibleSpace();
         GUILayout.EndHorizontal();

         GUILayout.FlexibleSpace();
         GUILayout.EndArea();
      }

      public float GetHeight()
      {
         if (HasError)
            return 160;

         return 130f;
      }

      private IDeleteTagHandler GetSelectedFileTagSource()
      {
         IDeleteTagHandler source = (IDeleteTagHandler)m_SourceFileOptions[m_SelectedSourceFileIndex];
         return source;
      }

      private void ValidateFields()
      {
         m_ValidationError = null;

         if (string.IsNullOrEmpty(m_TagToDelete.Name))
         {
            m_ValidationError = "Invalid tag to delete.";
            return;
         }

         if (m_SourceFileOptions.Length == 0)
         {
            m_ValidationError = "No writable source is available for this tag.";
            return;
         }

         if (m_SourceFileOptions[m_SelectedSourceFileIndex] is FileGameplayTagSource source && !File.Exists(source.FilePath))
         {
            m_ValidationError = "The selected source file no longer exists.";
         }
      }
   }
}
