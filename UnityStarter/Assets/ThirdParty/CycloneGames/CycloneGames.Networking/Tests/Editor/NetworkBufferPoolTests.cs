using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Rpc;
using CycloneGames.Networking.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
            public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
            {
                if (message is RpcPayload payload)
                    LastRpcPayload = payload;
            }

            public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
            public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct { }
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
