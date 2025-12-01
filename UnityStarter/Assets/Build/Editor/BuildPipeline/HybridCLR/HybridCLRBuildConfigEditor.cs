using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(HybridCLRBuildConfig))]
    public class HybridCLRBuildConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty hotUpdateAssemblies;
        private SerializedProperty hotUpdateDllOutputDirectory;

        private bool hasValidationErrors = false;

        private void OnEnable()
        {
            hotUpdateAssemblies = serializedObject.FindProperty("hotUpdateAssemblies");
            hotUpdateDllOutputDirectory = serializedObject.FindProperty("hotUpdateDllOutputDirectory");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            hasValidationErrors = false;

            EditorGUILayout.LabelField("HybridCLR Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Hot Update Configuration
            EditorGUILayout.LabelField("Hot Update Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hotUpdateAssemblies);
            ValidateHotUpdateAssemblies();
            EditorGUILayout.Space(10);

            // Output Settings
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hotUpdateDllOutputDirectory);
            ValidateOutputDirectory();
            EditorGUILayout.Space(10);

            // Show validation summary at the end if there are errors
            if (hasValidationErrors)
            {
                EditorGUILayout.Space(5);
                DrawValidationSummary();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawValidationSummary()
        {
            DrawHelpBox(
                "⚠ Configuration Issues Detected\n" +
                "Please fix the errors below before building.",
                MessageType.Warning);
        }

        private void ValidateHotUpdateAssemblies()
        {
            if (hotUpdateAssemblies == null || hotUpdateAssemblies.arraySize == 0)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ No Hot Update Assemblies assigned!\n\n" +
                    "How to fix:\n" +
                    "1. Click the '+' button to add a slot\n" +
                    "2. Drag an Assembly Definition Asset (.asmdef) from your project into the slot\n" +
                    "3. The .asmdef file should be located in your project (e.g., Assets/YourAssembly/YourAssembly.asmdef)\n\n" +
                    "Example:\n" +
                    "• Assets/Gameplay/Gameplay.asmdef\n" +
                    "• Assets/UI/UI.asmdef\n" +
                    "• Assets/Network/Network.asmdef",
                    MessageType.Error);
            }
            else
            {
                int nullCount = 0;
                for (int i = 0; i < hotUpdateAssemblies.arraySize; i++)
                {
                    var element = hotUpdateAssemblies.GetArrayElementAtIndex(i);
                    if (element.objectReferenceValue == null)
                    {
                        nullCount++;
                    }
                }

                if (nullCount > 0)
                {
                    DrawHelpBox(
                        $"⚠ Warning: {nullCount} empty slot(s) in Hot Update Assemblies list.\n\n" +
                        "How to fix:\n" +
                        "• Remove empty slots by clicking the '-' button, OR\n" +
                        "• Assign valid Assembly Definition Assets to empty slots\n\n" +
                        "Tip: Empty slots will be ignored during build, but it's better to remove them for clarity.",
                        MessageType.Warning);
                }
            }
        }

        private void ValidateOutputDirectory()
        {
            string path = hotUpdateDllOutputDirectory.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Output Directory is required!\n\n" +
                    "How to fill:\n" +
                    "Enter a path relative to your project root, starting with 'Assets/'.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL\n" +
                    "• Assets/Game/HotUpdate/Assemblies\n\n" +
                    "The directory will be created automatically if it doesn't exist.",
                    MessageType.Error);
                return;
            }

            string trimmedPath = path.Trim();
            if (trimmedPath.Length == 0)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Output Directory cannot be empty!\n\n" +
                    "How to fill:\n" +
                    "Enter a valid directory path starting with 'Assets/'.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL",
                    MessageType.Error);
                return;
            }

            // Validate path format
            if (!trimmedPath.StartsWith("Assets/") && !trimmedPath.StartsWith("Assets\\"))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Output Directory must be within the Assets folder!\n\n" +
                    "Current value: " + trimmedPath + "\n\n" +
                    "How to fix:\n" +
                    "The path must start with 'Assets/' (case-sensitive).\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL\n" +
                    "• Assets/Game/HotUpdate/Assemblies\n\n" +
                    "Incorrect Examples:\n" +
                    "• HotUpdateDLL (missing 'Assets/' prefix)\n" +
                    "• assets/HotUpdateDLL (wrong case, should be 'Assets')\n" +
                    "• Assets\\HotUpdateDLL (use forward slash '/' instead of backslash)",
                    MessageType.Error);
                return;
            }

            // Check for invalid characters
            char[] invalidChars = Path.GetInvalidPathChars();
            bool hasInvalidChar = false;
            char invalidChar = '\0';
            foreach (char c in invalidChars)
            {
                if (trimmedPath.Contains(c))
                {
                    hasInvalidChar = true;
                    invalidChar = c;
                    break;
                }
            }

            if (hasInvalidChar)
            {
                hasValidationErrors = true;
                string charDisplay = invalidChar == '\0' ? "null" : $"'{invalidChar}'";
                DrawHelpBox(
                    $"❌ Output Directory contains invalid character: {charDisplay}\n\n" +
                    "How to fix:\n" +
                    "Remove all invalid characters from the path. Use only letters, numbers, underscores, hyphens, and forward slashes.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/HotUpdate_DLL\n" +
                    "• Assets/HotUpdate-DLL\n\n" +
                    "Invalid Characters:\n" +
                    "• < > : \" | ? * and other special characters",
                    MessageType.Error);
                return;
            }

            // Check for relative path issues
            if (trimmedPath.Contains(".."))
            {
                DrawHelpBox(
                    "⚠ Warning: Output path contains '..' which may cause issues.\n\n" +
                    "Current value: " + trimmedPath + "\n\n" +
                    "How to fix:\n" +
                    "Use a direct path relative to the Assets folder instead of using '..'.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL (instead of Assets/../HotUpdateDLL)\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL",
                    MessageType.Warning);
            }

            // Check if directory exists or can be created
            string fullPath = GetFullOutputPath();
            if (!string.IsNullOrEmpty(fullPath))
            {
                if (Directory.Exists(fullPath))
                {
                    DrawHelpBox(
                        $"✓ Output directory exists and is ready to use.\n\n" +
                        $"Full Path:\n{fullPath}\n\n" +
                        "The hot update DLLs will be copied to this directory during build.",
                        MessageType.Info);
                }
                else
                {
                    // Check if parent directory exists
                    string parentDir = Path.GetDirectoryName(fullPath);
                    if (Directory.Exists(parentDir))
                    {
                        DrawHelpBox(
                            $"ℹ Output directory will be created automatically during build.\n\n" +
                            $"Full Path:\n{fullPath}\n\n" +
                            "The directory doesn't exist yet, but it will be created when you build.",
                            MessageType.Info);
                    }
                    else
                    {
                        DrawHelpBox(
                            $"⚠ Warning: Parent directory does not exist.\n\n" +
                            $"Full Path:\n{fullPath}\n\n" +
                            "How to fix:\n" +
                            "Ensure the parent path is valid. The directory will be created during build, but make sure the path structure is correct.\n\n" +
                            "Example:\n" +
                            "If you entered 'Assets/Game/HotUpdateDLL', make sure 'Assets/Game' exists or can be created.",
                            MessageType.Warning);
                    }
                }
            }
        }

        private string GetFullOutputPath()
        {
            if (hotUpdateDllOutputDirectory == null || string.IsNullOrEmpty(hotUpdateDllOutputDirectory.stringValue))
                return null;

            string relativePath = hotUpdateDllOutputDirectory.stringValue.Trim();
            if (!relativePath.StartsWith("Assets/") && !relativePath.StartsWith("Assets\\"))
                return null;

            // Normalize path separators
            relativePath = relativePath.Replace('\\', '/');

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath).Replace('\\', '/');
        }

        private void DrawHelpBox(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }
    }
}
