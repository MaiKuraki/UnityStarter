using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Compression;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Transports;
using CycloneGames.Networking.Unity.Runtime.Serialization;
using UnityEngine;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkBufferPoolTests
    {
        [SetUp]
        public void SetUp()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        [Test]
        public void Return_RejectsDoubleDisposeAndStaleLease()
        {
            int invalidReturnsBefore = NetworkBufferPool.InvalidReturnCount;
            NetworkBuffer buffer = NetworkBufferPool.Get();
            NetworkBuffer staleCopy = buffer;
            buffer.WriteInt(123);

            Assert.AreEqual(1, NetworkBufferPool.OutstandingCount);
            buffer.Dispose();

            Assert.AreEqual(0, NetworkBufferPool.OutstandingCount);
            Assert.Throws<ObjectDisposedException>(() => staleCopy.WriteInt(456));
            Assert.Throws<ObjectDisposedException>(() => staleCopy.Dispose());
            Assert.AreEqual(invalidReturnsBefore + 1, NetworkBufferPool.InvalidReturnCount);

            using NetworkBuffer next = NetworkBufferPool.Get();
            next.WriteInt(456);

            Assert.AreEqual(4, next.Position);
            Assert.Throws<ObjectDisposedException>(() => staleCopy.WriteByte(1));
        }

        [Test]
        public void Return_ConcurrentCopiesAcceptsExactlyOneReturn()
        {
            int invalidReturnsBefore = NetworkBufferPool.InvalidReturnCount;
            NetworkBuffer buffer = NetworkBufferPool.Get();
            NetworkBuffer firstCopy = buffer;
            NetworkBuffer secondCopy = buffer;
            int successfulReturns = 0;
            int rejectedReturns = 0;

            Parallel.Invoke(
                () =>
                {
                    try
                    {
                        firstCopy.Dispose();
                        Interlocked.Increment(ref successfulReturns);
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref rejectedReturns);
                    }
                },
                () =>
                {
                    try
                    {
                        secondCopy.Dispose();
                        Interlocked.Increment(ref successfulReturns);
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref rejectedReturns);
                    }
                });

            Assert.AreEqual(1, successfulReturns);
            Assert.AreEqual(1, rejectedReturns);
            Assert.AreEqual(0, NetworkBufferPool.OutstandingCount);
            Assert.AreEqual(1, NetworkBufferPool.Count);
            Assert.AreEqual(invalidReturnsBefore + 1, NetworkBufferPool.InvalidReturnCount);
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
        public void ForwardSeek_Initializes_Bytes_Exposed_By_A_Reused_Lease()
        {
            NetworkBufferPool.Configure(maxPoolSize: 1, clearBuffersOnReturn: false);

            NetworkBuffer first = NetworkBufferPool.Get();
            first.WriteBytes(new byte[] { 91, 92, 93, 94, 95, 96, 97, 98 });
            first.Dispose();

            using NetworkBuffer second = NetworkBufferPool.Get();
            second.Position = 8;
            ArraySegment<byte> segment = second.ToArraySegment();

            for (int i = 0; i < segment.Count; i++)
                Assert.AreEqual(0, segment.Array[segment.Offset + i]);
        }

        [Test]
        public void ReadMode_Position_Cannot_Move_Beyond_Payload()
        {
            using NetworkBuffer buffer = NetworkBufferPool.GetWithData(new byte[] { 7 });

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Position = 2);
            Assert.AreEqual(1, buffer.Remaining);

            buffer.Position = 1;
            Assert.AreEqual(0, buffer.Remaining);
            Assert.Throws<InvalidOperationException>(() => buffer.WriteByte(8));
        }

        [Test]
        public void GetWithData_FailureReturnsOutstandingLease()
        {
            int outstandingBefore = NetworkBufferPool.OutstandingCount;
            byte[] oversizedPayload = new byte[ushort.MaxValue + 1];

            Assert.Throws<InvalidOperationException>(() =>
                NetworkBufferPool.GetWithData(new ReadOnlySpan<byte>(oversizedPayload)));

            Assert.AreEqual(outstandingBefore, NetworkBufferPool.OutstandingCount);
            Assert.AreEqual(1, NetworkBufferPool.Count);
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
        public void PrimitiveWireFields_AreLittleEndian()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteInt(0x01020304);
            buffer.WriteFloat(1f);

            ArraySegment<byte> payload = buffer.ToArraySegment();
            CollectionAssert.AreEqual(
                new byte[] { 0x04, 0x03, 0x02, 0x01, 0x00, 0x00, 0x80, 0x3F },
                payload);
        }

        [Test]
        public void DeltaCompressor_FullBaselineUsesExplicitLittleEndianFields()
        {
            var sender = new DeltaCompressor();
            var receiver = new DeltaCompressor();
            var position = new Vector3(1f, 2f, 3f);
            var rotation = new Quaternion(0f, 0f, 0f, 1f);
            using NetworkBuffer buffer = NetworkBufferPool.Get();

            DeltaFlags flags = sender.WriteDelta(buffer, position, rotation);
            ArraySegment<byte> payload = buffer.ToArraySegment();

            Assert.AreEqual(DeltaFlags.FullPosition | DeltaFlags.FullRotation, flags);
            Assert.AreEqual(29, payload.Count);
            Assert.AreEqual((byte)flags, payload.Array[payload.Offset]);
            CollectionAssert.AreEqual(
                new byte[] { 0x00, 0x00, 0x80, 0x3F },
                new ArraySegment<byte>(payload.Array, payload.Offset + 1, 4));

            buffer.FlipForRead();
            receiver.ReadDelta(buffer, out Vector3 decodedPosition, out Quaternion decodedRotation);

            Assert.AreEqual(position, decodedPosition);
            Assert.AreEqual(rotation, decodedRotation);
            Assert.AreEqual(0, buffer.Remaining);
        }

        [Test]
        public void DeltaCompressor_ReadDeltaRejectsMalformedFlags()
        {
            var receiver = new DeltaCompressor();
            using NetworkBuffer unsupported = NetworkBufferPool.Get();
            unsupported.WriteByte((byte)DeltaFlags.DeltaRotation);
            unsupported.FlipForRead();

            Assert.Throws<FormatException>(() => receiver.ReadDelta(unsupported, out _, out _));

            using NetworkBuffer conflicting = NetworkBufferPool.Get();
            conflicting.WriteByte((byte)(DeltaFlags.FullPosition | DeltaFlags.DeltaPosition | DeltaFlags.FullRotation));
            conflicting.FlipForRead();

            Assert.Throws<FormatException>(() => receiver.ReadDelta(conflicting, out _, out _));
        }

        [Test]
        public void QuantizedVector3_ReadFromRejectsOverflowingVarInt()
        {
            using NetworkBuffer buffer = NetworkBufferPool.GetWithData(
                new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 });

            Assert.Throws<FormatException>(() => QuantizedVector3.ReadFrom(buffer));
        }

        [Test]
        public void NetworkRuntimeContext_RegistersBackendServicesWithoutConcreteSdkDependency()
        {
            var network = new CapturingMessageEndpoint();
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
                    new CapturingMessageEndpoint())
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

        [Serializable]
        private struct JsonRoundTripData
        {
            public int A;
            public float B;
        }

        private sealed class CapturingMessageEndpoint : INetworkMessageEndpoint
        {
            private readonly NetworkMessageHandlerRegistry _handlers = new NetworkMessageHandlerRegistry();

            public INetTransport Transport => null;
            public bool IsAcceptingMessages => true;

            public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel) =>
                NetworkConstants.DefaultMaxPayloadSize;

            public NetworkMessageHandlerLease RegisterHandler(
                ushort messageId,
                NetworkMessageHandler handler) => _handlers.Register(messageId, handler);

            public NetworkSendResult SendToServer(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => NetworkSendResult.Accepted(payload.Length, 0);

            public NetworkSendResult SendToClient(
                INetConnection connection,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) => NetworkSendResult.Accepted(payload.Length, 0, connection);

            public NetworkSendResult BroadcastToClients(
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) =>
                NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, payload.Length, 0, 0);

            public NetworkSendResult Broadcast(
                IReadOnlyList<INetConnection> connections,
                ushort messageId,
                ReadOnlySpan<byte> payload,
                NetworkChannel channel = NetworkChannel.Reliable) =>
                NetworkSendResult.Broadcast(
                    NetworkSendStatus.Accepted,
                    payload.Length * (connections?.Count ?? 0),
                    connections?.Count ?? 0,
                    0);

            public void Disconnect(INetConnection connection) { }
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
