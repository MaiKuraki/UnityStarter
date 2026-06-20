using System;
using System.Collections.Generic;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;
using NUnit.Framework;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Networking.Tests.Editor
{
    public sealed class AIPerceptionNetworkingIntegrationTests
    {
        [Test]
        public void Protocol_RegisterMessageCatalog_UsesAIPerceptionRange()
        {
            var catalog = new NetworkMessageCatalog();

            AIPerceptionNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.TryGet(
                AIPerceptionNetworkProtocol.MSG_DETECTION_EVENT,
                out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(AIPerceptionNetworkProtocol.MessageRange.Contains(descriptor.MessageId), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(descriptor.MessageId), Is.True);
            Assert.That(catalog.TryGetRegisteredModuleRange(descriptor.MessageId, out NetworkMessageIdRange range), Is.True);
            Assert.That(range.Name, Is.EqualTo(AIPerceptionNetworkProtocol.MessageOwner));
            Assert.That(descriptor.Owner, Is.EqualTo(AIPerceptionNetworkProtocol.MessageOwner));
            Assert.That(descriptor.Kind, Is.EqualTo(NetworkMessageKind.Module));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(NetworkChannel.UnreliableSequenced));
        }

        [Test]
        public void Protocol_RegisterMessageCatalog_IsIdempotent()
        {
            var catalog = new NetworkMessageCatalog();

            AIPerceptionNetworkProtocol.RegisterMessageCatalog(catalog);
            AIPerceptionNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.Count, Is.EqualTo(6));
            Assert.That(catalog.ModuleRangeCount, Is.EqualTo(1));
        }

        [Test]
        public void Profile_AllowsProjectSpecificExtensionSettings()
        {
            AIPerceptionNetworkProfile profile = AIPerceptionNetworkProfiles
                .CreateSharedTeamAwarenessBuilder()
                .SetInt("project.ai.maxSharedSightTargets", 12)
                .SetString("project.ai.visibilityLane", "squad")
                .Build();

            Assert.That(profile.TryGetInt("project.ai.maxSharedSightTargets", out int maxTargets), Is.True);
            Assert.That(maxTargets, Is.EqualTo(12));
            Assert.That(profile.TryGetString("project.ai.visibilityLane", out string lane), Is.True);
            Assert.That(lane, Is.EqualTo("squad"));
            Assert.That(profile.HasFeature(AIPerceptionNetworkFeatureFlags.TeamShared), Is.True);
        }

        [Test]
        public void SyncBridge_DetectionEvent_UsesStableNetworkTarget()
        {
            var bridge = new AIPerceptionNetworkSyncBridge();
            var resolver = new TestTargetResolver();
            var handle = new PerceptibleHandle(3, 11);
            resolver.Map(handle, new AIPerceptionNetworkTarget(9001u, perceptibleTypeId: 42));

            DetectionResult detection = CreateDetection(handle, SensorType.Sight, isFromMemory: true);

            bool created = bridge.TryCreateDetectionEvent(
                observerNetworkId: 100u,
                detection,
                resolver,
                tick: 77,
                sequence: 8,
                eventKind: AIPerceptionNetworkEventKind.Memory,
                out AIPerceptionDetectionEventMessage message);

            Assert.That(created, Is.True);
            Assert.That(message.IsValid, Is.True);
            Assert.That(message.ObserverNetworkId, Is.EqualTo(100u));
            Assert.That(message.Entry.TargetNetworkId, Is.EqualTo(9001u));
            Assert.That(message.Entry.PerceptibleTypeId, Is.EqualTo(42));
            Assert.That(message.Entry.SensorKind, Is.EqualTo(AIPerceptionNetworkSensorKind.Sight));
            Assert.That((message.Entry.Flags & AIPerceptionDetectionFlags.FromMemory) != 0, Is.True);
            Assert.That(message.StateHash, Is.Not.EqualTo(0UL));
        }

        [Test]
        public void SyncBridge_Snapshot_ComputesStableStateHash()
        {
            var bridge = new AIPerceptionNetworkSyncBridge();
            var resolver = new TestTargetResolver();
            var first = new PerceptibleHandle(1, 1);
            var second = new PerceptibleHandle(2, 1);
            resolver.Map(first, new AIPerceptionNetworkTarget(101u, perceptibleTypeId: 1));
            resolver.Map(second, new AIPerceptionNetworkTarget(102u, perceptibleTypeId: 2));

            DetectionResult[] detections =
            {
                CreateDetection(first, SensorType.Sight, isFromMemory: false),
                CreateDetection(second, SensorType.Hearing, isFromMemory: false)
            };

            SpanBuffer<AIPerceptionDetectionEntry> buffer = new SpanBuffer<AIPerceptionDetectionEntry>(4);
            int count = bridge.WriteDetectionEntries(detections, resolver, buffer.Span, tick: 20);
            AIPerceptionDetectionSnapshotMessage snapshot = bridge.CreateSnapshot(
                observerNetworkId: 100u,
                AIPerceptionNetworkSensorKind.Any,
                buffer.Span.Slice(0, count),
                tick: 20,
                sequence: 1);

            Assert.That(snapshot.IsValid, Is.True);
            Assert.That(snapshot.EntryCount, Is.EqualTo(2));
            Assert.That(snapshot.StateHash, Is.EqualTo(AIPerceptionNetworkHash.Compute(snapshot.Entries)));
        }

        [Test]
        public void AuthorityResolver_AssignsExpectedRoles()
        {
            var resolver = new ServerAuthoritativeAIPerceptionAuthorityResolver();
            var observer = new NetworkedAIPerceptionObserver(
                100u,
                ownerConnectionId: 7,
                ownerPlayerId: 700UL,
                teamId: 2,
                interestLayerMask: uint.MaxValue,
                alwaysRelevant: false,
                interestPosition: NetworkVector3.Zero);

            Assert.That(
                resolver.GetRole(new AIPerceptionNetworkAuthorityContext(true, false, 0), observer),
                Is.EqualTo(AIPerceptionNetworkAuthorityRole.ServerAuthority));

            Assert.That(
                resolver.GetRole(new AIPerceptionNetworkAuthorityContext(false, true, 7), observer),
                Is.EqualTo(AIPerceptionNetworkAuthorityRole.AutonomousObserver));

            Assert.That(
                resolver.GetRole(new AIPerceptionNetworkAuthorityContext(false, true, 8), observer),
                Is.EqualTo(AIPerceptionNetworkAuthorityRole.SimulatedObserver));
        }

        [Test]
        public void ObserverResolver_AreaPolicyFiltersByDistanceLayerAndAuthentication()
        {
            var resolver = new AIPerceptionNetworkObserverResolver();
            var observerSource = new TestObserverSource();
            var near = new TestConnection(2, isAuthenticated: true);
            var far = new TestConnection(3, isAuthenticated: true);
            var unauthenticated = new TestConnection(4, isAuthenticated: false);
            var results = new List<INetConnection>(4);

            observerSource.SetObserver(2, new NetworkInterestObserver(near, new NetworkVector3(3f, 0f, 4f), 20f, 0b0001u));
            observerSource.SetObserver(3, new NetworkInterestObserver(far, new NetworkVector3(50f, 0f, 0f), 20f, 0b0001u));
            observerSource.SetObserver(4, new NetworkInterestObserver(unauthenticated, NetworkVector3.Zero, 20f, 0b0001u));

            var observer = new NetworkedAIPerceptionObserver(
                100u,
                ownerConnectionId: 99,
                ownerPlayerId: 0UL,
                teamId: 0,
                interestLayerMask: 0b0001u,
                alwaysRelevant: false,
                interestPosition: NetworkVector3.Zero);

            var context = new AIPerceptionReplicationContext(
                observer,
                NetworkReplicationPolicy.Area(20f, requireAuthenticated: true));

            int count = resolver.ResolveObservers(
                context,
                new INetConnection[] { near, far, unauthenticated },
                observerSource,
                results);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(results[0], Is.SameAs(near));
        }

        private static DetectionResult CreateDetection(PerceptibleHandle handle, SensorType sensorType, bool isFromMemory)
        {
            return new DetectionResult
            {
                Target = handle,
                Distance = 12.5f,
                LastKnownPosition = new float3(1f, 2f, 3f),
                DetectionTime = 1.25f,
                Visibility = 0.75f,
                SensorType = (int)sensorType,
                IsFromMemory = isFromMemory
            };
        }

        private sealed class TestTargetResolver : IAIPerceptionNetworkTargetResolver
        {
            private readonly Dictionary<PerceptibleHandle, AIPerceptionNetworkTarget> _targets =
                new Dictionary<PerceptibleHandle, AIPerceptionNetworkTarget>();

            public void Map(PerceptibleHandle handle, AIPerceptionNetworkTarget target)
            {
                _targets[handle] = target;
            }

            public bool TryResolveNetworkTarget(PerceptibleHandle handle, out AIPerceptionNetworkTarget target)
            {
                return _targets.TryGetValue(handle, out target);
            }
        }

        private readonly struct SpanBuffer<T>
        {
            private readonly T[] _items;

            public SpanBuffer(int capacity)
            {
                _items = new T[capacity];
            }

            public Span<T> Span => _items;
        }

        private sealed class TestObserverSource : IAIPerceptionNetworkObserverSource
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
