using NUnit.Framework;

using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;
using CycloneGames.RPGFoundation.Movement.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor
{
    public sealed class MovementNetworkingAuthorityTests
    {
        private const ulong ENTITY_ID = 1001UL;
        private const ulong EPOCH = 7UL;

        [Test]
        public void InputValidator_RejectsNullSenderAndEpochMismatch()
        {
            var authenticated = new FakeConnection(true);
            var nullContext = MakeContext(null, EPOCH, 20);
            var wrongEpoch = MakeContext(authenticated, EPOCH + 1UL, 20);

            Assert.That(DefaultMovementNetworkInputValidator.Instance
                .Validate(CreateInput(20, 3), nullContext).IsAccepted, Is.False);
            Assert.That(DefaultMovementNetworkInputValidator.Instance
                .Validate(CreateInput(20, 3), wrongEpoch).IsAccepted, Is.False);
            Assert.That(DefaultMovementNetworkInputValidator.Instance
                .Validate(CreateInput(20, 3), MakeContext(authenticated, EPOCH, 20)).IsAccepted, Is.True);
        }

        [Test]
        public void InputAuthority_RecordsAcceptedInputAndRejectsReplay()
        {
            var authority = CreateAuthority(64);
            MovementInputCommandMessage command = CreateInput(20, 3);
            var context = MakeContext(new FakeConnection(true), EPOCH, 20);

            Assert.That(authority.TryAccept(command, context, out NetworkActionResult accepted), Is.True);
            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(authority.TryAccept(command, context, out NetworkActionResult duplicate), Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(NetworkActionResultCode.Duplicate));
        }

        [Test]
        public void TimeDilationAuditor_RejectsSameTickFloodImmediately()
        {
            var auditor = new MovementTimeDilationAuditor(MovementDilationPolicy.Default);
            int accepted = 0;
            for (int i = 0; i < 64; i++)
                if (auditor.Audit(ENTITY_ID, 1.0, 100)) accepted++;

            Assert.That(accepted, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void TimeDilationAuditor_AcceptsHonest60HzStream()
        {
            var auditor = new MovementTimeDilationAuditor(MovementDilationPolicy.Default);
            for (int i = 0; i < 1000; i++)
                Assert.That(auditor.Audit(ENTITY_ID, 1.0 / 60.0, i), Is.True, "command " + i);
        }

        [Test]
        public void TimeDilationAuditor_EvictsIdleEntitiesInsteadOfFailingClosed()
        {
            var policy = new MovementDilationPolicy(60, 120, 1.25, 2, 0.05, 64);
            var auditor = new MovementTimeDilationAuditor(policy);
            for (ulong id = 1UL; id <= 50UL; id++)
                Assert.That(auditor.Audit(id, 1.0 / 60.0, (long)id), Is.True);

            Assert.That(auditor.TrackedEntityCount, Is.LessThanOrEqualTo(2));
            Assert.That(auditor.GetDiagnostics().EntityEvictionCount, Is.GreaterThan(0));
        }

        [Test]
        public void EntityHistory_KeepsPerEntityRecordsAndPrunesOnAck()
        {
            var history = new MovementNetworkInputHistory(new MovementHistoryCapacity(8, 16));
            for (int i = 1; i <= 10; i++)
            {
                history.Record(CreateInput(1UL, i, (ushort)i));
                history.Record(CreateInput(2UL, i, (ushort)i));
            }

            Assert.That(history.Contains(CreateInput(1UL, 1, 1)), Is.True);
            Assert.That(history.Contains(CreateInput(2UL, 10, 10)), Is.True);
            Assert.That(history.GetDiagnostics().TrackedEntities, Is.EqualTo(2));

            var ack = new MovementInputAckMessage(1UL, EPOCH, 20L, 5L, 5, true);
            Assert.That(history.PruneUpTo(ack), Is.EqualTo(5));
            Assert.That(history.Contains(CreateInput(1UL, 1, 1)), Is.False);
            Assert.That(history.Contains(CreateInput(1UL, 6, 6)), Is.True);
        }

        [Test]
        public void Reconciliation_IgnoresLocalOnlyFlagBits()
        {
            MovementNetworkSnapshotMessage predicted = CreateSnapshot(0f);
            predicted.Flags = MovementNetworkSnapshotFlags.Predicted;
            MovementNetworkSnapshotMessage authoritative = CreateSnapshot(0f);

            Assert.That(predicted.IsWellFormed, Is.True);
            Assert.That(predicted.IsValid, Is.False);
            Assert.That(MovementNetworkReconciliation.RequiresCorrection(
                predicted, authoritative, MovementNetworkCorrectionPolicy.Default, out float error), Is.False);
            Assert.That(error, Is.EqualTo(0f));
        }

        [Test]
        public void Reconciliation_RejectsUnknownFlagBitsWithoutSpuriousCorrection()
        {
            MovementNetworkSnapshotMessage malformed = CreateSnapshot(0f);
            malformed.Flags = 0x80;

            Assert.That(malformed.IsValid, Is.False);
            Assert.That(MovementNetworkReconciliation.RequiresCorrection(
                malformed, CreateSnapshot(0f), MovementNetworkCorrectionPolicy.Default, out _), Is.False);
        }

        [Test]
        public void InputBatch_RejectsNonMonotonicTicks()
        {
            MovementInputCommandMessage older = CreateInput(10, 1);
            MovementInputCommandMessage newer = CreateInput(9, 2);
            var batch = new MovementInputBatchMessage(ENTITY_ID, EPOCH, 2, older, newer);

            Assert.That(batch.IsValid, Is.False);
        }

        private static MovementNetworkInputAuthority CreateAuthority(int capacity)
        {
            var history = new MovementNetworkInputHistory(new MovementHistoryCapacity(64, capacity));
            var auditor = new MovementTimeDilationAuditor(MovementDilationPolicy.Default);
            return new MovementNetworkInputAuthority(history, auditor);
        }

        private static MovementNetworkInputValidationContext MakeContext(
            INetConnection sender, ulong epoch, long serverTick)
        {
            return new MovementNetworkInputValidationContext(
                sender, epoch, new NetworkTickId(serverTick), NetworkTickId.Invalid);
        }

        private static MovementInputCommandMessage CreateInput(long clientTick, ushort sequence)
        {
            return CreateInput(ENTITY_ID, clientTick, sequence);
        }

        private static MovementInputCommandMessage CreateInput(ulong entityId, long clientTick, ushort sequence)
        {
            return new MovementInputCommandMessage(
                entityId, EPOCH, clientTick, clientTick > 0 ? clientTick - 1 : 0L,
                sequence, 1U, 0U, 1f / 60f,
                new NetworkVector3(1f, 0f, 0f), NetworkVector3.Forward, sequence);
        }

        private static MovementNetworkSnapshotMessage CreateSnapshot(float x)
        {
            return new MovementNetworkSnapshotMessage(
                ENTITY_ID, EPOCH, 10L, 11L, 1, (ushort)0, 0, 0,
                0f, 0.5, new NetworkVector3(x, 0f, 0f),
                NetworkVector3.Zero, NetworkVector3.Up);
        }

        private sealed class FakeConnection : INetConnection
        {
            private readonly bool _authenticated;
            public FakeConnection(bool authenticated) { _authenticated = authenticated; }
            public int ConnectionId => 1;
            public string RemoteAddress => "127.0.0.1";
            public bool IsConnected => true;
            public bool IsAuthenticated => _authenticated;
            public int Ping => 30;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0.0;
            public long BytesSent => 0;
            public long BytesReceived => 0;
            public ulong PlayerId { get; set; }
            public bool Equals(INetConnection other) => ReferenceEquals(this, other);
            public override bool Equals(object obj) => ReferenceEquals(this, obj);
            public override int GetHashCode() => 1;
        }
    }
}
