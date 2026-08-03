using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class BuildAuthoringPathField
    {
        public static void DrawProjectRelativeDirectory(
            SerializedProperty property,
            GUIContent label,
            string fallbackDirectory,
            bool allowEmpty)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            using (new EditorGUI.DisabledScope(true))
            {
                string display = string.IsNullOrWhiteSpace(property.stringValue)
                    ? $"<Default: {fallbackDirectory}>"
                    : property.stringValue;
                EditorGUILayout.TextField(display);
            }

            if (GUILayout.Button("Browse", GUILayout.Width(64f)))
            {
                string projectRoot = GetProjectRoot();
                string relative = string.IsNullOrWhiteSpace(property.stringValue)
                    ? fallbackDirectory
                    : property.stringValue;
                string current = ResolveExistingDirectory(projectRoot, relative);
                string selected = EditorUtility.OpenFolderPanel(label.text, current, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    if (TryMakeProjectRelative(projectRoot, selected, out string selectedRelative))
                    {
                        property.stringValue = selectedRelative;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Invalid Project Directory",
                            "The selected directory must be inside the Unity project so the profile remains portable across workstations and CI agents.",
                            "OK");
                    }
                }
            }

            if (allowEmpty && GUILayout.Button("Default", GUILayout.Width(62f)))
            {
                property.stringValue = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        public static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        public static string ResolveExistingDirectory(string projectRoot, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return projectRoot;
            }

            string absolute = Path.GetFullPath(Path.Combine(projectRoot, relative));
            return Directory.Exists(absolute) ? absolute : projectRoot;
        }

        public static bool TryMakeProjectRelative(
            string projectRoot,
            string selectedDirectory,
            out string relative)
        {
            string normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedSelected = Path.GetFullPath(selectedDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!BuildPathPolicy.IsStrictDescendant(
                    normalizedRoot,
                    normalizedSelected))
            {
                relative = null;
                return false;
            }

            relative = normalizedSelected.Substring(prefix.Length).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                return false;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(relative, "Selected directory");
                return true;
            }
            catch (ArgumentException)
            {
                relative = null;
                return false;
            }
        }
    }
}
