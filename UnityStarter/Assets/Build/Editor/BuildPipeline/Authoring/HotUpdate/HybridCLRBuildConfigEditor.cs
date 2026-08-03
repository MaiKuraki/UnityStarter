using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(HybridCLRBuildConfig))]
    public sealed class HybridCLRBuildConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty hotUpdateAssemblies;
        private SerializedProperty hotUpdateDllOutputDirectory;
        private SerializedProperty aotDllOutputDirectory;
        private SerializedProperty obfuscateHotUpdateAssemblies;

        private void OnEnable()
        {
            hotUpdateAssemblies = Find("hotUpdateAssemblies");
            hotUpdateDllOutputDirectory = Find("hotUpdateDllOutputDirectory");
            aotDllOutputDirectory = Find("aotDllOutputDirectory");
            obfuscateHotUpdateAssemblies = Find("obfuscateHotUpdateAssemblies");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader("Hot-Update Compilation");
            EditorGUILayout.PropertyField(hotUpdateAssemblies, includeChildren: true);
            EditorGUILayout.HelpBox(
                "Assign project-owned .asmdef assets below Assets. Package assemblies are AOT dependencies and must not be placed in this list.",
                MessageType.None);

            DrawHeader("Transactional Publication");
            EditorGUILayout.PropertyField(
                hotUpdateDllOutputDirectory,
                new GUIContent("Hot-Update DLL Directory"));
            EditorGUILayout.PropertyField(
                aotDllOutputDirectory,
                new GUIContent("AOT Metadata DLL Directory"));
            EditorGUILayout.HelpBox(
                "Each directory must be a distinct, non-overlapping folder below Assets and must contain only build-managed output.",
                MessageType.Info);

            DrawHeader("Obfuscation");
            EditorGUILayout.PropertyField(obfuscateHotUpdateAssemblies);
            if (obfuscateHotUpdateAssemblies.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "This option requires compatible Obfuz and Obfuz4HybridCLR integrations. It does not enable Player obfuscation.",
                    MessageType.Info);
            }

            IReadOnlyList<string> issues = ValidateConfiguration();
            if (issues.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Error);
            }
            else
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "HybridCLR configuration is authoring-valid. Package availability and generated-output ownership are verified again at build preflight.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private IReadOnlyList<string> ValidateConfiguration()
        {
            var issues = new List<string>();
            ValidateAssemblies(issues);

            string hotUpdatePath = ValidateOutputDirectory(
                hotUpdateDllOutputDirectory,
                "Hot-Update DLL Directory",
                issues);
            string aotPath = ValidateOutputDirectory(
                aotDllOutputDirectory,
                "AOT Metadata DLL Directory",
                issues);
            if (!string.IsNullOrEmpty(hotUpdatePath)
                && !string.IsNullOrEmpty(aotPath)
                && (string.Equals(hotUpdatePath, aotPath, StringComparison.OrdinalIgnoreCase)
                    || IsParentPath(hotUpdatePath, aotPath)
                    || IsParentPath(aotPath, hotUpdatePath)))
            {
                issues.Add("HybridCLR output directories must be distinct and must not contain one another.");
            }

            return issues;
        }

        private void ValidateAssemblies(ICollection<string> issues)
        {
            if (hotUpdateAssemblies.arraySize == 0)
            {
                issues.Add("At least one Hot-Update Assembly is required.");
                return;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < hotUpdateAssemblies.arraySize; index++)
            {
                UnityEngine.Object value = hotUpdateAssemblies
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (value == null)
                {
                    issues.Add($"Hot-Update Assembly element {index} is empty.");
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(value).Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    issues.Add($"Hot-Update Assembly '{value.name}' must be an .asmdef below Assets.");
                }
                else if (!paths.Add(path))
                {
                    issues.Add($"Hot-Update Assembly '{value.name}' is configured more than once.");
                }
            }
        }

        private static string ValidateOutputDirectory(
            SerializedProperty property,
            string label,
            ICollection<string> issues)
        {
            if (property.objectReferenceValue == null)
            {
                issues.Add(label + " is required.");
                return null;
            }

            string path = AssetDatabase.GetAssetPath(property.objectReferenceValue).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                issues.Add(label + " must reference a folder below Assets.");
                return null;
            }

            return path.TrimEnd('/');
        }

        private static bool IsParentPath(string parent, string child)
        {
            return child.StartsWith(parent.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private SerializedProperty Find(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"HybridCLRBuildConfig serialized property '{propertyName}' was not found.");
            }

            return property;
        }

        private static void DrawHeader(string label)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}
