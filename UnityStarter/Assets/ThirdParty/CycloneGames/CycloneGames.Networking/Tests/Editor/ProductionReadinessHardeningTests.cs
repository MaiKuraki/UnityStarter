using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ProductionReadinessHardeningTests
    {
        [Test]
        public void Evaluate_MassiveShardScenario_PassesWhenContractsAreCovered()
        {
            NetworkProductionReadinessScenario scenario = NetworkProductionReadinessScenarios
                .CreateMassiveShardBuilder()
                .Build();

            NetworkProductionReadinessReport report = NetworkProductionReadinessEvaluator.Evaluate(
                scenario,
                CreateMassiveReadyInput());

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(0, report.BlockingIssueCount);
            Assert.AreEqual(0, report.WarningCount);
        }

        [Test]
        public void Evaluate_MissingCapabilitiesAndFaultCoverage_ReportsBlockingIssues()
        {
            NetworkProductionReadinessScenario scenario = NetworkProductionReadinessScenarios
                .CreateAuthoritativeArenaBuilder()
                .Build();
            NetworkRuntimeProfile profile = NetworkRuntimeProfiles.CreateDefaultBuilder().Build();
            NetworkNodeCapabilities node = new NetworkNodeCapabilitiesBuilder
            {
                NodeId = "small-node",
                MaxConnections = 4,
                MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize
            }.Build();

            var input = new NetworkProductionReadinessInput
            {
                RuntimeProfile = profile,
                NodeCapabilities = node
            }.AddProtocolManifest(CreateManifest("Project.Arena", 30000, 30010));

            NetworkProductionReadinessReport report = NetworkProductionReadinessEvaluator.Evaluate(scenario, input);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(report.HasIssue(NetworkHardeningRequirementIds.NodeCapabilities));
            Assert.IsTrue(report.HasIssue(NetworkHardeningRequirementIds.FaultCoverage));
            Assert.GreaterOrEqual(report.BlockingIssueCount, 2);
        }

        [Test]
        public void Evaluate_ProtocolManifestConflict_ReportsCriticalProtocolIssue()
        {
            NetworkProductionReadinessScenario scenario = new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("project.protocol_conflict"),
                DisplayName = "Protocol Conflict",
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 2
            }.Build();

            NetworkProtocolManifest first = new NetworkProtocolManifestBuilder(
                    "Project.First",
                    30000,
                    30010,
                    NetworkMessageKind.User)
                .AddMessage<TestInputMessage>(30000)
                .Build();
            NetworkProtocolManifest second = new NetworkProtocolManifestBuilder(
                    "Project.Second",
                    30000,
                    30010,
                    NetworkMessageKind.User)
                .AddMessage<TestInventoryMessage>(30000)
                .Build();

            var input = new NetworkProductionReadinessInput
            {
                RuntimeProfile = NetworkRuntimeProfiles.CreateDefaultBuilder().Build()
            }
                .AddProtocolManifest(first)
                .AddProtocolManifest(second);

            NetworkProductionReadinessReport report = NetworkProductionReadinessEvaluator.Evaluate(scenario, input);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(report.HasIssue(NetworkHardeningRequirementIds.ProtocolManifest));
        }

        [Test]
        public void Evaluate_ProjectDefinedCapabilityAndFault_DoNotRequireCycloneEnumChanges()
        {
            var customCapability = new NetworkCapabilityId("project.cloud.save_fleet");
            var customFault = new NetworkFaultId("project.region_failover");
            NetworkProductionReadinessScenario scenario = new NetworkProductionReadinessScenarioBuilder
            {
                ScenarioId = new NetworkHardeningScenarioId("project.custom"),
                DisplayName = "Project Custom",
                MinimumProfileConnections = 64,
                MinimumNodeConnections = 64,
                RequireProtocolManifest = true,
                MinimumProtocolManifestCount = 1
            }
                .RequireCapability(customCapability, minimumLevel: 2)
                .RequireFault(customFault, minimumDurationSeconds: 60d)
                .Build();
            NetworkNodeCapabilities node = new NetworkNodeCapabilitiesBuilder
            {
                NodeId = "project-node",
                MaxConnections = 64,
                MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize
            }
                .Add(customCapability, level: 2)
                .Build();
            NetworkFailureInjectionPlan faultPlan = new NetworkFailureInjectionPlanBuilder
            {
                PlanId = "project-failover"
            }
                .AddFault(customFault, durationSeconds: 60d)
                .Build();

            var input = new NetworkProductionReadinessInput
            {
                RuntimeProfile = new NetworkRuntimeProfileBuilder
                {
                    ProfileId = "project.custom",
                    MaxConnections = 64,
                    TickRate = NetworkConstants.DefaultTickRate,
                    SendRate = NetworkConstants.DefaultSendRate,
                    Mtu = NetworkConstants.DefaultMTU,
                    MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                    BufferSize = NetworkConstants.DefaultBufferSize,
                    PoolSize = NetworkConstants.DefaultPoolSize,
                    SnapshotBufferSize = NetworkConstants.MaxSnapshotBufferSize
                }.Build(),
                NodeCapabilities = node
            }
                .AddProtocolManifest(CreateManifest("Project.Custom", 30100, 30110))
                .AddFailurePlan(faultPlan);

            NetworkProductionReadinessReport report = NetworkProductionReadinessEvaluator.Evaluate(scenario, input);

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(0, report.BlockingIssueCount);
        }

        private static NetworkProductionReadinessInput CreateMassiveReadyInput()
        {
            NetworkRuntimeProfile profile = new NetworkRuntimeProfileBuilder
            {
                ProfileId = "project.massive",
                DisplayName = "Project Massive",
                MaxConnections = 10000,
                TickRate = 30,
                SendRate = 20,
                Mtu = NetworkConstants.DefaultMTU,
                MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                BufferSize = NetworkConstants.DefaultBufferSize,
                PoolSize = NetworkConstants.DefaultPoolSize,
                SnapshotBufferSize = 512,
                SessionSearchMaxResults = 500,
                ReconnectWindowSeconds = 300d,
                HostMigrationTimeoutSeconds = 8d
            }.Build();
            NetworkNodeCapabilities node = new NetworkNodeCapabilitiesBuilder
            {
                NodeId = "massive-zone-001",
                RuntimeId = NetworkRuntimeId.FromAsciiCode("Shard"),
                RuntimeName = "ProjectShard",
                Region = "global",
                Platform = "linux-headless",
                MaxConnections = 10000,
                MaxPayloadBytes = NetworkConstants.DefaultMaxPayloadSize,
                CpuScore = 100,
                MemoryScore = 100
            }
                .Add(NetworkCapabilityIds.RealtimeTransport)
                .Add(NetworkCapabilityIds.AuthoritativeSimulation)
                .Add(NetworkCapabilityIds.Sharding)
                .Add(NetworkCapabilityIds.ZoneTransfer)
                .Add(NetworkCapabilityIds.Persistence)
                .Build();
            NetworkFailureInjectionPlan faultPlan = new NetworkFailureInjectionPlanBuilder
            {
                PlanId = "massive-faults",
                DisplayName = "Massive Faults"
            }
                .AddFault(NetworkFaultIds.BandwidthCap, 30d)
                .AddFault(NetworkFaultIds.ReconnectStorm, 30d)
                .AddFault(NetworkFaultIds.BackendUnavailable, 30d)
                .AddFault(NetworkFaultIds.ClockDrift, 30d)
                .Build();

            return new NetworkProductionReadinessInput
            {
                RuntimeProfile = profile,
                NodeCapabilities = node
            }
                .AddProtocolManifest(CreateManifest("Project.Massive", 30000, 30010))
                .AddFailurePlan(faultPlan);
        }

        private static NetworkProtocolManifest CreateManifest(string owner, ushort minMessageId, ushort maxMessageId)
        {
            return new NetworkProtocolManifestBuilder(
                    owner,
                    minMessageId,
                    maxMessageId,
                    NetworkMessageKind.User)
                .AddMessage<TestInputMessage>(minMessageId, NetworkChannel.UnreliableSequenced, 128)
                .Build();
        }

        private struct TestInputMessage { }

        private struct TestInventoryMessage { }
    }
}
