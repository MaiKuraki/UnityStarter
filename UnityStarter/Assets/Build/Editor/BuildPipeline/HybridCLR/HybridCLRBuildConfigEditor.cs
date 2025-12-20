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
        private SerializedProperty enableObfuz;
        private SerializedProperty aotDllOutputDirectory;

        private bool hasValidationErrors = false;

        private void OnEnable()
        {
            hotUpdateAssemblies = serializedObject.FindProperty("hotUpdateAssemblies");
            hotUpdateDllOutputDirectory = serializedObject.FindProperty("hotUpdateDllOutputDirectory");
            enableObfuz = serializedObject.FindProperty("enableObfuz");
            aotDllOutputDirectory = serializedObject.FindProperty("aotDllOutputDirectory");
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

            // Hot Update DLL Output Directory (drag & drop folder)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Hot Update DLL Output Directory", GUILayout.Width(200));
            EditorGUILayout.PropertyField(hotUpdateDllOutputDirectory, GUIContent.none);
            EditorGUILayout.EndHorizontal();
            ValidateHotUpdateDllOutputDirectory();
            EditorGUILayout.Space(5);

            // AOT DLL Output Settings
            EditorGUILayout.LabelField("AOT DLL Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("AOT DLL Output Directory", GUILayout.Width(200));
            EditorGUILayout.PropertyField(aotDllOutputDirectory, GUIContent.none);
            EditorGUILayout.EndHorizontal();
            ValidateAOTDllOutputDirectory();
            EditorGUILayout.Space(10);

            // Obfuz Settings
            EditorGUILayout.LabelField("Obfuz Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableObfuz);
            if (enableObfuz.boolValue)
            {
                DrawHelpBox(
                    "ℹ Obfuz is enabled. Hot update assemblies will be obfuscated before being copied to the output directory.\n\n" +
                    "When obfuscation is enabled, the build process will:\n" +
                    "1. Generate encryption VM and secret key files\n" +
                    "2. Configure ObfuzSettings (add Assembly-CSharp to reference list)\n" +
                    "3. Compile hot update assemblies\n" +
                    "4. Apply obfuscation to the assemblies\n" +
                    "5. Copy obfuscated assemblies to the output directory\n\n" +
                    "Note: BuildData.UseObfuz takes priority. If BuildData.UseObfuz is enabled, this setting is automatically considered enabled. AOT DLLs are still needed and will be copied if AOT DLL Output Directory is configured.",
                    MessageType.Info);
            }
            EditorGUILayout.Space(10);

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

        private void ValidateHotUpdateDllOutputDirectory()
        {
            if (hotUpdateDllOutputDirectory.objectReferenceValue == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Hot Update DLL Output Directory is required!\n\n" +
                    "How to fix:\n" +
                    "1. Drag a folder from your project (e.g., Assets/HotUpdateDLL) into the field above\n" +
                    "2. The folder must be within the Assets directory\n" +
                    "3. The directory will be created automatically if it doesn't exist\n\n" +
                    "Example folders:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL\n" +
                    "• Assets/Game/HotUpdate/Assemblies",
                    MessageType.Error);
                return;
            }

            string path = AssetDatabase.GetAssetPath(hotUpdateDllOutputDirectory.objectReferenceValue);
            if (string.IsNullOrEmpty(path))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Invalid folder reference!\n\n" +
                    "Please drag a valid folder from your project into the field above.",
                    MessageType.Error);
                return;
            }

            // Validate it's a folder (not a file)
            if (!AssetDatabase.IsValidFolder(path))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Selected asset is not a folder!\n\n" +
                    "Please drag a folder (not a file) from your project into the field above.",
                    MessageType.Error);
                return;
            }

            // Validate path format
            if (!path.StartsWith("Assets/") && !path.StartsWith("Assets\\"))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Output Directory must be within the Assets folder!\n\n" +
                    "Current value: " + path + "\n\n" +
                    "How to fix:\n" +
                    "The folder must be within the Assets directory.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdateDLL\n" +
                    "• Assets/StreamingAssets/HotUpdateDLL",
                    MessageType.Error);
                return;
            }

            // Show success message
            string fullPath = GetFullHotUpdateDllOutputPath();
            if (!string.IsNullOrEmpty(fullPath))
            {
                if (Directory.Exists(fullPath))
                {
                    DrawHelpBox(
                        $"✓ Hot Update DLL output directory is ready.\n\n" +
                        $"Path: {path}\n" +
                        $"Full Path:\n{fullPath}\n\n" +
                        "The hot update DLLs will be copied to this directory during build.",
                        MessageType.Info);
                }
                else
                {
                    DrawHelpBox(
                        $"ℹ Hot Update DLL output directory will be created during build.\n\n" +
                        $"Path: {path}\n" +
                        $"Full Path:\n{fullPath}",
                        MessageType.Info);
                }
            }
        }

        private void ValidateAOTDllOutputDirectory()
        {
            if (aotDllOutputDirectory.objectReferenceValue == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ AOT DLL Output Directory is required!\n\n" +
                    "AOT DLLs are essential for HybridCLR supplementary metadata generation at runtime. " +
                    "Without AOT DLLs, HybridCLR cannot properly load hot update assemblies that:\n" +
                    "• Reference AOT types (e.g., System.Collections.Generic.List<T>)\n" +
                    "• Use generics with value types defined in hot update code\n" +
                    "• Access AOT assembly members (types, methods, fields)\n\n" +
                    "How to fix:\n" +
                    "1. Drag a folder from your project (e.g., Assets/HotUpdate/Compiled/AOT) into the field above\n" +
                    "2. The folder must be within the Assets directory\n" +
                    "3. This directory will store AOT assemblies needed for metadata generation\n\n" +
                    "Example folders:\n" +
                    "• Assets/HotUpdate/Compiled/AOT\n" +
                    "• Assets/StreamingAssets/AOT\n" +
                    "• Assets/Game/HotUpdate/AOT\n\n" +
                    "Note: HybridCLR's MissingMetadataChecker will report errors if hot update code references AOT assemblies without supplementary metadata.",
                    MessageType.Error);
                return;
            }

            string path = AssetDatabase.GetAssetPath(aotDllOutputDirectory.objectReferenceValue);
            if (string.IsNullOrEmpty(path))
            {
                DrawHelpBox(
                    "⚠ Invalid folder reference!\n\n" +
                    "Please drag a valid folder from your project into the field above.",
                    MessageType.Warning);
                return;
            }

            // Validate it's a folder (not a file)
            if (!AssetDatabase.IsValidFolder(path))
            {
                DrawHelpBox(
                    "⚠ Selected asset is not a folder!\n\n" +
                    "Please drag a folder (not a file) from your project into the field above.",
                    MessageType.Warning);
                return;
            }

            // Validate path format
            if (!path.StartsWith("Assets/") && !path.StartsWith("Assets\\"))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ AOT DLL Output Directory must be within the Assets folder!\n\n" +
                    "Current value: " + path + "\n\n" +
                    "How to fix:\n" +
                    "The folder must be within the Assets directory.\n\n" +
                    "Correct Examples:\n" +
                    "• Assets/HotUpdate/Compiled/AOT\n" +
                    "• Assets/StreamingAssets/AOT",
                    MessageType.Error);
                return;
            }

            // Show success message
            string fullPath = GetFullAOTDllOutputPath();
            if (!string.IsNullOrEmpty(fullPath))
            {
                DrawHelpBox(
                    $"✓ AOT DLL output directory is configured.\n\n" +
                    $"Path: {path}\n" +
                    $"Full Path:\n{fullPath}\n\n" +
                    "AOT assemblies will be copied to this directory for HybridCLR metadata generation at runtime.",
                    MessageType.Info);
            }
        }

        private string GetFullHotUpdateDllOutputPath()
        {
            if (hotUpdateDllOutputDirectory == null || hotUpdateDllOutputDirectory.objectReferenceValue == null)
                return null;

            string relativePath = AssetDatabase.GetAssetPath(hotUpdateDllOutputDirectory.objectReferenceValue);
            if (string.IsNullOrEmpty(relativePath))
                return null;

            relativePath = relativePath.Replace('\\', '/');

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath).Replace('\\', '/');
        }

        private string GetFullAOTDllOutputPath()
        {
            if (aotDllOutputDirectory == null || aotDllOutputDirectory.objectReferenceValue == null)
                return null;

            string relativePath = AssetDatabase.GetAssetPath(aotDllOutputDirectory.objectReferenceValue);
            if (string.IsNullOrEmpty(relativePath))
                return null;

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