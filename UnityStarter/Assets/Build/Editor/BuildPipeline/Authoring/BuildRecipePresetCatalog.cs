using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    internal enum BuildRecipePreset
    {
        PlayerWithDependencies,
        ContentWithDependencies,
        HotUpdateOnly
    }

    internal sealed class BuildRecipeAnalysis
    {
        internal BuildRecipeAnalysis(
            BuildRecipePreset? matchedPreset,
            bool includesPlayer,
            bool includesAssetContent,
            bool includesHotUpdate,
            bool includesCustomSteps,
            bool producesAssetContent,
            bool producesHotUpdate,
            IReadOnlyList<string> blockingIssues)
        {
            MatchedPreset = matchedPreset;
            IncludesPlayer = includesPlayer;
            IncludesAssetContent = includesAssetContent;
            IncludesHotUpdate = includesHotUpdate;
            IncludesCustomSteps = includesCustomSteps;
            ProducesAssetContent = producesAssetContent;
            ProducesHotUpdate = producesHotUpdate;
            BlockingIssues = blockingIssues ?? Array.Empty<string>();
        }

        public BuildRecipePreset? MatchedPreset { get; }
        public bool IncludesPlayer { get; }
        public bool IncludesAssetContent { get; }
        public bool IncludesHotUpdate { get; }
        public bool IncludesCustomSteps { get; }
        public bool ProducesPlayer => IncludesPlayer;
        public bool ProducesAssetContent { get; }
        public bool ProducesHotUpdate { get; }
        public IReadOnlyList<string> BlockingIssues { get; }
        public bool IsReady => BlockingIssues.Count == 0;
    }

    internal static class BuildRecipePresetCatalog
    {
        private static readonly string[] PlayerWithDependencies =
        {
            BuildStepIds.HotUpdate,
            BuildStepIds.AssetContent,
            BuildStepIds.Player
        };

        private static readonly string[] ContentWithDependencies =
        {
            BuildStepIds.HotUpdate,
            BuildStepIds.AssetContent
        };

        private static readonly string[] HotUpdateOnly =
        {
            BuildStepIds.HotUpdate
        };

        public static string GetDisplayName(BuildRecipePreset preset)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerWithDependencies:
                    return "Player + Dependencies";
                case BuildRecipePreset.ContentWithDependencies:
                    return "Content + Dependencies";
                case BuildRecipePreset.HotUpdateOnly:
                    return "Hot Update Only";
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public static string GetDescription(BuildRecipePreset preset)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerWithDependencies:
                    return "Build the Player and every configured optional dependency. Disabled capabilities are skipped.";
                case BuildRecipePreset.ContentWithDependencies:
                    return "Build asset-content packages without a Player. HybridCLR output is included when enabled.";
                case BuildRecipePreset.HotUpdateOnly:
                    return "Build HybridCLR hot-update and AOT metadata outputs without content packages or a Player.";
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public static string[] GetStepIds(BuildRecipePreset preset)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerWithDependencies:
                    return (string[])PlayerWithDependencies.Clone();
                case BuildRecipePreset.ContentWithDependencies:
                    return (string[])ContentWithDependencies.Clone();
                case BuildRecipePreset.HotUpdateOnly:
                    return (string[])HotUpdateOnly.Clone();
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public static bool CanApply(
            BuildRecipePreset preset,
            bool useHybridClr,
            bool hasHybridClrConfiguration,
            bool hasAssetContentProvider,
            bool hasAssetContentConfiguration,
            out string reason)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerWithDependencies:
                    reason = string.Empty;
                    return true;
                case BuildRecipePreset.ContentWithDependencies:
                    if (!hasAssetContentProvider)
                    {
                        reason = "Select an Asset Content Provider before applying the Content preset.";
                        return false;
                    }

                    if (!hasAssetContentConfiguration)
                    {
                        reason = "Assign the selected provider's Configuration before applying the Content preset.";
                        return false;
                    }

                    if (useHybridClr && !hasHybridClrConfiguration)
                    {
                        reason = "Assign a HybridCLR Build Config because HybridCLR is enabled.";
                        return false;
                    }

                    reason = string.Empty;
                    return true;
                case BuildRecipePreset.HotUpdateOnly:
                    if (!useHybridClr)
                    {
                        reason = "Enable HybridCLR before applying the Hot Update preset.";
                        return false;
                    }

                    if (!hasHybridClrConfiguration)
                    {
                        reason = "Assign a HybridCLR Build Config before applying the Hot Update preset.";
                        return false;
                    }

                    reason = string.Empty;
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public static bool TryIdentify(IReadOnlyList<string> stepIds, out BuildRecipePreset preset)
        {
            if (Matches(stepIds, PlayerWithDependencies))
            {
                preset = BuildRecipePreset.PlayerWithDependencies;
                return true;
            }

            if (Matches(stepIds, ContentWithDependencies))
            {
                preset = BuildRecipePreset.ContentWithDependencies;
                return true;
            }

            if (Matches(stepIds, HotUpdateOnly))
            {
                preset = BuildRecipePreset.HotUpdateOnly;
                return true;
            }

            preset = default;
            return false;
        }

        public static BuildRecipeAnalysis Analyze(
            IReadOnlyList<string> stepIds,
            bool useHybridClr,
            bool hasAssetContentProvider)
        {
            IReadOnlyList<string> ids = stepIds ?? Array.Empty<string>();
            bool includesPlayer = Contains(ids, BuildStepIds.Player);
            bool includesAssetContent = Contains(ids, BuildStepIds.AssetContent);
            bool includesHotUpdate = Contains(ids, BuildStepIds.HotUpdate);
            bool includesCustomSteps = ids.Any(id =>
                !string.IsNullOrWhiteSpace(id)
                && !IsBuiltInStep(id));
            bool producesAssetContent = includesAssetContent && hasAssetContentProvider;
            bool producesHotUpdate = includesHotUpdate && useHybridClr;
            var blockingIssues = new List<string>();

            if (useHybridClr
                && (includesAssetContent || includesPlayer)
                && !includesHotUpdate)
            {
                blockingIssues.Add(
                    $"The recipe requires '{BuildStepIds.HotUpdate}' because HybridCLR is enabled.");
            }

            if (hasAssetContentProvider
                && includesPlayer
                && !includesAssetContent)
            {
                blockingIssues.Add(
                    $"The recipe requires '{BuildStepIds.AssetContent}' because a content provider is configured.");
            }

            if (includesAssetContent
                && !hasAssetContentProvider
                && !includesPlayer)
            {
                blockingIssues.Add(
                    "Asset Content is requested, but no Asset Content Provider is configured.");
            }

            bool hasPotentialOutput = includesPlayer
                || producesAssetContent
                || producesHotUpdate
                || includesCustomSteps;
            if (ids.Count > 0 && !hasPotentialOutput && blockingIssues.Count == 0)
            {
                blockingIssues.Add(
                    "The recipe has no step that can currently produce an output. Enable its capability or choose another recipe.");
            }

            BuildRecipePreset? matchedPreset = TryIdentify(ids, out BuildRecipePreset identified)
                ? identified
                : (BuildRecipePreset?)null;
            return new BuildRecipeAnalysis(
                matchedPreset,
                includesPlayer,
                includesAssetContent,
                includesHotUpdate,
                includesCustomSteps,
                producesAssetContent,
                producesHotUpdate,
                blockingIssues.ToArray());
        }

        public static bool Contains(IReadOnlyList<string> stepIds, string expectedId)
        {
            if (stepIds == null || string.IsNullOrWhiteSpace(expectedId))
            {
                return false;
            }

            for (int index = 0; index < stepIds.Count; index++)
            {
                if (string.Equals(
                        stepIds[index]?.Trim(),
                        expectedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        {
            if (actual == null || actual.Count != expected.Count)
            {
                return false;
            }

            for (int index = 0; index < actual.Count; index++)
            {
                if (!string.Equals(
                        actual[index]?.Trim(),
                        expected[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBuiltInStep(string stepId)
        {
            return string.Equals(stepId.Trim(), BuildStepIds.HotUpdate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stepId.Trim(), BuildStepIds.AssetContent, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stepId.Trim(), BuildStepIds.Player, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class BuildRecipePresetAuthoring
    {
        private const string PipelineStepsPropertyName = "pipelineSteps";

        public static bool Apply(BuildData profile, BuildRecipePreset preset)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!BuildRecipePresetCatalog.CanApply(
                    preset,
                    profile.UseHybridCLR,
                    profile.HybridCLRBuildConfig != null,
                    !string.IsNullOrWhiteSpace(profile.AssetContentProviderId),
                    profile.AssetContentConfiguration != null,
                    out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            string[] desired = BuildRecipePresetCatalog.GetStepIds(preset);
            if (profile.PipelineSteps.SequenceEqual(desired, StringComparer.Ordinal))
            {
                return false;
            }

            var serializedProfile = new SerializedObject(profile);
            SerializedProperty steps = serializedProfile.FindProperty(PipelineStepsPropertyName);
            if (steps == null)
            {
                throw new InvalidOperationException(
                    $"BuildData serialized property '{PipelineStepsPropertyName}' was not found.");
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Build Recipe Preset");
            try
            {
                steps.arraySize = desired.Length;
                for (int index = 0; index < desired.Length; index++)
                {
                    steps.GetArrayElementAtIndex(index).stringValue = desired[index];
                }

                return serializedProfile.ApplyModifiedProperties();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
