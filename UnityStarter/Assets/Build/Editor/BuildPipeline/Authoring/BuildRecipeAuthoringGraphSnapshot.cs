using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Dependency-only authoring input used to build an immutable Inspector graph snapshot.
    /// </summary>
    internal sealed class BuildRecipeAuthoringGraphNode
    {
        private readonly ReadOnlyCollection<string> dependencyIds;

        public BuildRecipeAuthoringGraphNode(
            string invocationId,
            string stepTypeId,
            IReadOnlyList<string> dependencyIds)
        {
            InvocationId = invocationId ?? string.Empty;
            StepTypeId = stepTypeId ?? string.Empty;

            int count = dependencyIds?.Count ?? 0;
            var snapshot = new string[count];
            for (int index = 0; index < count; index++)
            {
                snapshot[index] = dependencyIds[index] ?? string.Empty;
            }

            this.dependencyIds = Array.AsReadOnly(snapshot);
        }

        public string InvocationId { get; }
        public string StepTypeId { get; }
        public IReadOnlyList<string> DependencyIds => dependencyIds;
    }

    /// <summary>
    /// Immutable construction diagnostics used by structural performance tests.
    /// </summary>
    internal readonly struct BuildRecipeAuthoringGraphBuildMetrics : IEquatable<BuildRecipeAuthoringGraphBuildMetrics>
    {
        public BuildRecipeAuthoringGraphBuildMetrics(
            int sourceNodeReadCount,
            int sourceDependencyReadCount,
            int reverseReachabilityPassCount,
            int reverseReachabilityNodeVisitCount,
            int reverseReachabilityEdgeVisitCount)
        {
            SourceNodeReadCount = sourceNodeReadCount;
            SourceDependencyReadCount = sourceDependencyReadCount;
            ReverseReachabilityPassCount = reverseReachabilityPassCount;
            ReverseReachabilityNodeVisitCount = reverseReachabilityNodeVisitCount;
            ReverseReachabilityEdgeVisitCount = reverseReachabilityEdgeVisitCount;
        }

        public int SourceNodeReadCount { get; }
        public int SourceDependencyReadCount { get; }
        public int ReverseReachabilityPassCount { get; }
        public int ReverseReachabilityNodeVisitCount { get; }
        public int ReverseReachabilityEdgeVisitCount { get; }

        public bool Equals(BuildRecipeAuthoringGraphBuildMetrics other)
        {
            return SourceNodeReadCount == other.SourceNodeReadCount
                && SourceDependencyReadCount == other.SourceDependencyReadCount
                && ReverseReachabilityPassCount == other.ReverseReachabilityPassCount
                && ReverseReachabilityNodeVisitCount == other.ReverseReachabilityNodeVisitCount
                && ReverseReachabilityEdgeVisitCount == other.ReverseReachabilityEdgeVisitCount;
        }

        public override bool Equals(object obj)
        {
            return obj is BuildRecipeAuthoringGraphBuildMetrics other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceNodeReadCount;
                hash = (hash * 397) ^ SourceDependencyReadCount;
                hash = (hash * 397) ^ ReverseReachabilityPassCount;
                hash = (hash * 397) ^ ReverseReachabilityNodeVisitCount;
                hash = (hash * 397) ^ ReverseReachabilityEdgeVisitCount;
                return hash;
            }
        }
    }

    /// <summary>
    /// Immutable, case-insensitive graph index shared by every Advanced DAG row in one
    /// Inspector pass. Construction performs the serialized graph scan and reverse
    /// reachability traversal once; popup and cycle queries never rescan SerializedProperty.
    /// </summary>
    internal sealed class BuildRecipeAuthoringGraphSnapshot
    {
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        private readonly string[] invocationIds;
        private readonly string[] stepTypeIds;
        private readonly string[][] dependencyIds;
        private readonly Dictionary<string, int> firstInvocationIndex;
        private readonly Dictionary<string, int> invocationIdCounts;
        private readonly Dictionary<string, int> stepTypeCounts;
        private readonly Dictionary<string, int> totalDependencyReferenceCounts;
        private readonly Dictionary<string, int>[] directDependencyCounts;
        private readonly bool[][] candidateCanReachOwner;
        private readonly ReadOnlyCollection<string>[] cycleSafeCandidateIds;

        private BuildRecipeAuthoringGraphSnapshot(
            string[] invocationIds,
            string[] stepTypeIds,
            string[][] dependencyIds,
            Dictionary<string, int> firstInvocationIndex,
            Dictionary<string, int> invocationIdCounts,
            Dictionary<string, int> stepTypeCounts,
            Dictionary<string, int> totalDependencyReferenceCounts,
            Dictionary<string, int>[] directDependencyCounts,
            bool[][] candidateCanReachOwner,
            ReadOnlyCollection<string>[] cycleSafeCandidateIds,
            int dependencyCount,
            BuildRecipeAuthoringGraphBuildMetrics buildMetrics)
        {
            this.invocationIds = invocationIds;
            this.stepTypeIds = stepTypeIds;
            this.dependencyIds = dependencyIds;
            this.firstInvocationIndex = firstInvocationIndex;
            this.invocationIdCounts = invocationIdCounts;
            this.stepTypeCounts = stepTypeCounts;
            this.totalDependencyReferenceCounts = totalDependencyReferenceCounts;
            this.directDependencyCounts = directDependencyCounts;
            this.candidateCanReachOwner = candidateCanReachOwner;
            this.cycleSafeCandidateIds = cycleSafeCandidateIds;
            DependencyCount = dependencyCount;
            BuildMetrics = buildMetrics;
        }

        public int InvocationCount => invocationIds.Length;
        public int DependencyCount { get; }
        public BuildRecipeAuthoringGraphBuildMetrics BuildMetrics { get; }

        public static BuildRecipeAuthoringGraphSnapshot Create(
            IReadOnlyList<BuildRecipeAuthoringGraphNode> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            int nodeCount = nodes.Count;
            if (nodeCount > BuildPipelineBudgets.MaximumInvocationCount)
            {
                throw new ArgumentException(
                    $"Build recipe graph exceeds the {BuildPipelineBudgets.MaximumInvocationCount}-invocation safety budget.",
                    nameof(nodes));
            }

            var invocationIds = new string[nodeCount];
            var stepTypeIds = new string[nodeCount];
            var dependencyIds = new string[nodeCount][];
            var firstInvocationIndex = new Dictionary<string, int>(IdComparer);
            var invocationIdCounts = new Dictionary<string, int>(IdComparer);
            var stepTypeCounts = new Dictionary<string, int>(IdComparer);
            var totalDependencyReferenceCounts = new Dictionary<string, int>(IdComparer);
            var directDependencyCounts = new Dictionary<string, int>[nodeCount];
            int dependencyCount = 0;

            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                BuildRecipeAuthoringGraphNode node = nodes[nodeIndex]
                    ?? throw new ArgumentException(
                        $"Build recipe graph node at index {nodeIndex} is null.",
                        nameof(nodes));
                string invocationId = Normalize(node.InvocationId);
                string stepTypeId = Normalize(node.StepTypeId);
                invocationIds[nodeIndex] = invocationId;
                stepTypeIds[nodeIndex] = stepTypeId;
                IncrementCount(invocationIdCounts, invocationId);
                IncrementCount(stepTypeCounts, stepTypeId);
                if (!string.IsNullOrEmpty(invocationId)
                    && !firstInvocationIndex.ContainsKey(invocationId))
                {
                    firstInvocationIndex.Add(invocationId, nodeIndex);
                }

                IReadOnlyList<string> sourceDependencies = node.DependencyIds;
                if (sourceDependencies.Count
                    > BuildPipelineBudgets.MaximumDependencyEdgeCount - dependencyCount)
                {
                    throw new ArgumentException(
                        $"Build recipe graph exceeds the {BuildPipelineBudgets.MaximumDependencyEdgeCount}-edge dependency safety budget.",
                        nameof(nodes));
                }

                var normalizedDependencies = new string[sourceDependencies.Count];
                var ownerCounts = new Dictionary<string, int>(IdComparer);
                for (int dependencyIndex = 0;
                     dependencyIndex < sourceDependencies.Count;
                     dependencyIndex++)
                {
                    string dependencyId = Normalize(sourceDependencies[dependencyIndex]);
                    normalizedDependencies[dependencyIndex] = dependencyId;
                    IncrementCount(ownerCounts, dependencyId);
                    IncrementCount(totalDependencyReferenceCounts, dependencyId);
                    dependencyCount++;
                }

                dependencyIds[nodeIndex] = normalizedDependencies;
                directDependencyCounts[nodeIndex] = ownerCounts;
            }

            List<int>[] reverseAdjacency = CreateReverseAdjacency(
                invocationIds,
                dependencyIds,
                firstInvocationIndex);
            bool[][] candidateCanReachOwner = BuildReverseReachability(
                invocationIds,
                firstInvocationIndex,
                reverseAdjacency,
                out int reachabilityPasses,
                out int reachabilityNodeVisits,
                out int reachabilityEdgeVisits);
            ReadOnlyCollection<string>[] cycleSafeCandidateIds = BuildCycleSafeCandidates(
                invocationIds,
                firstInvocationIndex,
                candidateCanReachOwner);

            return new BuildRecipeAuthoringGraphSnapshot(
                invocationIds,
                stepTypeIds,
                dependencyIds,
                firstInvocationIndex,
                invocationIdCounts,
                stepTypeCounts,
                totalDependencyReferenceCounts,
                directDependencyCounts,
                candidateCanReachOwner,
                cycleSafeCandidateIds,
                dependencyCount,
                new BuildRecipeAuthoringGraphBuildMetrics(
                    nodeCount,
                    dependencyCount,
                    reachabilityPasses,
                    reachabilityNodeVisits,
                    reachabilityEdgeVisits));
        }

        public int FindInvocationIndex(string invocationId)
        {
            string normalized = Normalize(invocationId);
            return !string.IsNullOrEmpty(normalized)
                && firstInvocationIndex.TryGetValue(normalized, out int index)
                    ? index
                    : -1;
        }

        public bool MatchesInvocation(
            int invocationIndex,
            string invocationId,
            string stepTypeId,
            int dependencyCount)
        {
            return invocationIndex >= 0
                && invocationIndex < invocationIds.Length
                && string.Equals(
                    invocationIds[invocationIndex],
                    Normalize(invocationId),
                    StringComparison.Ordinal)
                && string.Equals(
                    stepTypeIds[invocationIndex],
                    Normalize(stepTypeId),
                    StringComparison.Ordinal)
                && dependencyIds[invocationIndex].Length == dependencyCount;
        }

        public bool MatchesDependency(
            int invocationIndex,
            int dependencyIndex,
            string dependencyId)
        {
            return invocationIndex >= 0
                && invocationIndex < dependencyIds.Length
                && dependencyIndex >= 0
                && dependencyIndex < dependencyIds[invocationIndex].Length
                && string.Equals(
                    dependencyIds[invocationIndex][dependencyIndex],
                    Normalize(dependencyId),
                    StringComparison.Ordinal);
        }

        public bool IsInvocationIdConfiguredAtAnotherIndex(
            string invocationId,
            int currentIndex)
        {
            return IsValueConfiguredAtAnotherIndex(
                invocationIds,
                invocationIdCounts,
                invocationId,
                currentIndex);
        }

        public bool IsStepTypeConfiguredAtAnotherIndex(
            string stepTypeId,
            int currentIndex)
        {
            return IsValueConfiguredAtAnotherIndex(
                stepTypeIds,
                stepTypeCounts,
                stepTypeId,
                currentIndex);
        }

        public int CountDependencyReferences(string invocationId, int ignoredOwnerIndex)
        {
            string normalized = Normalize(invocationId);
            if (string.IsNullOrEmpty(normalized)
                || !totalDependencyReferenceCounts.TryGetValue(normalized, out int count))
            {
                return 0;
            }

            if (ignoredOwnerIndex >= 0
                && ignoredOwnerIndex < directDependencyCounts.Length
                && directDependencyCounts[ignoredOwnerIndex].TryGetValue(
                    normalized,
                    out int ignoredCount))
            {
                count -= ignoredCount;
            }

            return count;
        }

        public bool WouldCreateDependencyCycle(int ownerIndex, string candidateId)
        {
            if (ownerIndex < 0 || ownerIndex >= invocationIds.Length)
            {
                return false;
            }

            string ownerId = invocationIds[ownerIndex];
            string normalizedCandidateId = Normalize(candidateId);
            if (string.IsNullOrEmpty(ownerId) || string.IsNullOrEmpty(normalizedCandidateId))
            {
                return false;
            }

            if (IdComparer.Equals(ownerId, normalizedCandidateId))
            {
                return true;
            }

            return firstInvocationIndex.TryGetValue(
                    normalizedCandidateId,
                    out int candidateIndex)
                && candidateCanReachOwner[ownerIndex][candidateIndex];
        }

        public string FindFirstAvailableDependencyId(int ownerIndex)
        {
            if (ownerIndex < 0 || ownerIndex >= cycleSafeCandidateIds.Length)
            {
                return null;
            }

            ReadOnlyCollection<string> candidates = cycleSafeCandidateIds[ownerIndex];
            Dictionary<string, int> used = directDependencyCounts[ownerIndex];
            for (int index = 0; index < candidates.Count; index++)
            {
                string candidate = candidates[index];
                if (!used.TryGetValue(candidate, out int count) || count == 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        public IReadOnlyList<string> GetAvailableDependencyIds(
            int ownerIndex,
            string currentDependencyId)
        {
            if (ownerIndex < 0 || ownerIndex >= cycleSafeCandidateIds.Length)
            {
                return Array.Empty<string>();
            }

            string normalizedCurrent = Normalize(currentDependencyId);
            ReadOnlyCollection<string> candidates = cycleSafeCandidateIds[ownerIndex];
            Dictionary<string, int> used = directDependencyCounts[ownerIndex];
            var available = new List<string>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                string candidate = candidates[index];
                used.TryGetValue(candidate, out int count);
                if (!string.IsNullOrEmpty(normalizedCurrent)
                    && IdComparer.Equals(candidate, normalizedCurrent))
                {
                    count--;
                }

                if (count <= 0)
                {
                    available.Add(candidate);
                }
            }

            return available.Count == 0
                ? Array.Empty<string>()
                : Array.AsReadOnly(available.ToArray());
        }

        private static List<int>[] CreateReverseAdjacency(
            IReadOnlyList<string> invocationIds,
            IReadOnlyList<string[]> dependencyIds,
            IReadOnlyDictionary<string, int> firstInvocationIndex)
        {
            var reverse = new List<int>[invocationIds.Count];
            for (int index = 0; index < reverse.Length; index++)
            {
                reverse[index] = new List<int>();
            }

            for (int sourceIndex = 0; sourceIndex < invocationIds.Count; sourceIndex++)
            {
                string sourceId = invocationIds[sourceIndex];
                if (string.IsNullOrEmpty(sourceId)
                    || !firstInvocationIndex.TryGetValue(sourceId, out int canonicalSourceIndex)
                    || canonicalSourceIndex != sourceIndex)
                {
                    continue;
                }

                string[] dependencies = dependencyIds[sourceIndex];
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Length;
                     dependencyIndex++)
                {
                    string dependencyId = dependencies[dependencyIndex];
                    if (!string.IsNullOrEmpty(dependencyId)
                        && firstInvocationIndex.TryGetValue(
                            dependencyId,
                            out int targetIndex))
                    {
                        reverse[targetIndex].Add(sourceIndex);
                    }
                }
            }

            return reverse;
        }

        private static bool[][] BuildReverseReachability(
            IReadOnlyList<string> invocationIds,
            IReadOnlyDictionary<string, int> firstInvocationIndex,
            IReadOnlyList<List<int>> reverseAdjacency,
            out int passCount,
            out int nodeVisitCount,
            out int edgeVisitCount)
        {
            int nodeCount = invocationIds.Count;
            var result = new bool[nodeCount][];
            passCount = 0;
            nodeVisitCount = 0;
            edgeVisitCount = 0;
            for (int ownerIndex = 0; ownerIndex < nodeCount; ownerIndex++)
            {
                var reachable = new bool[nodeCount];
                result[ownerIndex] = reachable;
                string ownerId = invocationIds[ownerIndex];
                if (string.IsNullOrEmpty(ownerId)
                    || !firstInvocationIndex.TryGetValue(ownerId, out int canonicalOwnerIndex))
                {
                    continue;
                }

                passCount++;
                var pending = new Stack<int>();
                pending.Push(canonicalOwnerIndex);
                while (pending.Count > 0)
                {
                    int current = pending.Pop();
                    if (reachable[current])
                    {
                        continue;
                    }

                    reachable[current] = true;
                    nodeVisitCount++;
                    List<int> incoming = reverseAdjacency[current];
                    edgeVisitCount += incoming.Count;
                    for (int index = 0; index < incoming.Count; index++)
                    {
                        pending.Push(incoming[index]);
                    }
                }
            }

            return result;
        }

        private static ReadOnlyCollection<string>[] BuildCycleSafeCandidates(
            IReadOnlyList<string> invocationIds,
            IReadOnlyDictionary<string, int> firstInvocationIndex,
            IReadOnlyList<bool[]> candidateCanReachOwner)
        {
            int nodeCount = invocationIds.Count;
            var result = new ReadOnlyCollection<string>[nodeCount];
            for (int ownerIndex = 0; ownerIndex < nodeCount; ownerIndex++)
            {
                string ownerId = invocationIds[ownerIndex];
                var candidates = new List<string>(Math.Max(0, nodeCount - 1));
                for (int candidateAuthoredIndex = 0;
                     candidateAuthoredIndex < nodeCount;
                     candidateAuthoredIndex++)
                {
                    if (candidateAuthoredIndex == ownerIndex)
                    {
                        continue;
                    }

                    string candidateId = invocationIds[candidateAuthoredIndex];
                    if (string.IsNullOrEmpty(candidateId))
                    {
                        continue;
                    }

                    bool createsCycle = !string.IsNullOrEmpty(ownerId)
                        && (IdComparer.Equals(ownerId, candidateId)
                            || (firstInvocationIndex.TryGetValue(
                                    candidateId,
                                    out int canonicalCandidateIndex)
                                && candidateCanReachOwner[ownerIndex][canonicalCandidateIndex]));
                    if (!createsCycle)
                    {
                        candidates.Add(candidateId);
                    }
                }

                result[ownerIndex] = candidates.AsReadOnly();
            }

            return result;
        }

        private static bool IsValueConfiguredAtAnotherIndex(
            IReadOnlyList<string> values,
            IReadOnlyDictionary<string, int> counts,
            string value,
            int currentIndex)
        {
            string normalized = Normalize(value);
            if (string.IsNullOrEmpty(normalized)
                || !counts.TryGetValue(normalized, out int count))
            {
                return false;
            }

            if (currentIndex >= 0
                && currentIndex < values.Count
                && IdComparer.Equals(values[currentIndex], normalized))
            {
                count--;
            }

            return count > 0;
        }

        private static void IncrementCount(IDictionary<string, int> counts, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            counts.TryGetValue(value, out int count);
            counts[value] = checked(count + 1);
        }

        private static string Normalize(string value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
