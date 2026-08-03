using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private BuildRecipeAnalysis DrawPipelineRecipe()
        {
            DrawSectionHeader("Build Recipe");
            EditorGUILayout.HelpBox(
                "Apply a safe preset or compose registered steps manually. Presets replace only the ordered step list and support Undo; " +
                "the saved step IDs remain the single source of truth for the Inspector, menu commands, and CI.",
                MessageType.None);

            DrawRecipePresets();
            stepList?.DoLayoutList();
            string[] stepIds = GetSerializedStepIds();
            BuildRecipeAnalysis analysis = BuildRecipePresetCatalog.Analyze(
                stepIds,
                useHybridCLR.boolValue,
                !string.IsNullOrWhiteSpace(assetContentProviderId.stringValue));
            DrawRecipeSummary(analysis, stepIds);
            return analysis;
        }

        private void DrawRecipePresets()
        {
            bool useHybridClr = useHybridCLR.boolValue;
            bool hasHybridClrConfiguration = hybridCLRBuildConfig.objectReferenceValue != null;
            bool hasProvider = !string.IsNullOrWhiteSpace(assetContentProviderId.stringValue);
            bool hasProviderConfiguration = assetContentConfiguration.objectReferenceValue != null;

            EditorGUILayout.LabelField("Quick Presets", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawRecipePresetButton(
                BuildRecipePreset.PlayerWithDependencies,
                enabled: true,
                unavailableReason: string.Empty);

            bool contentEnabled = BuildRecipePresetCatalog.CanApply(
                BuildRecipePreset.ContentWithDependencies,
                useHybridClr,
                hasHybridClrConfiguration,
                hasProvider,
                hasProviderConfiguration,
                out string contentReason);
            DrawRecipePresetButton(
                BuildRecipePreset.ContentWithDependencies,
                contentEnabled,
                contentReason);

            bool hotUpdateEnabled = BuildRecipePresetCatalog.CanApply(
                BuildRecipePreset.HotUpdateOnly,
                useHybridClr,
                hasHybridClrConfiguration,
                hasProvider,
                hasProviderConfiguration,
                out string hotUpdateReason);
            DrawRecipePresetButton(
                BuildRecipePreset.HotUpdateOnly,
                hotUpdateEnabled,
                hotUpdateReason);
            EditorGUILayout.EndHorizontal();

            var unavailable = new List<string>(2);
            if (!contentEnabled)
            {
                unavailable.Add("Content: " + contentReason);
            }

            if (!hotUpdateEnabled)
            {
                unavailable.Add("Hot Update: " + hotUpdateReason);
            }

            if (unavailable.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", unavailable), MessageType.None);
            }
        }

        private void DrawRecipePresetButton(
            BuildRecipePreset preset,
            bool enabled,
            string unavailableReason)
        {
            string tooltip = BuildRecipePresetCatalog.GetDescription(preset);
            if (!enabled && !string.IsNullOrWhiteSpace(unavailableReason))
            {
                tooltip += "\n\n" + unavailableReason;
            }

            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(new GUIContent(
                        BuildRecipePresetCatalog.GetDisplayName(preset),
                        tooltip)))
                {
                    ApplyRecipePreset(preset);
                }
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

        private void DrawRecipeSummary(
            BuildRecipeAnalysis analysis,
            IReadOnlyList<string> stepIds)
        {
            string recipeName = analysis.MatchedPreset.HasValue
                ? BuildRecipePresetCatalog.GetDisplayName(analysis.MatchedPreset.Value)
                : "Custom";
            EditorGUILayout.LabelField("Current Recipe", recipeName);
            EditorGUILayout.LabelField("Expected Outputs", DescribeExpectedOutputs(analysis));

            var skipped = new List<string>(2);
            if (analysis.IncludesHotUpdate && !analysis.ProducesHotUpdate)
            {
                skipped.Add("Hot Update (HybridCLR disabled)");
            }

            if (analysis.IncludesAssetContent && !analysis.ProducesAssetContent)
            {
                skipped.Add("Asset Content (no provider)");
            }

            if (skipped.Count > 0)
            {
                EditorGUILayout.LabelField("Inactive Steps", string.Join(", ", skipped));
            }

            string ciOverride = BuildCommandLineOptionNames.Steps + " " +
                string.Join(",", stepIds);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    new GUIContent("CI Override", "Equivalent command-line step override."),
                    ciOverride);
            }

            if (GUILayout.Button("Copy", GUILayout.Width(52f)))
            {
                EditorGUIUtility.systemCopyBuffer = ciOverride;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRunActions(
            IReadOnlyList<string> errors,
            BuildRecipeAnalysis analysis)
        {
            DrawSectionHeader("Run This Recipe");
            string profilePath = AssetDatabase.GetAssetPath(target);
            EditorGUILayout.LabelField(
                "Profile",
                string.IsNullOrWhiteSpace(profilePath) ? target.name : profilePath);
            EditorGUILayout.LabelField(
                "Active Target",
                EditorUserBuildSettings.activeBuildTarget.ToString());

            bool editorBusy = EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || UnityEditor.BuildPipeline.isBuildingPlayer;
            bool canRun = errors.Count == 0
                && string.IsNullOrEmpty(catalogError)
                && !editorBusy;

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (analysis.ProducesPlayer)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Release (Clean)"))
                    {
                        ScheduleRun(
                            debug: false,
                            incrementality: BuildIncrementality.Clean);
                    }

                    if (GUILayout.Button("Release (Incremental)"))
                    {
                        ScheduleRun(
                            debug: false,
                            incrementality: BuildIncrementality.Incremental);
                    }

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Development (Clean)"))
                    {
                        ScheduleRun(
                            debug: true,
                            incrementality: BuildIncrementality.Clean);
                    }

                    if (GUILayout.Button("Development (Incremental)"))
                    {
                        ScheduleRun(
                            debug: true,
                            incrementality: BuildIncrementality.Incremental);
                    }

                    EditorGUILayout.EndHorizontal();

                    if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                        && GUILayout.Button("Export Android Gradle Project (Clean Release)"))
                    {
                        ScheduleRun(
                            debug: false,
                            incrementality: BuildIncrementality.Clean,
                            exportAndroidProject: true);
                    }
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Clean Build"))
                    {
                        ScheduleRun(
                            debug: false,
                            incrementality: BuildIncrementality.Clean);
                    }

                    if (GUILayout.Button("Incremental Build"))
                    {
                        ScheduleRun(
                            debug: false,
                            incrementality: BuildIncrementality.Incremental);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            if (editorBusy)
            {
                EditorGUILayout.HelpBox(
                    "Build actions are disabled while Unity is compiling, updating assets, or building a Player.",
                    MessageType.Warning);
            }
            else if (!analysis.ProducesPlayer)
            {
                EditorGUILayout.HelpBox(
                    "This recipe does not build a Player. Development Player options do not apply, so the quick actions use Release mode.",
                    MessageType.Info);
            }

            EditorGUILayout.HelpBox(
                "The displayed Build Profile is saved before execution, then the shared pipeline Runner starts after the current Inspector event completes.",
                MessageType.None);
        }

        private void ScheduleRun(
            bool debug,
            BuildIncrementality incrementality,
            bool exportAndroidProject = false)
        {
            serializedObject.ApplyModifiedProperties();
            var profile = (BuildData)target;
            string profilePath = AssetDatabase.GetAssetPath(profile);
            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                AssetDatabase.SaveAssetIfDirty(profile);
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
                        incrementality,
                        exportAndroidProject);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };

            GUIUtility.ExitGUI();
        }

        private string[] GetSerializedStepIds()
        {
            var ids = new string[pipelineSteps.arraySize];
            for (int index = 0; index < pipelineSteps.arraySize; index++)
            {
                ids[index] = pipelineSteps.GetArrayElementAtIndex(index).stringValue?.Trim()
                    ?? string.Empty;
            }

            return ids;
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
