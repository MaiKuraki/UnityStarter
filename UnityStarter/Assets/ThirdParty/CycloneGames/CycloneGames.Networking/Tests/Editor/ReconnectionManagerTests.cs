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

            Assert.IsTrue(manager.TryReconnect(connection, 10, token, 0.5d));
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

            Assert.IsFalse(manager.TryReconnect(connection, 10, token, 0.5d));
            Assert.AreEqual(ReconnectRejectReason.PlayerMismatch, rejectReason);
            Assert.IsTrue(manager.HasReservation(10));
        }

        [Test]
        public void TryReconnect_Rejects_MissingPlayerIdentityForBoundReservation()
        {
            var manager = new ReconnectionManager(requireAuthenticatedConnection: true);
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;

            Assert.IsFalse(manager.TryReconnect(
                new TestConnection { PlayerId = 0UL, Authenticated = true },
                10,
                token,
                0.5d));
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

            Assert.IsFalse(manager.TryReconnect(new TestConnection { PlayerId = 100UL }, 10, invalidToken, 0.5d));
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
            Assert.IsTrue(manager.TryReconnect(new TestConnection { PlayerId = 100UL }, 10, token, 0.5d));

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
                Assert.IsFalse(manager.HasReservation(id));
                failedConnectionId = id;
                failedReason = reason;
            };

            manager.OnClientDisconnected(10, 0d, 0UL, 0, out ReconnectToken token);

            Assert.IsTrue(manager.TryReconnect(new TestConnection(), 10, token, 0.5d));
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

            Assert.IsFalse(manager.TryReconnect(
                new TestConnection { PlayerId = 100UL, Connected = false },
                10,
                token,
                0.5d));
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
            }, 10, token, 0.5d));
            Assert.AreEqual(ReconnectRejectReason.Unauthenticated, rejectReason);
        }

        [Test]
        public void TryReconnect_RejectsExpiredReservation_WithoutUpdate()
        {
            var manager = new ReconnectionManager
            {
                ReconnectWindow = 1d
            };

            ReconnectRejectReason rejectReason = ReconnectRejectReason.None;
            int expiredConnectionId = -1;
            manager.OnReconnectRejected += (_, reason) => rejectReason = reason;
            manager.OnReconnectWindowExpired += id => expiredConnectionId = id;

            manager.OnClientDisconnected(10, 2d, 100UL, 3, out ReconnectToken token);

            Assert.IsFalse(manager.TryReconnect(
                new TestConnection { PlayerId = 100UL },
                10,
                token,
                3d));
            Assert.AreEqual(ReconnectRejectReason.WindowExpired, rejectReason);
            Assert.AreEqual(10, expiredConnectionId);
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void CatchUpInitializationException_RemovesReservation_AndFailsClosed()
        {
            var manager = new ReconnectionManager(new ThrowingCatchUp());
            int failedConnectionId = -1;
            string failedReason = null;
            manager.OnCatchUpFailed += (id, reason) =>
            {
                Assert.IsFalse(manager.HasReservation(id));
                failedConnectionId = id;
                failedReason = reason;
            };

            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            Assert.IsFalse(manager.TryReconnect(
                new TestConnection { PlayerId = 100UL },
                10,
                token,
                0.5d));
            Assert.AreEqual(10, failedConnectionId);
            Assert.AreEqual("catch-up startup failed", failedReason);
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void CatchUpCompletion_RemovesReservation_BeforePublishingEvents()
        {
            var manager = new ReconnectionManager(new CompletingCatchUp());
            bool completed = false;
            bool reconnected = false;
            manager.OnCatchUpComplete += id =>
            {
                Assert.IsFalse(manager.HasReservation(id));
                completed = true;
            };
            manager.OnClientReconnected += (id, _) =>
            {
                Assert.IsFalse(manager.HasReservation(id));
                reconnected = true;
            };

            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);

            Assert.IsTrue(manager.TryReconnect(
                new TestConnection { PlayerId = 100UL },
                10,
                token,
                0.5d));
            Assert.IsTrue(completed);
            Assert.IsTrue(reconnected);
            Assert.IsFalse(manager.HasReservation(10));
        }

        [Test]
        public void OnClientDisconnected_Rejects_When_Reservation_Capacity_Is_Exhausted()
        {
            var manager = new ReconnectionManager(maxReservations: 1);
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out _);

            Assert.Throws<InvalidOperationException>(() =>
                manager.OnClientDisconnected(11, 0d, 101UL, 3, out _));
            Assert.AreEqual(1, manager.ReservationCount);
        }

        [Test]
        public void Update_Rejects_NonFinite_Time()
        {
            var manager = new ReconnectionManager();

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Update(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Update(double.PositiveInfinity));
        }

        [Test]
        public void Update_Commits_All_Expirations_Before_Publishing_Observers()
        {
            var manager = new ReconnectionManager { ReconnectWindow = 1d };
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out _);
            manager.OnClientDisconnected(11, 0d, 101UL, 3, out _);
            manager.OnReconnectWindowExpired += _ => throw new InvalidOperationException("observer failed");

            Assert.Throws<InvalidOperationException>(() => manager.Update(2d));
            Assert.IsFalse(manager.HasReservation(10));
            Assert.IsFalse(manager.HasReservation(11));
        }

        [Test]
        public void OnClientDisconnected_IdempotentDuplicate_ReturnsExistingToken()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 1d, 100UL, 3, out ReconnectToken originalToken);

            manager.OnClientDisconnected(10, 1.5d, 100UL, 3, out ReconnectToken duplicateToken);

            Assert.AreEqual(originalToken, duplicateToken);
            Assert.AreEqual(1, manager.ReservationCount);
        }

        [Test]
        public void OnClientDisconnected_RejectsActiveOwnershipOrProtocolConflict()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 1d, 100UL, 3, out ReconnectToken originalToken);

            ReconnectToken rejectedToken = originalToken;
            Assert.Throws<InvalidOperationException>(() =>
                manager.OnClientDisconnected(10, 1.5d, 101UL, 3, out rejectedToken));
            Assert.IsFalse(rejectedToken.IsValid);

            rejectedToken = originalToken;
            Assert.Throws<InvalidOperationException>(() =>
                manager.OnClientDisconnected(10, 1.5d, 100UL, 4, out rejectedToken));
            Assert.IsFalse(rejectedToken.IsValid);
            Assert.IsTrue(manager.HasReservation(10));
        }

        [Test]
        public void OnClientDisconnected_RejectsClockRegressionForExistingReservation()
        {
            var manager = new ReconnectionManager();
            manager.OnClientDisconnected(10, 2d, 100UL, 3, out ReconnectToken originalToken);

            ReconnectToken rejectedToken = originalToken;
            Assert.Throws<InvalidOperationException>(() =>
                manager.OnClientDisconnected(10, 1.5d, 100UL, 3, out rejectedToken));

            Assert.IsFalse(rejectedToken.IsValid);
            Assert.IsTrue(manager.HasReservation(10));
        }

        [Test]
        public void OnClientDisconnected_RejectsDuplicateWhileCatchUpIsActive()
        {
            var manager = new ReconnectionManager(new PendingCatchUp());
            manager.OnClientDisconnected(10, 0d, 100UL, 3, out ReconnectToken token);
            Assert.IsTrue(manager.TryReconnect(
                new TestConnection { PlayerId = 100UL },
                10,
                token,
                0.5d));

            Assert.Throws<InvalidOperationException>(() =>
                manager.OnClientDisconnected(10, 0.75d, 100UL, 3, out _));
            Assert.IsTrue(manager.HasReservation(10));
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

        private sealed class ThrowingCatchUp : IStateCatchUp
        {
            public void BeginCatchUp(INetConnection connection, int originalConnectionId,
                Action<float> onProgress, Action onComplete, Action<string> onFailed)
            {
                throw new InvalidOperationException("catch-up startup failed");
            }
        }

        private sealed class CompletingCatchUp : IStateCatchUp
        {
            public void BeginCatchUp(INetConnection connection, int originalConnectionId,
                Action<float> onProgress, Action onComplete, Action<string> onFailed)
            {
                onComplete();
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
