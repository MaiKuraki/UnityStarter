using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   internal class DeleteTagPanel
   {

      public event Action OnClose;
      public event Action OnTagDeleted;

      private bool HasError => !string.IsNullOrEmpty(m_ValidationError);

      private readonly GameplayTag m_TagToDelete;
      private readonly IGameplayTagSource[] m_SourceFileOptions;
      private readonly string[] m_SourceFileNameOptions;

      private int m_AllSourcesOptionIndex = -1;
      private int m_SelectedSourceFileIndex;
      private string m_ValidationError;

      private GUIStyle m_PanelStyle;
      private GUIStyle m_PanelTitleStyle;

      public DeleteTagPanel(GameplayTag tag)
      {
         m_TagToDelete = tag;

         List<IGameplayTagSource> sources = new();

         foreach (IGameplayTagSource source in tag.Definition.GetAllSources())
         {
            if (source is IDeleteTagHandler)
               sources.Add(source);
         }

         List<string> sourceFileOptions = new();
         List<IGameplayTagSource> sourceFileList = new();

         for (int i = 0; i < sources.Count; i++)
         {
            sourceFileOptions.Add(sources[i].Name);
            sourceFileList.Add(sources[i]);
         }

         if (sources.Count > 1)
         {
            sourceFileOptions.Add("All Sources");
            sourceFileList.Add(null);
            m_AllSourcesOptionIndex = sourceFileOptions.Count - 1;
            m_SelectedSourceFileIndex = m_AllSourcesOptionIndex;
         }
         else
         {
            m_SelectedSourceFileIndex = 0;
         }

         m_SourceFileNameOptions = sourceFileOptions.ToArray();
         m_SourceFileOptions = sourceFileList.ToArray();

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
                  if (IsAllSourcesSelected())
                  {
                     foreach (IDeleteTagHandler source in m_SourceFileOptions)
                     {
                        if (source != null)
                           source.DeleteTag(m_TagToDelete.Name);
                     }
                  }
                  else
                  {
                     IDeleteTagHandler source = GetSelectedFileTagSource();
                     source.DeleteTag(m_TagToDelete.Name);
                  }

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
         if (IsAllSourcesSelected())
            throw new InvalidOperationException("All Sources selected. No single source available.");

         IDeleteTagHandler source = (IDeleteTagHandler)m_SourceFileOptions[m_SelectedSourceFileIndex];
         return source;
      }

      private bool IsAllSourcesSelected()
      {
         return m_SelectedSourceFileIndex == m_AllSourcesOptionIndex;
      }

      private void ValidateFields()
      {
         m_ValidationError = null;

         if (string.IsNullOrEmpty(m_TagToDelete.Name))
         {
            m_ValidationError = "Invalid tag to delete.";
            return;
         }

         if (IsAllSourcesSelected())
         {
            bool anyValid = false;
            foreach (IGameplayTagSource source in m_SourceFileOptions)
            {
               if (source != null && source is FileGameplayTagSource fileSource && File.Exists(fileSource.FilePath))
               {
                  anyValid = true;
                  break;
               }
            }

            if (!anyValid)
               m_ValidationError = "No valid sources available to delete from.";
         }
         else
         {
            if (m_SourceFileOptions[m_SelectedSourceFileIndex] is FileGameplayTagSource source && (source == null || !File.Exists(source.FilePath)))
            {
               m_ValidationError = "The selected source file no longer exists.";
            }
         }
      }
   }
}
