using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Rpc;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Transports;
using CycloneGames.Networking.Unity.Runtime.Serialization;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkBufferPoolTests
    {
        [SetUp]
        public void SetUp()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.ResetConfiguration();
            SerializerFactory.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.ResetConfiguration();
            SerializerFactory.Reset();
        }

        [Test]
        public void Return_Ignores_Double_Dispose()
        {
            NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteInt(123);

            Assert.DoesNotThrow(() =>
            {
                buffer.Dispose();
                buffer.Dispose();
            });

            using NetworkBuffer next = NetworkBufferPool.Get();
            next.WriteInt(456);

            Assert.AreEqual(4, next.Position);
        }

        [Test]
        public void Configure_MaxPoolSizeZero_ReleasesReturnedBuffers()
        {
            NetworkBufferPool.Configure(maxPoolSize: 0, clearBuffersOnReturn: true);

            NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteInt(123);
            buffer.Dispose();

            Assert.AreEqual(0, NetworkBufferPool.Count);
        }

        [Test]
        public void Configure_ClearBuffersOnReturn_ClearsRetainedBuffer()
        {
            NetworkBufferPool.Configure(maxPoolSize: 1, clearBuffersOnReturn: true);

            NetworkBuffer first = NetworkBufferPool.Get();
            first.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            first.Dispose();

            using NetworkBuffer second = NetworkBufferPool.Get();
            second.Position = 8;
            ArraySegment<byte> segment = second.ToArraySegment();

            for (int i = 0; i < segment.Count; i++)
                Assert.AreEqual(0, segment.Array[segment.Offset + i]);
        }

        [Test]
        public void WriteBytes_RejectsPayloadsBeyondNetworkBufferCapacity()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            byte[] oversizedPayload = new byte[NetworkConstants.MaxMTU + 1];

            Assert.Throws<InvalidOperationException>(() => buffer.WriteBytes(oversizedPayload));
        }

        [Test]
        public void SerializerFactory_Freeze_BlocksRuntimeMutation()
        {
            SerializerFactory.RegisterCreator(SerializerType.Json, () => DummySerializer.Instance);
            SerializerFactory.Freeze();

            Assert.Throws<InvalidOperationException>(() =>
                SerializerFactory.RegisterCreator(SerializerType.MessagePack, () => DummySerializer.Instance));

            Assert.AreSame(DummySerializer.Instance, SerializerFactory.GetDefault());
        }

        [Test]
        public void SerializerFactory_Reset_ClearsFrozenRegistry()
        {
            SerializerFactory.RegisterCreator(SerializerType.Json, () => DummySerializer.Instance);
            SerializerFactory.Freeze();

            SerializerFactory.Reset();

            Assert.IsFalse(SerializerFactory.IsFrozen);
            Assert.IsFalse(SerializerFactory.IsAvailable(SerializerType.Json));
        }

        [Test]
        public void SerializerFactory_Freeze_IsStableUnderConcurrentMutationAttempts()
        {
            SerializerFactory.RegisterCreator(SerializerType.Json, () => DummySerializer.Instance);
            SerializerFactory.Freeze();

            int failures = 0;
            Parallel.For(0, 64, _ =>
            {
                try
                {
                    SerializerFactory.RegisterCreator(SerializerType.MessagePack, () => DummySerializer.Instance);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref failures);
                }
            });

            Assert.AreEqual(64, failures);
            Assert.IsTrue(SerializerFactory.IsFrozen);
            Assert.IsFalse(SerializerFactory.IsAvailable(SerializerType.MessagePack));
        }

        [Test]
        public void UnityJsonSerializer_WriterRoundTripsThroughNetworkBuffer()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            var value = new JsonRoundTripData
            {
                A = 17,
                B = 2.5f
            };

            UnityJsonSerializerAdapter.Instance.Serialize(value, buffer);
            Assert.Greater(buffer.Position, 0);

            buffer.FlipForRead();
            JsonRoundTripData result = UnityJsonSerializerAdapter.Instance.Deserialize<JsonRoundTripData>(buffer);

            Assert.AreEqual(value.A, result.A);
            Assert.AreEqual(value.B, result.B);
        }

        [Test]
        public void RpcPayload_FromBlittable_UsesInlineStorageForSmallStructs()
        {
            var value = new SmallRpcData
            {
                A = 10,
                B = 20
            };

            RpcPayload payload = RpcPayload.FromBlittable(123, value);

            Assert.IsTrue(payload.IsInline);
            Assert.IsNull(payload.Data);
            Assert.AreEqual(8, payload.Length);
        }

        [Test]
        public void RpcPayload_FromBlittable_UsesHeapStorageForLargeStructs()
        {
            var value = new LargeRpcData
            {
                A = 1,
                B = 2,
                C = 3,
                D = 4,
                E = 5
            };

            RpcPayload payload = RpcPayload.FromBlittable(124, value);

            Assert.IsFalse(payload.IsInline);
            Assert.IsNotNull(payload.Data);
            Assert.AreEqual(40, payload.Length);
        }

        [Test]
        public void RpcProcessor_Register_AssignsFirstAutoIdFromRpcRange()
        {
            var network = new CapturingNetworkManager();
            var processor = new RpcProcessor(network);

            ushort rpcId = processor.Register<SmallRpcData>((_, _) => { });

            Assert.AreEqual(NetworkConstants.RpcMsgIdMin, rpcId);
        }

        [Test]
        public void MessageRanges_ModuleRanges_DoNotOverlapRpcRange()
        {
            Assert.Greater(NetworkConstants.ModuleMsgIdMin, NetworkConstants.RpcMsgIdMax);
            Assert.Greater(NetworkConstants.UserMsgIdMin, NetworkConstants.ModuleMsgIdMax);

            Assert.IsTrue(NetworkMessageRanges.TryGetKnownRange(
                NetworkConstants.ModuleMsgIdMin,
                out NetworkMessageIdRange range));
            Assert.AreEqual(NetworkMessageKind.Module, range.Kind);
        }

        [Test]
        public void NetworkMessageCatalog_Fingerprint_IsIndependentOfRegistrationOrder()
        {
            NetworkMessageDescriptor first = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.UserMsgIdMin,
                "test",
                NetworkMessageKind.User);
            NetworkMessageDescriptor second = NetworkMessageDescriptor.Create<LargeRpcData>(
                NetworkConstants.UserMsgIdMin + 1,
                "test",
                NetworkMessageKind.User,
                NetworkChannel.Unreliable);
            var userRange = new NetworkMessageIdRange(
                "test",
                NetworkConstants.UserMsgIdMin,
                NetworkConstants.UserMsgIdMin + 9,
                NetworkMessageKind.User);
            var moduleA = new NetworkMessageIdRange(
                "test.a",
                NetworkConstants.ModuleMsgIdMin,
                NetworkConstants.ModuleMsgIdMin + 9,
                NetworkMessageKind.Module);
            var moduleB = new NetworkMessageIdRange(
                "test.b",
                NetworkConstants.ModuleMsgIdMin + 10,
                NetworkConstants.ModuleMsgIdMin + 19,
                NetworkMessageKind.Module);

            var catalogA = new NetworkMessageCatalog();
            var catalogB = new NetworkMessageCatalog();

            catalogA.RegisterModuleRange(moduleA);
            catalogA.RegisterModuleRange(moduleB);
            catalogA.RegisterRange(userRange);
            catalogA.Register(first);
            catalogA.Register(second);
            catalogB.RegisterModuleRange(moduleB);
            catalogB.RegisterModuleRange(moduleA);
            catalogB.RegisterRange(userRange);
            catalogB.Register(second);
            catalogB.Register(first);

            Assert.AreEqual(catalogA.ProtocolFingerprint, catalogB.ProtocolFingerprint);
        }

        [Test]
        public void NetworkMessageCatalog_ModuleRangeRegistration_RejectsOverlaps()
        {
            var catalog = new NetworkMessageCatalog();
            var first = new NetworkMessageIdRange(
                "package.first",
                NetworkConstants.ModuleMsgIdMin,
                NetworkConstants.ModuleMsgIdMin + 99,
                NetworkMessageKind.Module);
            var overlap = new NetworkMessageIdRange(
                "package.overlap",
                NetworkConstants.ModuleMsgIdMin + 50,
                NetworkConstants.ModuleMsgIdMin + 150,
                NetworkMessageKind.Module);
            var sameOwnerNonOverlapping = new NetworkMessageIdRange(
                "package.first",
                NetworkConstants.ModuleMsgIdMin + 100,
                NetworkConstants.ModuleMsgIdMin + 199,
                NetworkMessageKind.Module);

            Assert.IsTrue(catalog.TryRegisterModuleRange(first));
            Assert.IsTrue(catalog.TryRegisterModuleRange(first));
            Assert.AreEqual(1, catalog.ModuleRangeCount);
            Assert.IsFalse(catalog.TryRegisterModuleRange(overlap));
            Assert.IsTrue(catalog.TryRegisterModuleRange(sameOwnerNonOverlapping));
            Assert.AreEqual(2, catalog.ModuleRangeCount);
            Assert.IsTrue(catalog.TryGetRegisteredModuleRange(
                NetworkConstants.ModuleMsgIdMin + 1,
                out NetworkMessageIdRange range));
            Assert.AreEqual(first.Name, range.Name);
        }

        [Test]
        public void NetworkMessageCatalog_UserRangeRegistration_RejectsOverlaps()
        {
            var catalog = new NetworkMessageCatalog();
            var first = new NetworkMessageIdRange(
                "project.first",
                NetworkConstants.UserMsgIdMin,
                NetworkConstants.UserMsgIdMin + 99,
                NetworkMessageKind.User);
            var overlap = new NetworkMessageIdRange(
                "project.second",
                NetworkConstants.UserMsgIdMin + 50,
                NetworkConstants.UserMsgIdMin + 150,
                NetworkMessageKind.User);
            var nonOverlapping = new NetworkMessageIdRange(
                "project.second",
                NetworkConstants.UserMsgIdMin + 100,
                NetworkConstants.UserMsgIdMin + 199,
                NetworkMessageKind.User);

            Assert.IsTrue(catalog.TryRegisterRange(first));
            Assert.IsTrue(catalog.TryRegisterRange(first));
            Assert.AreEqual(1, catalog.RangeCount);
            Assert.IsFalse(catalog.TryRegisterRange(overlap));
            Assert.IsTrue(catalog.TryRegisterRange(nonOverlapping));
            Assert.AreEqual(2, catalog.RangeCount);
            Assert.IsTrue(catalog.TryGetRegisteredRange(
                NetworkConstants.UserMsgIdMin + 1,
                out NetworkMessageIdRange range));
            Assert.AreEqual(first.Name, range.Name);
        }

        [Test]
        public void NetworkMessageCatalog_ModuleDescriptors_RequireRegisteredOwnerRange()
        {
            var catalog = new NetworkMessageCatalog();
            var first = new NetworkMessageIdRange(
                "package.first",
                NetworkConstants.ModuleMsgIdMin,
                NetworkConstants.ModuleMsgIdMin + 99,
                NetworkMessageKind.Module);
            NetworkMessageDescriptor missingRangeDescriptor = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.ModuleMsgIdMin,
                first.Name,
                NetworkMessageKind.Module);

            Assert.Throws<ArgumentException>(() => catalog.TryRegister(missingRangeDescriptor));

            catalog.RegisterModuleRange(first);

            NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.ModuleMsgIdMin,
                first.Name,
                NetworkMessageKind.Module);
            NetworkMessageDescriptor wrongOwner = NetworkMessageDescriptor.Create<LargeRpcData>(
                NetworkConstants.ModuleMsgIdMin + 1,
                "package.other",
                NetworkMessageKind.Module);

            Assert.IsTrue(catalog.TryRegister(descriptor));
            Assert.Throws<ArgumentException>(() => catalog.TryRegister(wrongOwner));
        }

        [Test]
        public void NetworkMessageCatalog_UserDescriptors_RequireRegisteredOwnerRange()
        {
            var catalog = new NetworkMessageCatalog();
            var first = new NetworkMessageIdRange(
                "project.first",
                NetworkConstants.UserMsgIdMin,
                NetworkConstants.UserMsgIdMin + 99,
                NetworkMessageKind.User);
            NetworkMessageDescriptor missingRangeDescriptor = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.UserMsgIdMin,
                first.Name,
                NetworkMessageKind.User);

            Assert.Throws<ArgumentException>(() => catalog.TryRegister(missingRangeDescriptor));

            catalog.RegisterRange(first);

            NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.UserMsgIdMin,
                first.Name,
                NetworkMessageKind.User);
            NetworkMessageDescriptor wrongOwner = NetworkMessageDescriptor.Create<LargeRpcData>(
                NetworkConstants.UserMsgIdMin + 1,
                "project.other",
                NetworkMessageKind.User);

            Assert.IsTrue(catalog.TryRegister(descriptor));
            Assert.Throws<ArgumentException>(() => catalog.TryRegister(wrongOwner));
        }

        [Test]
        public void NetworkMessageCatalog_RejectsDuplicateMessageIds()
        {
            var catalog = new NetworkMessageCatalog();
            var userRange = new NetworkMessageIdRange(
                "test",
                NetworkConstants.UserMsgIdMin,
                NetworkConstants.UserMsgIdMin,
                NetworkMessageKind.User);
            NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<SmallRpcData>(
                NetworkConstants.UserMsgIdMin,
                "test",
                NetworkMessageKind.User);

            catalog.RegisterRange(userRange);
            Assert.IsTrue(catalog.TryRegister(descriptor));
            Assert.IsFalse(catalog.TryRegister(descriptor));
        }

        [Test]
        public void RpcProcessor_DispatchesInlinePayloadWithoutDataArray()
        {
            var network = new CapturingNetworkManager();
            var processor = new RpcProcessor(network);
            SmallRpcData received = default;
            var connection = new TestConnection(7);

            processor.RegisterWithId<SmallRpcData>(150, (_, data) => received = data);
            processor.Send(150, new SmallRpcData { A = 11, B = 22 });

            Assert.IsTrue(network.LastRpcPayload.IsInline);
            Assert.IsNull(network.LastRpcPayload.Data);

            network.Deliver(connection, 150, network.LastRpcPayload);

            Assert.AreEqual(11, received.A);
            Assert.AreEqual(22, received.B);
        }

        [Test]
        public void NetworkRuntimeContext_RegistersBackendServicesWithoutConcreteSdkDependency()
        {
            var network = new CapturingNetworkManager();
            var sessionService = new TestSessionService();
            var matchService = new TestMatchStateService();

            using INetworkRuntimeContext context = new NetworkRuntimeContext(
                    new NetworkRuntimeId(1001UL),
                    "test-backend",
                    network)
                .AddService<INetworkSessionService>(sessionService)
                .AddService<INetworkMatchStateService>(matchService)
                .AddFeature(NetworkBackendFeatures.AuthSession)
                .AddFeature(NetworkBackendFeatures.MatchState)
                .AddFeature(NetworkBackendFeatures.BackendRpc)
                .Build();

            Assert.IsTrue(context.IsFrozen);
            Assert.IsTrue((context.Features & NetworkBackendFeatures.AuthSession) != 0);
            Assert.IsTrue((context.Features & NetworkBackendFeatures.MatchState) != 0);
            Assert.IsTrue(context.TryGetService(out INetworkSessionService resolvedSession));
            Assert.AreSame(sessionService, resolvedSession);
            Assert.IsTrue(context.TryGetService(out INetworkMatchStateService resolvedMatch));
            Assert.AreSame(matchService, resolvedMatch);
            Assert.IsTrue(context.TryGetService(out INetworkMessageCatalog catalog));
            Assert.IsNotNull(catalog);
        }

        [Test]
        public void NetworkRuntimeId_FromAsciiCode_RoundTripsReadableIds()
        {
            NetworkRuntimeId mirror = NetworkRuntimeId.FromAsciiCode("Mirror");

            Assert.AreEqual(NetworkRuntimeIds.Mirror, mirror);
            Assert.IsTrue(mirror.TryGetAsciiCode(out string code));
            Assert.AreEqual("Mirror", code);
            Assert.AreEqual("Mirror", mirror.ToString());
            Assert.AreEqual("0x00000000000003E9", new NetworkRuntimeId(1001UL).ToString());
        }

        [Test]
        public void NetworkRuntimeContext_Dispose_ReleasesRegisteredServiceReferences()
        {
            var context = new NetworkRuntimeContext(
                    new NetworkRuntimeId(1002UL),
                    "test-backend",
                    new CapturingNetworkManager())
                .AddService<INetworkSessionService>(new TestSessionService())
                .Build();

            context.Dispose();

            Assert.IsFalse(context.TryGetService(out INetworkSessionService _));
        }

        [Test]
        public void BandwidthTrackingTransport_Dispose_UnsubscribesForwardedEvents()
        {
            var inner = new EventSourceTransport();
            var wrapper = new BandwidthTrackingTransport(inner);
            var connection = new TestConnection(42);
            byte[] payload = { 1, 2, 3 };
            int connectedToServerCount = 0;
            int clientConnectedCount = 0;
            int disconnectedFromServerCount = 0;
            int clientDisconnectedCount = 0;
            int dataReceivedCount = 0;
            int errorCount = 0;

            wrapper.OnConnectedToServer += () => connectedToServerCount++;
            wrapper.OnClientConnected += _ => clientConnectedCount++;
            wrapper.OnDisconnectedFromServer += () => disconnectedFromServerCount++;
            wrapper.OnClientDisconnected += _ => clientDisconnectedCount++;
            wrapper.OnDataReceived += (_, _, _) => dataReceivedCount++;
            wrapper.OnError += (_, _, _) => errorCount++;

            inner.RaiseConnectedToServer();
            inner.RaiseClientConnected(connection);
            inner.RaiseDisconnectedFromServer();
            inner.RaiseClientDisconnected(connection);
            inner.RaiseDataReceived(connection, new ArraySegment<byte>(payload), 0);
            inner.RaiseError(connection, TransportError.InvalidSend, "before dispose");

            Assert.AreEqual(1, connectedToServerCount);
            Assert.AreEqual(1, clientConnectedCount);
            Assert.AreEqual(1, disconnectedFromServerCount);
            Assert.AreEqual(1, clientDisconnectedCount);
            Assert.AreEqual(1, dataReceivedCount);
            Assert.AreEqual(1, errorCount);

            wrapper.Dispose();
            inner.RaiseConnectedToServer();
            inner.RaiseClientConnected(connection);
            inner.RaiseDisconnectedFromServer();
            inner.RaiseClientDisconnected(connection);
            inner.RaiseDataReceived(connection, new ArraySegment<byte>(payload), 0);
            inner.RaiseError(connection, TransportError.InvalidSend, "after dispose");

            Assert.AreEqual(1, connectedToServerCount);
            Assert.AreEqual(1, clientConnectedCount);
            Assert.AreEqual(1, disconnectedFromServerCount);
            Assert.AreEqual(1, clientDisconnectedCount);
            Assert.AreEqual(1, dataReceivedCount);
            Assert.AreEqual(1, errorCount);
        }

        private sealed class DummySerializer : INetSerializer
        {
            public static readonly DummySerializer Instance = new DummySerializer();

            public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
            {
                writtenBytes = 0;
            }

            public void Serialize<T>(in T value, INetWriter writer) where T : struct { }
            public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct => default;
            public T Deserialize<T>(INetReader reader) where T : struct => default;
        }

        private struct SmallRpcData
        {
            public int A;
            public int B;
        }

        private struct LargeRpcData
        {
            public long A;
            public long B;
            public long C;
            public long D;
            public long E;
        }

        [Serializable]
        private struct JsonRoundTripData
        {
            public int A;
            public float B;
        }

        private sealed class CapturingNetworkManager : INetworkManager
        {
            private Action<INetConnection, RpcPayload> _handler;

            public RpcPayload LastRpcPayload;
            public INetTransport Transport => null;
            public INetSerializer Serializer => DummySerializer.Instance;

            public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
            {
                if (typeof(T) == typeof(RpcPayload))
                    _handler = (conn, payload) => handler(conn, (T)(object)payload);
            }

            public void UnregisterHandler(ushort msgId) { }
            public NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                if (message is RpcPayload payload)
                    LastRpcPayload = payload;

                return NetworkSendResult.Accepted(0, 0);
            }

            public NetworkSendResult SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                return NetworkSendResult.Accepted(0, 0, connection);
            }

            public NetworkSendResult BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, 0);
            }

            public NetworkSendResult Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, connections?.Count ?? 0, 0);
            }
            public void DisconnectClient(INetConnection connection) { }

            public void Deliver(INetConnection connection, ushort msgId, RpcPayload payload)
            {
                _handler?.Invoke(connection, payload);
            }
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId)
            {
                ConnectionId = connectionId;
            }

            public int ConnectionId { get; }
            public string RemoteAddress => "test";
            public bool IsConnected => true;
            public bool IsAuthenticated => true;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Excellent;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }
            public bool Equals(INetConnection other) => other != null && other.ConnectionId == ConnectionId;
        }

        private sealed class EventSourceTransport : INetTransport, IDisposable
        {
            public bool IsServer => false;
            public bool IsClient => false;
            public bool IsRunning => false;
            public bool IsEncrypted => false;
            public bool Available => true;
            public NetworkTransportCapabilities Capabilities => NetworkTransportCapabilities.None;

            public event Action<INetConnection> OnClientConnected;
            public event Action<INetConnection> OnClientDisconnected;
            public event Action OnConnectedToServer;
            public event Action OnDisconnectedFromServer;
            public event Action<INetConnection, TransportError, string> OnError;
            public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

            public void RaiseClientConnected(INetConnection connection)
            {
                OnClientConnected?.Invoke(connection);
            }

            public void RaiseClientDisconnected(INetConnection connection)
            {
                OnClientDisconnected?.Invoke(connection);
            }

            public void RaiseConnectedToServer()
            {
                OnConnectedToServer?.Invoke();
            }

            public void RaiseDisconnectedFromServer()
            {
                OnDisconnectedFromServer?.Invoke();
            }

            public void RaiseDataReceived(INetConnection connection, ArraySegment<byte> payload, int channelId)
            {
                OnDataReceived?.Invoke(connection, payload, channelId);
            }

            public void RaiseError(INetConnection connection, TransportError error, string message)
            {
                OnError?.Invoke(connection, error, message);
            }

            public void StartServer() { }
            public void StartClient(string address) { }
            public void Stop() { }
            public void Disconnect(INetConnection connection) { }
            public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
            {
                return NetworkSendResult.Accepted(payload.Count, channelId, connection);
            }

            public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
            {
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, payload.Count * (connections?.Count ?? 0), connections?.Count ?? 0, channelId);
            }
            public int GetChannelId(NetworkChannel channel) => 0;
            public int GetMaxPacketSize(int channelId) => NetworkConstants.DefaultMTU;
            public NetworkStatistics GetStatistics() => default;
            public void Dispose() { }
        }

        private sealed class TestSessionService : INetworkSessionService
        {
            public bool HasSession => true;
            public INetworkSession CurrentSession { get; } = new TestSession();
            public void ClearSession() { }
        }

        private sealed class TestSession : INetworkSession
        {
            public string UserId => "user";
            public string Username => "name";
            public bool IsExpired => false;
            public long ExpireTimeUnixSeconds => long.MaxValue;
        }

        private sealed class TestMatchStateService : INetworkMatchStateService
        {
            public event Action<NetworkMatchId, INetConnection, ArraySegment<byte>, long> OnMatchState
            {
                add { }
                remove { }
            }
            public NetworkMatchId CurrentMatchId { get; } = new NetworkMatchId("match");
            public bool IsInMatch => true;

            public bool TrySendMatchState(NetworkMatchId matchId, long operationCode, in ArraySegment<byte> payload, NetworkChannel channel = NetworkChannel.Reliable)
            {
                return matchId.IsValid;
            }

            public void LeaveMatch(NetworkMatchId matchId) { }
        }
    }
}
