using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   internal class AddNewTagPanel
   {
      private const int k_NewFileOptionIndex = 0;

      public event Action OnClose;
      public event Action<GameplayTag> OnTagAdded;

      private bool HasError => !string.IsNullOrEmpty(m_ValidationError);

      private string m_NewTagName;
      private string m_NewTagComment;
      private string m_NewSourceFileName;
      private string[] m_SourceFileNameOptions;
      private FileGameplayTagSource[] m_SourceFileOptions;
      private int m_SelectedSourceFileIndex;
      private string m_ValidationError;

      private GUIStyle m_PanelStyle;
      private GUIStyle m_PanelTitleStyle;

      public AddNewTagPanel()
      {
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

         List<string> sourceFileOptions = new() { "New File" };
         List<FileGameplayTagSource> sourceFileList = new() { null };

         foreach (FileGameplayTagSource source in FileGameplayTagSource.GetAllFileSources())
         {
            sourceFileOptions.Add(source.Name);
            sourceFileList.Add(source);
         }

         m_SourceFileNameOptions = sourceFileOptions.ToArray();
         m_SourceFileOptions = sourceFileList.ToArray();
         m_SelectedSourceFileIndex = Mathf.Min(1, m_SourceFileNameOptions.Length - 1);

         if (m_SelectedSourceFileIndex == k_NewFileOptionIndex)
            m_NewSourceFileName = "DefaultGameplayTags.json";

         m_NewTagName = string.Empty;
         m_NewTagComment = string.Empty;
      }

      public void OnGUI(Rect rect)
      {
         GUILayout.BeginArea(rect, m_PanelStyle);
         GUILayout.FlexibleSpace();

         GUILayout.Label("Add New Tag", m_PanelTitleStyle);

         EditorGUI.BeginChangeCheck();

         float previousLabelWidth = EditorGUIUtility.labelWidth;
         EditorGUIUtility.labelWidth = 90;

         m_NewTagName = EditorGUILayout.TextField("Name", m_NewTagName);
         m_NewTagComment = EditorGUILayout.TextField("Comment", m_NewTagComment);

         GUILayout.BeginHorizontal();

         m_SelectedSourceFileIndex = EditorGUILayout.Popup("Source File", m_SelectedSourceFileIndex, m_SourceFileNameOptions);

         if (m_SelectedSourceFileIndex == k_NewFileOptionIndex)
            m_NewSourceFileName = EditorGUILayout.TextField(GUIContent.none, m_NewSourceFileName);

         GUILayout.EndHorizontal();

         EditorGUIUtility.labelWidth = previousLabelWidth;

         if (HasError)
            EditorGUILayout.HelpBox(m_ValidationError, MessageType.Error);

         GUILayout.Space(10);

         GUILayout.BeginHorizontal();
         GUILayout.FlexibleSpace();

         if (GUILayout.Button("Add"))
         {
            ValidateFields();

            if (!HasError)
            {
               try
               {
                  FileGameplayTagSource source = GetOrCreateFileTagSource();
                  source.AddTag(m_NewTagName, m_NewTagComment);

                  GameplayTagManager.ReloadTags();

                  GameplayTag addedTag = GameplayTagManager.RequestTag(m_NewTagName);

                  if (!addedTag.IsValid)
                  {
                     Debug.LogError("Tag was added but could not be found after reloading.");
                     return;
                  }

                  OnTagAdded?.Invoke(addedTag);
               }
               catch (Exception e)
               {
                  m_ValidationError = $"Failed to add tag: {e.Message}";
               }

               if (!HasError)
                  OnClose?.Invoke();
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

      private FileGameplayTagSource GetOrCreateFileTagSource()
      {
         if (m_SelectedSourceFileIndex != k_NewFileOptionIndex)
            return m_SourceFileOptions[m_SelectedSourceFileIndex];

         string filePath = Path.Combine(FileGameplayTagSource.DirectoryPath, m_NewSourceFileName);
         filePath = Path.GetFullPath(filePath);
         filePath = Path.ChangeExtension(filePath, ".json");

         FileGameplayTagSource newSource = new(filePath);

         if (!newSource.TryLoad())
            throw new Exception("Failed to create new source file.");

         return newSource;
      }

      private void ValidateFields()
      {
         m_ValidationError = null;

         if (!GameplayTagUtility.IsNameValid(m_NewTagName, out m_ValidationError))
            return;

         if (m_SelectedSourceFileIndex == k_NewFileOptionIndex)
         {
            if (m_NewSourceFileName.Length == 0)
            {
               m_ValidationError = "Source file name cannot be empty.";
               return;
            }

            string extension = Path.GetExtension(m_NewSourceFileName);
            if (!string.IsNullOrEmpty(extension) && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
               m_ValidationError = "Invalid file extension. Must be .json or can be omitted.";
               return;
            }

            string filePath = Path.Combine(FileGameplayTagSource.DirectoryPath, m_NewSourceFileName);
            filePath = Path.GetFullPath(filePath);
            filePath = Path.ChangeExtension(filePath, ".json");

            if (File.Exists(filePath))
            {
               m_ValidationError = "A source file with this name already exists.";
               return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
               m_ValidationError = "The specified directory does not exist.";
               return;
            }

            if (!filePath.StartsWith(FileGameplayTagSource.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
               m_ValidationError = $"Source file must be created inside the '{FileGameplayTagSource.DirectoryPath}' directory.";
               return;
            }
         }
         else
         {
            FileGameplayTagSource source = m_SourceFileOptions[m_SelectedSourceFileIndex];
            if (source == null)
            {
               m_ValidationError = "Invalid source file selection.";
               return;
            }

            if (!File.Exists(source.FilePath))
            {
               m_ValidationError = "The selected source file no longer exists.";
               return;
            }
         }
      }
   }
}
