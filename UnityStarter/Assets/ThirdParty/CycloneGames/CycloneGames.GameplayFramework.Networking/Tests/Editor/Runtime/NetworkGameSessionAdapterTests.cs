using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using NUnit.Framework;
using UnityEngine;
using PlayerLoginRequest = CycloneGames.GameplayFramework.Core.PlayerLoginRequest;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class NetworkGameSessionAdapterTests
    {
        [Test]
        public void AdmissionRequiresAndValidatesStagedConnection()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var request = new PlayerLoginRequest(42, "Remote", remoteAddress: "127.0.0.1");

            Assert.IsFalse(session.ApproveLogin(in request, out _));
            var connection = new TestConnection(7, "127.0.0.1");
            Assert.IsTrue(session.TryStageConnection(42, connection, out string stageError), stageError);
            Assert.IsTrue(session.ApproveLogin(in request, out string approvalError), approvalError);
            Assert.IsTrue(session.RemoveStagedConnection(42, connection));
            Assert.IsFalse(session.AtCapacity(spectator: false));
        }

        [Test]
        public void AdmissionRejectsDisconnectedOrUnauthenticatedConnection()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(8, "10.0.0.8")
            {
                IsConnectedValue = false,
                IsAuthenticatedValue = false,
            };
            var request = new PlayerLoginRequest(8, "Remote", remoteAddress: "10.0.0.8");

            Assert.IsTrue(session.TryStageConnection(8, connection, out _));
            Assert.IsFalse(session.ApproveLogin(in request, out _));
            connection.IsConnectedValue = true;
            Assert.IsFalse(session.ApproveLogin(in request, out _));
            connection.IsAuthenticatedValue = true;
            Assert.IsTrue(session.ApproveLogin(in request, out string error), error);
        }

        [Test]
        public void ConnectionIdentityAndAddressBanAreEnforced()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(9, "10.0.0.9");
            var sameConnectionId = new TestConnection(9, "10.0.0.9");

            Assert.IsTrue(session.TryStageConnection(9, connection, out _));
            Assert.IsFalse(session.TryStageConnection(10, sameConnectionId, out _));
            Assert.IsTrue(session.BanAddress("10.0.0.9"));
            var request = new PlayerLoginRequest(9, "Remote");
            Assert.IsFalse(session.ApproveLogin(in request, out _));
        }

        [Test]
        public void StagedConnectionRejectsChangedConnectionIdentity()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var connection = new TestConnection(9, "10.0.0.9");
            var request = new PlayerLoginRequest(9, "Remote", remoteAddress: "10.0.0.9");
            Assert.IsTrue(session.TryStageConnection(9, connection, out _));

            connection.ConnectionId = 10;

            Assert.IsFalse(session.ApproveLogin(in request, out _));
            Assert.IsTrue(session.RemoveStagedConnection(9, connection));
            Assert.AreEqual(0, session.StagedConnectionCount);
        }

        [Test]
        public void StagingRejectsNonPositiveConnectionIdentifiers()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);

            Assert.IsFalse(session.TryStageConnection(1, new TestConnection(0, "127.0.0.1"), out _));
            Assert.IsFalse(session.TryStageConnection(1, new TestConnection(-1, "127.0.0.1"), out _));
            Assert.AreEqual(0, session.StagedConnectionCount);
        }

        [Test]
        public void BindingRejectsNonPositiveConnectionIdentifiers()
        {
            var playerObject = new GameObject("PlayerController");
            try
            {
                PlayerController player = playerObject.AddComponent<PlayerController>();
                var session = new NetworkGameSessionAdapter(new ContainingSession(player));

                Assert.IsFalse(session.TryBindConnection(
                    player,
                    new TestConnection(0, "127.0.0.1"),
                    out _));
                Assert.IsFalse(session.TryBindConnection(
                    player,
                    new TestConnection(-1, "127.0.0.1"),
                    out _));
                Assert.AreEqual(0, session.BoundConnectionCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void AddressBanBudgetIsConfiguredPerSession()
        {
            var session = new NetworkGameSessionAdapter(maximumBannedAddressCount: 1);

            Assert.AreEqual(1, session.MaximumBannedAddressCount);
            Assert.IsTrue(session.BanAddress("10.0.0.1"));
            Assert.IsFalse(session.BanAddress("10.0.0.2"));
            Assert.IsFalse(session.BanAddress("10.0.0.1"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NetworkGameSessionAdapter(
                    maximumBannedAddressCount:
                        NetworkGameSessionAdapter.MaximumSupportedBannedAddressCount + 1));
        }

        [Test]
        public void ComposedSessionCapacityCannotExceedCoreCeiling()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NetworkGameSessionAdapter(new OversizedSession()));
        }

        [Test]
        public void MessageEndpointCannotChangeWhileConnectionsAreStaged()
        {
            var session = new NetworkGameSessionAdapter(maxPlayers: 2, maxSpectators: 0);
            var endpoint = new TestMessageEndpoint();
            session.SetMessageEndpoint(endpoint);
            Assert.IsTrue(session.TryStageConnection(11, new TestConnection(11, "10.0.0.11"), out _));

            Assert.DoesNotThrow(() => session.SetMessageEndpoint(endpoint));
            Assert.Throws<InvalidOperationException>(() =>
                session.SetMessageEndpoint(new TestMessageEndpoint()));
        }

        [Test]
        public void WorkerThreadAccessIsRejected()
        {
            var session = new NetworkGameSessionAdapter();
            Exception captured = null;
            using var completed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    session.TryStageConnection(1, new TestConnection(1, "127.0.0.1"), out _);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.Start();
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }

        private sealed class TestConnection : INetConnection
        {
            public TestConnection(int connectionId, string remoteAddress)
            {
                ConnectionId = connectionId;
                RemoteAddress = remoteAddress;
            }

            public int ConnectionId { get; set; }
            public string RemoteAddress { get; }
            public bool IsConnectedValue { get; set; } = true;
            public bool IsAuthenticatedValue { get; set; } = true;
            public bool IsConnected => IsConnectedValue;
            public bool IsAuthenticated => IsAuthenticatedValue;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Good;
            public double Jitter => 0d;
            public long BytesSent => 0;
            public long BytesReceived => 0;
            public ulong PlayerId { get; set; }
            public bool Equals(INetConnection other) => other != null && other.ConnectionId == ConnectionId;
        }

        private sealed class OversizedSession : IGameSession
        {
            public int MaxPlayers => ParticipantRoster.MaximumSupportedParticipants;
            public int MaxSpectators => 1;
            public int PlayerCount => 0;
            public int SpectatorCount => 0;
            public bool AtCapacity(bool spectator) => false;
            public bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage) { errorMessage = null; return true; }
            public bool TryRegisterPlayer(PlayerController player, bool spectator, out string errorMessage) { errorMessage = null; return true; }
            public bool ContainsPlayer(PlayerController player) => false;
            public bool UnregisterPlayer(PlayerController player) => false;
            public bool TrySetSpectatorStatus(PlayerController player, bool spectator, out string errorMessage) { errorMessage = null; return false; }
            public void HandleMatchHasStarted() { }
            public void HandleMatchHasEnded() { }
        }

        private sealed class ContainingSession : IGameSession
        {
            private readonly PlayerController player;

            public ContainingSession(PlayerController player)
            {
                this.player = player;
            }

            public int MaxPlayers => 1;
            public int MaxSpectators => 0;
            public int PlayerCount => 1;
            public int SpectatorCount => 0;
            public bool AtCapacity(bool spectator) => false;
            public bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage) { errorMessage = null; return true; }
            public bool TryRegisterPlayer(PlayerController candidate, bool spectator, out string errorMessage) { errorMessage = null; return ReferenceEquals(player, candidate); }
            public bool ContainsPlayer(PlayerController candidate) => ReferenceEquals(player, candidate);
            public bool UnregisterPlayer(PlayerController candidate) => ReferenceEquals(player, candidate);
            public bool TrySetSpectatorStatus(PlayerController candidate, bool spectator, out string errorMessage) { errorMessage = null; return false; }
            public void HandleMatchHasStarted() { }
            public void HandleMatchHasEnded() { }
        }

        private sealed class TestMessageEndpoint : INetworkMessageEndpoint
        {
            private readonly NetworkMessageHandlerRegistry handlers = new NetworkMessageHandlerRegistry();
            public INetTransport Transport => null;
            public bool IsAcceptingMessages => true;
            public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel) =>
                NetworkConstants.DefaultMaxPayloadSize;
            public NetworkMessageHandlerLease RegisterHandler(ushort messageId, NetworkMessageHandler handler) =>
                handlers.Register(messageId, handler);
            public NetworkSendResult SendToServer(ushort id, ReadOnlySpan<byte> payload, NetworkChannel channel = NetworkChannel.Reliable) => default;
            public NetworkSendResult SendToClient(INetConnection connection, ushort id, ReadOnlySpan<byte> payload, NetworkChannel channel = NetworkChannel.Reliable) => default;
            public NetworkSendResult BroadcastToClients(ushort id, ReadOnlySpan<byte> payload, NetworkChannel channel = NetworkChannel.Reliable) => default;
            public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, ushort id, ReadOnlySpan<byte> payload, NetworkChannel channel = NetworkChannel.Reliable) => default;
            public void Disconnect(INetConnection connection) { }
        }
    }
}
