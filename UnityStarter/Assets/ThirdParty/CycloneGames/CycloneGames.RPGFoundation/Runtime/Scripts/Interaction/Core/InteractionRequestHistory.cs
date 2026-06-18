using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public sealed class InteractionRequestHistory
    {
        private readonly Dictionary<RequestKey, int> _seenServerTicks = new Dictionary<RequestKey, int>();
        private readonly Queue<Entry> _entries = new Queue<Entry>();

        public int Count => _seenServerTicks.Count;

        public InteractionRequestHistoryResult MarkSeen(
            in InteractionRequest request,
            int serverTick,
            int historyWindowTicks,
            int capacity)
        {
            if (request.RequestId <= 0 || historyWindowTicks <= 0)
            {
                return InteractionRequestHistoryResult.Accepted;
            }

            Purge(serverTick, historyWindowTicks);

            var key = new RequestKey(request);
            if (_seenServerTicks.ContainsKey(key))
            {
                return InteractionRequestHistoryResult.Duplicate;
            }

            if (capacity > 0 && _seenServerTicks.Count >= capacity)
            {
                return InteractionRequestHistoryResult.CapacityExceeded;
            }

            _seenServerTicks.Add(key, serverTick);
            _entries.Enqueue(new Entry(key, serverTick));
            return InteractionRequestHistoryResult.Accepted;
        }

        public void Clear()
        {
            _seenServerTicks.Clear();
            _entries.Clear();
        }

        private void Purge(int serverTick, int historyWindowTicks)
        {
            while (_entries.Count > 0)
            {
                Entry entry = _entries.Peek();
                if (serverTick < entry.ServerTick || serverTick - entry.ServerTick <= historyWindowTicks)
                {
                    return;
                }

                _entries.Dequeue();
                _seenServerTicks.Remove(entry.Key);
            }
        }

        private readonly struct RequestKey : IEquatable<RequestKey>
        {
            public readonly int RequestId;
            public readonly ulong InstigatorStableId;
            public readonly int InstigatorId;

            public RequestKey(InteractionRequest request)
            {
                RequestId = request.RequestId;
                if (request.InstigatorStableId != InteractionStableId.None)
                {
                    InstigatorStableId = request.InstigatorStableId;
                    InstigatorId = 0;
                }
                else
                {
                    InstigatorStableId = InteractionStableId.None;
                    InstigatorId = request.InstigatorId;
                }
            }

            public bool Equals(RequestKey other)
            {
                return RequestId == other.RequestId &&
                    InstigatorStableId == other.InstigatorStableId &&
                    InstigatorId == other.InstigatorId;
            }

            public override bool Equals(object obj)
            {
                return obj is RequestKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RequestId;
                    hash = (hash * 397) ^ InstigatorStableId.GetHashCode();
                    hash = (hash * 397) ^ InstigatorId;
                    return hash;
                }
            }
        }

        private readonly struct Entry
        {
            public readonly RequestKey Key;
            public readonly int ServerTick;

            public Entry(RequestKey key, int serverTick)
            {
                Key = key;
                ServerTick = serverTick;
            }
        }
    }
}
