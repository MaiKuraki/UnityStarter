using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Networking.Tests.Editor
{
    public sealed class BehaviorTreeNetworkingIntegrationTests
    {
        [Test]
        public void Protocol_RegisterMessageCatalog_UsesBehaviorTreeRange()
        {
            var catalog = new NetworkMessageCatalog();

            BehaviorTreeNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.TryGet(
                BehaviorTreeNetworkProtocol.MSG_FULL_SNAPSHOT,
                out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(BehaviorTreeNetworkProtocol.MessageRange.Contains(descriptor.MessageId), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(descriptor.MessageId), Is.True);
            Assert.That(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange range), Is.True);
            Assert.That(range.Name, Is.EqualTo(BehaviorTreeNetworkProtocol.MessageOwner));
            Assert.That(descriptor.Owner, Is.EqualTo(BehaviorTreeNetworkProtocol.MessageOwner));
            Assert.That(descriptor.ContractId, Is.EqualTo("BehaviorTreeStatePayloadMessage:v1"));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(NetworkChannel.Reliable));
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotent()
        {
            var catalog = new NetworkMessageCatalog();

            BehaviorTreeNetworkProtocol.RegisterMessageCatalog(catalog);
            BehaviorTreeNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.MessageCount, Is.EqualTo(6));
            Assert.That(catalog.ManifestCount, Is.EqualTo(1));
        }

        [Test]
        public void ProtocolManifest_UsesFrozenV1SchemasAndFingerprint()
        {
            NetworkProtocolManifest manifest = BehaviorTreeNetworkProtocol.CreateProtocolManifest();
            string[] canonicalSchemaLiterals =
            {
                "BehaviorTreeManifestHandshakeMessage:v1",
                "BehaviorTreeStatePayloadMessage:v1",
                "BehaviorTreeStatePayloadMessage:v1",
                "BehaviorTreeDesyncReportMessage:v1",
                "BehaviorTreeTickControlMessage:v1",
                "BehaviorTreeAuthorityTransferMessage:v1"
            };
            ulong[] expectedSchemaHashes =
            {
                0x059263302E9505CDUL,
                0xA5D8529342EA168CUL,
                0xA5D8529342EA168CUL,
                0x7CA942FF64163207UL,
                0x6299F932DCE53765UL,
                0x94B78D8EED490D89UL
            };

            Assert.That(manifest.Fingerprint, Is.EqualTo(0xA694B4E414728407UL));
            Assert.That(BehaviorTreeNetworkProtocol.ProtocolFingerprint, Is.EqualTo(manifest.Fingerprint));
            Assert.That(canonicalSchemaLiterals.Length, Is.EqualTo(expectedSchemaHashes.Length));
            Assert.That(manifest.Messages.Count, Is.EqualTo(expectedSchemaHashes.Length));
            for (int i = 0; i < expectedSchemaHashes.Length; i++)
            {
                Assert.That(
                    ComputeFnv1a64(canonicalSchemaLiterals[i]),
                    Is.EqualTo(expectedSchemaHashes[i]),
                    canonicalSchemaLiterals[i]);
                Assert.That(manifest.Messages[i].SchemaHash, Is.EqualTo(expectedSchemaHashes[i]));
                Assert.That(manifest.Messages[i].ContractId, Is.EqualTo(canonicalSchemaLiterals[i]));
            }
        }

        private static ulong ComputeFnv1a64(string canonicalLiteral)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int i = 0; i < canonicalLiteral.Length; i++)
            {
                char character = canonicalLiteral[i];
                if (character > 0x7F)
                {
                    throw new AssertionException("Canonical schema literals must contain ASCII characters only.");
                }

                hash ^= (byte)character;
                hash = unchecked(hash * prime);
            }

            return hash;
        }

        [Test]
        public void Profile_AllowsProjectSpecificExtensionSettings()
        {
            BehaviorTreeNetworkProfile profile = BehaviorTreeNetworkProfiles
                .CreateServerAuthoritativeBuilder()
                .SetInt("project.ai.maxBurstTicks", 3)
                .SetString("project.ai.syncLane", "boss")
                .Build();

            Assert.That(profile.TryGetInt("project.ai.maxBurstTicks", out int burstTicks), Is.True);
            Assert.That(burstTicks, Is.EqualTo(3));
            Assert.That(profile.TryGetString("project.ai.syncLane", out string syncLane), Is.True);
            Assert.That(syncLane, Is.EqualTo("boss"));
            Assert.That(profile.HasFeature(BehaviorTreeNetworkFeatureFlags.HostMigrationSnapshot), Is.True);
        }

        [Test]
        public void AuthorityResolver_AssignsExpectedRoles()
        {
            var resolver = new ServerAuthoritativeBehaviorTreeAuthorityResolver();
            var tree = CreateTree(RuntimeState.Running);
            var agent = CreateAgent(tree, ownerConnectionId: 7);

            try
            {
                Assert.That(
                    resolver.GetRole(new BehaviorTreeNetworkAuthorityContext(true, false, 0), agent),
                    Is.EqualTo(BehaviorTreeNetworkAuthorityRole.ServerAuthority));

                Assert.That(
                    resolver.GetRole(new BehaviorTreeNetworkAuthorityContext(false, true, 7), agent),
                    Is.EqualTo(BehaviorTreeNetworkAuthorityRole.AutonomousProxy));

                Assert.That(
                    resolver.GetRole(new BehaviorTreeNetworkAuthorityContext(false, true, 8), agent),
                    Is.EqualTo(BehaviorTreeNetworkAuthorityRole.SimulatedProxy));

                Assert.That(
                    resolver.CanTickAuthoritativeTree(new BehaviorTreeNetworkAuthorityContext(true, false, 0), agent),
                    Is.True);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SyncBridge_FullSnapshotRestoresRemoteBlackboard()
        {
            const int HealthKey = 101;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            var bridge = new BehaviorTreeNetworkSyncBridge();

            try
            {
                source.Blackboard.SetInt(HealthKey, 42);

                BehaviorTreeStatePayloadMessage message = bridge.CaptureSnapshot(1u, source, tick: 10, sequence: 2);

                Assert.That(message.IsValid, Is.True);
                Assert.That(bridge.ApplyPayload(target, message), Is.True);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(42));
                Assert.That(target.Blackboard.ComputeHash(), Is.EqualTo(source.Blackboard.ComputeHash()));
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_BlackboardDeltaRestoresTrackedMutation()
        {
            const int AlertKey = 202;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            var delta = new BTBlackboardDelta();
            var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);

            try
            {
                delta.TrackKey(AlertKey);
                source.Blackboard.SetBool(AlertKey, true);

                Assert.That(bridge.TryCreateBlackboardDelta(
                    1u,
                    source.Blackboard,
                    delta,
                    tick: 12,
                    sequence: 3,
                    treeTemplateHash: 0UL,
                    out BehaviorTreeStatePayloadMessage message), Is.True);

                Assert.That(message.PayloadKind, Is.EqualTo(BehaviorTreeNetworkPayloadKind.BlackboardDelta));
                Assert.That(bridge.ApplyPayload(target, message), Is.True);
                Assert.That(target.Blackboard.GetBool(AlertKey), Is.True);
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void ObserverResolver_AreaPolicyFiltersByDistanceLayerAndAuthentication()
        {
            var resolver = new BehaviorTreeNetworkObserverResolver();
            var observerSource = new TestObserverSource();
            var near = new TestConnection(2, isAuthenticated: true);
            var far = new TestConnection(3, isAuthenticated: true);
            var unauthenticated = new TestConnection(4, isAuthenticated: false);
            var results = new List<INetConnection>(4);
            var tree = CreateTree(RuntimeState.Running);

            try
            {
                observerSource.SetObserver(2, new NetworkInterestObserver(near, new NetworkVector3(3f, 0f, 4f), 20f, 0b0001u));
                observerSource.SetObserver(3, new NetworkInterestObserver(far, new NetworkVector3(50f, 0f, 0f), 20f, 0b0001u));
                observerSource.SetObserver(4, new NetworkInterestObserver(unauthenticated, NetworkVector3.Zero, 20f, 0b0001u));

                NetworkedBehaviorTreeAgent agent = new NetworkedBehaviorTreeAgent(
                    tree,
                    1u,
                    ownerConnectionId: 99,
                    ownerPlayerId: 0UL,
                    teamId: 0,
                    interestLayerMask: 0b0001u,
                    alwaysRelevant: false,
                    interestPosition: NetworkVector3.Zero);

                var context = new BehaviorTreeReplicationContext(
                    agent,
                    NetworkReplicationPolicy.Area(20f, requireAuthenticated: true));

                int count = resolver.ResolveObservers(
                    context,
                    new INetConnection[] { near, far, unauthenticated },
                    observerSource,
                    results);

                Assert.That(count, Is.EqualTo(1));
                Assert.That(results[0], Is.SameAs(near));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsOversizedIncomingSnapshotPayload()
        {
            var profile = BehaviorTreeNetworkProfiles.CreateServerAuthoritativeBuilder();
            profile.MaxSnapshotPayloadBytes = 4;
            var bridge = new BehaviorTreeNetworkSyncBridge(profile.Build());
            var target = CreateTree(RuntimeState.Running);
            var message = new BehaviorTreeStatePayloadMessage(
                1u,
                sequence: 1,
                tick: 1,
                BehaviorTreeNetworkPayloadKind.FullSnapshot,
                treeTemplateHash: 0UL,
                blackboardHash: 0UL,
                treeStateHash: 0UL,
                payload: new byte[5]);

            try
            {
                Assert.That(bridge.ApplyPayload(target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsMalformedIncomingSnapshotPayload()
        {
            var profile = BehaviorTreeNetworkProfiles.CreateServerAuthoritativeBuilder();
            profile.MaxSnapshotPayloadBytes = 64;
            var bridge = new BehaviorTreeNetworkSyncBridge(profile.Build());
            var target = CreateTree(RuntimeState.Running);
            var message = new BehaviorTreeStatePayloadMessage(
                1u,
                sequence: 1,
                tick: 1,
                BehaviorTreeNetworkPayloadKind.FullSnapshot,
                treeTemplateHash: 0UL,
                blackboardHash: 0UL,
                treeStateHash: 0UL,
                payload: new byte[] { 1, 2, 3 });

            try
            {
                Assert.That(bridge.ApplyPayload(target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsOversizedIncomingDeltaPayload()
        {
            var profile = BehaviorTreeNetworkProfiles.CreateBlackboardReplicatedBuilder();
            profile.MaxDeltaPayloadBytes = 4;
            var bridge = new BehaviorTreeNetworkSyncBridge(profile.Build());
            var target = CreateTree(RuntimeState.Running);
            var message = new BehaviorTreeStatePayloadMessage(
                1u,
                sequence: 1,
                tick: 1,
                BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                treeTemplateHash: 0UL,
                blackboardHash: 0UL,
                treeStateHash: 0UL,
                payload: new byte[5]);

            try
            {
                Assert.That(bridge.ApplyPayload(target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsMalformedIncomingDeltaPayload()
        {
            var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            var target = CreateTree(RuntimeState.Running);
            var message = new BehaviorTreeStatePayloadMessage(
                1u,
                sequence: 1,
                tick: 1,
                BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                treeTemplateHash: 0UL,
                blackboardHash: 0UL,
                treeStateHash: 0UL,
                payload: new byte[] { 1, 0, 0 });

            try
            {
                Assert.That(bridge.ApplyPayload(target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsPartiallyValidDeltaWithoutMutation()
        {
            const int HealthKey = 101;
            const int OtherKey = 202;
            var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            var target = CreateTree(RuntimeState.Running);

            byte[] payload;
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write(2);
                writer.Write(HealthKey);
                writer.Write((byte)0);
                writer.Write(99);
                writer.Write(OtherKey);
                writer.Write((byte)99);
                writer.Flush();
                payload = stream.ToArray();
            }

            var message = new BehaviorTreeStatePayloadMessage(
                1u,
                sequence: 1,
                tick: 1,
                BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                treeTemplateHash: 0UL,
                blackboardHash: 0UL,
                treeStateHash: 0UL,
                payload);

            try
            {
                target.Blackboard.SetInt(HealthKey, 7);

                Assert.That(bridge.ApplyPayload(target, message), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(7));
                Assert.That(target.Blackboard.HasKey(OtherKey), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        private static RuntimeBehaviorTree CreateTree(RuntimeState state)
        {
            return new RuntimeBehaviorTree(new FixedStateNode(state), new RuntimeBlackboard());
        }

        private static NetworkedBehaviorTreeAgent CreateAgent(RuntimeBehaviorTree tree, int ownerConnectionId)
        {
            return new NetworkedBehaviorTreeAgent(
                tree,
                1u,
                ownerConnectionId,
                0UL,
                0,
                uint.MaxValue,
                false,
                NetworkVector3.Zero);
        }

        private sealed class FixedStateNode : RuntimeNode
        {
            private readonly RuntimeState _state;

            public FixedStateNode(RuntimeState state)
            {
                _state = state;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                return _state;
            }
        }

        private sealed class TestObserverSource : IBehaviorTreeNetworkObserverSource
        {
            private readonly Dictionary<int, NetworkInterestObserver> _observers = new Dictionary<int, NetworkInterestObserver>();

            public void SetObserver(int connectionId, in NetworkInterestObserver observer)
            {
                _observers[connectionId] = observer;
            }

            public bool TryGetObserver(int connectionId, out NetworkInterestObserver observer)
            {
                return _observers.TryGetValue(connectionId, out observer);
            }
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, bool isAuthenticated)
            {
                ConnectionId = connectionId;
                IsAuthenticated = isAuthenticated;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => string.Empty;
            public bool IsConnected => true;
            public bool IsAuthenticated { get; }
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }

            public bool Equals(INetConnection other)
            {
                return other != null && other.ConnectionId == ConnectionId;
            }
        }
    }
}
