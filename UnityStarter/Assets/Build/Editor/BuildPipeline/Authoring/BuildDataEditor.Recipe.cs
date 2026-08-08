using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private static readonly BuildRecipePreset[] RecipePresetOrder =
        {
            BuildRecipePreset.PlayerOnly,
            BuildRecipePreset.PlayerWithContent,
            BuildRecipePreset.PlayerWithDependencies,
            BuildRecipePreset.ContentOnly,
            BuildRecipePreset.ContentWithHotUpdate,
            BuildRecipePreset.HotUpdateOnly
        };

        private static readonly BuildRecipePreset[] FocusedPresetOrder =
        {
            BuildRecipePreset.HotUpdateOnly,
            BuildRecipePreset.ContentOnly,
            BuildRecipePreset.ContentWithHotUpdate
        };

        private bool TryGetRecipeBudgetViolation(out string violation)
        {
            if (recipeInvocations == null)
            {
                violation = "BuildData is missing its serialized recipe collection.";
                return true;
            }

            int invocationCount = recipeInvocations.arraySize;
            if (invocationCount > BuildPipelineBudgets.MaximumInvocationCount)
            {
                violation =
                    $"Build Recipe contains {invocationCount} invocations and exceeds the " +
                    $"{BuildPipelineBudgets.MaximumInvocationCount}-invocation safety budget.";
                return true;
            }

            int dependencyCount = 0;
            for (int invocationIndex = 0;
                 invocationIndex < invocationCount;
                 invocationIndex++)
            {
                SerializedProperty invocation =
                    recipeInvocations.GetArrayElementAtIndex(invocationIndex);
                SerializedProperty dependencies =
                    invocation.FindPropertyRelative("dependencies");
                if (dependencies == null)
                {
                    violation =
                        $"Build Recipe invocation at index {invocationIndex} is missing its dependency collection.";
                    return true;
                }

                int invocationDependencyCount = dependencies.arraySize;
                if (invocationDependencyCount
                    > BuildPipelineBudgets.MaximumDependencyEdgeCount - dependencyCount)
                {
                    violation =
                        $"Build Recipe exceeds the {BuildPipelineBudgets.MaximumDependencyEdgeCount}-edge dependency safety budget.";
                    return true;
                }

                dependencyCount += invocationDependencyCount;
            }

            violation = string.Empty;
            return false;
        }

        private void DrawRecipeBudgetRecovery(string violation)
        {
            BuildInspectorUi.DrawInspectorTitle(
                "Build Profile Recovery",
                "The serialized recipe exceeds the bounded Inspector contract.",
                BuildInspectorUi.HotUpdateColor,
                new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED"));
            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawNotice(
                violation +
                " The Inspector will not materialize, analyze, or draw an unbounded graph. " +
                "Restore a valid version-controlled profile or explicitly replace this recipe.",
                BuildInspectorTone.Error);

            var commands = new[]
            {
                new BuildInspectorCommand(
                    0,
                    new GUIContent(
                        "Replace Recipe with Player Only",
                        "Discard the oversized recipe graph after explicit confirmation. Configuration assets are not deleted."),
                    role: BuildInspectorActionRole.Destructive)
            };
            if (BuildInspectorUi.DrawCommandGrid(commands, maximumColumns: 1) != 0)
            {
                BuildInspectorUi.EndPanel();
                return;
            }

            BuildInspectorUi.EndPanel();

            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Oversized Build Recipe",
                "This removes every current recipe invocation and dependency from this BuildData asset, " +
                "then creates one enabled Player invocation. Configuration assets are not deleted. " +
                "This operation supports Undo.",
                "Replace Recipe",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            Undo.RecordObject(target, "Replace Oversized Build Recipe");
            recipeInvocations.arraySize = 1;
            SerializedProperty invocation =
                recipeInvocations.GetArrayElementAtIndex(0);
            invocation.FindPropertyRelative("enabled").boolValue = true;
            invocation.FindPropertyRelative("invocationId").stringValue =
                BuildStepTypeIds.Player;
            invocation.FindPropertyRelative("stepTypeId").stringValue =
                BuildStepTypeIds.Player;
            invocation.FindPropertyRelative("configuration").objectReferenceValue = null;
            invocation.FindPropertyRelative("incrementality").enumValueIndex =
                (int)BuildIncrementality.Clean;
            invocation.FindPropertyRelative("dependencies").arraySize = 0;
            serializedObject.ApplyModifiedProperties();
            InvalidateRecipeGraphSnapshot();
            GUIUtility.ExitGUI();
        }

        private BuildRecipeAnalysis DrawPipelineRecipe()
        {
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                GetSerializedRecipeInvocations());
            showRecipe = BuildInspectorUi.DrawFoldoutHeader(
                "Build Recipe",
                showRecipe,
                BuildInspectorUi.RecipeColor,
                new BuildInspectorStatus(
                    analysis.BlockingIssues.Count == 0
                        ? BuildInspectorTone.Info
                        : BuildInspectorTone.Error,
                    GetRecipeDisplayName(analysis).ToUpperInvariant()));
            if (!showRecipe)
            {
                return analysis;
            }

            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawMutedText(
                "Quick Setup writes a standard dependency graph for common Player, content, and hot-update outputs. " +
                "Use Advanced DAG only for multiple providers, custom steps, or non-standard routing.");

            DrawRecipePresets(analysis);
            DrawStandardInvocationCards();
            using (BuildInspectorFoldoutScope foldout =
                   BuildInspectorUi.BeginNestedFoldout(
                       new GUIContent(
                           "Advanced DAG & CI",
                           "Edit invocation identities, registered step types, dependency edges, and the shared CI profile argument."),
                       showAdvancedRecipe,
                       new BuildInspectorStatus(
                           BuildInspectorTone.Neutral,
                           $"{recipeInvocations.arraySize} INVOCATION{(recipeInvocations.arraySize == 1 ? string.Empty : "S")}"),
                       "Use advanced routing only for multiple providers, custom steps, or non-standard dependency graphs."))
            {
                showAdvancedRecipe = foldout.Expanded;
                if (foldout.Expanded)
                {
                    BuildInspectorUi.DrawNotice(
                        "Dependencies are the only sequencing contract. Required also selects a producer; If Selected only orders two invocations already selected for the run. " +
                        "The list is not execution order: independent ready invocations are compiled by stable Invocation ID.",
                        BuildInspectorTone.Info);
                    stepList?.DoLayoutList();
                    DrawCiProfileArguments();
                }
            }

            analysis = BuildRecipePresetCatalog.Analyze(
                GetSerializedRecipeInvocations());
            DrawRecipeSummary(analysis);
            BuildInspectorUi.EndPanel();
            return analysis;
        }

        private void DrawRecipePresets(BuildRecipeAnalysis analysis)
        {
            BuildData profile = (BuildData)target;
            bool narrow = BuildInspectorUi.IsNarrowInspector();
            BuildInspectorUi.DrawSubsectionLabel("Quick Setup");
            var commands = new BuildInspectorCommand[RecipePresetOrder.Length];
            for (int index = 0; index < RecipePresetOrder.Length; index++)
            {
                BuildRecipePreset preset = RecipePresetOrder[index];
                bool enabled = BuildRecipePresetCatalog.CanApply(
                    profile,
                    preset,
                    out string unavailableReason);
                string tooltip = BuildRecipePresetCatalog.GetDescription(preset);
                if (!enabled && !string.IsNullOrWhiteSpace(unavailableReason))
                {
                    tooltip += "\n\n" + unavailableReason;
                }

                commands[index] = new BuildInspectorCommand(
                    index,
                    new GUIContent(
                        narrow
                            ? GetCompactRecipePresetDisplayName(preset)
                            : BuildRecipePresetCatalog.GetDisplayName(preset),
                        tooltip),
                    enabled,
                    analysis.MatchedPreset == preset
                        ? BuildInspectorActionRole.Selected
                        : BuildInspectorActionRole.Secondary);
            }

            int clicked = DrawResponsiveCommandGrid(commands);
            if (clicked >= 0 && clicked < RecipePresetOrder.Length)
            {
                ApplyRecipePreset(RecipePresetOrder[clicked]);
            }
        }

        private void ApplyRecipePreset(BuildRecipePreset preset)
        {
            serializedObject.ApplyModifiedProperties();
            bool changed = BuildRecipePresetAuthoring.Apply((BuildData)target, preset);
            serializedObject.Update();
            GUI.FocusControl(null);
            Repaint();
            if (changed)
            {
                GUIUtility.ExitGUI();
            }
        }

        private static string GetCompactRecipePresetDisplayName(
            BuildRecipePreset preset)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerOnly:
                    return "Player";
                case BuildRecipePreset.PlayerWithContent:
                    return "Player + Content";
                case BuildRecipePreset.PlayerWithDependencies:
                    return "Full Player";
                case BuildRecipePreset.ContentOnly:
                    return "Content";
                case BuildRecipePreset.ContentWithHotUpdate:
                    return "Content + Hot";
                case BuildRecipePreset.HotUpdateOnly:
                    return "Hot Update";
                default:
                    return BuildRecipePresetCatalog.GetDisplayName(preset);
            }
        }

        private static int DrawResponsiveCommandGrid(
            IReadOnlyList<BuildInspectorCommand> commands,
            int maximumColumns = 3,
            bool expandIncompleteRow = false)
        {
            return BuildInspectorUi.IsNarrowInspector()
                ? BuildInspectorUi.DrawCommandGrid(
                    commands,
                    maximumColumns,
                    BuildInspectorUi.CompactGridCellWidth,
                    expandIncompleteRow)
                : BuildInspectorUi.DrawCommandGrid(
                    commands,
                    maximumColumns,
                    expandIncompleteRow: expandIncompleteRow);
        }

        private void DrawStandardInvocationCards()
        {
            EditorGUILayout.Space(3f);
            BuildInspectorUi.DrawSubsectionLabel("Standard Outputs");
            DrawStandardInvocationCard(BuildStepTypeIds.Player, "Player");
            DrawStandardInvocationCard(BuildStepTypeIds.AssetContent, "Asset Content");
            DrawStandardInvocationCard(BuildStepTypeIds.HotUpdate, "Hot Update");
        }

        private void DrawStandardInvocationCard(string invocationId, string label)
        {
            int index = FindSerializedInvocationIndex(invocationId);
            if (index < 0)
            {
                EditorGUILayout.HelpBox(
                    $"Canonical invocation '{invocationId}' is not present. Use Advanced DAG to inspect the custom graph.",
                    MessageType.Info);
                return;
            }

            SerializedProperty entry = recipeInvocations.GetArrayElementAtIndex(index);
            SerializedProperty enabled = entry.FindPropertyRelative("enabled");
            SerializedProperty stepTypeId = entry.FindPropertyRelative("stepTypeId");
            SerializedProperty configuration = entry.FindPropertyRelative("configuration");
            SerializedProperty incrementality = entry.FindPropertyRelative("incrementality");
            BuildStepDescriptor descriptor = FindStepDescriptor(stepTypeId.stringValue);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool missingConfiguration = enabled.boolValue
                && descriptor?.ConfigurationRequired == true
                && configuration.objectReferenceValue == null;
            BuildInspectorUi.DrawStatusRow(
                label,
                missingConfiguration
                    ? "Config required"
                    : enabled.boolValue
                        ? "Included"
                        : "Retained",
                missingConfiguration
                    ? BuildInspectorTone.Warning
                    : enabled.boolValue
                        ? BuildInspectorTone.Ready
                        : BuildInspectorTone.Disabled,
                missingConfiguration
                    ? "This included invocation requires a configuration asset before it can build."
                    : enabled.boolValue
                        ? "Included in the saved recipe."
                        : "Retained in the profile and available to focused builds, but not included in the saved recipe.");

            if (descriptor?.ConfigurationType != null)
            {
                var actions = new[]
                {
                    new BuildInspectorCommand(
                        0,
                        new GUIContent(
                            "Create",
                            $"Create and assign a {descriptor.ConfigurationType.Name} asset."),
                        role: BuildInspectorActionRole.Accessory)
                };
                BuildInspectorObjectFieldResult result =
                    BuildInspectorUi.DrawObjectFieldWithActions(
                    new GUIContent("Configuration", descriptor.ConfigurationType.Name),
                    configuration.objectReferenceValue,
                    descriptor.ConfigurationType,
                    allowSceneObjects: false,
                    actions);
                configuration.objectReferenceValue = result.Value;
                if (result.CommandId == 0)
                {
                    ShowCreateStepConfigurationMenu(
                        result.CommandRect,
                        index,
                        descriptor);
                }
            }

            EditorGUILayout.PropertyField(
                incrementality,
                new GUIContent(
                    "Incrementality",
                    "Clean or incremental policy for this invocation only."));
            EditorGUILayout.EndVertical();
        }

        private int FindSerializedInvocationIndex(string invocationId)
        {
            int found = -1;
            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                string candidate = recipeInvocations.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("invocationId")
                    .stringValue?.Trim();
                if (!string.Equals(
                        candidate,
                        invocationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (found >= 0)
                {
                    return -1;
                }

                found = index;
            }

            return found;
        }

        private void DrawRecipeSummary(BuildRecipeAnalysis analysis)
        {
            BuildInspectorUi.DrawSubsectionLabel("Compiled Summary");
            BuildInspectorUi.DrawStatusRow(
                "Current Recipe",
                GetRecipeDisplayName(analysis),
                analysis.BlockingIssues.Count == 0
                    ? BuildInspectorTone.Info
                    : BuildInspectorTone.Error);
            BuildInspectorUi.DrawStatusRow(
                "Expected Outputs",
                DescribeExpectedOutputs(analysis),
                BuildInspectorTone.Info);
            BuildInspectorUi.DrawStatusRow(
                "Compiled Execution Plan",
                analysis.ExecutionOrderInvocationIds.Count == 0
                    ? "Unavailable"
                    : string.Join("  →  ", analysis.ExecutionOrderInvocationIds),
                analysis.ExecutionOrderInvocationIds.Count == 0
                    ? BuildInspectorTone.Warning
                    : BuildInspectorTone.Ready);
        }

        private void DrawCiProfileArguments()
        {
            string ciArguments = CreateCiProfileArguments();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    new GUIContent(
                        "CI Profile",
                        "Version-controlled profile selection. CI reads the same typed configs and DAG as the Inspector without expanding the graph into command-line arguments."),
                    ciArguments);
            }

            if (BuildInspectorUi.DrawAccessoryButton(new GUIContent(
                    "Copy",
                    "Copy the short, version-controlled CI profile argument.")))
            {
                EditorGUIUtility.systemCopyBuffer = ciArguments;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string GetRecipeDisplayName(BuildRecipeAnalysis analysis)
        {
            return analysis.MatchedPreset.HasValue
                ? BuildRecipePresetCatalog.GetDisplayName(analysis.MatchedPreset.Value)
                : "Custom";
        }

        private string CreateCiProfileArguments()
        {
            string path = AssetDatabase.GetAssetPath(target)?.Replace('\\', '/');
            return string.IsNullOrWhiteSpace(path)
                ? "Save this Build Profile asset to generate CI arguments."
                : BuildCommandLineOptionNames.Profile + " " + QuoteCommandLineToken(path);
        }

        private static string QuoteCommandLineToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            bool requiresQuotes = false;
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]) || value[index] == '"')
                {
                    requiresQuotes = true;
                    break;
                }
            }

            if (!requiresQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void DrawRunActions(
            IReadOnlyList<string> errors,
            BuildRecipeAnalysis analysis,
            bool workspaceIsReady,
            IReadOnlyList<UnityEngine.Object> dirtyAssets,
            bool editorBusy)
        {
            var profile = (BuildData)target;
            string profilePath = AssetDatabase.GetAssetPath(profile);
            bool canRunBase = string.IsNullOrEmpty(catalogError)
                && workspaceIsReady
                && !editorBusy
                && dirtyAssets.Count == 0;
            bool savedRecipeReady = canRunBase && errors.Count == 0;
            BuildInspectorStatus status = GetBuildActionStatus(
                errors,
                workspaceIsReady,
                dirtyAssets,
                editorBusy);
            showBuildActions = BuildInspectorUi.DrawFoldoutHeader(
                "Build Actions",
                showBuildActions,
                BuildInspectorUi.ActionColor,
                status,
                "Run the saved recipe or a focused immutable selection without modifying this profile.");
            if (!showBuildActions)
            {
                return;
            }

            BuildInspectorUi.BeginPanel();
            BuildInspectorUi.DrawStatusRow(
                "Profile",
                string.IsNullOrWhiteSpace(profilePath) ? target.name : profilePath,
                dirtyAssets.Count == 0
                    ? BuildInspectorTone.Ready
                    : BuildInspectorTone.Warning);
            BuildInspectorUi.DrawStatusRow(
                "Active Target",
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                editorBusy ? BuildInspectorTone.Busy : BuildInspectorTone.Info);

            if (dirtyAssets.Count > 0)
            {
                BuildInspectorUi.DrawNotice(
                    "The profile or one of its configuration assets has unsaved changes. " +
                    "Builds are disabled until those assets are explicitly saved.",
                    BuildInspectorTone.Warning);
                var saveCommands = new[]
                {
                    new BuildInspectorCommand(
                        0,
                        new GUIContent(
                            "Save Build Authoring Assets",
                            "Save only this profile and its referenced dirty configuration assets."),
                        role: BuildInspectorActionRole.Primary)
                };
                if (BuildInspectorUi.DrawCommandGrid(saveCommands, maximumColumns: 1) == 0)
                {
                    SaveBuildAuthoringAssets(dirtyAssets);
                    GUIUtility.ExitGUI();
                }
            }

            BuildInspectorUi.DrawSubsectionLabel("Run Saved Recipe");
            DrawSavedRecipeButtons(analysis, savedRecipeReady);

            EditorGUILayout.Space(4f);
            BuildInspectorUi.DrawSubsectionLabel("Focused Output (Does Not Modify Profile)");
            DrawFocusedBuildButtons(
                profile,
                canRunBase,
                GetBuildActionDisabledReason(
                    errors,
                    workspaceIsReady,
                    dirtyAssets,
                    editorBusy));

            if (editorBusy)
            {
                BuildInspectorUi.DrawNotice(
                    "Build actions are disabled while Unity is compiling, updating assets, or building a Player.",
                    BuildInspectorTone.Busy);
            }
            else if (!workspaceIsReady)
            {
                BuildInspectorUi.DrawNotice(
                    "Build actions are disabled until Workspace Safety reports Clean. " +
                    "Open Workspace Health to inspect or explicitly recover durable transaction evidence.",
                    BuildInspectorTone.Warning);
            }

            BuildInspectorUi.EndPanel();
        }

        private void DrawSavedRecipeButtons(
            BuildRecipeAnalysis analysis,
            bool enabled)
        {
            if (analysis.ProducesPlayer)
            {
                int commandCount = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                    ? 3
                    : 2;
                var commands = new BuildInspectorCommand[commandCount];
                commands[0] = new BuildInspectorCommand(
                    0,
                    new GUIContent("Release", "Run the saved recipe as a Release build."),
                    enabled,
                    BuildInspectorActionRole.Primary);
                commands[1] = new BuildInspectorCommand(
                    1,
                    new GUIContent("Development", "Run the saved recipe as a Development build."),
                    enabled);
                if (commandCount == 3)
                {
                    commands[2] = new BuildInspectorCommand(
                        2,
                        new GUIContent(
                            "Export Android Project",
                            "Run a Release recipe and export an Android Gradle project."),
                        enabled);
                }

                int clicked = DrawResponsiveCommandGrid(
                    commands,
                    maximumColumns: 2,
                    expandIncompleteRow: true);
                if (clicked == 0)
                {
                    ScheduleRun(false);
                }
                else if (clicked == 1)
                {
                    ScheduleRun(true);
                }
                else if (clicked == 2)
                {
                    ScheduleRun(
                        false,
                        exportAndroidProject: true);
                }
            }
            else
            {
                var commands = new[]
                {
                    new BuildInspectorCommand(
                        0,
                        new GUIContent(
                            "Build Saved Recipe",
                            "Run every enabled invocation in the saved recipe."),
                        enabled,
                        BuildInspectorActionRole.Primary)
                };
                if (BuildInspectorUi.DrawCommandGrid(commands, maximumColumns: 1) == 0)
                {
                    ScheduleRun(false);
                }
            }
        }

        private void DrawFocusedBuildButtons(
            BuildData profile,
            bool canRunBase,
            string baseDisabledReason)
        {
            IReadOnlyList<BuildRecipeInvocation> authored =
                GetSerializedRecipeInvocations();
            var selections = new IReadOnlyList<string>[FocusedPresetOrder.Length];
            var commands = new BuildInspectorCommand[FocusedPresetOrder.Length];
            for (int index = 0; index < FocusedPresetOrder.Length; index++)
            {
                BuildRecipePreset preset = FocusedPresetOrder[index];
                IReadOnlyList<BuildRecipeTemplate> templates =
                    BuildRecipePresetCatalog.GetTemplates(preset);
                bool available = TryResolveFocusedInvocationIds(
                    authored,
                    templates,
                    out IReadOnlyList<string> selectedInvocationIds,
                    out string reason);
                selections[index] = selectedInvocationIds;
                BuildRecipeAnalysis focusedAnalysis = BuildRecipePresetCatalog.Analyze(
                    authored,
                    selectedInvocationIds);
                IReadOnlyList<string> focusedErrors = ValidateSerializedProfile(
                    focusedAnalysis,
                    selectedInvocationIds);
                if (focusedErrors.Count > 0)
                {
                    available = false;
                    reason = string.Join("\n", focusedErrors);
                }

                IReadOnlyList<UnityEngine.Object> focusedDirtyAssets =
                    BuildAuthoringAssetGuard.GetDirtyAssets(profile, selectedInvocationIds);
                if (focusedDirtyAssets.Count > 0)
                {
                    available = false;
                    reason = "Save the profile and selected step configuration assets before building.";
                }

                available &= canRunBase;
                if (!canRunBase && !string.IsNullOrWhiteSpace(baseDisabledReason))
                {
                    reason = baseDisabledReason;
                }

                string tooltip = BuildRecipePresetCatalog.GetDescription(preset);
                if (!available && !string.IsNullOrWhiteSpace(reason))
                {
                    tooltip += "\n\n" + reason;
                }

                commands[index] = new BuildInspectorCommand(
                    index,
                    new GUIContent(
                        BuildRecipePresetCatalog.GetDisplayName(preset),
                        tooltip),
                    available);
            }

            int clicked = DrawResponsiveCommandGrid(
                commands,
                expandIncompleteRow: true);
            if (clicked >= 0 && clicked < selections.Length)
            {
                ScheduleRun(
                    debug: false,
                    invocationIdsOverride: selections[clicked]);
            }
            DrawFocusedInvocationSelector(
                profile,
                authored,
                canRunBase,
                baseDisabledReason);
        }

        private void DrawFocusedInvocationSelector(
            BuildData profile,
            IReadOnlyList<BuildRecipeInvocation> authored,
            bool canRunBase,
            string baseDisabledReason)
        {
            BuildRecipeInvocation[] candidates = authored
                .Where(invocation => invocation != null
                    && !string.IsNullOrWhiteSpace(invocation.InvocationId)
                    && !string.Equals(
                        invocation.StepTypeId,
                        BuildStepTypeIds.Player,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(3f);
            BuildInspectorUi.DrawSubsectionLabel("Exact Invocation");
            string[] labels = candidates.Select(invocation =>
            {
                BuildStepDescriptor descriptor = FindStepDescriptor(invocation.StepTypeId);
                string stepName = descriptor?.DisplayName ?? invocation.StepTypeId;
                return invocation.InvocationId + " — " + stepName
                    + (invocation.Enabled ? string.Empty : " (retained)");
            }).ToArray();
            focusedInvocationIndex = Mathf.Clamp(
                focusedInvocationIndex,
                0,
                candidates.Length - 1);
            focusedInvocationIndex = EditorGUILayout.Popup(
                "Invocation",
                focusedInvocationIndex,
                labels);

            BuildRecipeInvocation selected = candidates[focusedInvocationIndex];
            bool available = BuildRecipeSelection.TryExpandRequiredClosure(
                authored,
                new[] { selected.InvocationId },
                out IReadOnlyList<string> selectedInvocationIds,
                out string reason);
            BuildRecipeAnalysis focusedAnalysis = BuildRecipePresetCatalog.Analyze(
                authored,
                selectedInvocationIds);
            IReadOnlyList<string> focusedErrors = ValidateSerializedProfile(
                focusedAnalysis,
                selectedInvocationIds);
            if (focusedErrors.Count > 0)
            {
                available = false;
                reason = string.Join("\n", focusedErrors);
            }

            if (BuildAuthoringAssetGuard.GetDirtyAssets(
                    profile,
                    selectedInvocationIds).Count > 0)
            {
                available = false;
                reason = "Save the profile and selected step configuration assets before building.";
            }

            available &= canRunBase;
            if (!canRunBase && !string.IsNullOrWhiteSpace(baseDisabledReason))
            {
                reason = baseDisabledReason;
            }

            var commands = new[]
            {
                new BuildInspectorCommand(
                    0,
                    new GUIContent(
                        "Build Selected Invocation",
                        available
                            ? "Build exactly this invocation plus its transitive Required dependencies. If Selected dependencies remain optional."
                            : reason),
                    available,
                    BuildInspectorActionRole.Primary)
            };
            if (BuildInspectorUi.DrawCommandGrid(commands, maximumColumns: 1) == 0)
            {
                ScheduleRun(
                    debug: false,
                    invocationIdsOverride: selectedInvocationIds);
            }
        }

        private BuildInspectorStatus GetBuildActionStatus(
            IReadOnlyList<string> errors,
            bool workspaceIsReady,
            IReadOnlyList<UnityEngine.Object> dirtyAssets,
            bool editorBusy)
        {
            if (editorBusy)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Busy, "BUSY");
            }

            if (!workspaceIsReady || !string.IsNullOrEmpty(catalogError))
            {
                return new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED");
            }

            if (dirtyAssets.Count > 0)
            {
                return new BuildInspectorStatus(BuildInspectorTone.Warning, "SAVE REQUIRED");
            }

            if (errors.Count > 0)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Error,
                    $"{errors.Count} ISSUE{(errors.Count == 1 ? string.Empty : "S")}");
            }

            return new BuildInspectorStatus(BuildInspectorTone.Ready, "READY");
        }

        private string GetBuildActionDisabledReason(
            IReadOnlyList<string> errors,
            bool workspaceIsReady,
            IReadOnlyList<UnityEngine.Object> dirtyAssets,
            bool editorBusy)
        {
            if (!string.IsNullOrEmpty(catalogError))
            {
                return catalogError;
            }

            if (editorBusy)
            {
                return "Unity is compiling, updating assets, or building a Player.";
            }

            if (!workspaceIsReady)
            {
                return "Workspace Safety must report Clean before starting another build.";
            }

            if (dirtyAssets.Count > 0)
            {
                return "Save the profile and referenced configuration assets before building.";
            }

            return errors.Count == 0 ? string.Empty : string.Join("\n", errors);
        }

        private static bool TryResolveFocusedInvocationIds(
            IReadOnlyList<BuildRecipeInvocation> authored,
            IReadOnlyList<BuildRecipeTemplate> templates,
            out IReadOnlyList<string> selectedInvocationIds,
            out string reason)
        {
            var roots = new List<string>(templates.Count);
            foreach (BuildRecipeTemplate template in templates)
            {
                BuildRecipeInvocation[] canonical = authored
                    .Where(invocation => string.Equals(
                        invocation.InvocationId,
                        template.InvocationId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (canonical.Length > 1)
                {
                    selectedInvocationIds = Array.Empty<string>();
                    reason = $"Invocation ID '{template.InvocationId}' is duplicated.";
                    return false;
                }

                BuildRecipeInvocation selected = canonical.SingleOrDefault();
                if (selected != null
                    && !string.Equals(
                        selected.StepTypeId,
                        template.StepTypeId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedInvocationIds = Array.Empty<string>();
                    reason =
                        $"Canonical invocation '{template.InvocationId}' is assigned to step type " +
                        $"'{selected.StepTypeId}' instead of '{template.StepTypeId}'. Fix it in Advanced DAG.";
                    return false;
                }

                if (selected == null)
                {
                    BuildRecipeInvocation[] typeMatches = authored
                        .Where(invocation => string.Equals(
                            invocation.StepTypeId,
                            template.StepTypeId,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (typeMatches.Length != 1)
                    {
                        selectedInvocationIds = Array.Empty<string>();
                        reason = typeMatches.Length == 0
                            ? $"No '{template.StepTypeId}' invocation is configured."
                            : $"More than one '{template.StepTypeId}' invocation is configured. Use Exact Invocation to choose one safely.";
                        return false;
                    }

                    selected = typeMatches[0];
                }

                roots.Add(selected.InvocationId);
            }

            return BuildRecipeSelection.TryExpandRequiredClosure(
                authored,
                roots,
                out selectedInvocationIds,
                out reason);
        }

        private void ScheduleRun(
            bool debug,
            bool exportAndroidProject = false,
            IReadOnlyList<string> invocationIdsOverride = null)
        {
            serializedObject.ApplyModifiedProperties();
            var profile = (BuildData)target;
            try
            {
                BuildAuthoringAssetGuard.EnsureSaved(profile, invocationIdsOverride);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Unsaved Build Configuration",
                    exception.Message,
                    "OK");
                return;
            }

            string[] selectedInvocations = null;
            if (invocationIdsOverride != null)
            {
                selectedInvocations = new string[invocationIdsOverride.Count];
                for (int index = 0; index < invocationIdsOverride.Count; index++)
                {
                    selectedInvocations[index] = invocationIdsOverride[index];
                }
            }

            BuildTarget buildTarget = exportAndroidProject
                ? BuildTarget.Android
                : EditorUserBuildSettings.activeBuildTarget;
            EditorApplication.delayCall += () =>
            {
                if (profile == null)
                {
                    return;
                }

                try
                {
                    BuildEntryPoints.RunProfile(
                        profile,
                        buildTarget,
                        debug,
                        exportAndroidProject,
                        selectedInvocations);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };

            GUIUtility.ExitGUI();
        }

        private static void SaveBuildAuthoringAssets(
            IReadOnlyList<UnityEngine.Object> dirtyAssets)
        {
            for (int index = 0; index < dirtyAssets.Count; index++)
            {
                UnityEngine.Object asset = dirtyAssets[index];
                if (asset != null)
                {
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
        }

        private IReadOnlyList<BuildRecipeInvocation> GetSerializedRecipeInvocations()
        {
            if (TryGetRecipeBudgetViolation(out string violation))
            {
                throw new InvalidOperationException(violation);
            }

            var invocations = new BuildRecipeInvocation[recipeInvocations.arraySize];
            for (int index = 0; index < recipeInvocations.arraySize; index++)
            {
                SerializedProperty entry = recipeInvocations.GetArrayElementAtIndex(index);
                SerializedProperty dependencies = entry.FindPropertyRelative("dependencies");
                var dependencyValues = new BuildInvocationDependency[dependencies.arraySize];
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.arraySize;
                     dependencyIndex++)
                {
                    SerializedProperty dependency =
                        dependencies.GetArrayElementAtIndex(dependencyIndex);
                    dependencyValues[dependencyIndex] = new BuildInvocationDependency(
                        dependency.FindPropertyRelative("invocationId").stringValue,
                        (BuildDependencyMode)dependency.FindPropertyRelative("mode").enumValueIndex);
                }

                invocations[index] = new BuildRecipeInvocation(
                    entry.FindPropertyRelative("invocationId").stringValue,
                    entry.FindPropertyRelative("stepTypeId").stringValue,
                    entry.FindPropertyRelative("enabled").boolValue,
                    entry.FindPropertyRelative("configuration").objectReferenceValue
                        as ScriptableObject,
                    (BuildIncrementality)entry.FindPropertyRelative("incrementality").enumValueIndex,
                    dependencyValues);
            }

            return Array.AsReadOnly(invocations);
        }

        private static string DescribeExpectedOutputs(BuildRecipeAnalysis analysis)
        {
            var outputs = new List<string>(4);
            if (analysis.ProducesHotUpdate)
            {
                outputs.Add("Hot-update DLLs");
            }

            if (analysis.ProducesAssetContent)
            {
                outputs.Add("Asset Content");
            }

            if (analysis.ProducesPlayer)
            {
                outputs.Add("Player");
            }

            if (analysis.IncludesCustomSteps)
            {
                outputs.Add("Custom step outputs");
            }

            return outputs.Count == 0 ? "None" : string.Join(", ", outputs);
        }
    }
}
