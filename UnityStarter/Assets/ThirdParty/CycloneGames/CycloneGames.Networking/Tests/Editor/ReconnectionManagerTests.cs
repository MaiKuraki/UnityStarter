using System;
using CycloneGames.Networking.Session;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ReconnectionManagerTests
    {
        [Test]
        public void TryReconnect_WithValidToken_CompletesReconnect()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            var connection = new TestConnection { PlayerId = 100UL };

            Assert.IsTrue(manager.TryReconnect(connection, 10, token));
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void TryReconnect_Rejects_PlayerMismatch()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;

            var connection = new TestConnection { PlayerId = 200UL };

            Assert.IsFalse(manager.TryReconnect(connection, 10, token));
            Assert.AreEqual(ReconnectRejectReason.PlayerMismatch, rejectReason);
            Assert.IsTrue(manager.HasReservation(10));
        }

        [Test]
        public void TryReconnect_Rejects_ProtocolMismatch()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            var invalidToken = ReconnectToken.Create(
                token.OriginalConnectionId,
                token.PlayerId,
                protocolVersion: 4,
                token.Nonce);

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;

            Assert.IsFalse(manager.TryReconnect(new TestConnection { PlayerId = 100UL }, 10, invalidToken));
            Assert.AreEqual(ReconnectRejectReason.ProtocolMismatch, rejectReason);
        }

        [Test]
        public void Update_Expires_CatchingUp_Reservation()
        {
            var catchUp = new PendingCatchUp();
            var manager = new ReconnectionManager(catchUp)
            {
                ReconnectWindow = 1d
            };

            int expiredConnectionId = -1;
            manager.OnReconnectWindowExpired += id => expiredConnectionId = id;

            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);
            Assert.IsTrue(manager.TryReconnect(new TestConnection { PlayerId = 100UL }, 10, token));

            manager.Update(1.1d);

            Assert.AreEqual(10, expiredConnectionId);
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void CatchUpFailure_RemovesReservation_AndRaisesEvent()
        {
            var catchUp = new FailingCatchUp();
            var manager = new ReconnectionManager(catchUp);

            int failedConnectionId = -1;
            string failedReason = null;
            manager.OnCatchUpFailed += (id, reason) =>
            {
                failedConnectionId = id;
                failedReason = reason;
            };

            manager.OnClientDisconnected(10, 0d, 0UL, 0, out ReconnectToken token);

            Assert.IsTrue(manager.TryReconnect(new TestConnection(), 10, token));
            Assert.AreEqual(10, failedConnectionId);
            Assert.AreEqual("snapshot mismatch", failedReason);
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void TryReconnect_Rejects_DisconnectedConnection()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;

            Assert.IsFalse(manager.TryReconnect(new TestConnection { PlayerId = 100UL, Connected = false }, 10, token));
            Assert.AreEqual(ReconnectRejectReason.InvalidConnection, rejectReason);
        }

        [Test]
        public void TryReconnect_Rejects_UnauthenticatedConnection_WhenRequired()
        {
            var manager = new ReconnectionManager(requireAuthenticatedConnection: true);
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;

            Assert.IsFalse(manager.TryReconnect(new TestConnection
            {
                PlayerId = 100UL,
                Authenticated = false
            }, 10, token));
            Assert.AreEqual(ReconnectRejectReason.Unauthenticated, rejectReason);
        }

        private sealed class PendingCatchUp : IStateCatchUp
        {
            public void BeginCatchUp(INetConnection connection, int originalConnectionId,
                Action<float> onProgress, Action onComplete, Action<string> onFailed)
            {
                onProgress(0.5f);
            }
        }

        private sealed class FailingCatchUp : IStateCatchUp
        {
            public void BeginCatchUp(INetConnection connection, int originalConnectionId,
                Action<float> onProgress, Action onComplete, Action<string> onFailed)
            {
                onProgress(0.5f);
                onFailed("snapshot mismatch");
            }
        }

        private sealed class TestConnection : INetConnection
        {
            public int ConnectionId { get; set; }
            public string RemoteAddress { get; set; } = "test";
            public bool Connected { get; set; } = true;
            public bool Authenticated { get; set; } = true;
            public bool IsConnected => Connected;
            public bool IsAuthenticated => Authenticated;
            public int Ping => 0;
            public ConnectionQuality Quality => ConnectionQuality.Excellent;
            public double Jitter => 0d;
            public long BytesSent => 0L;
            public long BytesReceived => 0L;
            public ulong PlayerId { get; set; }

            public bool Equals(INetConnection other)
            {
                return ReferenceEquals(this, other);
            }
        }
    }
}
