using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor
{
    public sealed class MovementNetworkingIntegrationTests
    {
        private const ulong ENTITY_ID = 1001UL;

        [Test]
        public void Protocol_RegisterMessageCatalog_UsesMovementRange()
        {
            var catalog = new NetworkMessageCatalog();

            MovementNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.TryGet(
                MovementNetworkProtocol.MSG_AUTHORITATIVE_SNAPSHOT,
                out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(MovementNetworkProtocol.MessageRange.Contains(descriptor.MessageId), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(descriptor.MessageId), Is.True);
            Assert.That(catalog.TryGetRegisteredModuleRange(descriptor.MessageId, out NetworkMessageIdRange range), Is.True);
            Assert.That(range.Name, Is.EqualTo(MovementNetworkProtocol.MessageOwner));
            Assert.That(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange protocolRange), Is.True);
            Assert.That(protocolRange.Name, Is.EqualTo(MovementNetworkProtocol.MessageOwner));
            Assert.That(descriptor.Owner, Is.EqualTo(MovementNetworkProtocol.MessageOwner));
            Assert.That(descriptor.Kind, Is.EqualTo(NetworkMessageKind.Module));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(NetworkChannel.UnreliableSequenced));
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotent()
        {
            var catalog = new NetworkMessageCatalog();

            MovementNetworkProtocol.RegisterMessageCatalog(catalog);
            MovementNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.Count, Is.EqualTo(7));
            Assert.That(catalog.RangeCount, Is.EqualTo(1));
            Assert.That(catalog.ModuleRangeCount, Is.EqualTo(1));
        }

        [Test]
        public void ManifestHandshake_UsesProtocolFingerprint()
        {
            MovementManifestHandshakeMessage message = MovementManifestHandshakeMessage.CreateDefault();

            Assert.That(message.IsValid, Is.True);
            Assert.That(message.ProtocolFingerprint, Is.EqualTo(MovementNetworkProtocol.ProtocolFingerprint));
            Assert.That(message.MessageIdMin, Is.EqualTo(MovementNetworkProtocol.MESSAGE_ID_BASE));
            Assert.That(message.MessageIdMax, Is.EqualTo(MovementNetworkProtocol.MESSAGE_ID_MAX));
        }

        [Test]
        public void Snapshot_RoundTripsMovementSnapshot()
        {
            MovementSnapshot source = CreateSnapshot(MovementStateType.Run, isGrounded: true);

            MovementNetworkSnapshotMessage message = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID,
                source,
                serverTick: 20,
                sequence: 3);
            MovementSnapshot roundTrip = message.ToMovementSnapshot();

            Assert.That(message.IsValid, Is.True);
            Assert.That(message.IsGrounded, Is.True);
            Assert.That(roundTrip.Position.x, Is.EqualTo(source.Position.x));
            Assert.That(roundTrip.Position.y, Is.EqualTo(source.Position.y));
            Assert.That(roundTrip.Position.z, Is.EqualTo(source.Position.z));
            Assert.That(roundTrip.StateType, Is.EqualTo(source.StateType));
            Assert.That(roundTrip.IsGrounded, Is.EqualTo(source.IsGrounded));
            Assert.That(roundTrip.Tick, Is.EqualTo(source.Tick));
        }

        [Test]
        public void AuthorityBridge_CapturesAndAppliesSnapshot()
        {
            var provider = new TestSnapshotProvider(CreateSnapshot(MovementStateType.Walk, isGrounded: true));
            var bridge = new MovementNetworkAuthorityBridge(provider);

            MovementNetworkSnapshotMessage message = bridge.CaptureSnapshot(ENTITY_ID, serverTick: 30, sequence: 7);

            Assert.That(message.IsValid, Is.True);
            provider.SetSnapshot(CreateSnapshot(MovementStateType.Idle, isGrounded: false));
            Assert.That(bridge.ApplySnapshot(message), Is.True);
            Assert.That(provider.Current.StateType, Is.EqualTo(MovementStateType.Walk));
            Assert.That(provider.Current.IsGrounded, Is.True);
        }

        [Test]
        public void AuthorityBridge_UsesValidatorForMovementLegality()
        {
            MovementSnapshot previous = CreateSnapshot(MovementStateType.Walk, isGrounded: true);
            MovementSnapshot next = CreateSnapshot(MovementStateType.Jump, isGrounded: false);
            next.Position = previous.Position + new float3(2f, 0f, 0f);
            var provider = new TestSnapshotProvider(previous);
            var validator = new TestMovementValidator(maxDistanceSqr: 4.1f, allowJump: true);
            var bridge = new MovementNetworkAuthorityBridge(provider, validator);

            MovementNetworkSnapshotMessage previousMessage = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID,
                previous,
                serverTick: 10);
            MovementNetworkSnapshotMessage nextMessage = MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID,
                next,
                serverTick: 11);

            Assert.That(bridge.ValidateTransition(previousMessage, nextMessage, deltaTime: 0.033f), Is.True);

            next.Position = new float3(10f, 0f, 0f);
            nextMessage = MovementNetworkSnapshotMessage.FromMovementSnapshot(ENTITY_ID, next, serverTick: 12);

            Assert.That(bridge.ValidateTransition(previousMessage, nextMessage, deltaTime: 0.033f), Is.False);
        }

        [Test]
        public void InputCommand_UsesProjectExtensibleButtonMask()
        {
            const uint SprintButton = 1u << 2;
            var command = new MovementInputCommandMessage(
                ENTITY_ID,
                clientTick: 9,
                lastReceivedServerTick: 8,
                inputSequence: 2,
                buttonMask: SprintButton,
                customFlags: 0x80u,
                deltaTime: 0.016f,
                moveAxes: new NetworkVector3(1f, 0f, 0f),
                aimDirection: NetworkVector3.Forward,
                predictionKey: 99);

            Assert.That(command.IsValid, Is.True);
            Assert.That(command.HasButton(SprintButton), Is.True);
            Assert.That(command.CustomFlags, Is.EqualTo(0x80u));
            Assert.That(command.PredictionKey, Is.EqualTo(99));
        }

        [Test]
        public void TeleportMessage_ProducesTeleportSnapshot()
        {
            var teleport = new MovementTeleportMessage(
                ENTITY_ID,
                serverTick: 22,
                teleportSequence: 4,
                stateId: (ushort)MovementStateType.Fall,
                flags: MovementNetworkSnapshotFlags.None,
                reasonCode: 17u,
                position: new NetworkVector3(5f, 6f, 7f),
                velocity: NetworkVector3.Zero,
                worldUp: NetworkVector3.Up);

            MovementNetworkSnapshotMessage snapshot = teleport.ToSnapshotMessage(movementTick: 21, timestamp: 1.25f);

            Assert.That(teleport.IsValid, Is.True);
            Assert.That(snapshot.IsTeleport, Is.True);
            Assert.That(snapshot.Position, Is.EqualTo(teleport.Position));
            Assert.That(snapshot.StateId, Is.EqualTo((ushort)MovementStateType.Fall));
        }

        private static MovementSnapshot CreateSnapshot(MovementStateType stateType, bool isGrounded)
        {
            return new MovementSnapshot
            {
                Position = new float3(1f, 2f, 3f),
                Velocity = new float3(0.5f, 0f, 0f),
                WorldUp = new float3(0f, 1f, 0f),
                StateType = stateType,
                VerticalVelocity = -1.5f,
                IsGrounded = isGrounded,
                JumpCount = isGrounded ? 0 : 1,
                Tick = 12,
                Timestamp = 0.4f
            };
        }

        private sealed class TestSnapshotProvider : IMovementSnapshotProvider
        {
            public TestSnapshotProvider(in MovementSnapshot current)
            {
                Current = current;
            }

            public MovementSnapshot Current { get; private set; }

            public void SetSnapshot(in MovementSnapshot snapshot)
            {
                Current = snapshot;
            }

            public MovementSnapshot GetSnapshot()
            {
                return Current;
            }

            public void ApplySnapshot(in MovementSnapshot snapshot)
            {
                Current = snapshot;
            }

            public void ResetFromSnapshot(in MovementSnapshot snapshot)
            {
                Current = snapshot;
            }
        }

        private sealed class TestMovementValidator : IMovementValidator
        {
            private readonly float _maxDistanceSqr;
            private readonly bool _allowJump;

            public TestMovementValidator(float maxDistanceSqr, bool allowJump)
            {
                _maxDistanceSqr = maxDistanceSqr;
                _allowJump = allowJump;
            }

            public bool ValidatePosition(float3 from, float3 to, float deltaTime)
            {
                float3 delta = to - from;
                return math.lengthsq(delta) <= _maxDistanceSqr;
            }

            public bool ValidateStateTransition(MovementStateType from, MovementStateType to)
            {
                return to != MovementStateType.Jump || _allowJump;
            }
        }
    }
}
