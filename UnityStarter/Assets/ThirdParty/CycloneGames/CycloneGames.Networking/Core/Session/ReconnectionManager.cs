using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CycloneGames.Networking.Session
{
    public interface IReconnectionManager
    {
        double ReconnectWindow { get; set; }
        bool RequireAuthenticatedConnection { get; set; }

        bool HasReservation(int connectionId);

        void OnClientDisconnected(int connectionId, double disconnectTime, ulong playerId, int protocolVersion,
            out ReconnectToken token);
        bool TryReconnect(INetConnection newConnection, int originalConnectionId, in ReconnectToken token);
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
        private readonly Dictionary<int, ReconnectSlot> _reservedSlots =
            new Dictionary<int, ReconnectSlot>(16);

        private readonly IStateCatchUp _catchUp;
        private int[] _expiredBuffer = new int[16];

        public double ReconnectWindow { get; set; } = 300.0;
        public bool RequireAuthenticatedConnection { get; set; }

        public event Action<int, INetConnection> OnClientReconnected;
        public event Action<int, ReconnectRejectReason> OnReconnectRejected;
        public event Action<int> OnReconnectWindowExpired;
        public event Action<int, float> OnCatchUpProgress;
        public event Action<int> OnCatchUpComplete;
        public event Action<int, string> OnCatchUpFailed;

        public ReconnectionManager(IStateCatchUp catchUp = null, bool requireAuthenticatedConnection = false)
        {
            _catchUp = catchUp;
            RequireAuthenticatedConnection = requireAuthenticatedConnection;
        }

        public void OnClientDisconnected(int connectionId, double disconnectTime, ulong playerId, int protocolVersion,
            out ReconnectToken token)
        {
            if (_reservedSlots.TryGetValue(connectionId, out var existingSlot))
            {
                token = existingSlot.Token;
                return;
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

        public bool TryReconnect(INetConnection newConnection, int originalConnectionId, in ReconnectToken token)
        {
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
                BeginCatchUp(newConnection, originalConnectionId);
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
            int expiredCount = CollectExpiredReservations(currentTime);
            for (int i = 0; i < expiredCount; i++)
            {
                int connectionId = _expiredBuffer[i];
                _reservedSlots.Remove(connectionId);
                OnReconnectWindowExpired?.Invoke(connectionId);
            }
        }

        private void BeginCatchUp(INetConnection newConnection, int originalConnectionId)
        {
            _catchUp.BeginCatchUp(newConnection, originalConnectionId,
                progress => OnCatchUpProgress?.Invoke(originalConnectionId, progress),
                () => CompleteCatchUp(originalConnectionId, newConnection),
                reason => FailCatchUp(originalConnectionId, reason));
        }

        private void CompleteCatchUp(int originalConnectionId, INetConnection newConnection)
        {
            if (!_reservedSlots.TryGetValue(originalConnectionId, out var slot))
                return;

            slot.State = ReconnectState.Reconnected;
            _reservedSlots[originalConnectionId] = slot;
            OnCatchUpComplete?.Invoke(originalConnectionId);
            OnClientReconnected?.Invoke(originalConnectionId, newConnection);
            _reservedSlots.Remove(originalConnectionId);
        }

        private void FailCatchUp(int originalConnectionId, string reason)
        {
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
            if (slot.PlayerId != 0UL && newConnection != null && newConnection.PlayerId != 0UL &&
                newConnection.PlayerId != slot.PlayerId)
                return ReconnectRejectReason.PlayerMismatch;

            return ReconnectRejectReason.None;
        }

        private static ulong GenerateNonce()
        {
            byte[] bytes = new byte[8];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return BitConverter.ToUInt64(bytes, 0);
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
        ProtocolMismatch
    }
}
