using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Data;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        internal static bool IsAssetCreationPathOccupied(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return true;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                return true;
            }

            string absolutePath = Path.GetFullPath(
                Path.Combine(BuildAuthoringPathField.GetProjectRoot(), assetPath));
            return File.Exists(absolutePath)
                || Directory.Exists(absolutePath)
                || File.Exists(absolutePath + ".meta");
        }

        private void DrawVersionInfoDestination(bool requiredByRecipe)
        {
            string path = versionInfoAssetPath.stringValue?.Replace('\\', '/') ?? string.Empty;
            string directory = GetAssetDirectory(path);
            UnityEngine.Object targetAsset = AssetDatabase.LoadMainAssetAtPath(path);
            RefreshVersionInfoTargetOccupation(path, targetAsset);

            UnityEngine.Object current = targetAsset;
            if (current == null
                && string.IsNullOrEmpty(versionInfoTargetOccupationError)
                && !string.IsNullOrEmpty(directory))
            {
                current = AssetDatabase.LoadAssetAtPath<DefaultAsset>(directory);
            }

            bool usesDefault = string.Equals(
                path,
                BuildData.DefaultVersionInfoAssetPath,
                StringComparison.Ordinal);
            BuildInspectorUi.DrawStatusRow(
                "Runtime Version Info",
                requiredByRecipe
                    ? usesDefault
                        ? "Transactional default"
                        : "Custom destination"
                    : "Not used by this recipe",
                requiredByRecipe
                    ? BuildInspectorTone.Ready
                    : BuildInspectorTone.Disabled);
            BuildInspectorUi.DrawMutedText(
                requiredByRecipe
                    ? "VersionInfoData is installed only while the Player is being built, then the previous asset state is restored. " +
                      "Any missing folder chain created by this transaction is removed bottom-up after success or handled failure. " +
                      "After a hard process interruption, use Workspace Recovery before the next build."
                    : "This recipe does not build a Player, so it will not create VersionInfoData or a Resources folder.");

            BuildInspectorStatus destinationStatus =
                !string.IsNullOrEmpty(versionInfoTargetOccupationError)
                    ? new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED")
                    : usesDefault
                        ? new BuildInspectorStatus(BuildInspectorTone.Neutral, "DEFAULT")
                        : new BuildInspectorStatus(BuildInspectorTone.Info, "CUSTOM");
            using (BuildInspectorFoldoutScope foldout =
                   BuildInspectorUi.BeginNestedFoldout(
                       new GUIContent(
                           "Advanced Version Info Destination",
                           "Choose the temporary VersionInfoData asset destination used by Player builds."),
                       showAdvancedVersionInfo,
                       destinationStatus,
                       "The destination is installed transactionally and restored or removed after the build."))
            {
                showAdvancedVersionInfo = foldout.Expanded;
                if (!foldout.Expanded)
                {
                    return;
                }

                var destinationActions = new[]
                {
                    new BuildInspectorCommand(
                        0,
                        new GUIContent(
                            "Browse",
                            "Choose an Assets folder for the transactional VersionInfoData destination."),
                        role: BuildInspectorActionRole.Accessory),
                    new BuildInspectorCommand(
                        1,
                        new GUIContent(
                            "Reset",
                            "Restore the portable default transactional destination."),
                        enabled: !usesDefault,
                        role: BuildInspectorActionRole.Accessory)
                };
                BuildInspectorObjectFieldResult destinationResult =
                    BuildInspectorUi.DrawObjectFieldWithActions(
                        new GUIContent(
                            "Version Info Destination",
                            "Drag an existing VersionInfoData asset or an Assets folder. A missing destination folder is created only for the build and removed afterward."),
                        current,
                        typeof(UnityEngine.Object),
                        allowSceneObjects: false,
                        destinationActions);
                if (destinationResult.Value != current)
                {
                    ApplyVersionInfoObject(destinationResult.Value);
                }

                if (destinationResult.CommandId == 0)
                {
                    BrowseVersionInfoDirectory(directory);
                }

                if (destinationResult.CommandId == 1)
                {
                    versionInfoAssetPath.stringValue = BuildData.DefaultVersionInfoAssetPath;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        new GUIContent(
                            "Generated Asset Path",
                            $"CI may override this with {BuildCommandLineOptionNames.VersionInfo}."),
                        versionInfoAssetPath.stringValue);
                }

                if (current == null
                    && string.IsNullOrEmpty(versionInfoTargetOccupationError)
                    && !string.IsNullOrEmpty(directory))
                {
                    BuildInspectorUi.DrawNotice(
                        "This destination folder does not exist. A Player build will create it transactionally and remove the owned empty folder chain afterward. " +
                        "Unknown files are never deleted; they block cleanup and retain recovery evidence.",
                        BuildInspectorTone.Info);
                }
            }
        }

        private void RefreshVersionInfoTargetOccupation()
        {
            string path = versionInfoAssetPath.stringValue?.Replace('\\', '/') ?? string.Empty;
            UnityEngine.Object targetAsset = AssetDatabase.LoadMainAssetAtPath(path);
            RefreshVersionInfoTargetOccupation(path, targetAsset);
        }

        private void RefreshVersionInfoTargetOccupation(
            string path,
            UnityEngine.Object targetAsset)
        {
            versionInfoTargetOccupationError =
                GetVersionInfoTargetOccupationError(path, targetAsset);
        }

        private static string GetVersionInfoTargetOccupationError(
            string assetPath,
            UnityEngine.Object mainAsset)
        {
            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    assetPath,
                    "Version Info Asset Path");
            }
            catch (ArgumentException)
            {
                return null;
            }

            string absolutePath = Path.GetFullPath(
                Path.Combine(BuildAuthoringPathField.GetProjectRoot(), assetPath));
            bool containsVersionInfoAsset = mainAsset is VersionInfoData;
            string occupyingAssetType = mainAsset == null || containsVersionInfoAsset
                ? null
                : mainAsset.GetType().Name;
            return DescribeVersionInfoTargetOccupation(
                assetPath,
                containsVersionInfoAsset,
                occupyingAssetType,
                File.Exists(absolutePath),
                Directory.Exists(absolutePath),
                File.Exists(absolutePath + ".meta"));
        }

        internal static string DescribeVersionInfoTargetOccupation(
            string assetPath,
            bool containsVersionInfoAsset,
            string occupyingAssetType,
            bool targetFileExists,
            bool targetDirectoryExists,
            bool targetMetaExists)
        {
            if (containsVersionInfoAsset)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(occupyingAssetType))
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a {occupyingAssetType} asset. " +
                    "Select a VersionInfoData asset or another destination folder.";
            }

            if (targetDirectoryExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a directory at the generated asset file path.";
            }

            if (targetFileExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by a file that Unity cannot load as VersionInfoData.";
            }

            if (targetMetaExists)
            {
                return
                    $"Version Info target '{assetPath}' is occupied by an orphan .meta file.";
            }

            return null;
        }

        private void ApplyVersionInfoObject(UnityEngine.Object selected)
        {
            if (selected == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(selected).Replace('\\', '/');
            if (selected is VersionInfoData)
            {
                ApplyValidatedVersionInfoPath(path);
                return;
            }

            if (selected is DefaultAsset && AssetDatabase.IsValidFolder(path))
            {
                ApplyValidatedVersionInfoPath(
                    path.TrimEnd('/') + "/" + VersionInfoFileName);
                return;
            }

            EditorUtility.DisplayDialog(
                "Invalid Version Info Destination",
                "Select a VersionInfoData asset or a folder below Assets.",
                "OK");
        }

        private void BrowseVersionInfoDirectory(string currentDirectory)
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();
            string current = BuildAuthoringPathField.ResolveExistingDirectory(projectRoot, currentDirectory);
            string selected = EditorUtility.OpenFolderPanel("Version Info Destination", current, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            if (!BuildAuthoringPathField.TryMakeProjectRelative(projectRoot, selected, out string relative)
                || !relative.StartsWith("Assets/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Version Info Destination",
                    "VersionInfoData must be generated in a child directory below Assets so Unity can import it safely.",
                    "OK");
                return;
            }

            ApplyValidatedVersionInfoPath(
                relative.TrimEnd('/') + "/" + VersionInfoFileName);
        }

        private void ApplyValidatedVersionInfoPath(string candidate)
        {
            try
            {
                RuntimeVersionInfoPathPolicy.Validate(candidate);
                versionInfoAssetPath.stringValue = candidate;
            }
            catch (ArgumentException exception)
            {
                EditorUtility.DisplayDialog(
                    "Invalid Version Info Destination",
                    exception.Message,
                    "OK");
            }
        }

        private static string GetAssetDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(directory) ? string.Empty : directory.Replace('\\', '/');
        }
    }
}
