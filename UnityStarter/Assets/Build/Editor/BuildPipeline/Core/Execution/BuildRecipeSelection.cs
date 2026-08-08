using System;
using System.Collections.Generic;
using System.Linq;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Resolves exact authored roots into their transitive Required dependency
    /// closure. IfSelected edges never expand membership.
    /// </summary>
    internal static class BuildRecipeSelection
    {
        public static bool TryExpandRequiredClosure(
            IReadOnlyList<BuildRecipeInvocation> authoredInvocations,
            IReadOnlyList<string> rootInvocationIds,
            out IReadOnlyList<string> selectedInvocationIds,
            out string reason)
        {
            IReadOnlyList<BuildRecipeInvocation> authored = authoredInvocations
                ?? Array.Empty<BuildRecipeInvocation>();
            if (rootInvocationIds == null || rootInvocationIds.Count == 0)
            {
                selectedInvocationIds = Array.Empty<string>();
                reason = "Select at least one build invocation.";
                return false;
            }

            var byId = new Dictionary<string, BuildRecipeInvocation>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < authored.Count; index++)
            {
                BuildRecipeInvocation invocation = authored[index];
                string invocationId = invocation?.InvocationId?.Trim();
                if (string.IsNullOrWhiteSpace(invocationId)
                    || !byId.TryAdd(invocationId, invocation))
                {
                    selectedInvocationIds = Array.Empty<string>();
                    reason =
                        "Fix empty or duplicate Invocation IDs before selecting a focused build.";
                    return false;
                }
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            for (int index = 0; index < rootInvocationIds.Count; index++)
            {
                string rootId = rootInvocationIds[index]?.Trim();
                if (string.IsNullOrWhiteSpace(rootId) || !byId.ContainsKey(rootId))
                {
                    selectedInvocationIds = Array.Empty<string>();
                    reason =
                        $"Focused build references unknown invocation '{rootId ?? string.Empty}'.";
                    return false;
                }

                if (!requested.Add(rootId))
                {
                    selectedInvocationIds = Array.Empty<string>();
                    reason = $"Focused build selects invocation '{rootId}' more than once.";
                    return false;
                }

                if (selected.Add(rootId))
                {
                    pending.Enqueue(rootId);
                }
            }

            while (pending.Count > 0)
            {
                BuildRecipeInvocation consumer = byId[pending.Dequeue()];
                foreach (BuildInvocationDependency dependency in consumer.Dependencies)
                {
                    if (dependency == null
                        || dependency.Mode != BuildDependencyMode.Required)
                    {
                        continue;
                    }

                    string dependencyId = dependency.InvocationId?.Trim();
                    if (string.IsNullOrWhiteSpace(dependencyId)
                        || !byId.ContainsKey(dependencyId))
                    {
                        selectedInvocationIds = Array.Empty<string>();
                        reason =
                            $"Invocation '{consumer.InvocationId}' requires unknown invocation " +
                            $"'{dependencyId ?? string.Empty}'.";
                        return false;
                    }

                    if (selected.Add(dependencyId))
                    {
                        pending.Enqueue(dependencyId);
                    }
                }
            }

            selectedInvocationIds = authored
                .Where(invocation => selected.Contains(invocation.InvocationId))
                .Select(invocation => invocation.InvocationId)
                .ToArray();
            reason = string.Empty;
            return true;
        }
    }
}
