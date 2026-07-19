using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CycloneGames.Networking.Session
{
    /// <summary>
    /// Coordinates reconnect reservations on one authoritative owner thread or scheduler.
    /// </summary>
    /// <remarks>
    /// Implementations are not required to support concurrent calls. Any asynchronous
    /// catch-up implementation must marshal its callbacks back to the same owner before
    /// invoking them.
    /// </remarks>
    public interface IReconnectionManager
    {
        double ReconnectWindow { get; set; }
        bool RequireAuthenticatedConnection { get; set; }

        bool HasReservation(int connectionId);

        void OnClientDisconnected(int connectionId, double disconnectTime, ulong playerId, int protocolVersion,
            out ReconnectToken token);
        bool TryReconnect(INetConnection newConnection, int originalConnectionId, in ReconnectToken token, double currentTime);
        void CancelReservation(int connectionId);
        void Update(double currentTime);

        event Action<int, INetConnection> OnClientReconnected;
        event Action<int, ReconnectRejectReason> OnReconnectRejected;
        event Action<int> OnReconnectWindowExpired;
        event Action<int, float> OnCatchUpProgress;
        event Action<int> OnCatchUpComplete;
        event Action<int, string> OnCatchUpFailed;
    }

    public sealed class ReconnectionManager : IReconnectionManager
    {
        private const int DefaultMaxReservations = 4096;

        private readonly Dictionary<int, ReconnectSlot> _reservedSlots =
            new Dictionary<int, ReconnectSlot>(16);

        private readonly IStateCatchUp _catchUp;
        private readonly int _maxReservations;
        private int[] _expiredBuffer = new int[16];

        private double _reconnectWindow = 300d;

        public double ReconnectWindow
        {
            get => _reconnectWindow;
            set
            {
                if (value <= 0d || double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                _reconnectWindow = value;
            }
        }
        public bool RequireAuthenticatedConnection { get; set; }
        public int MaxReservations => _maxReservations;
        public int ReservationCount => _reservedSlots.Count;

        public event Action<int, INetConnection> OnClientReconnected;
        public event Action<int, ReconnectRejectReason> OnReconnectRejected;
        public event Action<int> OnReconnectWindowExpired;
        public event Action<int, float> OnCatchUpProgress;
        public event Action<int> OnCatchUpComplete;
        public event Action<int, string> OnCatchUpFailed;

        public ReconnectionManager(
            IStateCatchUp catchUp = null,
            bool requireAuthenticatedConnection = false,
            int maxReservations = DefaultMaxReservations)
        {
            if (maxReservations <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxReservations));

            _catchUp = catchUp;
            RequireAuthenticatedConnection = requireAuthenticatedConnection;
            _maxReservations = maxReservations;
        }

        public void OnClientDisconnected(int connectionId, double disconnectTime, ulong playerId, int protocolVersion,
            out ReconnectToken token)
        {
            if (connectionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            if (disconnectTime < 0d || double.IsNaN(disconnectTime) || double.IsInfinity(disconnectTime))
                throw new ArgumentOutOfRangeException(nameof(disconnectTime));

            if (_reservedSlots.TryGetValue(connectionId, out var existingSlot))
            {
                if (disconnectTime < existingSlot.DisconnectTime)
                {
                    token = default;
                    throw new InvalidOperationException(
                        "Reconnect disconnect time cannot move backwards for an existing reservation.");
                }

                if (disconnectTime - existingSlot.DisconnectTime < ReconnectWindow)
                {
                    if (existingSlot.State != ReconnectState.WaitingForReconnect
                        || existingSlot.PlayerId != playerId
                        || existingSlot.ProtocolVersion != protocolVersion)
                    {
                        token = default;
                        throw new InvalidOperationException(
                            "An active reconnect reservation already exists with different ownership, protocol, or state.");
                    }

                    token = existingSlot.Token;
                    return;
                }

                _reservedSlots.Remove(connectionId);
            }

            if (_reservedSlots.Count >= _maxReservations)
                Update(disconnectTime);

            if (_reservedSlots.Count >= _maxReservations)
            {
                token = default;
                throw new InvalidOperationException("Reconnect reservation capacity is exhausted.");
            }

            token = ReconnectToken.Create(connectionId, playerId, protocolVersion, GenerateNonce());
            _reservedSlots[connectionId] = new ReconnectSlot
            {
                OriginalConnectionId = connectionId,
                PlayerId = playerId,
                ProtocolVersion = protocolVersion,
                Token = token,
                DisconnectTime = disconnectTime,
                State = ReconnectState.WaitingForReconnect
            };
        }

        public bool HasReservation(int connectionId) => _reservedSlots.ContainsKey(connectionId);

        public bool TryReconnect(
            INetConnection newConnection,
            int originalConnectionId,
            in ReconnectToken token,
            double currentTime)
        {
            if (currentTime < 0d || double.IsNaN(currentTime) || double.IsInfinity(currentTime))
                throw new ArgumentOutOfRangeException(nameof(currentTime));

            if (!_reservedSlots.TryGetValue(originalConnectionId, out var slot))
            {
                OnReconnectRejected?.Invoke(originalConnectionId, ReconnectRejectReason.NoReservation);
                return false;
            }

            if (slot.State != ReconnectState.WaitingForReconnect)
            {
                OnReconnectRejected?.Invoke(originalConnectionId, ReconnectRejectReason.InvalidState);
                return false;
            }

            if (currentTime < slot.DisconnectTime || currentTime - slot.DisconnectTime >= ReconnectWindow)
            {
                _reservedSlots.Remove(originalConnectionId);
                OnReconnectRejected?.Invoke(originalConnectionId, ReconnectRejectReason.WindowExpired);
                OnReconnectWindowExpired?.Invoke(originalConnectionId);
                return false;
            }

            ReconnectRejectReason rejectReason = ValidateConnection(newConnection);
            if (rejectReason != ReconnectRejectReason.None)
            {
                OnReconnectRejected?.Invoke(originalConnectionId, rejectReason);
                return false;
            }

            rejectReason = ValidateToken(newConnection, slot, token);
            if (rejectReason != ReconnectRejectReason.None)
            {
                OnReconnectRejected?.Invoke(originalConnectionId, rejectReason);
                return false;
            }

            slot.State = ReconnectState.CatchingUp;
            slot.NewConnection = newConnection;
            _reservedSlots[originalConnectionId] = slot;

            if (_catchUp != null)
            {
                try
                {
                    BeginCatchUp(newConnection, originalConnectionId, slot.Token.Nonce);
                }
                catch (Exception exception)
                {
                    // A synchronous terminal callback may already have committed and
                    // removed this attempt. Do not rewrite that outcome as an
                    // initialization failure or swallow a subscriber exception.
                    if (!_reservedSlots.TryGetValue(originalConnectionId, out ReconnectSlot activeSlot)
                        || activeSlot.State != ReconnectState.CatchingUp
                        || activeSlot.Token.Nonce != slot.Token.Nonce
                        || !ReferenceEquals(activeSlot.NewConnection, newConnection))
                    {
                        throw;
                    }

                    FailCatchUp(
                        originalConnectionId,
                        slot.Token.Nonce,
                        string.IsNullOrEmpty(exception.Message)
                            ? "Catch-up initialization failed."
                            : exception.Message);
                    return false;
                }
            }
            else
            {
                slot.State = ReconnectState.Reconnected;
                _reservedSlots.Remove(originalConnectionId);
                OnClientReconnected?.Invoke(originalConnectionId, newConnection);
            }

            return true;
        }

        public void CancelReservation(int connectionId) => _reservedSlots.Remove(connectionId);

        public void Update(double currentTime)
        {
            if (currentTime < 0d || double.IsNaN(currentTime) || double.IsInfinity(currentTime))
                throw new ArgumentOutOfRangeException(nameof(currentTime));

            int expiredCount = CollectExpiredReservations(currentTime);
            for (int i = 0; i < expiredCount; i++)
            {
                _reservedSlots.Remove(_expiredBuffer[i]);
            }

            // Commit every expiry before invoking external observers. A throwing or
            // re-entrant subscriber must not leave later expired reservations alive.
            for (int i = 0; i < expiredCount; i++)
            {
                OnReconnectWindowExpired?.Invoke(_expiredBuffer[i]);
            }
        }

        private void BeginCatchUp(INetConnection newConnection, int originalConnectionId, ulong attemptNonce)
        {
            _catchUp.BeginCatchUp(newConnection, originalConnectionId,
                progress => ReportCatchUpProgress(originalConnectionId, attemptNonce, progress),
                () => CompleteCatchUp(originalConnectionId, newConnection, attemptNonce),
                reason => FailCatchUp(originalConnectionId, attemptNonce, reason));
        }

        private void ReportCatchUpProgress(int originalConnectionId, ulong attemptNonce, float progress)
        {
            if (_reservedSlots.TryGetValue(originalConnectionId, out ReconnectSlot slot)
                && slot.State == ReconnectState.CatchingUp
                && slot.Token.Nonce == attemptNonce)
            {
                OnCatchUpProgress?.Invoke(originalConnectionId, progress);
            }
        }

        private void CompleteCatchUp(int originalConnectionId, INetConnection newConnection, ulong attemptNonce)
        {
            if (!_reservedSlots.TryGetValue(originalConnectionId, out var slot))
                return;
            if (slot.State != ReconnectState.CatchingUp
                || slot.Token.Nonce != attemptNonce
                || !ReferenceEquals(slot.NewConnection, newConnection))
            {
                return;
            }

            // Commit ownership before publishing callbacks. A subscriber may throw or
            // re-enter the manager, but it must never observe a completed reservation.
            _reservedSlots.Remove(originalConnectionId);
            OnCatchUpComplete?.Invoke(originalConnectionId);
            OnClientReconnected?.Invoke(originalConnectionId, newConnection);
        }

        private void FailCatchUp(int originalConnectionId, ulong attemptNonce, string reason)
        {
            if (!_reservedSlots.TryGetValue(originalConnectionId, out ReconnectSlot slot)
                || slot.State != ReconnectState.CatchingUp
                || slot.Token.Nonce != attemptNonce)
            {
                return;
            }

            _reservedSlots.Remove(originalConnectionId);
            OnCatchUpFailed?.Invoke(originalConnectionId, reason);
        }

        private int CollectExpiredReservations(double currentTime)
        {
            int count = 0;
            foreach (var pair in _reservedSlots)
            {
                if (currentTime - pair.Value.DisconnectTime >= ReconnectWindow)
                {
                    if (count == _expiredBuffer.Length)
                        Array.Resize(ref _expiredBuffer, _expiredBuffer.Length * 2);

                    _expiredBuffer[count++] = pair.Key;
                }
            }

            return count;
        }

        private ReconnectRejectReason ValidateConnection(INetConnection newConnection)
        {
            if (newConnection == null || !newConnection.IsConnected)
                return ReconnectRejectReason.InvalidConnection;
            if (RequireAuthenticatedConnection && !newConnection.IsAuthenticated)
                return ReconnectRejectReason.Unauthenticated;

            return ReconnectRejectReason.None;
        }

        private static ReconnectRejectReason ValidateToken(INetConnection newConnection, in ReconnectSlot slot,
            in ReconnectToken token)
        {
            if (!token.IsValid)
                return ReconnectRejectReason.InvalidToken;
            if (token.OriginalConnectionId != slot.OriginalConnectionId)
                return ReconnectRejectReason.InvalidToken;
            if (token.PlayerId != slot.PlayerId)
                return ReconnectRejectReason.PlayerMismatch;
            if (token.ProtocolVersion != slot.ProtocolVersion)
                return ReconnectRejectReason.ProtocolMismatch;
            if (token.Nonce != slot.Token.Nonce)
                return ReconnectRejectReason.InvalidToken;
            if (slot.PlayerId != 0UL && newConnection.PlayerId != slot.PlayerId)
                return ReconnectRejectReason.PlayerMismatch;

            return ReconnectRejectReason.None;
        }

        private static ulong GenerateNonce()
        {
            byte[] bytes = new byte[8];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                ulong nonce;
                do
                {
                    rng.GetBytes(bytes);
                    nonce = BitConverter.ToUInt64(bytes, 0);
                }
                while (nonce == 0UL);

                return nonce;
            }
        }

        private struct ReconnectSlot
        {
            public int OriginalConnectionId;
            public ulong PlayerId;
            public int ProtocolVersion;
            public ReconnectToken Token;
            public double DisconnectTime;
            public ReconnectState State;
            public INetConnection NewConnection;
        }

        private enum ReconnectState : byte
        {
            WaitingForReconnect,
            CatchingUp,
            Reconnected
        }
    }

    /// <summary>
    /// Starts restoration of authoritative state for a reconnecting client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BeginCatchUp"/> is called on the owning
    /// <see cref="ReconnectionManager"/> thread or scheduler. Implementations may complete
    /// synchronously or asynchronously, but every callback must be serialized back onto
    /// that same owner. Never invoke callbacks concurrently.
    /// </para>
    /// <para>
    /// Invoke at most one terminal callback (<c>onComplete</c> or <c>onFailed</c>) and do
    /// not report progress after a terminal callback. A synchronous exception is treated
    /// as a failed attempt and the reservation is removed.
    /// </para>
    /// </remarks>
    public interface IStateCatchUp
    {
        void BeginCatchUp(INetConnection connection, int originalConnectionId,
            Action<float> onProgress, Action onComplete, Action<string> onFailed);
    }

    public readonly struct ReconnectToken : IEquatable<ReconnectToken>
    {
        public readonly int OriginalConnectionId;
        public readonly ulong PlayerId;
        public readonly int ProtocolVersion;
        public readonly ulong Nonce;

        public bool IsValid => Nonce != 0UL;

        private ReconnectToken(int originalConnectionId, ulong playerId, int protocolVersion, ulong nonce)
        {
            OriginalConnectionId = originalConnectionId;
            PlayerId = playerId;
            ProtocolVersion = protocolVersion;
            Nonce = nonce;
        }

        public static ReconnectToken Create(int originalConnectionId, ulong playerId, int protocolVersion, ulong nonce)
        {
            return new ReconnectToken(originalConnectionId, playerId, protocolVersion, nonce);
        }

        public bool Equals(ReconnectToken other)
        {
            return OriginalConnectionId == other.OriginalConnectionId
                   && PlayerId == other.PlayerId
                   && ProtocolVersion == other.ProtocolVersion
                   && Nonce == other.Nonce;
        }

        public override bool Equals(object obj) => obj is ReconnectToken other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = OriginalConnectionId;
                hash = (hash * 397) ^ PlayerId.GetHashCode();
                hash = (hash * 397) ^ ProtocolVersion;
                hash = (hash * 397) ^ Nonce.GetHashCode();
                return hash;
            }
        }
    }

    public enum ReconnectRejectReason : byte
    {
        None,
        NoReservation,
        InvalidState,
        InvalidConnection,
        Unauthenticated,
        InvalidToken,
        PlayerMismatch,
        ProtocolMismatch,
        WindowExpired
    }
}
