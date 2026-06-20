using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public readonly struct NetworkValidationId : IEquatable<NetworkValidationId>
    {
        public readonly string Value;

        public NetworkValidationId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Validation id must not be null or empty.", nameof(value));
            }

            Value = value;
        }

        public bool Equals(NetworkValidationId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkValidationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }
    }

    public static class NetworkValidationIds
    {
        public static readonly NetworkValidationId LoadSimulation = new NetworkValidationId("validation.load_simulation");
        public static readonly NetworkValidationId GcBudget = new NetworkValidationId("validation.gc_budget");
        public static readonly NetworkValidationId ProtocolFuzz = new NetworkValidationId("validation.protocol_fuzz");
        public static readonly NetworkValidationId AdapterContract = new NetworkValidationId("validation.adapter_contract");
        public static readonly NetworkValidationId ReconnectStorm = new NetworkValidationId("validation.reconnect_storm");
        public static readonly NetworkValidationId HostMigrationStorm = new NetworkValidationId("validation.host_migration_storm");
        public static readonly NetworkValidationId SecurityPipeline = new NetworkValidationId("validation.security_pipeline");
        public static readonly NetworkValidationId Soak = new NetworkValidationId("validation.soak");
    }

    public readonly struct NetworkValidationRequirement
    {
        public readonly NetworkValidationId Id;
        public readonly NetworkReadinessSeverity Severity;
        public readonly int MinimumConnectionCount;
        public readonly int MinimumSameAreaConnectionCount;
        public readonly int MinimumIterations;
        public readonly double MinimumDurationSeconds;
        public readonly long MaximumAllocatedBytesPerTick;
        public readonly double MaximumRejectedRatio;
        public readonly int MaximumFailureCount;
        public readonly bool RequirePassed;
        public readonly string Description;

        public NetworkValidationRequirement(
            NetworkValidationId id,
            NetworkReadinessSeverity severity = NetworkReadinessSeverity.Required,
            int minimumConnectionCount = 0,
            int minimumSameAreaConnectionCount = 0,
            int minimumIterations = 0,
            double minimumDurationSeconds = 0d,
            long maximumAllocatedBytesPerTick = -1L,
            double maximumRejectedRatio = -1d,
            int maximumFailureCount = 0,
            bool requirePassed = true,
            string description = "")
        {
            if (minimumConnectionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumConnectionCount));
            }

            if (minimumSameAreaConnectionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSameAreaConnectionCount));
            }

            if (minimumIterations < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumIterations));
            }

            if (minimumDurationSeconds < 0d || double.IsNaN(minimumDurationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(minimumDurationSeconds));
            }

            if (maximumAllocatedBytesPerTick < -1L)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAllocatedBytesPerTick));
            }

            if (maximumRejectedRatio < -1d || maximumRejectedRatio > 1d || double.IsNaN(maximumRejectedRatio))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRejectedRatio));
            }

            if (maximumFailureCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFailureCount));
            }

            Id = id;
            Severity = severity;
            MinimumConnectionCount = minimumConnectionCount;
            MinimumSameAreaConnectionCount = minimumSameAreaConnectionCount;
            MinimumIterations = minimumIterations;
            MinimumDurationSeconds = minimumDurationSeconds;
            MaximumAllocatedBytesPerTick = maximumAllocatedBytesPerTick;
            MaximumRejectedRatio = maximumRejectedRatio;
            MaximumFailureCount = maximumFailureCount;
            RequirePassed = requirePassed;
            Description = description ?? string.Empty;
        }
    }

    public readonly struct NetworkValidationEvidence
    {
        public readonly NetworkValidationId Id;
        public readonly bool Passed;
        public readonly int ConnectionCount;
        public readonly int SameAreaConnectionCount;
        public readonly int Iterations;
        public readonly double DurationSeconds;
        public readonly long AllocatedBytesPerTick;
        public readonly double RejectedRatio;
        public readonly int FailureCount;
        public readonly string Description;

        public NetworkValidationEvidence(
            NetworkValidationId id,
            bool passed,
            int connectionCount = 0,
            int sameAreaConnectionCount = 0,
            int iterations = 0,
            double durationSeconds = 0d,
            long allocatedBytesPerTick = -1L,
            double rejectedRatio = -1d,
            int failureCount = 0,
            string description = "")
        {
            if (connectionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCount));
            }

            if (sameAreaConnectionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sameAreaConnectionCount));
            }

            if (iterations < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            if (durationSeconds < 0d || double.IsNaN(durationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            if (allocatedBytesPerTick < -1L)
            {
                throw new ArgumentOutOfRangeException(nameof(allocatedBytesPerTick));
            }

            if (rejectedRatio < -1d || rejectedRatio > 1d || double.IsNaN(rejectedRatio))
            {
                throw new ArgumentOutOfRangeException(nameof(rejectedRatio));
            }

            if (failureCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureCount));
            }

            Id = id;
            Passed = passed;
            ConnectionCount = connectionCount;
            SameAreaConnectionCount = sameAreaConnectionCount;
            Iterations = iterations;
            DurationSeconds = durationSeconds;
            AllocatedBytesPerTick = allocatedBytesPerTick;
            RejectedRatio = rejectedRatio;
            FailureCount = failureCount;
            Description = description ?? string.Empty;
        }
    }

    public sealed class NetworkProductionValidationPlan
    {
        private readonly NetworkValidationRequirement[] _requirements;
        private readonly Dictionary<string, string> _metadata;

        internal NetworkProductionValidationPlan(NetworkProductionValidationPlanBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            PlanId = string.IsNullOrEmpty(builder.PlanId) ? "default" : builder.PlanId;
            DisplayName = string.IsNullOrEmpty(builder.DisplayName) ? PlanId : builder.DisplayName;
            Description = builder.Description ?? string.Empty;
            _requirements = builder.Requirements.ToArray();
            _metadata = new Dictionary<string, string>(builder.Metadata, StringComparer.Ordinal);
        }

        public string PlanId { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public IReadOnlyList<NetworkValidationRequirement> Requirements
        {
            get
            {
                return _requirements;
            }
        }

        public IReadOnlyDictionary<string, string> Metadata
        {
            get
            {
                return _metadata;
            }
        }
    }

    public sealed class NetworkProductionValidationPlanBuilder
    {
        internal readonly List<NetworkValidationRequirement> Requirements = new List<NetworkValidationRequirement>(8);
        internal readonly Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        public string PlanId { get; set; } = "default";
        public string DisplayName { get; set; } = "Default";
        public string Description { get; set; } = string.Empty;

        public NetworkProductionValidationPlanBuilder RequireEvidence(in NetworkValidationRequirement requirement)
        {
            Requirements.Add(requirement);
            return this;
        }

        public NetworkProductionValidationPlanBuilder RequireEvidence(
            NetworkValidationId id,
            NetworkReadinessSeverity severity = NetworkReadinessSeverity.Required,
            int minimumConnectionCount = 0,
            int minimumSameAreaConnectionCount = 0,
            int minimumIterations = 0,
            double minimumDurationSeconds = 0d,
            long maximumAllocatedBytesPerTick = -1L,
            double maximumRejectedRatio = -1d,
            int maximumFailureCount = 0,
            bool requirePassed = true,
            string description = "")
        {
            Requirements.Add(new NetworkValidationRequirement(
                id,
                severity,
                minimumConnectionCount,
                minimumSameAreaConnectionCount,
                minimumIterations,
                minimumDurationSeconds,
                maximumAllocatedBytesPerTick,
                maximumRejectedRatio,
                maximumFailureCount,
                requirePassed,
                description));
            return this;
        }

        public NetworkProductionValidationPlanBuilder SetMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Validation plan metadata key must not be null or empty.", nameof(key));
            }

            Metadata[key] = value ?? string.Empty;
            return this;
        }

        public NetworkProductionValidationPlan Build()
        {
            return new NetworkProductionValidationPlan(this);
        }
    }

    public static class NetworkProductionValidationPlans
    {
        private const int SMALL_SESSION_CONNECTIONS = 8;
        private const int ARENA_CONNECTIONS = 100;
        private const int LARGE_AREA_CONNECTIONS = 1000;
        private const int MASSIVE_CONNECTIONS = 10000;
        private const int DEFAULT_FUZZ_ITERATIONS = 1000;
        private const int DEFAULT_STORM_ITERATIONS = 100;
        private const double DEFAULT_SOAK_SECONDS = 3600d;

        public static NetworkProductionValidationPlanBuilder CreateSmallSessionBuilder()
        {
            return new NetworkProductionValidationPlanBuilder
            {
                PlanId = "validation.small_session",
                DisplayName = "Small Session Validation",
                Description = "Validation evidence for LAN, platform lobby, relay, or listen-server sessions."
            }
                .RequireEvidence(NetworkValidationIds.AdapterContract, minimumConnectionCount: SMALL_SESSION_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.ProtocolFuzz, minimumIterations: DEFAULT_FUZZ_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.ReconnectStorm, minimumConnectionCount: SMALL_SESSION_CONNECTIONS, minimumIterations: DEFAULT_STORM_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.HostMigrationStorm, NetworkReadinessSeverity.Warning, SMALL_SESSION_CONNECTIONS, SMALL_SESSION_CONNECTIONS, DEFAULT_STORM_ITERATIONS);
        }

        public static NetworkProductionValidationPlanBuilder CreateAuthoritativeArenaBuilder()
        {
            return new NetworkProductionValidationPlanBuilder
            {
                PlanId = "validation.authoritative_arena",
                DisplayName = "Authoritative Arena Validation",
                Description = "Validation evidence for server-authoritative action sessions."
            }
                .RequireEvidence(NetworkValidationIds.LoadSimulation, minimumConnectionCount: ARENA_CONNECTIONS, minimumSameAreaConnectionCount: ARENA_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.GcBudget, maximumAllocatedBytesPerTick: 0L)
                .RequireEvidence(NetworkValidationIds.ProtocolFuzz, minimumIterations: DEFAULT_FUZZ_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.SecurityPipeline, minimumIterations: DEFAULT_FUZZ_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.ReconnectStorm, minimumConnectionCount: ARENA_CONNECTIONS, minimumIterations: DEFAULT_STORM_ITERATIONS);
        }

        public static NetworkProductionValidationPlanBuilder CreateLargeAreaBuilder()
        {
            return new NetworkProductionValidationPlanBuilder
            {
                PlanId = "validation.large_area",
                DisplayName = "Large Area Validation",
                Description = "Validation evidence for AOI, send-budget, and reconnect-heavy large areas."
            }
                .RequireEvidence(NetworkValidationIds.LoadSimulation, minimumConnectionCount: LARGE_AREA_CONNECTIONS, minimumSameAreaConnectionCount: LARGE_AREA_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.GcBudget, maximumAllocatedBytesPerTick: 0L)
                .RequireEvidence(NetworkValidationIds.ProtocolFuzz, minimumIterations: DEFAULT_FUZZ_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.AdapterContract, minimumConnectionCount: LARGE_AREA_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.ReconnectStorm, minimumConnectionCount: LARGE_AREA_CONNECTIONS, minimumIterations: DEFAULT_STORM_ITERATIONS);
        }

        public static NetworkProductionValidationPlanBuilder CreateMassiveShardBuilder()
        {
            return new NetworkProductionValidationPlanBuilder
            {
                PlanId = "validation.massive_shard",
                DisplayName = "Massive Shard Validation",
                Description = "Validation evidence for shard, zone, gateway, fleet, and soak-test readiness."
            }
                .RequireEvidence(NetworkValidationIds.LoadSimulation, minimumConnectionCount: MASSIVE_CONNECTIONS, minimumSameAreaConnectionCount: MASSIVE_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.GcBudget, maximumAllocatedBytesPerTick: 0L)
                .RequireEvidence(NetworkValidationIds.ProtocolFuzz, minimumIterations: DEFAULT_FUZZ_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.AdapterContract, minimumConnectionCount: MASSIVE_CONNECTIONS)
                .RequireEvidence(NetworkValidationIds.ReconnectStorm, minimumConnectionCount: MASSIVE_CONNECTIONS, minimumIterations: DEFAULT_STORM_ITERATIONS)
                .RequireEvidence(NetworkValidationIds.Soak, minimumConnectionCount: MASSIVE_CONNECTIONS, minimumDurationSeconds: DEFAULT_SOAK_SECONDS);
        }
    }

    public sealed class NetworkProductionValidationInput
    {
        private readonly List<NetworkValidationEvidence> _evidence = new List<NetworkValidationEvidence>(16);

        public IReadOnlyList<NetworkValidationEvidence> Evidence
        {
            get
            {
                return _evidence;
            }
        }

        public NetworkProductionValidationInput AddEvidence(in NetworkValidationEvidence evidence)
        {
            _evidence.Add(evidence);
            return this;
        }
    }

    public readonly struct NetworkProductionValidationIssue
    {
        public readonly NetworkValidationId ValidationId;
        public readonly NetworkReadinessSeverity Severity;
        public readonly string Message;
        public readonly string Recommendation;

        public NetworkProductionValidationIssue(
            NetworkValidationId validationId,
            NetworkReadinessSeverity severity,
            string message,
            string recommendation = "")
        {
            ValidationId = validationId;
            Severity = severity;
            Message = message ?? string.Empty;
            Recommendation = recommendation ?? string.Empty;
        }

        public bool IsBlocking
        {
            get
            {
                return Severity >= NetworkReadinessSeverity.Required;
            }
        }
    }

    public sealed class NetworkProductionValidationReport
    {
        private readonly NetworkProductionValidationIssue[] _issues;

        internal NetworkProductionValidationReport(
            NetworkProductionValidationPlan plan,
            List<NetworkProductionValidationIssue> issues)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _issues = issues != null
                ? issues.ToArray()
                : Array.Empty<NetworkProductionValidationIssue>();
        }

        public NetworkProductionValidationPlan Plan { get; }

        public IReadOnlyList<NetworkProductionValidationIssue> Issues
        {
            get
            {
                return _issues;
            }
        }

        public bool Passed
        {
            get
            {
                return BlockingIssueCount == 0;
            }
        }

        public int BlockingIssueCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _issues.Length; i++)
                {
                    if (_issues[i].IsBlocking)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int WarningCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _issues.Length; i++)
                {
                    if (_issues[i].Severity == NetworkReadinessSeverity.Warning)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool HasIssue(NetworkValidationId validationId)
        {
            for (int i = 0; i < _issues.Length; i++)
            {
                if (_issues[i].ValidationId.Equals(validationId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class NetworkProductionValidationEvaluator
    {
        public static NetworkProductionValidationReport Evaluate(
            NetworkProductionValidationPlan plan,
            NetworkProductionValidationInput input)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var issues = new List<NetworkProductionValidationIssue>(16);
            for (int i = 0; i < plan.Requirements.Count; i++)
            {
                NetworkValidationRequirement requirement = plan.Requirements[i];
                if (!TryFindCoveringEvidence(input.Evidence, requirement, out NetworkValidationEvidence bestEvidence))
                {
                    AddIssue(issues, requirement, bestEvidence);
                }
            }

            return new NetworkProductionValidationReport(plan, issues);
        }

        private static bool TryFindCoveringEvidence(
            IReadOnlyList<NetworkValidationEvidence> evidence,
            in NetworkValidationRequirement requirement,
            out NetworkValidationEvidence bestEvidence)
        {
            bestEvidence = default;
            bool found = false;
            int bestScore = int.MinValue;

            for (int i = 0; i < evidence.Count; i++)
            {
                NetworkValidationEvidence current = evidence[i];
                if (!current.Id.Equals(requirement.Id))
                {
                    continue;
                }

                int score = Score(current);
                if (!found || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestEvidence = current;
                }

                if (Covers(current, requirement))
                {
                    bestEvidence = current;
                    return true;
                }
            }

            return false;
        }

        private static bool Covers(
            in NetworkValidationEvidence evidence,
            in NetworkValidationRequirement requirement)
        {
            if (requirement.RequirePassed && !evidence.Passed)
            {
                return false;
            }

            if (evidence.ConnectionCount < requirement.MinimumConnectionCount)
            {
                return false;
            }

            if (evidence.SameAreaConnectionCount < requirement.MinimumSameAreaConnectionCount)
            {
                return false;
            }

            if (evidence.Iterations < requirement.MinimumIterations)
            {
                return false;
            }

            if (evidence.DurationSeconds < requirement.MinimumDurationSeconds)
            {
                return false;
            }

            if (requirement.MaximumAllocatedBytesPerTick >= 0L
                && (evidence.AllocatedBytesPerTick < 0L
                    || evidence.AllocatedBytesPerTick > requirement.MaximumAllocatedBytesPerTick))
            {
                return false;
            }

            if (requirement.MaximumRejectedRatio >= 0d
                && (evidence.RejectedRatio < 0d || evidence.RejectedRatio > requirement.MaximumRejectedRatio))
            {
                return false;
            }

            return evidence.FailureCount <= requirement.MaximumFailureCount;
        }

        private static int Score(in NetworkValidationEvidence evidence)
        {
            int score = evidence.Passed ? 1000000 : 0;
            score += evidence.ConnectionCount;
            score += evidence.SameAreaConnectionCount;
            score += evidence.Iterations;
            score += (int)Math.Min(int.MaxValue / 4, evidence.DurationSeconds);
            score -= evidence.FailureCount * 1000;
            return score;
        }

        private static void AddIssue(
            List<NetworkProductionValidationIssue> issues,
            in NetworkValidationRequirement requirement,
            in NetworkValidationEvidence bestEvidence)
        {
            string message = string.IsNullOrEmpty(bestEvidence.Id.Value)
                ? $"Validation evidence is missing for {requirement.Id}."
                : $"Validation evidence for {requirement.Id} does not satisfy the production requirement.";
            string recommendation = "Run or import a project-owned validation probe for this requirement before treating the scenario as production-ready.";
            issues.Add(new NetworkProductionValidationIssue(
                requirement.Id,
                requirement.Severity,
                message,
                recommendation));
        }
    }
}
