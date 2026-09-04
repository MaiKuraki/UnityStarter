using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   /// <summary>
   /// Renames a tag in the file that declares it, optionally leaving a redirect so existing serialized
   /// data and gameplay code that still uses the old name keep resolving.
   /// </summary>
   /// <remarks>
   /// The rename is add-then-remove inside one source file, so a failure between the two steps leaves the
   /// original tag in place rather than a half-renamed pair. The redirect is written to the process-wide
   /// redirector afterwards; it is process state, not file state, which matches how the runtime resolves
   /// names - see <see cref="GameplayTagRedirector"/>.
   /// </remarks>
   internal class RenameTagPanel
   {
      public event Action OnClose;
      public event Action OnTagRenamed;

      private bool HasError => !string.IsNullOrEmpty(m_ValidationError);

      private readonly GameplayTag m_TagToRename;
      private readonly IGameplayTagSource[] m_SourceFileOptions;
      private readonly string[] m_SourceFileNameOptions;

      private int m_SelectedSourceFileIndex;
      private string m_NewTagName;
      private bool m_AddRedirect = true;
      private string m_ValidationError;

      private GUIStyle m_PanelStyle;
      private GUIStyle m_PanelTitleStyle;

      public RenameTagPanel(GameplayTag tag)
      {
         m_TagToRename = tag;

         IReadOnlyList<IGameplayTagSource> sources = GameplayTagManager.GetTagSources(tag);
         int editableSourceCount = 0;
         for (int i = 0; i < sources.Count; i++)
         {
            if (sources[i] is IDeleteTagHandler)
               editableSourceCount++;
         }

         m_SourceFileNameOptions = new string[editableSourceCount];
         m_SourceFileOptions = new IGameplayTagSource[editableSourceCount];
         int destinationIndex = 0;
         for (int i = 0; i < sources.Count; i++)
         {
            IGameplayTagSource source = sources[i];
            if (source is not IDeleteTagHandler)
               continue;

            m_SourceFileNameOptions[destinationIndex] = source.Name;
            m_SourceFileOptions[destinationIndex] = source;
            destinationIndex++;
         }

         m_SelectedSourceFileIndex = 0;
         m_NewTagName = tag.Name;

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

         GUILayout.Label("Rename Tag", m_PanelTitleStyle);

         float previousLabelWidth = EditorGUIUtility.labelWidth;
         EditorGUIUtility.labelWidth = 90;

         EditorGUILayout.TextField("Old name", m_TagToRename.Name);

         EditorGUI.BeginChangeCheck();
         m_NewTagName = EditorGUILayout.TextField("New name", m_NewTagName);
         if (EditorGUI.EndChangeCheck())
            ValidateFields();

         m_SelectedSourceFileIndex = EditorGUILayout.Popup("From", m_SelectedSourceFileIndex, m_SourceFileNameOptions);
         m_AddRedirect = EditorGUILayout.Toggle(
            new GUIContent(
               "Leave redirect",
               "Writes a redirect so serialized data and code that still uses the old name keeps resolving."),
            m_AddRedirect);

         EditorGUIUtility.labelWidth = previousLabelWidth;

         if (HasError)
            EditorGUILayout.HelpBox(m_ValidationError, MessageType.Error);

         GUILayout.Space(10);

         GUILayout.BeginHorizontal();
         GUILayout.FlexibleSpace();

         using (new EditorGUI.DisabledScope(HasError))
         {
            if (GUILayout.Button("Rename"))
            {
               ValidateFields();

               if (!HasError)
               {
                  try
                  {
                     IDeleteTagHandler source = GetSelectedFileTagSource();
                     // Add before remove: a failure adding leaves the original untouched, whereas a
                     // failure after removing would leave the tag deleted with no replacement.
                     source.AddTag(m_NewTagName, m_TagToRename.Description);
                     source.DeleteTag(m_TagToRename.Name);

                     if (m_AddRedirect)
                        GameplayTagRedirector.AddRedirect(m_TagToRename.Name, m_NewTagName);

                     GameplayTagManager.Reload();

                     OnTagRenamed?.Invoke();
                     OnClose?.Invoke();
                  }
                  catch (Exception e)
                  {
                     m_ValidationError = $"Failed to rename tag: {e.Message}";
                  }
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
            return 210f;

         return 180f;
      }

      private IDeleteTagHandler GetSelectedFileTagSource()
      {
         return (IDeleteTagHandler)m_SourceFileOptions[m_SelectedSourceFileIndex];
      }

      private void ValidateFields()
      {
         m_ValidationError = null;

         string newName = m_NewTagName?.Trim();
         if (string.IsNullOrEmpty(newName))
         {
            m_ValidationError = "New tag name cannot be empty.";
            return;
         }

         if (!GameplayTagUtility.IsNameValid(newName, out string nameError))
         {
            m_ValidationError = nameError;
            return;
         }

         if (string.Equals(newName, m_TagToRename.Name, StringComparison.Ordinal))
         {
            m_ValidationError = "The new name is the same as the current name.";
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
            return;
         }

         // Renaming onto an existing tag would silently merge the two; the delete step would then remove
         // the name the user asked to rename away from, leaving one tag where they expected two distinct
         // ones. Surface it here instead.
         if (GameplayTagManager.TryRequest(newName, out GameplayTag existing) && existing.IsValid)
         {
            m_ValidationError = $"A tag named '{newName}' is already registered.";
         }
      }
   }
}
