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
    [CustomEditor(typeof(BuildData))]
    public sealed partial class BuildDataEditor : UnityEditor.Editor
    {
        private const string VersionInfoFileName = RuntimeVersionInfoPathPolicy.AssetFileName;

        private static readonly GUIContent SourceCleanlinessPolicyLabel = new GUIContent(
            "Source Cleanliness Policy",
            "Controls local interactive source qualification. Allow Dirty Local Release lets the " +
            "Inspector Release action use an isolated, non-distributable Local Release Player when " +
            "source is not clean. Batch-mode and qualified Release builds always require verified-clean source.");
        private static readonly GUIContent CheatBuildModeLabel = new GUIContent(
            "Cheat Build Mode",
            "Controls whether ENABLE_CHEAT is applied during player builds.");

        private SerializedProperty launchScene;
        private SerializedProperty additionalScenes;
        private SerializedProperty applicationVersion;
        private SerializedProperty outputBasePath;
        private SerializedProperty companyName;
        private SerializedProperty productName;
        private SerializedProperty applicationIdentifier;
        private SerializedProperty versionInfoAssetPath;
        private SerializedProperty recipeInvocations;
        private SerializedProperty sourceCleanlinessPolicy;
        private SerializedProperty cheatBuildMode;
        private BuildDataInspectorContractReport inspectorContractReport;

        private IReadOnlyList<BuildStepDescriptor> stepDescriptors = Array.Empty<BuildStepDescriptor>();
        private IReadOnlyList<AssetContentProviderDescriptor> providerDescriptors =
            Array.Empty<AssetContentProviderDescriptor>();
        private IReadOnlyList<HotUpdateProviderDescriptor> hotUpdateProviderDescriptors =
            Array.Empty<HotUpdateProviderDescriptor>();
        private GUIContent[] stepChoiceLabels = Array.Empty<GUIContent>();
        private string[] stepChoiceIds = Array.Empty<string>();
        private ReorderableList additionalSceneList;
        private ReorderableList stepList;
        private string catalogError;
        private string versionInfoTargetOccupationError;
        private BuildWorkspaceSnapshot workspaceSnapshot;
        private string workspaceInspectionError;
        private bool showAdvancedRecipe;
        private bool showAdvancedVersionInfo;
        private bool showReadiness = true;
        private bool showRecipe = true;
        private bool showScenes = true;
        private bool showVersionAndOutput = true;
        private bool showProductIdentity;
        private bool showPlayerOptions;
        private bool showSourceQualification = true;
        private bool showValidation = true;
        private bool showBuildActions = true;
        private bool showWorkspaceDetails;
        private int focusedInvocationIndex;

        private void OnEnable()
        {
            InvalidateRecipeGraphSnapshot();
            BuildDataInspectorPropertyBinding binding =
                BuildDataInspectorSerializedFieldContract.Bind(serializedObject);
            inspectorContractReport = binding.Report;
            if (!inspectorContractReport.IsValid)
            {
                return;
            }

            launchScene = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.LaunchScene);
            additionalScenes = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.AdditionalScenes);
            applicationVersion = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.ApplicationVersion);
            outputBasePath = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.OutputBasePath);
            companyName = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.CompanyName);
            productName = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.ProductName);
            applicationIdentifier = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.ApplicationIdentifier);
            versionInfoAssetPath = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.VersionInfoAssetPath);
            recipeInvocations = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.RecipeInvocations);
            sourceCleanlinessPolicy = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.SourceCleanlinessPolicy);
            cheatBuildMode = binding.GetRequired(
                BuildDataInspectorFieldNames.Profile.CheatBuildMode);

            RefreshCatalog();
            CreateAdditionalSceneList();
            CreateStepList();
            RefreshWorkspaceSnapshot();
            InitializeSourceWorkspaceMonitor();
        }

        public override void OnInspectorGUI()
        {
            using (BuildInspectorUi.BeginOuterContent())
            using (BuildInspectorUi.BeginResponsiveLabelWidth())
            {
                DrawInspectorContents();
            }
        }

        private void DrawInspectorContents()
        {
            serializedObject.Update();
            if (inspectorContractReport == null || !inspectorContractReport.IsValid)
            {
                DrawInspectorContractFailure();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (TryGetRecipeBudgetViolation(out string recipeBudgetViolation))
            {
                InvalidateRecipeGraphSnapshot();
                DrawRecipeBudgetRecovery(recipeBudgetViolation);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            RefreshRecipeGraphSnapshotValidity();

            BuildRecipeAnalysis preview = BuildRecipePresetCatalog.Analyze(
                GetSerializedRecipeInvocations());
            RefreshVersionInfoTargetOccupation();
            IReadOnlyList<string> previewErrors = ValidateSerializedProfile(preview);
            IReadOnlyList<UnityEngine.Object> previewDirtyAssets =
                BuildAuthoringAssetGuard.GetDirtyAssets((BuildData)target);
            bool editorBusy = IsEditorBusy();
            bool localPreviewAvailable =
                BuildRequestFactory.TryResolveLocalReleasePreviewSelection(
                    (BuildData)target,
                    out _,
                    out _);
            BuildInspectorStatus overallStatus = GetOverallStatus(
                previewErrors,
                previewDirtyAssets,
                editorBusy,
                preview.ProducesPlayer,
                localPreviewAvailable);
            BuildInspectorUi.DrawInspectorTitle(
                "Build Profile",
                $"{GetRecipeDisplayName(preview)}  •  {EditorUserBuildSettings.activeBuildTarget}",
                BuildInspectorUi.SetupColor,
                overallStatus);
            DrawReadinessOverview(
                preview,
                previewErrors,
                previewDirtyAssets,
                editorBusy,
                overallStatus);

            BuildRecipeAnalysis recipe = DrawPipelineRecipe();
            if (recipe.IncludesPlayer || recipe.IncludesCustomSteps)
            {
                DrawScenes();
            }
            DrawVersionAndOutput(recipe);
            if (recipe.IncludesPlayer
                || recipe.IncludesHotUpdate
                || recipe.IncludesCustomSteps)
            {
                DrawProductIdentity();
            }

            if (recipe.IncludesPlayer || recipe.IncludesCustomSteps)
            {
                DrawPlayerOptions();
            }
            DrawSourceQualification();
            bool workspaceIsReady = DrawWorkspaceHealth();

            IReadOnlyList<string> errors = ValidateSerializedProfile(recipe);
            IReadOnlyList<UnityEngine.Object> dirtyAssets =
                BuildAuthoringAssetGuard.GetDirtyAssets((BuildData)target);
            DrawValidationSummary(errors);
            DrawRunActions(
                errors,
                recipe,
                workspaceIsReady,
                dirtyAssets,
                IsEditorBusy());
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScenes()
        {
            bool ready = launchScene.objectReferenceValue != null;
            showScenes = BuildInspectorUi.DrawFoldoutHeader(
                "Scenes",
                showScenes,
                BuildInspectorUi.SetupColor,
                new BuildInspectorStatus(
                    ready ? BuildInspectorTone.Ready : BuildInspectorTone.Warning,
                    ready ? "READY" : "REQUIRED"));
            if (!showScenes)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            EditorGUILayout.PropertyField(launchScene);
            using (BuildInspectorFoldoutScope foldout =
                   BuildInspectorUi.BeginNestedFoldout(
                       new GUIContent(
                           "Additional Scenes",
                           "Scenes appended after the launch scene in this exact authoring order."),
                       additionalScenes.isExpanded,
                       new BuildInspectorStatus(
                           additionalScenes.arraySize == 0
                               ? BuildInspectorTone.Neutral
                               : BuildInspectorTone.Info,
                           additionalScenes.arraySize.ToString())))
            {
                additionalScenes.isExpanded = foldout.Expanded;
                if (foldout.Expanded)
                {
                    additionalSceneList?.DoLayoutList();
                }
            }
            BuildInspectorUi.EndPanel();
        }

        private void CreateAdditionalSceneList()
        {
            additionalSceneList = new ReorderableList(
                serializedObject,
                additionalScenes,
                draggable: true,
                displayHeader: false,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index < 0 || index >= additionalScenes.arraySize)
                    {
                        return;
                    }

                    rect.y += 1f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(
                        rect,
                        additionalScenes.GetArrayElementAtIndex(index),
                        GUIContent.none);
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 2f,
                onAddCallback = list =>
                {
                    int index = additionalScenes.arraySize;
                    additionalScenes.InsertArrayElementAtIndex(index);
                    additionalScenes.GetArrayElementAtIndex(index).objectReferenceValue = null;
                    list.index = index;
                }
            };
        }

        private void DrawInspectorContractFailure()
        {
            BuildInspectorUi.DrawInspectorTitle(
                "Inspector Contract Failure",
                "Build authoring is disabled until every serialized field has an explicit Inspector owner.",
                BuildInspectorUi.HotUpdateColor,
                new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED"));
            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawNotice(
                "The BuildData custom Inspector did not fall back to an unstructured default editor. " +
                "Update the serialized-field contract and its focused tests before editing or building this profile.",
                BuildInspectorTone.Error);

            if (inspectorContractReport == null)
            {
                BuildInspectorUi.DrawNotice(
                    "The serialized-field contract was not initialized.",
                    BuildInspectorTone.Error);
            }
            else
            {
                foreach (BuildDataInspectorContractIssue issue in inspectorContractReport.Issues)
                {
                    BuildInspectorUi.DrawNotice(
                        issue.Message,
                        BuildInspectorTone.Error);
                }
            }

            BuildInspectorUi.EndPanel();
        }

        private void DrawVersionAndOutput(BuildRecipeAnalysis recipe)
        {
            showVersionAndOutput = BuildInspectorUi.DrawFoldoutHeader(
                "Version and Output",
                showVersionAndOutput,
                BuildInspectorUi.ContentColor,
                new BuildInspectorStatus(
                    recipe.IncludesPlayer
                        ? BuildInspectorTone.Info
                        : BuildInspectorTone.Neutral,
                    recipe.IncludesPlayer ? "AUTO VERSION" : "OUTPUT"));
            if (!showVersionAndOutput)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            EditorGUILayout.PropertyField(applicationVersion);
            BuildAuthoringPathField.DrawProjectRelativeDirectory(
                outputBasePath,
                new GUIContent(
                    "Output Base Directory",
                    $"Project-relative root for all build results. CI may override it with {BuildCommandLineOptionNames.OutputRoot}."),
                fallbackDirectory: "Build",
                allowEmpty: false);
            DrawVersionInfoDestination(recipe.IncludesPlayer);
            BuildInspectorUi.EndPanel();
        }

        private void DrawProductIdentity()
        {
            bool ready = !string.IsNullOrWhiteSpace(productName.stringValue)
                && !string.IsNullOrWhiteSpace(applicationIdentifier.stringValue);
            showProductIdentity = BuildInspectorUi.DrawFoldoutHeader(
                "Product Identity",
                showProductIdentity,
                BuildInspectorUi.PlayerColor,
                new BuildInspectorStatus(
                    ready ? BuildInspectorTone.Ready : BuildInspectorTone.Warning,
                    ready ? "READY" : "REQUIRED"));
            if (!showProductIdentity)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            EditorGUILayout.PropertyField(companyName);
            EditorGUILayout.PropertyField(productName);
            EditorGUILayout.PropertyField(applicationIdentifier);
            BuildInspectorUi.EndPanel();
        }

        private void DrawPlayerOptions()
        {
            bool cheatEnabled = cheatBuildMode.enumValueIndex != (int)CheatBuildMode.Disabled;
            showPlayerOptions = BuildInspectorUi.DrawFoldoutHeader(
                "Player Options",
                showPlayerOptions,
                BuildInspectorUi.PlayerColor,
                new BuildInspectorStatus(
                    cheatEnabled ? BuildInspectorTone.Warning : BuildInspectorTone.Neutral,
                    cheatEnabled ? "CHEAT ENABLED" : "STANDARD"));
            if (!showPlayerOptions)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawResponsivePropertyField(
                cheatBuildMode,
                CheatBuildModeLabel);
            BuildInspectorUi.DrawMutedText(
                "Cheat Build Mode controls the per-build ENABLE_CHEAT symbol for the Player. " +
                "Hot Update and Asset Content are independent recipe entries with their own configuration assets.");
            BuildInspectorUi.EndPanel();
        }

        private void DrawReadinessOverview(
            BuildRecipeAnalysis recipe,
            IReadOnlyList<string> errors,
            IReadOnlyList<UnityEngine.Object> dirtyAssets,
            bool editorBusy,
            BuildInspectorStatus overallStatus)
        {
            showReadiness = BuildInspectorUi.DrawFoldoutHeader(
                "Build Readiness",
                showReadiness,
                BuildInspectorUi.SafetyColor,
                overallStatus,
                "A read-only projection of the current profile, workspace, authoring, and Unity Editor state.");
            if (!showReadiness)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawStatusRow(
                "Recipe",
                GetRecipeDisplayName(recipe),
                recipe.BlockingIssues.Count == 0
                    ? BuildInspectorTone.Info
                    : BuildInspectorTone.Error);
            BuildInspectorUi.DrawStatusRow(
                "Validation",
                errors.Count == 0 && string.IsNullOrEmpty(catalogError)
                    ? "Ready"
                    : $"{errors.Count + (string.IsNullOrEmpty(catalogError) ? 0 : 1)} issue(s)",
                errors.Count == 0 && string.IsNullOrEmpty(catalogError)
                    ? BuildInspectorTone.Ready
                    : BuildInspectorTone.Error);
            BuildInspectorUi.DrawStatusRow(
                "Build Transaction",
                GetWorkspaceStatusLabel(),
                GetWorkspaceTone());
            BuildInspectorUi.DrawStatusRow(
                "Source Workspace",
                GetSourceWorkspaceStatusLabel(),
                GetSourceWorkspaceTone());
            BuildInspectorUi.DrawStatusRow(
                "Authoring",
                dirtyAssets.Count == 0
                    ? "Saved"
                    : $"{dirtyAssets.Count} unsaved asset(s)",
                dirtyAssets.Count == 0
                    ? BuildInspectorTone.Ready
                    : BuildInspectorTone.Warning);
            BuildInspectorUi.DrawStatusRow(
                "Active Target",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                editorBusy ? BuildInspectorTone.Busy : BuildInspectorTone.Info);
            BuildInspectorUi.EndPanel();
        }

        private void DrawValidationSummary(IReadOnlyList<string> errors)
        {
            int issueCount = errors.Count + (string.IsNullOrEmpty(catalogError) ? 0 : 1);
            showValidation = BuildInspectorUi.DrawFoldoutHeader(
                "Validation",
                showValidation,
                issueCount == 0
                    ? BuildInspectorUi.SafetyColor
                    : BuildInspectorUi.HotUpdateColor,
                new BuildInspectorStatus(
                    issueCount == 0 ? BuildInspectorTone.Ready : BuildInspectorTone.Error,
                    issueCount == 0 ? "READY" : $"{issueCount} ISSUE{(issueCount == 1 ? string.Empty : "S")}"));
            if (!showValidation)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            if (!string.IsNullOrEmpty(catalogError))
            {
                BuildInspectorUi.DrawNotice(catalogError, BuildInspectorTone.Error);
            }

            for (int index = 0; index < errors.Count; index++)
            {
                BuildInspectorUi.DrawNotice(errors[index], BuildInspectorTone.Error);
            }

            if (issueCount == 0)
            {
                BuildInspectorUi.DrawStatusRow(
                    "Profile",
                    "Ready for preflight",
                    BuildInspectorTone.Ready);
                BuildInspectorUi.DrawMutedText(
                    "Editor and CI use the same invocation IDs, typed configuration assets, dependency graph, and execution policies. " +
                    "Build preflight performs the final package, provenance, and output-safety checks before changing Unity state.");
            }

            BuildInspectorUi.EndPanel();
        }

        private BuildInspectorStatus GetOverallStatus(
            IReadOnlyList<string> errors,
            IReadOnlyList<UnityEngine.Object> dirtyAssets,
            bool editorBusy,
            bool developmentActionAvailable,
            bool localPreviewAvailable)
        {
            if (!string.IsNullOrEmpty(catalogError))
            {
                return new BuildInspectorStatus(BuildInspectorTone.Error, "CATALOG ERROR");
            }

            if (workspaceSnapshot == null
                || workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Blocked)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED");
            }

            if (workspaceSnapshot.Status == BuildWorkspaceHealthStatus.RecoveryRequired)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Warning, "RECOVERY");
            }

            if (editorBusy || workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Busy)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Busy, "BUSY");
            }

            if (errors.Count > 0)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Error,
                    $"{errors.Count} ISSUE{(errors.Count == 1 ? string.Empty : "S")}");
            }

            if (dirtyAssets.Count > 0)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Warning, "UNSAVED");
            }

            if (sourceWorkspaceCaptureTask != null)
            {
                return localPreviewAvailable
                    ? new BuildInspectorStatus(BuildInspectorTone.Warning, "LOCAL PREVIEW")
                    : new BuildInspectorStatus(BuildInspectorTone.Busy, "CHECKING SOURCE");
            }

            if (!IsSourceWorkspacePreviewAllowed(debugBuild: false))
            {
                if (developmentActionAvailable
                    && IsSourceWorkspacePreviewAllowed(debugBuild: true))
                {
                    return new BuildInspectorStatus(BuildInspectorTone.Warning, "DEV ONLY");
                }

                return localPreviewAvailable
                    ? new BuildInspectorStatus(BuildInspectorTone.Warning, "LOCAL PREVIEW")
                    : new BuildInspectorStatus(BuildInspectorTone.Error, "SOURCE BLOCKED");
            }

            return new BuildInspectorStatus(BuildInspectorTone.Ready, "READY");
        }

        private string GetWorkspaceStatusLabel()
        {
            if (workspaceSnapshot == null)
            {
                return "Unavailable";
            }

            switch (workspaceSnapshot.Status)
            {
                case BuildWorkspaceHealthStatus.Clean:
                    return "Clean";
                case BuildWorkspaceHealthStatus.RecoveryRequired:
                    return "Recovery required";
                case BuildWorkspaceHealthStatus.Blocked:
                    return "Blocked";
                case BuildWorkspaceHealthStatus.Busy:
                    return "Busy";
                default:
                    return workspaceSnapshot.Status.ToString();
            }
        }

        private BuildInspectorTone GetWorkspaceTone()
        {
            if (workspaceSnapshot == null)
            {
                return BuildInspectorTone.Error;
            }

            switch (workspaceSnapshot.Status)
            {
                case BuildWorkspaceHealthStatus.Clean:
                    return BuildInspectorTone.Ready;
                case BuildWorkspaceHealthStatus.RecoveryRequired:
                    return BuildInspectorTone.Warning;
                case BuildWorkspaceHealthStatus.Busy:
                    return BuildInspectorTone.Busy;
                default:
                    return BuildInspectorTone.Error;
            }
        }

        private static bool IsEditorBusy()
        {
            return EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || UnityEditor.BuildPipeline.isBuildingPlayer;
        }

        private void RefreshCatalog()
        {
            var errors = new List<string>();
            try
            {
                stepDescriptors = BuildPipelineRegistry.GetBuildStepDescriptors(errors);
            }
            catch (Exception exception)
            {
                stepDescriptors = Array.Empty<BuildStepDescriptor>();
                errors.Add("Build step catalog is invalid: " + exception.Message);
            }

            try
            {
                providerDescriptors = BuildPipelineRegistry.GetAssetContentProviderDescriptors(errors);
            }
            catch (Exception exception)
            {
                providerDescriptors = Array.Empty<AssetContentProviderDescriptor>();
                errors.Add("Asset provider catalog is invalid: " + exception.Message);
            }

            try
            {
                hotUpdateProviderDescriptors =
                    HotUpdateBuildAdapterRegistry.GetProviderDescriptors(errors);
            }
            catch (Exception exception)
            {
                hotUpdateProviderDescriptors =
                    Array.Empty<HotUpdateProviderDescriptor>();
                errors.Add(
                    "Hot-update provider catalog is invalid: " +
                    exception.Message);
            }

            catalogError = errors.Count == 0 ? null : string.Join("\n", errors);
            stepChoiceLabels = stepDescriptors
                .Select(descriptor => new GUIContent(
                    $"{descriptor.Category}/{descriptor.DisplayName}",
                    descriptor.Description))
                .ToArray();
            stepChoiceIds = stepDescriptors.Select(descriptor => descriptor.StepTypeId).ToArray();
        }

    }
}
