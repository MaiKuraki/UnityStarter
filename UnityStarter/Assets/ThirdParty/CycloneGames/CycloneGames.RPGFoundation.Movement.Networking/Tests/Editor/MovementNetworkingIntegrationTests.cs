using NUnit.Framework;

using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor
{
    public sealed class MovementNetworkingIntegrationTests
    {
        private const ulong ENTITY_ID = 1001UL;
        private const ulong EPOCH = 7UL;

        private static readonly MovementNetworkProtocol Protocol =
            new MovementNetworkProtocol(MovementWireCodecPolicy.Unspecified);

        [Test]
        public void Protocol_RegistersNineMessagesInTheReservedRange()
        {
            var catalog = new NetworkMessageCatalog();
            Protocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.MessageCount, Is.EqualTo(MovementWireIds.BuiltInMessageCount));
            Assert.That(catalog.ManifestCount, Is.EqualTo(1));
            Assert.That(catalog.TryGet(MovementWireIds.AuthoritativeSnapshot, out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(descriptor.ContractId, Is.EqualTo(MovementNetworkProtocol.GetContractId(2)));
            Assert.That(Protocol.MessageRange.Contains(descriptor.MessageId), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(descriptor.MessageId), Is.True);
        }

        [Test]
        public void Protocol_IsIdempotent()
        {
            var catalog = new NetworkMessageCatalog();
            Protocol.RegisterMessageCatalog(catalog);
            Protocol.RegisterMessageCatalog(catalog);
            Assert.That(catalog.MessageCount, Is.EqualTo(MovementWireIds.BuiltInMessageCount));
        }

        [Test]
        public void WireLayoutDigest_IsStableAndHandshakeRejectsMismatch()
        {
            Assert.That(Protocol.WireLayoutDigest, Is.Not.EqualTo(0UL));
            Assert.That(MovementWireLayout.DescriptorCount, Is.EqualTo(12));

            MovementManifestHandshakeMessage valid = Protocol.CreateHandshake();
            Assert.That(valid.IsValid, Is.True);
            Assert.That(valid.IsCompatibleWith(Protocol), Is.True);

            MovementManifestHandshakeMessage wrong = valid;
            wrong.WireLayoutDigest ^= 0xFFUL;
            Assert.That(wrong.IsCompatibleWith(Protocol), Is.False);
        }

        [Test]
        public void Snapshot_RoundTripsDeltaAndRejectsMalformedWireFields()
        {
            MovementSnapshot source = CreateSnapshot();
            MovementNetworkSnapshotMessage message = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID, EPOCH, source, serverTick: 20, sequence: 3);
            MovementSnapshot roundTrip = message.ToMovementSnapshot();

            Assert.That(message.IsValid, Is.True);
            Assert.That(roundTrip.StateId, Is.EqualTo(source.StateId));
            Assert.That(roundTrip.NetworkTick, Is.EqualTo(source.NetworkTick));

            message.StateId = 65535;
            Assert.That(message.IsValid, Is.False);

            message = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID, EPOCH, source, serverTick: 20, sequence: 3);
            message.Flags = 0x80;
            Assert.That(message.IsValid, Is.False);
        }

        [Test]
        public void Correction_CarriesCompleteState()
        {
            MovementNetworkSnapshotMessage predicted = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID, EPOCH, CreateSnapshot(), serverTick: 20, sequence: 3);
            MovementFullStateMessage authoritative = new MovementFullStateMessage(
                ENTITY_ID, EPOCH, 21, 4, CreateFullState(1f));

            bool created = MovementNetworkReconciliation.TryCreateCorrection(
                predicted, authoritative, MovementNetworkCorrectionPolicy.Default,
                out MovementCorrectionMessage correction);

            Assert.That(created, Is.True);
            Assert.That(correction.IsValid, Is.True);
            Assert.That(correction.FullState.State.IsValid, Is.True);
        }

        [Test]
        public void AuthorityBridge_RejectsImpossibleTransition()
        {
            var provider = new FakeProvider();
            var bridge = new MovementNetworkAuthorityBridge(provider, new MovementDefaultValidator(20f));
            MovementNetworkSnapshotMessage previous = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID, EPOCH, CreateSnapshot(), serverTick: 10);
            MovementSnapshot far = CreateSnapshot();
            far.Position = new MovementVector3(100000f, 0f, 0f);
            MovementNetworkSnapshotMessage next = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID, EPOCH, far, serverTick: 11);

            Assert.That(bridge.ValidateTransition(previous, next, 0.016f), Is.False);
        }

        [Test]
        public void Teleport_ProjectsVerticalVelocityOntoWorldUp()
        {
            var teleport = new MovementTeleportMessage(
                ENTITY_ID, EPOCH, 22, 4, (ushort)MovementStateType.Fall,
                MovementNetworkSnapshotFlags.None, 17u,
                new NetworkVector3(5f, 6f, 7f),
                new NetworkVector3(3f, 4f, 0f),
                new NetworkVector3(1f, 0f, 0f));

            Assert.That(teleport.IsValid, Is.True);
            Assert.That(teleport.VerticalVelocity, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(teleport.ToFullStateMessage(21, 1.25).IsValid, Is.True);
        }

        [Test]
        public void AuthorityTransfer_RejectsReplayAndWrongEpoch()
        {
            var transfer = new MovementAuthorityTransferMessage(
                ENTITY_ID, EPOCH, Protocol.ProtocolFingerprint, 5U, 100L,
                1, 2, 10UL, 20UL, 0U);

            Assert.That(transfer.IsValid, Is.True);
            Assert.That(transfer.IsAcceptableFor(EPOCH, Protocol.ProtocolFingerprint, 4U), Is.True);
            Assert.That(transfer.IsAcceptableFor(EPOCH, Protocol.ProtocolFingerprint, 5U), Is.False);
            Assert.That(transfer.IsAcceptableFor(EPOCH + 1UL, Protocol.ProtocolFingerprint, 4U), Is.False);
        }

        [Test]
        public void FullStateRequest_RequiresRequesterIdentity()
        {
            var valid = new MovementFullStateRequestMessage(ENTITY_ID, EPOCH, 3, 42UL, 10L, 1, 0U);
            var anonymous = new MovementFullStateRequestMessage(ENTITY_ID, EPOCH, 0, 0UL, 10L, 1, 0U);

            Assert.That(valid.IsValid, Is.True);
            Assert.That(anonymous.IsValid, Is.False);
        }

        private static MovementSnapshot CreateSnapshot()
        {
            MovementSnapshot snapshot = default;
            snapshot.Position = new MovementVector3(1f, 2f, 3f);
            snapshot.Velocity = new MovementVector3(0f, 0f, 0f);
            snapshot.WorldUp = new MovementVector3(0f, 1f, 0f);
            snapshot.StateId = (ushort)MovementStateType.Walk;
            snapshot.IsGrounded = true;
            snapshot.NetworkTick = 12;
            snapshot.NetworkTimeSeconds = 0.4;
            return snapshot;
        }

        private static MovementFullState CreateFullState(float x)
        {
            MovementFullState state = default;
            state.NetworkTick = 13;
            state.NetworkTimeSeconds = 0.42;
            state.Position = new MovementVector3(x, 0f, 0f);
            state.Velocity = new MovementVector3(0f, 0f, 0f);
            state.WorldUp = new MovementVector3(0f, 1f, 0f);
            state.GroundNormal = new MovementVector3(0f, 1f, 0f);
            state.StateId = (ushort)MovementStateType.Walk;
            state.IsGrounded = true;
            state.WasGrounded = true;
            state.PlatformLocalRotationW = 1f;
            return state;
        }

        private sealed class FakeProvider : IMovementSnapshotProvider
        {
            private MovementSnapshot _snapshot;
            private MovementFullState _fullState;

            public MovementSnapshot GetSnapshot() { return _snapshot; }
            public MovementFullState CaptureFullState() { return _fullState; }
            public void ApplySnapshot(in MovementSnapshot snapshot) { _snapshot = snapshot; }
            public void ApplyFullState(in MovementFullState state) { _fullState = state; }
            public void ResetFromFullState(in MovementFullState state) { ApplyFullState(state); }
        }
    }
}
