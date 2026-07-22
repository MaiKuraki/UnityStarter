using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        public void SnapshotTreeStateHash_IncludesCompositeAuxiliaryState()
        {
            var snapshot = new BTStateSnapshot
            {
                IsValid = true,
                NodeStates = new[] { (byte)RuntimeState.Running },
                NodeAuxInts = new[] { 1 },
                NodeCount = 1,
                BlackboardHash = 123UL
            };

            ulong firstHash = BTNetworkSync.ComputeTreeStateHash(snapshot);
            snapshot.NodeAuxInts[0] = 2;

            Assert.That(BTNetworkSync.ComputeTreeStateHash(snapshot), Is.Not.EqualTo(firstHash));
        }

        [Test]
        public void SyncBridge_EnforcesOwnerThreadAndIdempotentDispose()
        {
            var bridge = new BehaviorTreeNetworkSyncBridge();
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    bridge.IsDesynced(null, default);
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.That(workerException, Is.TypeOf<InvalidOperationException>());
            bridge.Dispose();
            bridge.Dispose();
            Assert.Throws<ObjectDisposedException>(() => bridge.IsDesynced(null, default));
        }

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
            Assert.That(descriptor.ContractId, Does.StartWith("BehaviorTreeStatePayloadMessage.FullSnapshot:v2|"));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(NetworkChannel.Reliable));
            Assert.That(descriptor.MaxPayloadSize, Is.EqualTo(NetworkConstants.DefaultMaxPayloadSize));
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
        public void ProtocolManifest_UsesFrozenSchemasAndV2StatePayloadFingerprint()
        {
            NetworkProtocolManifest manifest = BehaviorTreeNetworkProtocol.CreateProtocolManifest();
            string[] canonicalSchemaLiterals =
            {
                "BehaviorTreeManifestHandshakeMessage:v1",
                "BehaviorTreeStatePayloadMessage.FullSnapshot:v2|TargetNetworkId:u32le|Sequence:u16le|Tick:i32le|PayloadKind:u8=1|TreeTemplateHash:u64le|BlackboardHash:u64le|TreeStateHash:u64le|AuthorityGeneration:u32le|Payload:BTS2{Magic:u32le=0x32535442|IsValid:u8(0,1)|Timestamp:f64le|TreeStateHash:u64le|BlackboardHash:u64le|NodeCount:i32le|NodeStates:NodeCount*u8|NodeAuxInts:NodeCount*i32le|BlackboardLength:i32le|Blackboard:RuntimeBlackboardSnapshot{Scope:Snapshot|Sequence:u64le|IntCount:i32le|Ints:IntCount*(Key:i32le,Value:i32le)|FloatCount:i32le|Floats:FloatCount*(Key:i32le,Value:f32le)|BoolCount:i32le|Bools:BoolCount*(Key:i32le,Value:u8(0,1))|Vector3Count:i32le|Vector3s:Vector3Count*(Key:i32le,X:f32le,Y:f32le,Z:f32le)|LongCount:i32le|Longs:LongCount*(Key:i32le,Value:i64le)|Long2Count:i32le|Long2s:Long2Count*(Key:i32le,X:i64le,Y:i64le)|Long3Count:i32le|Long3s:Long3Count*(Key:i32le,X:i64le,Y:i64le,Z:i64le)|StampCount:i32le|Stamps:StampCount*(Key:i32le,Stamp:u64le)}}",
                "BehaviorTreeStatePayloadMessage.BlackboardDelta:v2|TargetNetworkId:u32le|Sequence:u16le|Tick:i32le|PayloadKind:u8=2|TreeTemplateHash:u64le|BlackboardHash:u64le|TreeStateHash:u64le|AuthorityGeneration:u32le|Payload:BTDP1{Magic:u32le=0x50445442|Version:u16le=1|HeaderSize:u16le=16|BodyLength:i32le|EntryCount:i32le|Entry:Key:i32le+Tag:u8+Value:tagged-le|Tags:0=i32,1=f32,2=bool-u8(0,1),3=3*f32,4=i64,5=2*i64,6=3*i64,255=remove}",
                "BehaviorTreeDesyncReportMessage:v2|TargetNetworkId:u32le|Sequence:u16le|LocalTick:i32le|RemoteTick:i32le|LocalBlackboardHash:u64le|RemoteBlackboardHash:u64le|LocalTreeStateHash:u64le|RemoteTreeStateHash:u64le|AuthorityGeneration:u32le",
                "BehaviorTreeTickControlMessage:v1",
                "BehaviorTreeAuthorityTransferMessage:v1"
            };
            ulong[] expectedSchemaHashes =
            {
                0x059263302E9505CDUL,
                0x750F7F22C73B0946UL,
                0x5528AAF0A310630DUL,
                0x566A9F2B1C5C9202UL,
                0x6299F932DCE53765UL,
                0x94B78D8EED490D89UL
            };

            Assert.That(manifest.CurrentVersion, Is.EqualTo(2));
            Assert.That(manifest.MinimumSupportedVersion, Is.EqualTo(2));
            Assert.That(manifest.IsCompatibleWith(1), Is.False);
            Assert.That(manifest.IsCompatibleWith(2), Is.True);
            Assert.That(manifest.Fingerprint, Is.EqualTo(0x633B1F15F69258ABUL));
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
        public void Profile_SettingViewsCannotMutateBuiltProfile()
        {
            BehaviorTreeNetworkProfile profile = BehaviorTreeNetworkProfiles
                .CreateServerAuthoritativeBuilder()
                .SetInt("project.ai.maxBurstTicks", 3)
                .SetString("project.ai.syncLane", "boss")
                .Build();

            var intSettings = (IDictionary<string, int>)profile.IntSettings;
            var stringSettings = (IDictionary<string, string>)profile.StringSettings;

            Assert.Throws<NotSupportedException>(() => intSettings["project.ai.maxBurstTicks"] = 99);
            Assert.Throws<NotSupportedException>(() => stringSettings["project.ai.syncLane"] = "mutated");
            Assert.That(profile.TryGetInt("project.ai.maxBurstTicks", out int burstTicks), Is.True);
            Assert.That(burstTicks, Is.EqualTo(3));
            Assert.That(profile.TryGetString("project.ai.syncLane", out string syncLane), Is.True);
            Assert.That(syncLane, Is.EqualTo("boss"));
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
        public void AuthorityResolver_RejectsPayloadFromAnotherAuthorityGeneration()
        {
            var resolver = new ServerAuthoritativeBehaviorTreeAuthorityResolver();
            using RuntimeBehaviorTree tree = CreateTree(RuntimeState.Running);
            var agent = new NetworkedBehaviorTreeAgent(
                tree, 1u, 7, 0UL, 0, uint.MaxValue, false, NetworkVector3.Zero,
                authorityGeneration: 4u);
            var context = new BehaviorTreeNetworkAuthorityContext(false, true, 7, authorityGeneration: 4u);
            var payload = new BehaviorTreeStatePayloadMessage(
                1u, 1, 1, BehaviorTreeNetworkPayloadKind.HashOnly, 0UL, 1UL, 2UL, null,
                authorityGeneration: 3u);

            Assert.That(resolver.CanApplyRemotePayload(context, agent, payload), Is.False);
            payload.AuthorityGeneration = 4u;
            Assert.That(resolver.CanApplyRemotePayload(context, agent, payload), Is.True);
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
                Assert.That(TryApplyFirst(bridge, target, message), Is.True);
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
        public void SyncBridge_FullSnapshotObserverFailurePropagatesAfterCommitWithoutAdvancingCursor()
        {
            const int HealthKey = 102;
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var bridge = new BehaviorTreeNetworkSyncBridge();
            source.Blackboard.SetInt(HealthKey, 42);
            BehaviorTreeStatePayloadMessage message = bridge.CaptureSnapshot(1u, source, tick: 10, sequence: 2);
            var receiveState = new BehaviorTreePayloadReceiveState(
                message.TargetNetworkId,
                message.TreeTemplateHash,
                message.AuthorityGeneration);
            target.Blackboard.AddObserver(
                HealthKey,
                (_, __) => throw new InvalidDataException("Application observer failure."));

            AggregateException exception = Assert.Throws<AggregateException>(
                () => bridge.TryApplyPayload(target, message, ref receiveState));
            Assert.That(exception.Flatten().InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(exception.Flatten().InnerExceptions[0], Is.TypeOf<InvalidDataException>());
            Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(42));
            Assert.That(receiveState.HasAcceptedPayload, Is.False);
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
                    source,
                    delta,
                    tick: 12,
                    sequence: 3,
                    treeTemplateHash: 0UL,
                    out BehaviorTreeStatePayloadMessage message), Is.True);

                Assert.That(message.PayloadKind, Is.EqualTo(BehaviorTreeNetworkPayloadKind.BlackboardDelta));
                Assert.That(message.Payload[0], Is.EqualTo((byte)'B'));
                Assert.That(message.Payload[1], Is.EqualTo((byte)'T'));
                Assert.That(message.Payload[2], Is.EqualTo((byte)'D'));
                Assert.That(message.Payload[3], Is.EqualTo((byte)'P'));
                Assert.That(message.Payload[4], Is.EqualTo(1));
                Assert.That(message.Payload[5], Is.EqualTo(0));
                Assert.That(ReadInt32LittleEndian(message.Payload, 8), Is.EqualTo(message.Payload.Length - 16));
                Assert.That(TryApplyFirst(bridge, target, message), Is.True);
                Assert.That(target.Blackboard.GetBool(AlertKey), Is.True);
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_DeltaCandidateIncludesUnchangedDeltaOnlyKeys()
        {
            RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
                .AddInt("Changed", RuntimeBlackboardSyncFlags.Delta)
                .AddInt("Unchanged", RuntimeBlackboardSyncFlags.Delta)
                .Build();
            int changedKey = RuntimeBlackboard.DefaultStringHashFunc("Changed");
            int unchangedKey = RuntimeBlackboard.DefaultStringHashFunc("Unchanged");
            using var source = new RuntimeBehaviorTree(
                new FixedStateNode(RuntimeState.Running),
                new RuntimeBlackboard(schema: schema));
            using var target = new RuntimeBehaviorTree(
                new FixedStateNode(RuntimeState.Running),
                new RuntimeBlackboard(schema: schema));
            using var delta = new BTBlackboardDelta(1);
            using var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);

            source.Blackboard.SetInt(changedKey, 10);
            source.Blackboard.SetInt(unchangedKey, 20);
            target.Blackboard.SetInt(changedKey, 5);
            target.Blackboard.SetInt(unchangedKey, 20);
            delta.TrackKey(changedKey);

            Assert.That(bridge.TryCreateBlackboardDelta(
                1u, source, delta, 1, 1, 0UL, out BehaviorTreeStatePayloadMessage message), Is.True);
            Assert.That(TryApplyFirst(bridge, target, message), Is.True);
            Assert.That(target.Blackboard.GetInt(changedKey), Is.EqualTo(10));
            Assert.That(target.Blackboard.GetInt(unchangedKey), Is.EqualTo(20));
        }

        [Test]
        public void SyncBridge_RejectsPrimitiveDeltaThatCollidesWithLocalObjectKey()
        {
            const int key = 204;
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var delta = new BTBlackboardDelta(1);
            using var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            var localObject = new object();
            source.Blackboard.SetInt(key, 10);
            target.Blackboard.SetObject(key, localObject);
            delta.TrackKey(key);
            Assert.That(bridge.TryCreateBlackboardDelta(
                1u, source, delta, 1, 1, 0UL, out BehaviorTreeStatePayloadMessage message), Is.True);

            Assert.That(TryApplyFirst(bridge, target, message), Is.False);
            Assert.That(target.Blackboard.GetObject<object>(key), Is.SameAs(localObject));
        }

        [Test]
        public void SyncBridge_HashOnlyIncludesNodeExecutionState()
        {
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var bridge = new BehaviorTreeNetworkSyncBridge();
            source.Tick();
            BehaviorTreeStatePayloadMessage message = bridge.CreateHashOnlyMessage(1u, source, 1, 1);

            Assert.That(TryApplyFirst(bridge, target, message), Is.False);
            target.Tick();
            Assert.That(TryApplyFirst(bridge, target, message), Is.True);
        }

        [Test]
        public void SyncBridge_ReceiveCursorRejectsStaleAuthorityGeneration()
        {
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var bridge = new BehaviorTreeNetworkSyncBridge();
            var receiveState = new BehaviorTreePayloadReceiveState(1u, 0UL, authorityGeneration: 2u);
            BehaviorTreeStatePayloadMessage stale = bridge.CaptureSnapshot(
                1u, source, 1, 1, authorityGeneration: 1u);
            BehaviorTreeStatePayloadMessage current = bridge.CaptureSnapshot(
                1u, source, 1, 1, authorityGeneration: 2u);

            Assert.That(bridge.TryApplyPayload(target, stale, ref receiveState), Is.False);
            Assert.That(bridge.TryApplyPayload(target, current, ref receiveState), Is.True);
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
        public void SyncBridge_CaptureSnapshotEnforcesWriteBudgetBeforePayloadCopy()
        {
            var builder = BehaviorTreeNetworkProfiles.CreateServerAuthoritativeBuilder();
            builder.MaxSnapshotPayloadBytes = 4;
            using var bridge = new BehaviorTreeNetworkSyncBridge(builder.Build());
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);

            Assert.Throws<InvalidDataException>(() => bridge.CaptureSnapshot(1u, source, 1, 1));
        }

        [Test]
        public void SyncBridge_InboundDeltaUsesProfileEntryCap()
        {
            var builder = BehaviorTreeNetworkProfiles.CreateBlackboardReplicatedBuilder();
            builder.MaxTrackedBlackboardKeys = 1;
            using var bridge = new BehaviorTreeNetworkSyncBridge(builder.Build());
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            byte[] payload;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteDeltaHeader(writer, entryCount: 2, bodyLength: 18);
                writer.Write(1);
                writer.Write((byte)0);
                writer.Write(1);
                writer.Write(2);
                writer.Write((byte)0);
                writer.Write(2);
                writer.Flush();
                payload = stream.ToArray();
            }

            var message = new BehaviorTreeStatePayloadMessage(
                1u, 1, 1, BehaviorTreeNetworkPayloadKind.BlackboardDelta,
                0UL, 0UL, 0UL, payload);
            Assert.That(TryApplyFirst(bridge, target, message), Is.False);
            Assert.That(target.Blackboard.HasKey(1), Is.False);
            Assert.That(target.Blackboard.HasKey(2), Is.False);
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
                Assert.That(TryApplyFirst(bridge, target, message), Is.False);
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
                Assert.That(TryApplyFirst(bridge, target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_DeltaObserverFailurePropagatesAfterCommitWithoutAdvancingCursor()
        {
            const int AlertKey = 203;
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var delta = new BTBlackboardDelta();
            using var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            delta.TrackKey(AlertKey);
            source.Blackboard.SetBool(AlertKey, true);
            Assert.That(bridge.TryCreateBlackboardDelta(
                1u,
                source,
                delta,
                tick: 12,
                sequence: 3,
                treeTemplateHash: 0UL,
                out BehaviorTreeStatePayloadMessage message), Is.True);
            var receiveState = new BehaviorTreePayloadReceiveState(
                message.TargetNetworkId,
                message.TreeTemplateHash,
                message.AuthorityGeneration);
            target.Blackboard.AddObserver(
                AlertKey,
                (_, __) => throw new InvalidDataException("Application observer failure."));

            AggregateException exception = Assert.Throws<AggregateException>(
                () => bridge.TryApplyPayload(target, message, ref receiveState));
            Assert.That(exception.Flatten().InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(exception.Flatten().InnerExceptions[0], Is.TypeOf<InvalidDataException>());
            Assert.That(target.Blackboard.GetBool(AlertKey), Is.True);
            Assert.That(receiveState.HasAcceptedPayload, Is.False);
        }

        [Test]
        public void SnapshotDecoder_RejectsNonCanonicalBoolAndImpossibleNodeCount()
        {
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            byte[] valid = BTNetworkSync.SerializeSnapshot(BTNetworkSync.CaptureSnapshot(source));

            byte[] invalidBool = (byte[])valid.Clone();
            invalidBool[4] = 2;
            Assert.Throws<InvalidDataException>(() => BTNetworkSync.DeserializeSnapshot(invalidBool));

            byte[] impossibleCount = (byte[])valid.Clone();
            WriteInt32LittleEndian(impossibleCount, 29, 1000);
            Assert.Throws<InvalidDataException>(() => BTNetworkSync.DeserializeSnapshot(impossibleCount));
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
                Assert.That(TryApplyFirst(bridge, target, message), Is.False);
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
                Assert.That(TryApplyFirst(bridge, target, message), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_RejectsInvalidFramingBeforeMutation()
        {
            const int key = 77;
            using var target = new RuntimeBlackboard();
            target.SetInt(key, 7);

            byte[] invalidMagic = CreateSingleIntDelta(key, 10);
            invalidMagic[0] ^= 0x01;
            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, invalidMagic));

            byte[] invalidVersion = CreateSingleIntDelta(key, 10);
            invalidVersion[4] = 2;
            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, invalidVersion));

            byte[] invalidLength = CreateSingleIntDelta(key, 10);
            WriteInt32LittleEndian(invalidLength, 8, 8);
            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, invalidLength));

            Assert.That(target.GetInt(key), Is.EqualTo(7));
        }

        [Test]
        public void BlackboardDelta_RejectsImpossibleCountAndNonCanonicalBoolBeforeMutation()
        {
            const int key = 78;
            using var target = new RuntimeBlackboard();
            target.SetBool(key, false);

            byte[] impossibleCount;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteDeltaHeader(writer, entryCount: int.MaxValue, bodyLength: 0);
                writer.Flush();
                impossibleCount = stream.ToArray();
            }

            Assert.Throws<InvalidDataException>(
                () => BTBlackboardDelta.Apply(target, impossibleCount, int.MaxValue));

            byte[] invalidBool;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteDeltaHeader(writer, entryCount: 1, bodyLength: 6);
                writer.Write(key);
                writer.Write((byte)2);
                writer.Write((byte)2);
                writer.Flush();
                invalidBool = stream.ToArray();
            }

            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, invalidBool));
            Assert.That(target.GetBool(key), Is.False);
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
                WriteDeltaHeader(writer, entryCount: 2, bodyLength: 14);
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

                Assert.That(TryApplyFirst(bridge, target, message), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(7));
                Assert.That(target.Blackboard.HasKey(OtherKey), Is.False);
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_FullSnapshotRejectsExecutionStateMismatchBeforeMutationOrCursorAdvance()
        {
            const int HealthKey = 101;
            using RuntimeBehaviorTree source = CreateTree(RuntimeState.Running);
            using RuntimeBehaviorTree target = CreateTree(RuntimeState.Running);
            using var bridge = new BehaviorTreeNetworkSyncBridge();
            var receiveState = new BehaviorTreePayloadReceiveState(1u, 0UL);

            source.Blackboard.SetInt(HealthKey, 42);
            target.Blackboard.SetInt(HealthKey, 7);
            source.Tick();
            BehaviorTreeStatePayloadMessage message = bridge.CaptureSnapshot(
                1u,
                source,
                tick: 10,
                sequence: 2);

            Assert.That(bridge.TryApplyPayload(target, message, ref receiveState), Is.False);
            Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(7));
            Assert.That(receiveState.HasAcceptedPayload, Is.False);
        }

        [Test]
        public void SyncBridge_EffectivePayloadBudgetFitsTransportWithoutFragmentation()
        {
            var builder = BehaviorTreeNetworkProfiles.CreateServerAuthoritativeBuilder();
            builder.MaxSnapshotPayloadBytes = int.MaxValue;
            builder.MaxDeltaPayloadBytes = int.MaxValue;

            using var bridge = new BehaviorTreeNetworkSyncBridge(builder.Build());
            int expectedInnerBudget = NetworkConstants.DefaultMaxPayloadSize -
                                      BehaviorTreeNetworkProtocol.STATE_PAYLOAD_FIXED_ENVELOPE_SIZE;

            Assert.That(bridge.EffectiveMaxSnapshotPayloadBytes, Is.EqualTo(expectedInnerBudget));
            Assert.That(bridge.EffectiveMaxDeltaPayloadBytes, Is.EqualTo(expectedInnerBudget));
            Assert.That(
                expectedInnerBudget + BehaviorTreeNetworkProtocol.STATE_PAYLOAD_FIXED_ENVELOPE_SIZE,
                Is.EqualTo(NetworkConstants.DefaultMaxPayloadSize));
        }

        [Test]
        public void SyncBridge_RejectsDuplicateSequenceAndOlderTick()
        {
            const int HealthKey = 101;
            const ulong TemplateHash = 0x1234UL;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            var bridge = new BehaviorTreeNetworkSyncBridge();
            var receiveState = new BehaviorTreePayloadReceiveState(1u, TemplateHash);

            try
            {
                source.Blackboard.SetInt(HealthKey, 10);
                BehaviorTreeStatePayloadMessage first = bridge.CaptureSnapshot(
                    1u, source, tick: 10, sequence: 10, treeTemplateHash: TemplateHash);
                Assert.That(bridge.TryApplyPayload(target, first, ref receiveState), Is.True);

                source.Blackboard.SetInt(HealthKey, 20);
                BehaviorTreeStatePayloadMessage duplicate = bridge.CaptureSnapshot(
                    1u, source, tick: 11, sequence: 10, treeTemplateHash: TemplateHash);
                Assert.That(bridge.TryApplyPayload(target, duplicate, ref receiveState), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(10));

                BehaviorTreeStatePayloadMessage olderTick = bridge.CaptureSnapshot(
                    1u, source, tick: 9, sequence: 11, treeTemplateHash: TemplateHash);
                Assert.That(bridge.TryApplyPayload(target, olderTick, ref receiveState), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(10));
            }
            finally
            {
                source.Dispose();
                target.Dispose();
                bridge.Dispose();
            }
        }

        [Test]
        public void SyncBridge_SequenceWrapAcceptsZeroAfterMaxValue()
        {
            const int HealthKey = 101;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            var bridge = new BehaviorTreeNetworkSyncBridge();
            var receiveState = new BehaviorTreePayloadReceiveState(1u, 0UL);

            try
            {
                source.Blackboard.SetInt(HealthKey, 1);
                BehaviorTreeStatePayloadMessage beforeWrap = bridge.CaptureSnapshot(
                    1u, source, tick: 1, sequence: ushort.MaxValue);
                Assert.That(bridge.TryApplyPayload(target, beforeWrap, ref receiveState), Is.True);

                source.Blackboard.SetInt(HealthKey, 2);
                BehaviorTreeStatePayloadMessage afterWrap = bridge.CaptureSnapshot(
                    1u, source, tick: 2, sequence: 0);
                Assert.That(bridge.TryApplyPayload(target, afterWrap, ref receiveState), Is.True);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(2));
            }
            finally
            {
                source.Dispose();
                target.Dispose();
                bridge.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsEnvelopeIdentityAndHashMismatchWithoutMutation()
        {
            const int HealthKey = 101;
            const ulong TemplateHash = 0xCAFEUL;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            var bridge = new BehaviorTreeNetworkSyncBridge();
            var receiveState = new BehaviorTreePayloadReceiveState(1u, TemplateHash);

            try
            {
                source.Blackboard.SetInt(HealthKey, 99);
                target.Blackboard.SetInt(HealthKey, 7);

                BehaviorTreeStatePayloadMessage wrongTarget = bridge.CaptureSnapshot(
                    2u, source, tick: 1, sequence: 1, treeTemplateHash: TemplateHash);
                Assert.That(bridge.TryApplyPayload(target, wrongTarget, ref receiveState), Is.False);

                BehaviorTreeStatePayloadMessage wrongTemplate = bridge.CaptureSnapshot(
                    1u, source, tick: 1, sequence: 1, treeTemplateHash: TemplateHash + 1UL);
                Assert.That(bridge.TryApplyPayload(target, wrongTemplate, ref receiveState), Is.False);

                BehaviorTreeStatePayloadMessage wrongHash = bridge.CaptureSnapshot(
                    1u, source, tick: 1, sequence: 1, treeTemplateHash: TemplateHash);
                wrongHash.BlackboardHash ^= 1UL;
                Assert.That(bridge.TryApplyPayload(target, wrongHash, ref receiveState), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(7));
                Assert.That(receiveState.HasAcceptedPayload, Is.False);
            }
            finally
            {
                source.Dispose();
                target.Dispose();
                bridge.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_StringTrackingUsesBlackboardHashProvider()
        {
            StringHashFunction hash = key => key.Length * 7919;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            using var delta = new BTBlackboardDelta();
            using var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            var receiveState = new BehaviorTreePayloadReceiveState(1u, 0UL);

            try
            {
                source.Blackboard.StringHashFunc = hash;
                target.Blackboard.StringHashFunc = hash;
                delta.TrackKey("alert", source.Blackboard);
                source.Blackboard.SetBool("alert", true);

                Assert.That(bridge.TryCreateBlackboardDelta(
                    1u,
                    source,
                    delta,
                    tick: 1,
                    sequence: 1,
                    treeTemplateHash: 0UL,
                    out BehaviorTreeStatePayloadMessage message), Is.True);
                Assert.That(bridge.TryApplyPayload(target, message, ref receiveState), Is.True);
                Assert.That(target.Blackboard.GetBool("alert"), Is.True);
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void SyncBridge_RejectsDeltaPostHashMismatchWithoutMutation()
        {
            const int HealthKey = 101;
            var source = CreateTree(RuntimeState.Running);
            var target = CreateTree(RuntimeState.Running);
            using var delta = new BTBlackboardDelta();
            using var bridge = new BehaviorTreeNetworkSyncBridge(BehaviorTreeNetworkProfiles.BlackboardReplicated);
            var receiveState = new BehaviorTreePayloadReceiveState(1u, 0UL);

            try
            {
                target.Blackboard.SetInt(HealthKey, 7);
                source.Blackboard.SetInt(HealthKey, 7);
                delta.TrackKey(HealthKey);
                Assert.That(delta.TryFlush(source.Blackboard, out _), Is.True);

                source.Blackboard.SetInt(HealthKey, 10);
                Assert.That(bridge.TryCreateBlackboardDelta(
                    1u,
                    source,
                    delta,
                    tick: 1,
                    sequence: 1,
                    treeTemplateHash: 0UL,
                    out BehaviorTreeStatePayloadMessage message), Is.True);

                message.BlackboardHash ^= 1UL;
                message.TreeStateHash = message.BlackboardHash;
                Assert.That(bridge.TryApplyPayload(target, message, ref receiveState), Is.False);
                Assert.That(target.Blackboard.GetInt(HealthKey), Is.EqualTo(7));
                Assert.That(receiveState.HasAcceptedPayload, Is.False);
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_RejectsDuplicateKeysBeforeMutation()
        {
            const int HealthKey = 101;
            var target = new RuntimeBlackboard();
            target.SetInt(HealthKey, 7);
            byte[] payload;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteDeltaHeader(writer, entryCount: 2, bodyLength: 18);
                writer.Write(HealthKey);
                writer.Write((byte)0);
                writer.Write(10);
                writer.Write(HealthKey);
                writer.Write((byte)0);
                writer.Write(20);
                writer.Flush();
                payload = stream.ToArray();
            }

            try
            {
                Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, payload));
                Assert.That(target.GetInt(HealthKey), Is.EqualTo(7));
            }
            finally
            {
                target.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_RejectsSchemaTypeAndSyncFlagBeforeMutation()
        {
            const int HealthKey = 101;
            var snapshotOnlySchema = new RuntimeBlackboardSchema(new[]
            {
                new RuntimeBlackboardKeyDefinition(
                    HealthKey,
                    "health",
                    RuntimeBlackboardValueType.Int,
                    RuntimeBlackboardSyncFlags.Snapshot,
                    hasDefaultValue: false,
                    defaultValue: default)
            });
            var target = new RuntimeBlackboard(schema: snapshotOnlySchema, applySchemaDefaults: false);
            target.SetInt(HealthKey, 7);

            try
            {
                byte[] snapshotOnlyPayload = CreateSingleIntDelta(HealthKey, 10);
                Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, snapshotOnlyPayload));
                Assert.That(target.GetInt(HealthKey), Is.EqualTo(7));

                var deltaSchema = new RuntimeBlackboardSchema(new[]
                {
                    new RuntimeBlackboardKeyDefinition(
                        HealthKey,
                        "health",
                        RuntimeBlackboardValueType.Int,
                        RuntimeBlackboardSyncFlags.Delta,
                        hasDefaultValue: false,
                        defaultValue: default)
                });
                target.BindSchema(deltaSchema, applyDefaults: false);
                byte[] wrongTypePayload;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    WriteDeltaHeader(writer, entryCount: 1, bodyLength: 6);
                    writer.Write(HealthKey);
                    writer.Write((byte)2);
                    writer.Write((byte)1);
                    writer.Flush();
                    wrongTypePayload = stream.ToArray();
                }

                Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(target, wrongTypePayload));
                Assert.That(target.GetInt(HealthKey), Is.EqualTo(7));
            }
            finally
            {
                target.Dispose();
            }
        }

        private static byte[] CreateSingleIntDelta(int key, int value)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                WriteDeltaHeader(writer, entryCount: 1, bodyLength: 9);
                writer.Write(key);
                writer.Write((byte)0);
                writer.Write(value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteDeltaHeader(BinaryWriter writer, int entryCount, int bodyLength)
        {
            writer.Write(0x50445442u);
            writer.Write((ushort)1);
            writer.Write((ushort)16);
            writer.Write(bodyLength);
            writer.Write(entryCount);
        }

        private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            uint bits = unchecked((uint)value);
            buffer[offset] = (byte)bits;
            buffer[offset + 1] = (byte)(bits >> 8);
            buffer[offset + 2] = (byte)(bits >> 16);
            buffer[offset + 3] = (byte)(bits >> 24);
        }

        private static int ReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset]
                 | (buffer[offset + 1] << 8)
                 | (buffer[offset + 2] << 16)
                 | (buffer[offset + 3] << 24);
        }

        private static RuntimeBehaviorTree CreateTree(RuntimeState state)
        {
            return new RuntimeBehaviorTree(new FixedStateNode(state), new RuntimeBlackboard());
        }

        private static bool TryApplyFirst(
            BehaviorTreeNetworkSyncBridge bridge,
            RuntimeBehaviorTree tree,
            in BehaviorTreeStatePayloadMessage message)
        {
            var receiveState = new BehaviorTreePayloadReceiveState(
                message.TargetNetworkId,
                message.TreeTemplateHash,
                message.AuthorityGeneration);
            return bridge.TryApplyPayload(tree, message, ref receiveState);
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
