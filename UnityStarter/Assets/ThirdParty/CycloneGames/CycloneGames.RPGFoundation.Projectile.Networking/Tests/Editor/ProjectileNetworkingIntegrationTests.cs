using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
{
    public sealed class ProjectileNetworkingIntegrationTests
    {
        [Test]
        public void ProtocolManifest_UsesProjectileRange()
        {
            NetworkProtocolManifest manifest = ProjectileNetworkProtocol.CreateProtocolManifest();

            Assert.That(manifest.Owner, Is.EqualTo(ProjectileNetworkProtocol.MessageOwner));
            Assert.That(manifest.MessageRange.Min, Is.EqualTo(ProjectileNetworkProtocol.MESSAGE_ID_BASE));
            Assert.That(manifest.MessageRange.Max, Is.EqualTo(ProjectileNetworkProtocol.MESSAGE_ID_MAX));
            Assert.That(manifest.Messages.Count, Is.EqualTo(7));
            Assert.That(ProjectileNetworkProtocol.IsProjectileMessageId(ProjectileNetworkProtocol.MSG_HIT), Is.True);
        }

        [Test]
        public void Handshake_Default_IsValidAndCompatible()
        {
            ProjectileManifestHandshakeMessage message = ProjectileManifestHandshakeMessage.CreateDefault();

            Assert.That(message.IsValid, Is.True);
            Assert.That(message.IsCompatibleWithLocalProtocol(), Is.True);
            Assert.That(message.MessageIdMin, Is.EqualTo(ProjectileNetworkProtocol.MESSAGE_ID_BASE));
        }

        [Test]
        public void SnapshotHistory_ReturnsLatestSnapshot()
        {
            var history = new ProjectileNetworkSnapshotHistory(capacity: 4);

            history.Record(CreateSnapshot(entityId: 9UL, tick: 1, sequence: 1));
            history.Record(CreateSnapshot(entityId: 9UL, tick: 2, sequence: 2));

            Assert.That(history.TryGetLatest(9UL, out ProjectileSnapshotMessage latest), Is.True);
            Assert.That(latest.ServerTick, Is.EqualTo(2));
            Assert.That(latest.Sequence, Is.EqualTo(2));
        }

        [Test]
        public void DefaultValidator_AcceptsValidSnapshot()
        {
            ProjectileSnapshotMessage message = CreateSnapshot(entityId: 9UL, tick: 2, sequence: 1);
            var context = new ProjectileNetworkValidationContext(
                sender: null,
                serverTick: new NetworkTickId(2),
                lastAcceptedServerTick: NetworkTickId.Invalid);

            NetworkActionResult result = DefaultProjectileNetworkMessageValidator.Instance.ValidateSnapshot(
                message,
                context);

            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.Accepted));
        }

        [Test]
        public void DefaultValidator_RejectsDuplicateSnapshot()
        {
            ProjectileSnapshotMessage message = CreateSnapshot(entityId: 9UL, tick: 2, sequence: 1);
            var context = new ProjectileNetworkValidationContext(
                sender: null,
                serverTick: new NetworkTickId(2),
                lastAcceptedServerTick: new NetworkTickId(2),
                lastAcceptedSequence: 1);

            NetworkActionResult result = DefaultProjectileNetworkMessageValidator.Instance.ValidateSnapshot(
                message,
                context);

            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.Duplicate));
        }

        [Test]
        public void Reconciliation_CreatesCorrectionForPositionError()
        {
            ProjectileSnapshotMessage predicted = CreateSnapshot(entityId: 9UL, tick: 2, sequence: 1, x: 0f);
            ProjectileSnapshotMessage authoritative = CreateSnapshot(entityId: 9UL, tick: 2, sequence: 2, x: 4f);

            bool created = ProjectileNetworkReconciliation.TryCreateCorrection(
                predicted,
                authoritative,
                ProjectileNetworkCorrectionPolicy.Default,
                out ProjectileCorrectionMessage correction);

            Assert.That(created, Is.True);
            Assert.That(correction.IsValid, Is.True);
            Assert.That((ProjectileNetworkCorrectionFlags)correction.CorrectionFlags & ProjectileNetworkCorrectionFlags.Transform, Is.Not.EqualTo(ProjectileNetworkCorrectionFlags.None));
        }

        [Test]
        public void AuthorityBridge_CapturesSnapshotFromProjectileWorld()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var world = new ProjectileWorld(
                capacity: 4,
                eventCapacity: 4,
                collisionHitCapacity: 4,
                in space);

            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(
                definitionId: 100,
                speed: 10f,
                radius: 0.1f,
                maxLifetime: 3f,
                collisionLayerMask: -1);

            ProjectileSpawnRequest request = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 9UL,
                spawnTick: 0,
                ProjectileVector3.Zero,
                ProjectileVector3.Forward);

            Assert.That(world.TrySpawn(in request, out _), Is.True);

            var source = new ProjectileWorldNetworkSnapshotSource(world);
            var bridge = new ProjectileNetworkAuthorityBridge(source);

            Assert.That(bridge.TryCaptureSnapshot(9UL, sequence: 3, out ProjectileSnapshotMessage message), Is.True);
            Assert.That(message.ProjectileEntityId, Is.EqualTo(9UL));
            Assert.That(message.Sequence, Is.EqualTo(3));
        }

        private static ProjectileSnapshotMessage CreateSnapshot(
            ulong entityId,
            int tick,
            ushort sequence,
            float x = float.NaN)
        {
            float positionX = float.IsNaN(x) ? tick : x;
            var snapshot = new ProjectileSnapshot(
                entityId,
                ownerEntityId: 1UL,
                targetEntityId: 0UL,
                new ProjectileDefinitionId(100),
                ProjectileLifecycleFlags.Authoritative,
                tick,
                predictionKey: 0,
                age: tick * 0.1f,
                radius: 0.2f,
                position: new ProjectileVector3(positionX, 0f, 0f),
                previousPosition: new ProjectileVector3(positionX - 1f, 0f, 0f),
                velocity: new ProjectileVector3(1f, 0f, 0f));

            return ProjectileSnapshotMessage.FromSnapshot(in snapshot, sequence);
        }
    }
}
