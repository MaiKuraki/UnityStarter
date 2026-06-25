using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkActionSimulationTests
    {
        [Test]
        public void Command_Validity_Requires_Entity_Action_Tick_And_Finite_Vectors()
        {
            var command = new NetworkActionCommand(
                entityId: 10UL,
                actionId: 20U,
                clientTick: new NetworkTickId(100),
                lastKnownServerTick: new NetworkTickId(95),
                sequence: 3,
                predictionKey: 9,
                primaryVector: NetworkVector3.Forward);

            Assert.That(command.IsValid, Is.True);
            Assert.That(command.HasPredictionKey, Is.True);
        }

        [Test]
        public void DefaultValidator_Accepts_Valid_Command_In_Window()
        {
            var command = new NetworkActionCommand(
                entityId: 10UL,
                actionId: 20U,
                clientTick: new NetworkTickId(100),
                lastKnownServerTick: new NetworkTickId(95),
                sequence: 4,
                predictionKey: 12,
                payloadHash: 99UL);
            var context = new NetworkActionValidationContext(
                sender: null,
                serverTick: new NetworkTickId(101),
                lastAcceptedClientTick: new NetworkTickId(90),
                maxAcceptedTickDrift: 8);

            NetworkActionResult result = DefaultNetworkActionValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Phase, Is.EqualTo(NetworkActionPhase.Confirmed));
            Assert.That(result.Sequence, Is.EqualTo(4));
            Assert.That(result.PredictionKey, Is.EqualTo(12));
            Assert.That(result.PayloadHash, Is.EqualTo(99UL));
        }

        [Test]
        public void DefaultValidator_Rejects_OutOfOrder_Tick()
        {
            var command = new NetworkActionCommand(
                entityId: 10UL,
                actionId: 20U,
                clientTick: new NetworkTickId(89),
                lastKnownServerTick: new NetworkTickId(88),
                sequence: 5);
            var context = new NetworkActionValidationContext(
                sender: null,
                serverTick: new NetworkTickId(100),
                lastAcceptedClientTick: new NetworkTickId(90),
                maxAcceptedTickDrift: 32);

            NetworkActionResult result = DefaultNetworkActionValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.OutOfOrder));
            Assert.That(result.Phase, Is.EqualTo(NetworkActionPhase.Rejected));
        }

        [Test]
        public void DefaultValidator_Rejects_Duplicate_Sequence()
        {
            var command = new NetworkActionCommand(
                entityId: 10UL,
                actionId: 20U,
                clientTick: new NetworkTickId(100),
                lastKnownServerTick: new NetworkTickId(99),
                sequence: 5);
            var context = new NetworkActionValidationContext(
                sender: null,
                serverTick: new NetworkTickId(100),
                lastAcceptedClientTick: new NetworkTickId(100),
                lastAcceptedSequence: 5,
                maxAcceptedTickDrift: 8);

            NetworkActionResult result = DefaultNetworkActionValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.Duplicate));
        }

        [Test]
        public void Correct_Result_Requires_Reconciliation()
        {
            NetworkActionResult result = NetworkActionResult.Correct(
                authoritativeTick: new NetworkTickId(42),
                sequence: 7,
                correctionFlags: NetworkActionCorrectionFlags.State | NetworkActionCorrectionFlags.Timeline,
                predictionKey: 11,
                authoritativeStateHash: 123UL);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.RequiresCorrection, Is.True);
            Assert.That(result.AuthoritativeStateHash, Is.EqualTo(123UL));
        }

        [Test]
        public void History_Records_And_Retrieves_Exact_Tick()
        {
            var history = new NetworkActionHistory<TestSnapshot>(capacity: 4);

            history.Record(1UL, new NetworkTickId(10), 1, new TestSnapshot(100));
            history.Record(2UL, new NetworkTickId(10), 1, new TestSnapshot(200));

            Assert.That(history.TryGet(1UL, new NetworkTickId(10), out TestSnapshot snapshot), Is.True);
            Assert.That(snapshot.Value, Is.EqualTo(100));
            Assert.That(history.TryGet(2UL, new NetworkTickId(10), 1, out snapshot), Is.True);
            Assert.That(snapshot.Value, Is.EqualTo(200));
        }

        [Test]
        public void History_Overwrites_Oldest_Record_When_Capacity_Is_Full()
        {
            var history = new NetworkActionHistory<TestSnapshot>(capacity: 2);

            history.Record(1UL, new NetworkTickId(1), 1, new TestSnapshot(10));
            history.Record(1UL, new NetworkTickId(2), 2, new TestSnapshot(20));
            history.Record(1UL, new NetworkTickId(3), 3, new TestSnapshot(30));

            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history.TryGet(1UL, new NetworkTickId(1), out _), Is.False);
            Assert.That(history.TryGetLatest(1UL, out NetworkActionHistoryEntry<TestSnapshot> latest), Is.True);
            Assert.That(latest.Tick, Is.EqualTo(new NetworkTickId(3)));
            Assert.That(latest.Snapshot.Value, Is.EqualTo(30));
        }

        [Test]
        public void History_RemoveEntity_Invalidates_All_Entity_Entries()
        {
            var history = new NetworkActionHistory<TestSnapshot>(capacity: 4);

            history.Record(1UL, new NetworkTickId(1), 1, new TestSnapshot(10));
            history.Record(2UL, new NetworkTickId(1), 1, new TestSnapshot(20));
            history.Record(1UL, new NetworkTickId(2), 2, new TestSnapshot(30));

            int removed = history.RemoveEntity(1UL);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history.TryGet(1UL, new NetworkTickId(2), out _), Is.False);
            Assert.That(history.TryGet(2UL, new NetworkTickId(1), out TestSnapshot remaining), Is.True);
            Assert.That(remaining.Value, Is.EqualTo(20));
        }

        private readonly struct TestSnapshot
        {
            public readonly int Value;

            public TestSnapshot(int value)
            {
                Value = value;
            }
        }
    }
}
