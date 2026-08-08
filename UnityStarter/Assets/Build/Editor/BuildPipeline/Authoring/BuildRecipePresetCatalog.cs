using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal enum BuildRecipePreset
    {
        PlayerOnly,
        PlayerWithContent,
        PlayerWithDependencies,
        ContentOnly,
        ContentWithHotUpdate,
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
            IReadOnlyList<string> executionOrderInvocationIds,
            IReadOnlyList<string> blockingIssues)
        {
            MatchedPreset = matchedPreset;
            IncludesPlayer = includesPlayer;
            IncludesAssetContent = includesAssetContent;
            IncludesHotUpdate = includesHotUpdate;
            IncludesCustomSteps = includesCustomSteps;
            ExecutionOrderInvocationIds = executionOrderInvocationIds
                ?? Array.Empty<string>();
            BlockingIssues = blockingIssues ?? Array.Empty<string>();
        }

        public BuildRecipePreset? MatchedPreset { get; }
        public bool IncludesPlayer { get; }
        public bool IncludesAssetContent { get; }
        public bool IncludesHotUpdate { get; }
        public bool IncludesCustomSteps { get; }
        public bool ProducesPlayer => IncludesPlayer;
        public bool ProducesAssetContent => IncludesAssetContent;
        public bool ProducesHotUpdate => IncludesHotUpdate;
        public IReadOnlyList<string> ExecutionOrderInvocationIds { get; }
        public IReadOnlyList<string> BlockingIssues { get; }
        public bool IsReady => BlockingIssues.Count == 0;
    }

    internal sealed class BuildRecipeTemplate
    {
        public BuildRecipeTemplate(
            string invocationId,
            string stepTypeId,
            params BuildInvocationDependency[] dependencies)
        {
            InvocationId = invocationId;
            StepTypeId = stepTypeId;
            Dependencies = dependencies ?? Array.Empty<BuildInvocationDependency>();
        }

        public string InvocationId { get; }
        public string StepTypeId { get; }
        public IReadOnlyList<BuildInvocationDependency> Dependencies { get; }
    }

    internal static class BuildRecipePresetCatalog
    {
        private static readonly IReadOnlyDictionary<BuildRecipePreset, BuildRecipeTemplate[]>
            Templates = new Dictionary<BuildRecipePreset, BuildRecipeTemplate[]>
            {
                {
                    BuildRecipePreset.PlayerOnly,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.Player)
                    }
                },
                {
                    BuildRecipePreset.PlayerWithContent,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.AssetContent),
                        Invocation(
                            BuildStepTypeIds.Player,
                            IfSelected(BuildStepTypeIds.AssetContent))
                    }
                },
                {
                    BuildRecipePreset.PlayerWithDependencies,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.HotUpdate),
                        Invocation(
                            BuildStepTypeIds.AssetContent,
                            IfSelected(BuildStepTypeIds.HotUpdate)),
                        Invocation(
                            BuildStepTypeIds.Player,
                            IfSelected(BuildStepTypeIds.HotUpdate),
                            IfSelected(BuildStepTypeIds.AssetContent))
                    }
                },
                {
                    BuildRecipePreset.ContentOnly,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.AssetContent)
                    }
                },
                {
                    BuildRecipePreset.ContentWithHotUpdate,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.HotUpdate),
                        Invocation(
                            BuildStepTypeIds.AssetContent,
                            IfSelected(BuildStepTypeIds.HotUpdate))
                    }
                },
                {
                    BuildRecipePreset.HotUpdateOnly,
                    new[]
                    {
                        Invocation(BuildStepTypeIds.HotUpdate)
                    }
                }
            };

        public static string GetDisplayName(BuildRecipePreset preset)
        {
            switch (preset)
            {
                case BuildRecipePreset.PlayerOnly:
                    return "Player Only";
                case BuildRecipePreset.PlayerWithContent:
                    return "Player + Content";
                case BuildRecipePreset.PlayerWithDependencies:
                    return "Full Player";
                case BuildRecipePreset.ContentOnly:
                    return "Content Only";
                case BuildRecipePreset.ContentWithHotUpdate:
                    return "Content + Hot Update";
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
                case BuildRecipePreset.PlayerOnly:
                    return "Build only the Unity Player.";
                case BuildRecipePreset.PlayerWithContent:
                    return "Build asset content and then the Unity Player without hot-update output.";
                case BuildRecipePreset.PlayerWithDependencies:
                    return "Build hot-update assemblies, asset content, and then the Player.";
                case BuildRecipePreset.ContentOnly:
                    return "Build asset content without hot-update output or a Player.";
                case BuildRecipePreset.ContentWithHotUpdate:
                    return "Build hot-update output and asset content without a Player.";
                case BuildRecipePreset.HotUpdateOnly:
                    return "Build only hot-update and AOT metadata outputs.";
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public static IReadOnlyList<BuildRecipeTemplate> GetTemplates(
            BuildRecipePreset preset)
        {
            if (!Templates.TryGetValue(preset, out BuildRecipeTemplate[] templates))
            {
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }

            return Array.AsReadOnly((BuildRecipeTemplate[])templates.Clone());
        }

        public static string[] GetInvocationIds(BuildRecipePreset preset)
        {
            return GetTemplates(preset)
                .Select(template => template.InvocationId)
                .ToArray();
        }

        public static bool CanApply(
            BuildData profile,
            BuildRecipePreset preset,
            out string reason)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            IReadOnlyList<BuildRecipeInvocation> authored = profile.RecipeInvocations;
            foreach (BuildRecipeTemplate template in GetTemplates(preset))
            {
                int canonicalMatches = authored.Count(invocation => string.Equals(
                    invocation.InvocationId,
                    template.InvocationId,
                    StringComparison.OrdinalIgnoreCase));
                if (canonicalMatches > 1)
                {
                    reason = $"Invocation ID '{template.InvocationId}' is duplicated. Fix the recipe before applying a preset.";
                    return false;
                }

                if (canonicalMatches == 0
                    && authored.Count(invocation => string.Equals(
                        invocation.StepTypeId,
                        template.StepTypeId,
                        StringComparison.OrdinalIgnoreCase)) > 1)
                {
                    reason =
                        $"More than one '{template.StepTypeId}' invocation could supply canonical ID " +
                        $"'{template.InvocationId}'. Re-key the intended invocation in Advanced DAG before applying this preset.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public static BuildRecipeAnalysis Analyze(
            IReadOnlyList<BuildRecipeInvocation> authoredInvocations,
            IReadOnlyCollection<string> selectedInvocationIds = null)
        {
            IReadOnlyList<BuildRecipeInvocation> authored = authoredInvocations
                ?? Array.Empty<BuildRecipeInvocation>();
            var blockingIssues = new List<string>();
            var known = new Dictionary<string, BuildRecipeInvocation>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                string invocationId = invocation?.InvocationId?.Trim();
                if (string.IsNullOrEmpty(invocationId))
                {
                    blockingIssues.Add(
                        $"Build recipe entry at index {index} has an empty Invocation ID.");
                    continue;
                }

                if (!known.TryAdd(invocationId, invocation))
                {
                    blockingIssues.Add(
                        $"Build Invocation ID '{invocationId}' is configured more than once.");
                }
            }

            HashSet<string> explicitlySelected = selectedInvocationIds == null
                ? null
                : new HashSet<string>(
                    selectedInvocationIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.OrdinalIgnoreCase);
            if (explicitlySelected != null)
            {
                foreach (string selectedId in explicitlySelected)
                {
                    if (!known.ContainsKey(selectedId))
                    {
                        blockingIssues.Add(
                            $"Focused build references unknown invocation '{selectedId}'.");
                    }
                }
            }

            var selected = new List<BuildRecipeInvocation>(authored.Count);
            var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                string invocationId = invocation?.InvocationId?.Trim();
                bool isSelected = invocation != null
                    && (explicitlySelected == null
                        ? invocation.Enabled
                        : explicitlySelected.Contains(invocationId));
                if (isSelected && selectedIds.Add(invocationId))
                {
                    selected.Add(invocation);
                }
            }

            if (selected.Count == 0)
            {
                blockingIssues.Add("Enable or select at least one build invocation.");
            }

            var outgoing = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var incomingCount = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            foreach (BuildRecipeInvocation invocation in selected)
            {
                outgoing[invocation.InvocationId] = new List<string>();
                incomingCount[invocation.InvocationId] = 0;
            }

            foreach (BuildRecipeInvocation invocation in selected)
            {
                var dependencyIds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (BuildInvocationDependency dependency in invocation.Dependencies)
                {
                    string dependencyId = dependency?.InvocationId?.Trim();
                    if (string.IsNullOrEmpty(dependencyId))
                    {
                        blockingIssues.Add(
                            $"Build invocation '{invocation.InvocationId}' has an empty dependency target.");
                        continue;
                    }

                    if (!dependencyIds.Add(dependencyId))
                    {
                        blockingIssues.Add(
                            $"Build invocation '{invocation.InvocationId}' declares dependency '{dependencyId}' more than once.");
                        continue;
                    }

                    if (string.Equals(
                            invocation.InvocationId,
                            dependencyId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        blockingIssues.Add(
                            $"Build invocation '{invocation.InvocationId}' cannot depend on itself.");
                        continue;
                    }

                    if (!known.ContainsKey(dependencyId))
                    {
                        blockingIssues.Add(
                            $"Build invocation '{invocation.InvocationId}' references unknown dependency '{dependencyId}'.");
                        continue;
                    }

                    if (!selectedIds.Contains(dependencyId))
                    {
                        if (dependency.Mode == BuildDependencyMode.Required)
                        {
                            blockingIssues.Add(
                                $"Build invocation '{invocation.InvocationId}' requires unselected invocation '{dependencyId}'.");
                        }

                        continue;
                    }

                    outgoing[dependencyId].Add(invocation.InvocationId);
                    incomingCount[invocation.InvocationId]++;
                }
            }

            var ready = incomingCount
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var executionOrder = new List<string>(selected.Count);
            while (ready.Count > 0)
            {
                string current = ready[0];
                ready.RemoveAt(0);
                executionOrder.Add(current);
                foreach (string consumer in outgoing[current])
                {
                    incomingCount[consumer]--;
                    if (incomingCount[consumer] == 0)
                    {
                        ready.Add(consumer);
                        ready.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            if (executionOrder.Count != selected.Count)
            {
                blockingIssues.Add(
                    "Build invocation dependency cycle detected: " +
                    string.Join(", ", incomingCount
                        .Where(pair => pair.Value > 0)
                        .Select(pair => pair.Key)
                        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)) + ".");
            }

            bool includesPlayer = selected.Any(invocation => IsStepType(
                invocation.StepTypeId,
                BuildStepTypeIds.Player));
            bool includesAssetContent = selected.Any(invocation => IsStepType(
                invocation.StepTypeId,
                BuildStepTypeIds.AssetContent));
            bool includesHotUpdate = selected.Any(invocation => IsStepType(
                invocation.StepTypeId,
                BuildStepTypeIds.HotUpdate));
            bool includesCustomSteps = selected.Any(invocation =>
                !string.IsNullOrWhiteSpace(invocation.StepTypeId)
                && !IsBuiltInStepType(invocation.StepTypeId));

            BuildRecipePreset? matchedPreset = blockingIssues.Count == 0
                && TryIdentify(
                    selected,
                    selectedIds,
                out BuildRecipePreset identified)
                ? identified
                : (BuildRecipePreset?)null;
            return new BuildRecipeAnalysis(
                matchedPreset,
                includesPlayer,
                includesAssetContent,
                includesHotUpdate,
                includesCustomSteps,
                executionOrder.AsReadOnly(),
                blockingIssues);
        }

        private static bool TryIdentify(
            IReadOnlyList<BuildRecipeInvocation> selected,
            IReadOnlyCollection<string> selectedIds,
            out BuildRecipePreset preset)
        {
            foreach (BuildRecipePreset candidate in Enum.GetValues(typeof(BuildRecipePreset)))
            {
                if (Matches(selected, selectedIds, GetTemplates(candidate)))
                {
                    preset = candidate;
                    return true;
                }
            }

            preset = default;
            return false;
        }

        private static bool Matches(
            IReadOnlyList<BuildRecipeInvocation> actual,
            IReadOnlyCollection<string> selectedIds,
            IReadOnlyList<BuildRecipeTemplate> expected)
        {
            if (actual == null || actual.Count != expected.Count)
            {
                return false;
            }

            var actualById = actual.ToDictionary(
                invocation => invocation.InvocationId,
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < expected.Count; index++)
            {
                BuildRecipeTemplate template = expected[index];
                if (!actualById.TryGetValue(
                        template.InvocationId,
                        out BuildRecipeInvocation invocation)
                    || !IsStepType(invocation.StepTypeId, template.StepTypeId))
                {
                    return false;
                }

                BuildInvocationDependency[] actualEdges = invocation.Dependencies
                    .Where(dependency => dependency != null
                        && selectedIds.Contains(dependency.InvocationId))
                    .ToArray();
                if (actualEdges.Length != template.Dependencies.Count)
                {
                    return false;
                }

                foreach (BuildInvocationDependency expectedEdge in template.Dependencies)
                {
                    BuildInvocationDependency actualEdge = actualEdges.SingleOrDefault(
                        edge => string.Equals(
                            edge.InvocationId,
                            expectedEdge.InvocationId,
                            StringComparison.OrdinalIgnoreCase));
                    if (actualEdge == null || actualEdge.Mode != expectedEdge.Mode)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsStepType(string actual, string expected)
        {
            return string.Equals(
                actual?.Trim(),
                expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBuiltInStepType(string stepTypeId)
        {
            return string.Equals(stepTypeId.Trim(), BuildStepTypeIds.HotUpdate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stepTypeId.Trim(), BuildStepTypeIds.AssetContent, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stepTypeId.Trim(), BuildStepTypeIds.Player, StringComparison.OrdinalIgnoreCase);
        }

        private static BuildRecipeTemplate Invocation(
            string stepTypeId,
            params BuildInvocationDependency[] dependencies)
        {
            return new BuildRecipeTemplate(stepTypeId, stepTypeId, dependencies);
        }

        private static BuildInvocationDependency IfSelected(string invocationId)
        {
            return new BuildInvocationDependency(
                invocationId,
                BuildDependencyMode.IfSelected);
        }
    }

    internal static class BuildRecipePresetAuthoring
    {
        private const string RecipeInvocationsPropertyName = "recipeInvocations";
        private const string EnabledPropertyName = "enabled";
        private const string InvocationIdPropertyName = "invocationId";
        private const string StepTypeIdPropertyName = "stepTypeId";
        private const string ConfigurationPropertyName = "configuration";
        private const string IncrementalityPropertyName = "incrementality";
        private const string DependenciesPropertyName = "dependencies";

        public static bool Apply(BuildData profile, BuildRecipePreset preset)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!BuildRecipePresetCatalog.CanApply(profile, preset, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            IReadOnlyList<BuildRecipeInvocation> current = profile.RecipeInvocations;
            EnsureUniqueInvocationIds(current);
            IReadOnlyList<BuildRecipeTemplate> templates =
                BuildRecipePresetCatalog.GetTemplates(preset);
            var next = new List<BuildRecipeInvocation>(
                Math.Max(current.Count, templates.Count));

            for (int index = 0; index < templates.Count; index++)
            {
                BuildRecipeTemplate template = templates[index];
                BuildRecipeInvocation existing = FindByInvocationId(
                    current,
                    template.InvocationId)
                    ?? FindByStepType(current, template.StepTypeId);
                next.Add(new BuildRecipeInvocation(
                    template.InvocationId,
                    template.StepTypeId,
                    enabled: true,
                    configuration: existing?.Configuration,
                    incrementality: existing?.Incrementality
                        ?? BuildIncrementality.Clean,
                    dependencies: template.Dependencies));
            }

            var desiredIds = new HashSet<string>(
                templates.Select(template => template.InvocationId),
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < current.Count; index++)
            {
                BuildRecipeInvocation existing = current[index];
                if (desiredIds.Contains(existing.InvocationId))
                {
                    continue;
                }

                next.Add(new BuildRecipeInvocation(
                    existing.InvocationId,
                    existing.StepTypeId,
                    enabled: false,
                    configuration: existing.Configuration,
                    incrementality: existing.Incrementality,
                    dependencies: existing.Dependencies));
            }

            if (Equivalent(current, next))
            {
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Build Recipe Preset");
            Undo.RecordObject(profile, "Apply Build Recipe Preset");
            try
            {
                var serializedProfile = new SerializedObject(profile);
                SerializedProperty invocations = serializedProfile.FindProperty(
                    RecipeInvocationsPropertyName)
                    ?? throw new InvalidOperationException(
                        $"BuildData serialized property '{RecipeInvocationsPropertyName}' was not found.");
                invocations.arraySize = next.Count;
                for (int index = 0; index < next.Count; index++)
                {
                    WriteInvocation(
                        invocations.GetArrayElementAtIndex(index),
                        next[index]);
                }

                bool changed = serializedProfile.ApplyModifiedPropertiesWithoutUndo();
                Undo.FlushUndoRecordObjects();
                return changed;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static void WriteInvocation(
            SerializedProperty element,
            BuildRecipeInvocation invocation)
        {
            element.FindPropertyRelative(EnabledPropertyName).boolValue =
                invocation.Enabled;
            element.FindPropertyRelative(InvocationIdPropertyName).stringValue =
                invocation.InvocationId;
            element.FindPropertyRelative(StepTypeIdPropertyName).stringValue =
                invocation.StepTypeId;
            element.FindPropertyRelative(ConfigurationPropertyName).objectReferenceValue =
                invocation.Configuration;
            element.FindPropertyRelative(IncrementalityPropertyName).enumValueIndex =
                (int)invocation.Incrementality;

            SerializedProperty dependencies = element.FindPropertyRelative(
                DependenciesPropertyName);
            dependencies.arraySize = invocation.Dependencies.Count;
            for (int index = 0; index < invocation.Dependencies.Count; index++)
            {
                BuildInvocationDependency value = invocation.Dependencies[index];
                SerializedProperty dependency = dependencies.GetArrayElementAtIndex(index);
                dependency.FindPropertyRelative(InvocationIdPropertyName).stringValue =
                    value.InvocationId;
                dependency.FindPropertyRelative("mode").enumValueIndex =
                    (int)value.Mode;
            }
        }

        private static BuildRecipeInvocation FindByInvocationId(
            IReadOnlyList<BuildRecipeInvocation> entries,
            string invocationId)
        {
            return entries.FirstOrDefault(entry => string.Equals(
                entry.InvocationId,
                invocationId,
                StringComparison.OrdinalIgnoreCase));
        }

        private static BuildRecipeInvocation FindByStepType(
            IReadOnlyList<BuildRecipeInvocation> entries,
            string stepTypeId)
        {
            return entries
                .Where(entry => string.Equals(
                    entry.StepTypeId,
                    stepTypeId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.Enabled)
                .ThenByDescending(entry => entry.Configuration != null)
                .FirstOrDefault();
        }

        private static void EnsureUniqueInvocationIds(
            IReadOnlyList<BuildRecipeInvocation> entries)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < entries.Count; index++)
            {
                string id = entries[index]?.InvocationId?.Trim();
                if (string.IsNullOrEmpty(id) || !ids.Add(id))
                {
                    throw new InvalidOperationException(
                        "Fix empty or duplicate recipe Invocation IDs before applying a preset.");
                }
            }
        }

        private static bool Equivalent(
            IReadOnlyList<BuildRecipeInvocation> left,
            IReadOnlyList<BuildRecipeInvocation> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                BuildRecipeInvocation leftEntry = left[index];
                BuildRecipeInvocation rightEntry = right[index];
                if (leftEntry.Enabled != rightEntry.Enabled
                    || !string.Equals(leftEntry.InvocationId, rightEntry.InvocationId, StringComparison.Ordinal)
                    || !string.Equals(leftEntry.StepTypeId, rightEntry.StepTypeId, StringComparison.Ordinal)
                    || leftEntry.Configuration != rightEntry.Configuration
                    || leftEntry.Incrementality != rightEntry.Incrementality
                    || !EquivalentDependencies(
                        leftEntry.Dependencies,
                        rightEntry.Dependencies))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EquivalentDependencies(
            IReadOnlyList<BuildInvocationDependency> left,
            IReadOnlyList<BuildInvocationDependency> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(
                        left[index].InvocationId,
                        right[index].InvocationId,
                        StringComparison.Ordinal)
                    || left[index].Mode != right[index].Mode)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
