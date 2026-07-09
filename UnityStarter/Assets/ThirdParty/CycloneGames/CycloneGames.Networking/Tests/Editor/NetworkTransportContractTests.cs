using System;
using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.Networking.Interest;
using CycloneGames.Networking.Simulation;
using CycloneGames.Networking.Stubs;
using CycloneGames.Networking.Transports;

namespace CycloneGames.Networking.Tests.Editor
{
    public abstract class NetworkTransportContractTests
    {
        protected abstract INetTransport CreateTransport();

        protected virtual string ConnectAddress => string.Empty;

        [Test]
        public void Lifecycle_StartAndStop_AreIdempotent()
        {
            using TransportScope server = CreateScope();

            Assert.IsTrue(server.Transport.Available);
            Assert.IsFalse(server.Transport.IsRunning);

            server.Transport.StartServer();
            server.Transport.StartServer();

            Assert.IsTrue(server.Transport.IsRunning);
            Assert.IsTrue(server.Transport.IsServer);
            Assert.IsFalse(server.Transport.IsClient);

            server.Transport.Stop();
            server.Transport.Stop();

            Assert.IsFalse(server.Transport.IsRunning);
            Assert.IsFalse(server.Transport.IsServer);
            Assert.IsFalse(server.Transport.IsClient);
        }

        [Test]
        public void Lifecycle_CanRestartAfterStop()
        {
            using TransportScope server = CreateScope();

            server.Transport.StartServer();
            server.Transport.Stop();
            server.Transport.StartServer();

            Assert.IsTrue(server.Transport.IsRunning);
            Assert.IsTrue(server.Transport.IsServer);
        }

        [Test]
        public void LifecycleSnapshot_ReportsStoppedAndRunningStates()
        {
            using TransportScope server = CreateScope();

            NetworkLifecycleSnapshot stopped = NetworkLifecycle.GetSnapshot(server.Transport);
            Assert.AreEqual(NetworkLifecycleState.Stopped, stopped.State);
            Assert.IsFalse(stopped.IsRunning);

            server.Transport.StartServer();

            NetworkLifecycleSnapshot running = NetworkLifecycle.GetSnapshot(server.Transport);
            Assert.AreEqual(NetworkLifecycleState.ServerRunning, running.State);
            Assert.IsTrue(running.IsRunning);
            Assert.IsTrue(running.IsServer);
            Assert.IsTrue(running.HasFeature(NetworkBackendFeatures.RealtimeTransport));
        }

        [Test]
        public void Connection_EventsFireOnce_WhenPairBecomesReady()
        {
            using ConnectedPair pair = CreateConnectedPair();

            Assert.AreEqual(1, pair.ServerConnectedCount);
            Assert.AreEqual(1, pair.ClientConnectedCount);

            PollPair(pair);
            PollPair(pair);

            Assert.AreEqual(1, pair.ServerConnectedCount);
            Assert.AreEqual(1, pair.ClientConnectedCount);
        }

        [Test]
        public void StopClient_NotifiesClientAndServerOnce()
        {
            using ConnectedPair pair = CreateConnectedPair();

            pair.Client.Transport.Stop();
            PollPair(pair);
            PollPair(pair);

            Assert.AreEqual(1, pair.ServerDisconnectedCount);
            Assert.AreEqual(1, pair.ClientDisconnectedCount);
        }

        [Test]
        public void StopServer_NotifiesServerAndClientOnce()
        {
            using ConnectedPair pair = CreateConnectedPair();

            pair.Server.Transport.Stop();
            PollPair(pair);
            PollPair(pair);

            Assert.AreEqual(1, pair.ServerDisconnectedCount);
            Assert.AreEqual(1, pair.ClientDisconnectedCount);
        }

        [Test]
        public void Send_ClientToServer_DispatchesPayloadAndUpdatesStatistics()
        {
            using ConnectedPair pair = CreateConnectedPair();
            byte[] payload = { 1, 2, 3, 4 };
            byte[] received = new byte[payload.Length];
            int receivedCount = 0;
            int receivedChannel = -1;

            pair.Server.Transport.OnDataReceived += (_, data, channelId) =>
            {
                receivedCount++;
                Buffer.BlockCopy(data.Array, data.Offset, received, 0, data.Count);
                receivedChannel = channelId;
            };

            int channelId = pair.Client.Transport.GetChannelId(NetworkChannel.Reliable);
            NetworkSendResult result = pair.Client.Transport.Send(pair.ServerConnection, new ArraySegment<byte>(payload), channelId);
            PollPair(pair);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(NetworkSendStatus.Accepted, result.Status);
            Assert.AreEqual(payload.Length, result.BytesAccepted);
            Assert.AreEqual(1, receivedCount);
            CollectionAssert.AreEqual(payload, received);
            Assert.AreEqual(pair.Server.Transport.GetChannelId(NetworkChannel.Reliable), receivedChannel);

            NetworkStatistics clientStats = pair.Client.Transport.GetStatistics();
            NetworkStatistics serverStats = pair.Server.Transport.GetStatistics();
            Assert.GreaterOrEqual(clientStats.BytesSent, payload.Length);
            Assert.GreaterOrEqual(clientStats.PacketsSent, 1);
            Assert.GreaterOrEqual(serverStats.BytesReceived, payload.Length);
            Assert.GreaterOrEqual(serverStats.PacketsReceived, 1);
        }

        [Test]
        public void Send_ServerToClient_DispatchesPayload()
        {
            using ConnectedPair pair = CreateConnectedPair();
            byte[] payload = { 9, 8, 7 };
            byte[] received = new byte[payload.Length];
            int receivedCount = 0;

            pair.Client.Transport.OnDataReceived += (_, data, _) =>
            {
                receivedCount++;
                Buffer.BlockCopy(data.Array, data.Offset, received, 0, data.Count);
            };

            int channelId = pair.Server.Transport.GetChannelId(NetworkChannel.Unreliable);
            NetworkSendResult result = pair.Server.Transport.Send(pair.ClientConnection, new ArraySegment<byte>(payload), channelId);
            PollPair(pair);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(payload.Length, result.BytesAccepted);
            Assert.AreEqual(1, receivedCount);
            CollectionAssert.AreEqual(payload, received);
        }

        [Test]
        public void Send_EmptyPayload_IsIgnored()
        {
            using ConnectedPair pair = CreateConnectedPair();
            int receivedCount = 0;

            pair.Server.Transport.OnDataReceived += (_, _, _) => receivedCount++;

            byte[] payload = Array.Empty<byte>();
            pair.Client.Transport.Send(pair.ServerConnection, new ArraySegment<byte>(payload), pair.Client.Transport.GetChannelId(NetworkChannel.Reliable));
            PollPair(pair);

            Assert.AreEqual(0, receivedCount);
        }

        [Test]
        public void Send_WithoutPeer_RaisesInvalidSend()
        {
            using TransportScope client = CreateScope();
            int errorCount = 0;
            TransportError lastError = TransportError.None;

            client.Transport.OnError += (_, error, _) =>
            {
                errorCount++;
                lastError = error;
            };

            client.Transport.StartClient(ConnectAddress);
            byte[] payload = { 5 };
            NetworkSendResult result = client.Transport.Send(null, new ArraySegment<byte>(payload), client.Transport.GetChannelId(NetworkChannel.Reliable));
            Poll(client.Transport);

            Assert.AreEqual(1, errorCount);
            Assert.AreEqual(TransportError.InvalidSend, lastError);
            Assert.AreEqual(NetworkSendStatus.NotConnected, result.Status);
        }

        [Test]
        public void LifecycleSnapshot_RecordsLastError_WhenProviderSupportsDiagnostics()
        {
            using TransportScope client = CreateScope();

            if (client.Transport is not INetworkLifecycleProvider)
            {
                Assert.Ignore("Transport does not expose INetworkLifecycleProvider diagnostics.");
                return;
            }

            client.Transport.StartClient(ConnectAddress);
            byte[] payload = { 5 };
            client.Transport.Send(null, new ArraySegment<byte>(payload), client.Transport.GetChannelId(NetworkChannel.Reliable));

            NetworkLifecycleSnapshot snapshot = NetworkLifecycle.GetSnapshot(client.Transport);
            Assert.AreEqual(TransportError.InvalidSend, snapshot.LastError);
            Assert.IsFalse(string.IsNullOrEmpty(snapshot.LastErrorMessage));
        }

        [Test]
        public void Send_OverMaxPacketSize_RaisesInvalidSendAndDoesNotDispatch()
        {
            using ConnectedPair pair = CreateConnectedPair();
            int errorCount = 0;
            int receivedCount = 0;

            pair.Client.Transport.OnError += (_, error, _) =>
            {
                if (error == TransportError.InvalidSend)
                    errorCount++;
            };
            pair.Server.Transport.OnDataReceived += (_, _, _) => receivedCount++;

            int channelId = pair.Client.Transport.GetChannelId(NetworkChannel.Reliable);
            byte[] payload = new byte[pair.Client.Transport.GetMaxPacketSize(channelId) + 1];
            NetworkSendResult result = pair.Client.Transport.Send(pair.ServerConnection, new ArraySegment<byte>(payload), channelId);
            PollPair(pair);

            Assert.AreEqual(1, errorCount);
            Assert.AreEqual(0, receivedCount);
            Assert.AreEqual(NetworkSendStatus.PayloadTooLarge, result.Status);
        }

        [Test]
        public void Broadcast_DispatchesToRegisteredConnection()
        {
            using ConnectedPair pair = CreateConnectedPair();
            byte[] payload = { 10, 11 };
            byte[] received = new byte[payload.Length];
            int receivedCount = 0;

            pair.Client.Transport.OnDataReceived += (_, data, _) =>
            {
                receivedCount++;
                Buffer.BlockCopy(data.Array, data.Offset, received, 0, data.Count);
            };

            var connections = new INetConnection[] { pair.ClientConnection };
            NetworkSendResult result = pair.Server.Transport.Broadcast(connections, new ArraySegment<byte>(payload), pair.Server.Transport.GetChannelId(NetworkChannel.Reliable));
            PollPair(pair);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(payload.Length, result.BytesAccepted);
            Assert.AreEqual(1, receivedCount);
            CollectionAssert.AreEqual(payload, received);
        }

        [Test]
        public void Broadcast_NullConnections_FailsFast()
        {
            using ConnectedPair pair = CreateConnectedPair();
            byte[] payload = { 1 };

            Assert.Throws<ArgumentNullException>(() =>
                pair.Server.Transport.Broadcast(null, new ArraySegment<byte>(payload), pair.Server.Transport.GetChannelId(NetworkChannel.Reliable)));
        }

        [Test]
        public void Dispose_PreventsRestart_WhenTransportOwnsDisposableResources()
        {
            INetTransport transport = CreateTransport();

            if (transport is not IDisposable disposable)
            {
                Assert.Ignore("Transport does not expose IDisposable.");
                return;
            }

            disposable.Dispose();

            Assert.Throws<ObjectDisposedException>(() => transport.StartServer());

            NetworkLifecycleSnapshot snapshot = NetworkLifecycle.GetSnapshot(transport);
            if (transport is INetworkLifecycleProvider)
                Assert.AreEqual(NetworkLifecycleState.Disposed, snapshot.State);
        }

        protected TransportScope CreateScope()
        {
            return new TransportScope(CreateTransport());
        }

        protected ConnectedPair CreateConnectedPair()
        {
            var pair = new ConnectedPair(CreateScope(), CreateScope());

            pair.Server.Transport.OnClientConnected += connection =>
            {
                pair.ServerConnectedCount++;
                pair.ClientConnection = connection;
            };
            pair.Server.Transport.OnClientDisconnected += _ => pair.ServerDisconnectedCount++;
            pair.Client.Transport.OnConnectedToServer += () => pair.ClientConnectedCount++;
            pair.Client.Transport.OnDisconnectedFromServer += () => pair.ClientDisconnectedCount++;

            pair.Server.Transport.StartServer();
            pair.Client.Transport.StartClient(ConnectAddress);
            PollUntilConnected(pair);

            Assert.AreEqual(1, pair.ServerConnectedCount);
            Assert.AreEqual(1, pair.ClientConnectedCount);

            pair.ServerConnection = pair.ClientConnection;
            return pair;
        }

        protected void PollUntilConnected(ConnectedPair pair)
        {
            for (int i = 0; i < 8; i++)
            {
                PollPair(pair);
                if (pair.ServerConnectedCount > 0 && pair.ClientConnectedCount > 0)
                    return;
            }
        }

        protected static void PollPair(ConnectedPair pair)
        {
            Poll(pair.Server.Transport);
            Poll(pair.Client.Transport);
        }

        protected static void Poll(INetTransport transport)
        {
            if (transport is IPollableTransport pollable)
                pollable.PollEvents();
        }

        protected sealed class TransportScope : IDisposable
        {
            public TransportScope(INetTransport transport)
            {
                Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            }

            public INetTransport Transport { get; }

            public void Dispose()
            {
                Transport.Stop();
                if (Transport is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        protected sealed class ConnectedPair : IDisposable
        {
            public ConnectedPair(TransportScope server, TransportScope client)
            {
                Server = server;
                Client = client;
            }

            public TransportScope Server { get; }
            public TransportScope Client { get; }
            public INetConnection ServerConnection { get; set; }
            public INetConnection ClientConnection { get; set; }
            public int ServerConnectedCount { get; set; }
            public int ServerDisconnectedCount { get; set; }
            public int ClientConnectedCount { get; set; }
            public int ClientDisconnectedCount { get; set; }

            public void Dispose()
            {
                Client.Dispose();
                Server.Dispose();
            }
        }
    }

    public sealed class LocalLoopTransportContractTests : NetworkTransportContractTests
    {
        protected override INetTransport CreateTransport()
        {
            return new LocalLoopTransport();
        }
    }

    public sealed class NoopNetTransportLifecycleTests
    {
        [Test]
        public void Snapshot_ReportsNoRuntimeFeatures()
        {
            var transport = new NoopNetTransport();

            NetworkLifecycleSnapshot snapshot = NetworkLifecycle.GetSnapshot(transport);

            Assert.AreEqual(NetworkLifecycleState.Stopped, snapshot.State);
            Assert.AreEqual(NetworkBackendFeatures.None, snapshot.Features);
            Assert.IsFalse(snapshot.HasFeature(NetworkBackendFeatures.RealtimeTransport));
        }
    }

    public sealed class NetworkTickAndReplicationContractTests
    {
        [Test]
        public void NetworkTickRate_ConvertsSecondsToTicks()
        {
            var tickRate = new NetworkTickRate(60);

            Assert.AreEqual(new NetworkTickId(90), tickRate.SecondsToTick(1.5d));
            Assert.AreEqual(1.5d, tickRate.TickToSeconds(new NetworkTickId(90)), 0.000001d);
        }

        [Test]
        public void ManualNetworkTimeSource_AdvancesDeterministically()
        {
            var time = new ManualNetworkTimeSource(new NetworkTickRate(30));

            time.Advance(0.5d);
            Assert.AreEqual(new NetworkTickId(15), time.LocalTick);

            time.Advance(-1d);
            Assert.AreEqual(new NetworkTickId(15), time.LocalTick);

            time.Reset();
            Assert.AreEqual(NetworkTickId.Zero, time.LocalTick);
        }

        [Test]
        public void SnapshotHeader_RejectsInvalidDeltaBaseline()
        {
            var valid = new NetworkSnapshotHeader(
                new NetworkTickId(10),
                new NetworkTickId(8),
                sequence: 1,
                entityCount: 4,
                NetworkSnapshotFlags.Delta);

            var invalid = new NetworkSnapshotHeader(
                new NetworkTickId(10),
                new NetworkTickId(10),
                sequence: 1,
                entityCount: 4,
                NetworkSnapshotFlags.Delta);

            Assert.IsTrue(valid.IsValid());
            Assert.IsFalse(invalid.IsValid());
        }

        [Test]
        public void SnapshotAckStore_TracksAndRemovesByConnectionId()
        {
            var store = new NetworkSnapshotAckStore();
            var connection = new TestConnection(7);
            var ack = new NetworkSnapshotAck(new NetworkTickId(42), sequence: 9);

            store.SetLastAck(connection, ack);

            Assert.IsTrue(store.TryGetLastAck(new TestConnection(7), out NetworkSnapshotAck stored));
            Assert.AreEqual(ack.Tick, stored.Tick);
            Assert.AreEqual(ack.Sequence, stored.Sequence);

            store.SetLastAck(connection, new NetworkSnapshotAck(NetworkTickId.Invalid, sequence: 10));
            Assert.IsFalse(store.TryGetLastAck(connection, out _));

            store.SetLastAck(connection, ack);
            store.Remove(connection);

            Assert.AreEqual(0, store.Count);
            Assert.IsFalse(store.TryGetLastAck(connection, out _));
        }

        [Test]
        public void NetworkTickSystem_IgnoresDuplicateTickableRegistration()
        {
            var system = new NetworkTickSystem(tickRate: 30);
            var tickable = new CountingTickable();

            system.RegisterTickable(tickable);
            system.RegisterTickable(tickable);
            system.Update(system.TickInterval);

            Assert.AreEqual(1, tickable.TickCount);
        }

        [Test]
        public void NetworkTickSystem_UsesStableSnapshotDuringTickMutation()
        {
            var system = new NetworkTickSystem(tickRate: 30);
            var selfRemoving = new SelfRemovingTickable(system);

            system.RegisterTickable(selfRemoving);
            system.Update(system.TickInterval);
            system.Update(system.TickInterval);

            Assert.AreEqual(1, selfRemoving.TickCount);
        }

        [Test]
        public void DistanceInterestRule_FiltersByRadiusAndLayer()
        {
            var rule = new DistanceInterestRule();
            var observer = new NetworkInterestObserver(
                connection: null,
                position: NetworkVector3.Zero,
                radius: 10f,
                layerMask: 0b0011u);

            var targets = new List<NetworkInterestTarget>
            {
                new NetworkInterestTarget(1, new NetworkVector3(3f, 0f, 4f), 0b0001u),
                new NetworkInterestTarget(2, new NetworkVector3(12f, 0f, 0f), 0b0001u),
                new NetworkInterestTarget(3, new NetworkVector3(1f, 0f, 0f), 0b0100u)
            };
            var visible = new List<uint>(4);

            int count = NetworkInterestUtility.BuildVisibleSet(observer, targets, rule, visible);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1u, visible[0]);
        }

        [Test]
        public void GridInterestManager_RejectsInvalidCellSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridInterestManager(0f, 100f));
        }

        [Test]
        public void GridInterestManager_NullObserverGroupsClearsGroupVisibility()
        {
            var manager = new GridInterestManager(cellSize: 10f, visibilityRange: 1f);
            var connection = new TestConnection(7);
            var entity = new TestEntity(100u, new UnityEngine.Vector3(100f, 0f, 0f), ownerConnectionId: -1, alwaysRelevant: false, relevanceGroup: 3);
            manager.SetObserverPosition(connection.ConnectionId, UnityEngine.Vector3.zero);
            manager.SetObserverGroups(connection.ConnectionId, new HashSet<int> { 3 });

            Assert.IsTrue(manager.IsVisible(connection, entity));

            manager.SetObserverGroups(connection.ConnectionId, null);

            Assert.IsFalse(manager.IsVisible(connection, entity));
        }

        [Test]
        public void CompositeInterestManager_IgnoresDuplicateManagers()
        {
            var composite = new CompositeInterestManager();
            var child = new CountingInterestManager();

            composite.Add(child);
            composite.Add(child);
            composite.PreUpdate(Array.Empty<INetworkEntity>());

            Assert.AreEqual(1, child.PreUpdateCount);
        }

        private sealed class CountingTickable : ITickable
        {
            public int TickCount { get; private set; }

            public void OnNetworkTick(NetworkTick tick, float tickDeltaTime)
            {
                TickCount++;
            }
        }

        private sealed class SelfRemovingTickable : ITickable
        {
            private readonly NetworkTickSystem _system;

            public SelfRemovingTickable(NetworkTickSystem system)
            {
                _system = system;
            }

            public int TickCount { get; private set; }

            public void OnNetworkTick(NetworkTick tick, float tickDeltaTime)
            {
                TickCount++;
                _system.UnregisterTickable(this);
            }
        }

        private sealed class CountingInterestManager : IInterestManager
        {
            public int PreUpdateCount { get; private set; }

            public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities, HashSet<uint> results)
            {
                results.Clear();
            }

            public bool IsVisible(INetConnection connection, INetworkEntity entity)
            {
                return false;
            }

            public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
            {
                PreUpdateCount++;
            }
        }

        private sealed class TestEntity : INetworkEntity
        {
            public TestEntity(
                uint networkId,
                UnityEngine.Vector3 position,
                int ownerConnectionId,
                bool alwaysRelevant,
                int relevanceGroup)
            {
                NetworkId = networkId;
                Position = position;
                OwnerConnectionId = ownerConnectionId;
                AlwaysRelevant = alwaysRelevant;
                RelevanceGroup = relevanceGroup;
            }

            public uint NetworkId { get; }
            public UnityEngine.Vector3 Position { get; }
            public int OwnerConnectionId { get; }
            public bool AlwaysRelevant { get; }
            public int RelevanceGroup { get; }
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId)
            {
                ConnectionId = connectionId;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => string.Empty;
            public bool IsConnected => true;
            public bool IsAuthenticated => true;
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
