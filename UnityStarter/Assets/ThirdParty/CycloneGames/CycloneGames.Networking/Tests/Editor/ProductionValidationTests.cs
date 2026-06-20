using CycloneGames.Networking.Replication;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ProductionValidationTests
    {
        [Test]
        public void Evaluate_MassiveValidationPlan_Passes_With_Imported_Evidence()
        {
            NetworkProductionValidationPlan plan = NetworkProductionValidationPlans
                .CreateMassiveShardBuilder()
                .Build();
            var input = new NetworkProductionValidationInput()
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.LoadSimulation,
                    connectionCount: 10000,
                    sameAreaConnectionCount: 10000,
                    iterations: 100000))
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.GcBudget,
                    allocatedBytesPerTick: 0L,
                    iterations: 100000))
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.ProtocolFuzz,
                    iterations: 1000,
                    rejectedRatio: 1d))
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.AdapterContract,
                    connectionCount: 10000,
                    iterations: 10000))
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.ReconnectStorm,
                    connectionCount: 10000,
                    iterations: 100))
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.Soak,
                    connectionCount: 10000,
                    durationSeconds: 3600d));

            NetworkProductionValidationReport report = NetworkProductionValidationEvaluator.Evaluate(plan, input);

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(0, report.BlockingIssueCount);
        }

        [Test]
        public void Evaluate_Missing_Validation_Evidence_Reports_Blocking_Issue()
        {
            NetworkProductionValidationPlan plan = NetworkProductionValidationPlans
                .CreateAuthoritativeArenaBuilder()
                .Build();
            var input = new NetworkProductionValidationInput()
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(
                    NetworkValidationIds.LoadSimulation,
                    connectionCount: 100,
                    sameAreaConnectionCount: 100,
                    iterations: 1000));

            NetworkProductionValidationReport report = NetworkProductionValidationEvaluator.Evaluate(plan, input);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(report.HasIssue(NetworkValidationIds.GcBudget));
            Assert.IsTrue(report.HasIssue(NetworkValidationIds.ProtocolFuzz));
            Assert.GreaterOrEqual(report.BlockingIssueCount, 3);
        }

        [Test]
        public void ProtocolFuzzProbe_Produces_Passing_Evidence()
        {
            NetworkValidationEvidence evidence = NetworkProtocolFuzzValidationProbe.Run(
                new NetworkProtocolFuzzValidationOptions(
                    iterations: 128,
                    maxPayloadBytes: 64,
                    validFrameInterval: 4,
                    seed: 123u));

            Assert.AreEqual(NetworkValidationIds.ProtocolFuzz, evidence.Id);
            Assert.IsTrue(evidence.Passed);
            Assert.AreEqual(128, evidence.Iterations);
            Assert.AreEqual(0, evidence.FailureCount);
            Assert.AreEqual(1d, evidence.RejectedRatio);
        }

        [Test]
        public void ReplicationLoadProbe_Produces_Load_Evidence()
        {
            var options = new NetworkReplicationLoadValidationOptions(
                new NetworkReplicationLoadSimulationOptions(
                    connectionCount: 4,
                    objectCount: 32,
                    tickCount: 3,
                    worldSize: 100f,
                    viewRadius: 30f,
                    dirtyRatio: 0.5f,
                    budgetBytes: 4096,
                    budgetMessages: 32,
                    resultCapacity: 32,
                    seed: 7u));

            NetworkValidationEvidence evidence = NetworkReplicationLoadValidationProbe.Run(options);

            Assert.AreEqual(NetworkValidationIds.LoadSimulation, evidence.Id);
            Assert.IsTrue(evidence.Passed);
            Assert.AreEqual(4, evidence.ConnectionCount);
            Assert.AreEqual(12, evidence.Iterations);
        }

        [Test]
        public void Project_Defined_Validation_Id_Does_Not_Require_Core_Changes()
        {
            var customId = new NetworkValidationId("project.cloud.region_failover_soak");
            NetworkProductionValidationPlan plan = new NetworkProductionValidationPlanBuilder
            {
                PlanId = "project.custom"
            }
                .RequireEvidence(customId, minimumDurationSeconds: 300d)
                .Build();
            var input = new NetworkProductionValidationInput()
                .AddEvidence(NetworkValidationEvidenceFactory.Passed(customId, durationSeconds: 300d));

            NetworkProductionValidationReport report = NetworkProductionValidationEvaluator.Evaluate(plan, input);

            Assert.IsTrue(report.Passed);
        }
    }
}
