using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    public sealed class NetworkProductionReadinessInput
    {
        private readonly List<NetworkProtocolManifest> _protocolManifests = new List<NetworkProtocolManifest>(8);
        private readonly List<NetworkFailureInjectionPlan> _failurePlans = new List<NetworkFailureInjectionPlan>(8);

        public NetworkRuntimeProfile RuntimeProfile { get; set; }
        public NetworkNodeCapabilities NodeCapabilities { get; set; }

        public IReadOnlyList<NetworkProtocolManifest> ProtocolManifests
        {
            get
            {
                return _protocolManifests;
            }
        }

        public IReadOnlyList<NetworkFailureInjectionPlan> FailurePlans
        {
            get
            {
                return _failurePlans;
            }
        }

        public NetworkProductionReadinessInput AddProtocolManifest(NetworkProtocolManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            _protocolManifests.Add(manifest);
            return this;
        }

        public NetworkProductionReadinessInput AddFailurePlan(NetworkFailureInjectionPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            _failurePlans.Add(plan);
            return this;
        }
    }

    public readonly struct NetworkProductionReadinessIssue
    {
        public readonly NetworkHardeningRequirementId RequirementId;
        public readonly NetworkReadinessSeverity Severity;
        public readonly string Message;
        public readonly string Recommendation;

        public NetworkProductionReadinessIssue(
            NetworkHardeningRequirementId requirementId,
            NetworkReadinessSeverity severity,
            string message,
            string recommendation = "")
        {
            RequirementId = requirementId;
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

    public sealed class NetworkProductionReadinessReport
    {
        private readonly NetworkProductionReadinessIssue[] _issues;

        internal NetworkProductionReadinessReport(
            NetworkProductionReadinessScenario scenario,
            List<NetworkProductionReadinessIssue> issues)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            _issues = issues != null
                ? issues.ToArray()
                : Array.Empty<NetworkProductionReadinessIssue>();
        }

        public NetworkProductionReadinessScenario Scenario { get; }

        public IReadOnlyList<NetworkProductionReadinessIssue> Issues
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

        public bool HasIssue(NetworkHardeningRequirementId requirementId)
        {
            for (int i = 0; i < _issues.Length; i++)
            {
                if (_issues[i].RequirementId.Equals(requirementId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class NetworkProductionReadinessEvaluator
    {
        public static NetworkProductionReadinessReport Evaluate(
            NetworkProductionReadinessScenario scenario,
            NetworkProductionReadinessInput input)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var issues = new List<NetworkProductionReadinessIssue>(16);
            EvaluateRuntimeProfile(scenario, input.RuntimeProfile, issues);
            EvaluateNodeCapabilities(scenario, input.NodeCapabilities, issues);
            EvaluateProtocolManifests(scenario, input.RuntimeProfile, input.ProtocolManifests, issues);
            EvaluateFaultCoverage(scenario, input.FailurePlans, issues);
            return new NetworkProductionReadinessReport(scenario, issues);
        }

        private static void EvaluateRuntimeProfile(
            NetworkProductionReadinessScenario scenario,
            NetworkRuntimeProfile profile,
            List<NetworkProductionReadinessIssue> issues)
        {
            if (profile == null)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.RuntimeProfile,
                    NetworkReadinessSeverity.Critical,
                    "Runtime profile is missing.",
                    "Build a project-owned NetworkRuntimeProfile and pass it into the readiness input.");
                return;
            }

            int requiredConnections = Math.Max(scenario.MinimumProfileConnections, scenario.MinimumSameAreaConnections);
            if (requiredConnections > 0 && profile.MaxConnections < requiredConnections)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.ConnectionCapacity,
                    NetworkReadinessSeverity.Required,
                    $"Runtime profile supports {profile.MaxConnections} connections but scenario requires {requiredConnections}.",
                    "Move product connection targets into a project runtime profile or model capacity across nodes/shards.");
            }

            if (scenario.MinimumTickRate > 0 && profile.TickRate < scenario.MinimumTickRate)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.TickBudget,
                    NetworkReadinessSeverity.Required,
                    $"Runtime profile tick rate is {profile.TickRate} but scenario requires {scenario.MinimumTickRate}.",
                    "Use a scenario-specific profile or lower the scenario requirement after profiling simulation cost.");
            }

            if (scenario.MinimumSendRate > 0 && profile.SendRate < scenario.MinimumSendRate)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.TickBudget,
                    NetworkReadinessSeverity.Warning,
                    $"Runtime profile send rate is {profile.SendRate} but scenario expects {scenario.MinimumSendRate}.",
                    "Validate interpolation, prediction, and bandwidth budgets for the lower send rate.");
            }

            if (scenario.MinimumPayloadBytes > 0 && profile.MaxPayloadBytes < scenario.MinimumPayloadBytes)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.PayloadBudget,
                    NetworkReadinessSeverity.Required,
                    $"Runtime profile payload limit is {profile.MaxPayloadBytes} bytes but scenario requires {scenario.MinimumPayloadBytes}.",
                    "Move larger project payload budgets into the project profile or split messages into smaller protocol units.");
            }

            if (profile.MaxPayloadBytes > profile.BufferSize)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.PayloadBudget,
                    NetworkReadinessSeverity.Critical,
                    $"Runtime profile allows {profile.MaxPayloadBytes} byte payloads but buffer size is {profile.BufferSize}.",
                    "Increase buffer size or reduce max payload bytes.");
            }

            if (profile.MaxPayloadBytes > profile.Mtu)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.PayloadBudget,
                    NetworkReadinessSeverity.Warning,
                    $"Runtime profile payload limit {profile.MaxPayloadBytes} is larger than MTU {profile.Mtu}.",
                    "Validate fragmentation, batching, and adapter-specific transport behavior for oversized payloads.");
            }

            if (profile.DisconnectTimeoutSeconds > 0f
                && profile.HeartbeatIntervalSeconds > 0f
                && profile.DisconnectTimeoutSeconds < profile.HeartbeatIntervalSeconds)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.TimeoutCoherence,
                    NetworkReadinessSeverity.Critical,
                    "Disconnect timeout is shorter than heartbeat interval.",
                    "Use a disconnect timeout that can tolerate at least one missed heartbeat.");
            }

            if (scenario.MinimumSessionSearchResults > 0
                && profile.SessionSearchMaxResults < scenario.MinimumSessionSearchResults)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.SessionSearch,
                    NetworkReadinessSeverity.Warning,
                    $"Session search returns {profile.SessionSearchMaxResults} results but scenario expects {scenario.MinimumSessionSearchResults}.",
                    "Raise search result limits or document why matchmaking does not require broad room discovery.");
            }

            if (scenario.MinimumReconnectWindowSeconds > 0d
                && profile.ReconnectWindowSeconds < scenario.MinimumReconnectWindowSeconds)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.Reconnection,
                    NetworkReadinessSeverity.Required,
                    $"Reconnect window is {profile.ReconnectWindowSeconds} seconds but scenario requires {scenario.MinimumReconnectWindowSeconds}.",
                    "Increase reconnect reservation lifetime or lower the scenario target after product decision.");
            }

            if (scenario.MinimumHostMigrationTimeoutSeconds > 0d
                && profile.HostMigrationTimeoutSeconds < scenario.MinimumHostMigrationTimeoutSeconds)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.HostMigration,
                    NetworkReadinessSeverity.Required,
                    $"Host migration timeout is {profile.HostMigrationTimeoutSeconds} seconds but scenario requires {scenario.MinimumHostMigrationTimeoutSeconds}.",
                    "Increase host migration timeout or disable host migration requirements for dedicated-server-only deployments.");
            }
        }

        private static void EvaluateNodeCapabilities(
            NetworkProductionReadinessScenario scenario,
            NetworkNodeCapabilities capabilities,
            List<NetworkProductionReadinessIssue> issues)
        {
            bool requiresNode = scenario.MinimumNodeConnections > 0
                                || scenario.MinimumPayloadBytes > 0
                                || scenario.RequiredCapabilities.Count > 0
                                || !string.IsNullOrEmpty(scenario.RequiredRegion)
                                || !string.IsNullOrEmpty(scenario.RequiredPlatform);

            if (capabilities == null)
            {
                if (requiresNode)
                {
                    AddIssue(
                        issues,
                        NetworkHardeningRequirementIds.NodeCapabilities,
                        NetworkReadinessSeverity.Required,
                        "Node capabilities are missing.",
                        "Provide NetworkNodeCapabilities from the selected client host, relay, shard, gateway, or dedicated server.");
                }

                return;
            }

            NetworkCapabilityQuery query = scenario.CreateCapabilityQuery();
            if (!NetworkNodeCapabilityMatcher.Matches(capabilities, query))
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.NodeCapabilities,
                    NetworkReadinessSeverity.Required,
                    "Node capabilities do not satisfy scenario requirements.",
                    "Expose backend capabilities through NetworkNodeCapabilities or choose a node that matches the scenario.");
            }

            int requiredAreaConnections = Math.Max(scenario.MinimumNodeConnections, scenario.MinimumSameAreaConnections);
            if (requiredAreaConnections > 0 && capabilities.MaxConnections < requiredAreaConnections)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.ConnectionCapacity,
                    NetworkReadinessSeverity.Required,
                    $"Node supports {capabilities.MaxConnections} connections but scenario requires {requiredAreaConnections}.",
                    "Split the scenario across shards/zones or choose a larger node/fleet deployment target.");
            }
        }

        private static void EvaluateProtocolManifests(
            NetworkProductionReadinessScenario scenario,
            NetworkRuntimeProfile profile,
            IReadOnlyList<NetworkProtocolManifest> manifests,
            List<NetworkProductionReadinessIssue> issues)
        {
            int manifestCount = CountNonNull(manifests);
            if (scenario.RequireProtocolManifest && manifestCount == 0)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.ProtocolManifest,
                    NetworkReadinessSeverity.Required,
                    "Protocol manifest is required but none were provided.",
                    "Register project protocol manifests so message ids, versions, and payload budgets are explicit.");
            }

            if (scenario.MinimumProtocolManifestCount > 0 && manifestCount < scenario.MinimumProtocolManifestCount)
            {
                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.ProtocolManifest,
                    NetworkReadinessSeverity.Required,
                    $"Scenario expects {scenario.MinimumProtocolManifestCount} protocol manifests but input has {manifestCount}.",
                    "Add manifests for each owned protocol surface before production validation.");
            }

            var descriptors = new Dictionary<ushort, NetworkMessageDescriptor>();
            for (int i = 0; i < manifests.Count; i++)
            {
                NetworkProtocolManifest manifest = manifests[i];
                if (manifest == null)
                {
                    continue;
                }

                for (int j = 0; j < manifest.Messages.Count; j++)
                {
                    NetworkMessageDescriptor descriptor = manifest.Messages[j];
                    if (profile != null && descriptor.MaxPayloadSize > profile.MaxPayloadBytes)
                    {
                        AddIssue(
                            issues,
                            NetworkHardeningRequirementIds.PayloadBudget,
                            NetworkReadinessSeverity.Required,
                            $"Message {descriptor.MessageId} allows {descriptor.MaxPayloadSize} bytes but runtime profile allows {profile.MaxPayloadBytes}.",
                            "Raise the project runtime profile payload budget or lower the message manifest max payload size.");
                    }

                    if (descriptors.TryGetValue(descriptor.MessageId, out NetworkMessageDescriptor existing))
                    {
                        if (!DescriptorsMatch(existing, descriptor))
                        {
                            AddIssue(
                                issues,
                                NetworkHardeningRequirementIds.ProtocolManifest,
                                NetworkReadinessSeverity.Critical,
                                $"Message id {descriptor.MessageId} is declared by multiple manifests with different descriptors.",
                                "Move message ownership into a single manifest or allocate a new id range.");
                        }

                        continue;
                    }

                    descriptors.Add(descriptor.MessageId, descriptor);
                }
            }
        }

        private static void EvaluateFaultCoverage(
            NetworkProductionReadinessScenario scenario,
            IReadOnlyList<NetworkFailureInjectionPlan> plans,
            List<NetworkProductionReadinessIssue> issues)
        {
            for (int i = 0; i < scenario.RequiredFaults.Count; i++)
            {
                NetworkFaultRequirement requirement = scenario.RequiredFaults[i];
                if (CoversFault(plans, requirement))
                {
                    continue;
                }

                AddIssue(
                    issues,
                    NetworkHardeningRequirementIds.FaultCoverage,
                    requirement.Severity,
                    $"Fault coverage is missing for {requirement.Id}.",
                    "Add a NetworkFailureInjectionPlan entry and wire it to the project's transport, simulator, or external load test.");
            }
        }

        private static int CountNonNull(IReadOnlyList<NetworkProtocolManifest> manifests)
        {
            int count = 0;
            for (int i = 0; i < manifests.Count; i++)
            {
                if (manifests[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool CoversFault(
            IReadOnlyList<NetworkFailureInjectionPlan> plans,
            NetworkFaultRequirement requirement)
        {
            for (int i = 0; i < plans.Count; i++)
            {
                NetworkFailureInjectionPlan plan = plans[i];
                if (plan != null && plan.Covers(requirement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DescriptorsMatch(
            in NetworkMessageDescriptor left,
            in NetworkMessageDescriptor right)
        {
            return left.MessageId == right.MessageId
                   && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                   && string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
                   && left.SchemaHash == right.SchemaHash
                   && left.Kind == right.Kind
                   && left.DefaultChannel == right.DefaultChannel
                   && left.MaxPayloadSize == right.MaxPayloadSize;
        }

        private static void AddIssue(
            List<NetworkProductionReadinessIssue> issues,
            NetworkHardeningRequirementId requirementId,
            NetworkReadinessSeverity severity,
            string message,
            string recommendation)
        {
            issues.Add(new NetworkProductionReadinessIssue(
                requirementId,
                severity,
                message,
                recommendation));
        }
    }
}
