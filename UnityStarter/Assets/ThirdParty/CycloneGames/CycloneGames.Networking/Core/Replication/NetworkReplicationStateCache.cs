using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Replication
{
    public readonly struct NetworkObjectReplicationState
    {
        public static readonly NetworkObjectReplicationState Unknown = new NetworkObjectReplicationState(
            NetworkReplicatedObject.NEVER_SENT,
            NetworkReplicatedObject.NEVER_SENT,
            NetworkReplicatedObject.NEVER_SENT,
            payloadHash: 0UL,
            payloadBytes: 0,
            sequence: 0,
            requiresFullState: true);

        public readonly int LastSentTick;
        public readonly int LastFullStateTick;
        public readonly int LastAckedTick;
        public readonly ulong LastPayloadHash;
        public readonly int LastPayloadBytes;
        public readonly ushort Sequence;
        public readonly bool RequiresFullState;

        public NetworkObjectReplicationState(
            int lastSentTick,
            int lastFullStateTick,
            int lastAckedTick,
            ulong payloadHash,
            int payloadBytes,
            ushort sequence,
            bool requiresFullState)
        {
            LastSentTick = lastSentTick;
            LastFullStateTick = lastFullStateTick;
            LastAckedTick = lastAckedTick;
            LastPayloadHash = payloadHash;
            LastPayloadBytes = payloadBytes;
            Sequence = sequence;
            RequiresFullState = requiresFullState;
        }

        public bool IsKnown
        {
            get
            {
                return LastSentTick != NetworkReplicatedObject.NEVER_SENT
                       || LastAckedTick != NetworkReplicatedObject.NEVER_SENT
                       || LastPayloadHash != 0UL;
            }
        }
    }

    public sealed class NetworkReplicationStateCache
    {
        private readonly Dictionary<StateKey, NetworkObjectReplicationState> _states;
        private readonly List<StateKey> _scratchKeys;

        public NetworkReplicationStateCache(int capacity = 1024)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _states = new Dictionary<StateKey, NetworkObjectReplicationState>(capacity);
            _scratchKeys = new List<StateKey>(Math.Min(Math.Max(capacity / 8, 16), 1024));
        }

        public int Count
        {
            get
            {
                return _states.Count;
            }
        }

        public bool TryGetState(int connectionId, ulong objectId, out NetworkObjectReplicationState state)
        {
            ValidateConnectionAndObject(connectionId, objectId);
            return _states.TryGetValue(new StateKey(connectionId, objectId), out state);
        }

        public NetworkObjectReplicationState GetStateOrUnknown(int connectionId, ulong objectId)
        {
            return TryGetState(connectionId, objectId, out NetworkObjectReplicationState state)
                ? state
                : NetworkObjectReplicationState.Unknown;
        }

        public NetworkReplicatedObject ApplyState(int connectionId, in NetworkReplicatedObject source)
        {
            NetworkObjectReplicationState state = GetStateOrUnknown(connectionId, source.ObjectId);
            bool requiresFullState = source.RequiresFullState || state.RequiresFullState || !state.IsKnown;

            return new NetworkReplicatedObject(
                source.ObjectId,
                source.Policy,
                source.Position,
                source.OwnerConnectionId,
                source.OwnerPlayerId,
                source.TeamId,
                source.InterestLayerMask,
                source.IsDirty,
                requiresFullState,
                state.IsKnown ? state.LastSentTick : NetworkReplicatedObject.NEVER_SENT,
                source.EstimatedPayloadBytes);
        }

        public void RequireFullState(int connectionId, ulong objectId)
        {
            ValidateConnectionAndObject(connectionId, objectId);
            var key = new StateKey(connectionId, objectId);
            NetworkObjectReplicationState previous = _states.TryGetValue(key, out NetworkObjectReplicationState state)
                ? state
                : NetworkObjectReplicationState.Unknown;

            _states[key] = new NetworkObjectReplicationState(
                previous.LastSentTick,
                previous.LastFullStateTick,
                previous.LastAckedTick,
                previous.LastPayloadHash,
                previous.LastPayloadBytes,
                previous.Sequence,
                requiresFullState: true);
        }

        public void MarkSent(
            int connectionId,
            ulong objectId,
            int serverTick,
            bool fullState,
            int payloadBytes,
            ulong payloadHash = 0UL,
            ushort sequence = 0)
        {
            ValidateConnectionAndObject(connectionId, objectId);
            if (serverTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            }

            if (payloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadBytes));
            }

            var key = new StateKey(connectionId, objectId);
            NetworkObjectReplicationState previous = _states.TryGetValue(key, out NetworkObjectReplicationState state)
                ? state
                : NetworkObjectReplicationState.Unknown;

            _states[key] = new NetworkObjectReplicationState(
                serverTick,
                fullState ? serverTick : previous.LastFullStateTick,
                previous.LastAckedTick,
                payloadHash,
                payloadBytes,
                sequence,
                requiresFullState: !fullState && previous.RequiresFullState);
        }

        public bool TryMarkAcked(int connectionId, ulong objectId, int ackedTick)
        {
            ValidateConnectionAndObject(connectionId, objectId);
            if (ackedTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ackedTick));
            }

            var key = new StateKey(connectionId, objectId);
            if (!_states.TryGetValue(key, out NetworkObjectReplicationState previous))
            {
                return false;
            }

            _states[key] = new NetworkObjectReplicationState(
                previous.LastSentTick,
                previous.LastFullStateTick,
                Math.Max(previous.LastAckedTick, ackedTick),
                previous.LastPayloadHash,
                previous.LastPayloadBytes,
                previous.Sequence,
                previous.RequiresFullState);
            return true;
        }

        public int RemoveConnection(int connectionId)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            _scratchKeys.Clear();
            foreach (KeyValuePair<StateKey, NetworkObjectReplicationState> pair in _states)
            {
                if (pair.Key.ConnectionId == connectionId)
                {
                    _scratchKeys.Add(pair.Key);
                }
            }

            return RemoveScratchKeys();
        }

        public int RemoveObject(ulong objectId)
        {
            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            _scratchKeys.Clear();
            foreach (KeyValuePair<StateKey, NetworkObjectReplicationState> pair in _states)
            {
                if (pair.Key.ObjectId == objectId)
                {
                    _scratchKeys.Add(pair.Key);
                }
            }

            return RemoveScratchKeys();
        }

        public void Clear()
        {
            _states.Clear();
            _scratchKeys.Clear();
        }

        private int RemoveScratchKeys()
        {
            int removed = 0;
            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                if (_states.Remove(_scratchKeys[i]))
                {
                    removed++;
                }
            }

            _scratchKeys.Clear();
            return removed;
        }

        private static void ValidateConnectionAndObject(int connectionId, ulong objectId)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }
        }

        private readonly struct StateKey : IEquatable<StateKey>
        {
            public readonly int ConnectionId;
            public readonly ulong ObjectId;

            public StateKey(int connectionId, ulong objectId)
            {
                ConnectionId = connectionId;
                ObjectId = objectId;
            }

            public bool Equals(StateKey other)
            {
                return ConnectionId == other.ConnectionId && ObjectId == other.ObjectId;
            }

            public override bool Equals(object obj)
            {
                return obj is StateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ConnectionId * 397) ^ ObjectId.GetHashCode();
                }
            }
        }
    }
}
