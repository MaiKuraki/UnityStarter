using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Authoring UI for <see cref="DataTableValidationConfiguration"/>. The launcher is stored as a
    /// project-relative path without a platform extension, so one asset resolves on every platform a
    /// team builds from. Resolved absolute paths are shown for diagnostics only and never serialized.
    /// </summary>
    [CustomEditor(typeof(DataTableValidationConfiguration))]
    public sealed class DataTableValidationConfigurationEditor : UnityEditor.Editor
    {
        private const string WindowsExtension = ".bat";
        private const string UnixExtension = ".sh";

        private SerializedProperty _profile;
        private SerializedProperty _launcherRelativePath;
        private SerializedProperty _timeoutSeconds;
        private bool _showResolvedPaths;
        private string _validationMessage;
        private bool _validationPending;
        private string _pendingRevealPath;
        private bool _revealPending;

        private void OnEnable()
        {
            _profile = serializedObject.FindProperty("profile");
            _launcherRelativePath = serializedObject.FindProperty("launcherRelativePath");
            _timeoutSeconds = serializedObject.FindProperty("timeoutSeconds");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string relative = ResolveRelativePath();
            string windowsPath = ResolveAbsolute(projectRoot, relative, WindowsExtension);
            string unixPath = ResolveAbsolute(projectRoot, relative, UnixExtension);
            bool windowsFound = File.Exists(windowsPath);
            bool unixFound = File.Exists(unixPath);
            bool windowsHost = Path.DirectorySeparatorChar == '\\';

            BuildInspectorUi.DrawSubsectionLabel("Project Setup");
            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawMutedText(
                "Path relative to the Unity project root, without a platform extension. " +
                "Empty uses " + DataTableValidationConfiguration.DefaultLauncherRelativePath + ".");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_launcherRelativePath, GUIContent.none);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                EditorApplication.delayCall += BrowseLauncher;
            }

            if (GUILayout.Button("Reset", GUILayout.Width(60f)))
            {
                _launcherRelativePath.stringValue = string.Empty;
            }

            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(_launcherRelativePath.stringValue))
            {
                EditorGUILayout.LabelField(
                    "Effective (default): " +
                    DataTableValidationConfiguration.DefaultLauncherRelativePath,
                    EditorStyles.miniLabel);
            }

            BuildInspectorUi.DrawStatusRow(
                windowsHost ? "Windows launcher (current)" : "Windows launcher",
                windowsFound ? "FOUND" : "MISSING",
                windowsFound ? BuildInspectorTone.Ready : BuildInspectorTone.Error,
                windowsPath);
            BuildInspectorUi.DrawStatusRow(
                windowsHost ? "macOS / Linux launcher" : "macOS / Linux launcher (current)",
                unixFound ? "FOUND" : "MISSING",
                unixFound ? BuildInspectorTone.Ready : BuildInspectorTone.Error,
                unixPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate", GUILayout.Width(80f)))
            {
                _validationMessage = BuildValidationMessage(
                    windowsPath,
                    windowsFound,
                    unixPath,
                    unixFound);
                if (!_validationPending)
                {
                    _validationPending = true;
                    EditorApplication.delayCall += ShowPendingValidation;
                }
            }

            if (GUILayout.Button("Copy Current Path", GUILayout.Width(130f)))
            {
                EditorGUIUtility.systemCopyBuffer = windowsHost ? windowsPath : unixPath;
            }

            EditorGUILayout.EndHorizontal();

            bool expanded = BuildInspectorUi.DrawInlineFoldout(
                EditorGUILayout.GetControlRect(hasLabel: false),
                _showResolvedPaths,
                new GUIContent("Resolved paths"));
            // The foldout flips state inside a MouseDown event. Applying it and drawing the body in
            // the same event unbalances the layout group, so the body renders from the next event on.
            bool toggledThisEvent = expanded != _showResolvedPaths;
            if (toggledThisEvent)
            {
                _showResolvedPaths = expanded;
                GUI.changed = true;
            }

            if (_showResolvedPaths && !toggledThisEvent)
            {
                EditorGUI.indentLevel++;
                DrawResolvedPath("Windows", relative + WindowsExtension, windowsPath);
                DrawResolvedPath("macOS / Linux", relative + UnixExtension, unixPath);
                EditorGUILayout.LabelField(
                    "Executes: " + Path.GetFileName(relative) +
                    (windowsHost ? WindowsExtension : UnixExtension) +
                    " check --profile " + _profile.stringValue,
                    EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            BuildInspectorUi.EndPanel();

            BuildInspectorUi.DrawSubsectionLabel("Validation");
            BuildInspectorUi.BeginPanel();
            EditorGUILayout.PropertyField(_profile, new GUIContent("Profile"));
            EditorGUILayout.PropertyField(_timeoutSeconds, new GUIContent("Timeout Seconds"));
            BuildInspectorUi.EndPanel();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawResolvedPath(string platform, string relativePath, string absolutePath)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(platform, GUILayout.Width(110f));
            EditorGUILayout.SelectableLabel(
                relativePath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Copy", GUILayout.Width(50f)))
            {
                EditorGUIUtility.systemCopyBuffer = absolutePath;
            }

            bool canReveal = File.Exists(absolutePath);
            using (new EditorGUI.DisabledScope(!canReveal))
            {
                if (GUILayout.Button("Reveal", GUILayout.Width(56f)) && canReveal)
                {
                    _pendingRevealPath = absolutePath;
                    if (!_revealPending)
                    {
                        _revealPending = true;
                        EditorApplication.delayCall += RevealPendingPath;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RevealPendingPath()
        {
            _revealPending = false;
            string path = _pendingRevealPath;
            _pendingRevealPath = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
        }

        private static string BuildValidationMessage(
            string windowsPath,
            bool windowsFound,
            string unixPath,
            bool unixFound)
        {
            return "Windows\n" +
                   (windowsFound ? "FOUND   " : "MISSING ") + windowsPath + "\n\n" +
                   "macOS / Linux\n" +
                   (unixFound ? "FOUND   " : "MISSING ") + unixPath + "\n\n" +
                   (windowsFound && unixFound
                       ? "Both launchers resolve. The step can run on any platform."
                       : "At least one platform will fail the DataTable check.");
        }

        private void ShowPendingValidation()
        {
            _validationPending = false;
            string message = _validationMessage;
            _validationMessage = null;
            if (!string.IsNullOrEmpty(message))
            {
                EditorUtility.DisplayDialog(
                    "DataTable Validation Configuration",
                    message,
                    "OK");
            }
        }

        private string ResolveRelativePath()
        {
            string configured = _launcherRelativePath.stringValue;
            return string.IsNullOrEmpty(configured)
                ? DataTableValidationConfiguration.DefaultLauncherRelativePath
                : configured;
        }

        private static string ResolveAbsolute(string projectRoot, string relative, string extension)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, relative + extension));
        }

        private void BrowseLauncher()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string resolved = ResolveAbsolute(projectRoot, ResolveRelativePath(), CurrentPlatformExtension());
            string directory = Path.GetDirectoryName(resolved);
            string current = !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                ? directory
                : projectRoot;

            string picked = EditorUtility.OpenFilePanel(
                "Select DataTable pipeline launcher",
                current,
                "bat,sh");
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            string extension = Path.GetExtension(picked);
            if (!string.Equals(extension, WindowsExtension, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, UnixExtension, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Launcher",
                    "Select gen_code_bin_to_project_lazyload.bat or .sh.",
                    "OK");
                return;
            }

            string relative = Path.GetRelativePath(projectRoot, Path.GetFullPath(picked))
                .Replace('\\', '/');
            _launcherRelativePath.stringValue = relative.Substring(
                0,
                relative.Length - extension.Length);
        }

        private static string CurrentPlatformExtension()
        {
            return Path.DirectorySeparatorChar == '\\' ? WindowsExtension : UnixExtension;
        }
    }
}
