using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(YooAssetBuildConfig))]
    public sealed class YooAssetBuildConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty buildOutputRoot;
        private SerializedProperty bundledFileRoot;
        private SerializedProperty packages;

        private void OnEnable()
        {
            buildOutputRoot = serializedObject.FindProperty("buildOutputRoot");
            bundledFileRoot = serializedObject.FindProperty("bundledFileRoot");
            packages = serializedObject.FindProperty("packages");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("YooAsset 3 Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The optional typed adapter supports com.tuyoogame.yooasset [3.0.5,4.0.0). " +
                "Package profiles are the CI source of truth and never read YooAsset EditorPrefs.",
                MessageType.Info);
            DrawPackageCatalogStatus();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("YooAsset Roots", EditorStyles.boldLabel);
            BuildAuthoringPathField.DrawProjectRelativeDirectory(
                buildOutputRoot,
                new GUIContent("Build Output Root", "Project-relative YooAsset package publication root."),
                YooAssetBuildRootPolicy.DefaultBuildOutputRoot,
                allowEmpty: true);
            BuildAuthoringPathField.DrawProjectRelativeDirectory(
                bundledFileRoot,
                new GUIContent(
                    "Built-In File Root",
                    "Project-relative destination for built-in files. Empty delegates to YooAsset settings."),
                "Assets/StreamingAssets",
                allowEmpty: true);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Package Profiles", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(packages, true);

            DrawValidation();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawValidation()
        {
            var errors = new List<string>();
            ValidateRootConfiguration(errors);
            if (packages.arraySize == 0)
            {
                errors.Add("At least one package profile is required.");
            }

            var packageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int enabledCount = 0;
            for (int index = 0; index < packages.arraySize; index++)
            {
                SerializedProperty profile = packages.GetArrayElementAtIndex(index);
                if (!profile.FindPropertyRelative("enabled").boolValue)
                {
                    continue;
                }

                enabledCount++;
                string packageName =
                    profile.FindPropertyRelative("packageName").stringValue ?? string.Empty;
                if (!IsSafePathSegment(packageName))
                {
                    errors.Add($"Package profile {index} has an invalid package name.");
                }
                else if (!packageNames.Add(packageName))
                {
                    errors.Add($"Package name '{packageName}' is configured more than once.");
                }

                string note = profile.FindPropertyRelative("packageNote").stringValue;
                if (string.IsNullOrWhiteSpace(note))
                {
                    errors.Add($"Package '{packageName}' requires a deterministic package note.");
                }

                var cryptography = profile.FindPropertyRelative("cryptography")
                    .objectReferenceValue as YooAssetCryptographyConfiguration;
                YooAssetCryptographyAvailability cryptographyAvailability =
                    YooAssetCryptographyAuthoringCatalog.Inspect(cryptography);
                if (cryptographyAvailability.Status != YooAssetCryptographyAvailabilityStatus.None
                    && !cryptographyAvailability.IsAvailable)
                {
                    errors.Add($"Package '{packageName}' cryptography is unavailable: {cryptographyAvailability.Diagnostic}");
                }

                var copyOption = (YooAssetBundledCopyOption)profile.FindPropertyRelative("bundledCopyOption").enumValueIndex;
                bool tagCopy = copyOption == YooAssetBundledCopyOption.ClearAndCopyByTags
                    || copyOption == YooAssetBundledCopyOption.OnlyCopyByTags;
                if (tagCopy && string.IsNullOrWhiteSpace(profile.FindPropertyRelative("bundledCopyTags").stringValue))
                {
                    errors.Add($"Package '{packageName}' uses tag-based bundled copy but has no tags.");
                }
            }

            if (enabledCount == 0)
            {
                errors.Add("At least one package profile must be enabled.");
            }

            foreach (string error in errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }

        private void ValidateRootConfiguration(ICollection<string> errors)
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();
            string resolvedBuildRoot = null;
            string resolvedBundledRoot = null;

            try
            {
                resolvedBuildRoot = YooAssetBuildRootPolicy.ResolveBuildOutputRoot(
                    projectRoot,
                    buildOutputRoot.stringValue);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }

            if (!string.IsNullOrWhiteSpace(bundledFileRoot.stringValue))
            {
                try
                {
                    resolvedBundledRoot =
                        YooAssetBuildRootPolicy.ResolveConfiguredBundledFileRoot(
                            projectRoot,
                            bundledFileRoot.stringValue);
                }
                catch (Exception exception)
                {
                    errors.Add(exception.Message);
                }
            }

            if (resolvedBuildRoot == null || resolvedBundledRoot == null)
            {
                return;
            }

            try
            {
                YooAssetBuildRootPolicy.EnsureRootsDoNotOverlap(
                    resolvedBuildRoot,
                    resolvedBundledRoot);
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void DrawPackageCatalogStatus()
        {
            YooAssetPackageCatalogSnapshot catalog =
                YooAssetPackageAuthoringCatalog.GetSnapshot();
            switch (catalog.Status)
            {
                case YooAssetPackageCatalogStatus.PackageUnavailable:
                    return;
                case YooAssetPackageCatalogStatus.Ready:
                    if (catalog.PackageNames.Count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "YooAsset Bundle Collector settings contain no packages.",
                            MessageType.Warning);
                    }
                    return;
                case YooAssetPackageCatalogStatus.SettingsMissing:
                    EditorGUILayout.HelpBox(catalog.Diagnostic, MessageType.Warning);
                    return;
                case YooAssetPackageCatalogStatus.Invalid:
                    EditorGUILayout.HelpBox(catalog.Diagnostic, MessageType.Error);
                    return;
                default:
                    EditorGUILayout.HelpBox(
                        "YooAsset package authoring returned an unknown state.",
                        MessageType.Error);
                    return;
            }
        }

        private static bool IsSafePathSegment(string value)
        {
            return YooAssetBuildTokenPolicy.IsValidPackageName(value);
        }
    }
}
