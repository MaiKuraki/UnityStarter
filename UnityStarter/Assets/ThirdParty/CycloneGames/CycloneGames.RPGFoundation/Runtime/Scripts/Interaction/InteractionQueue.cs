using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public sealed class InteractionQueue
    {
        private readonly Queue<InteractionRequest> _queue;
        private readonly HashSet<QueuedRequestKey> _queuedRequestKeys;
        private readonly Dictionary<ulong, int> _queuedStableInstigatorCounts;
        private readonly Dictionary<int, int> _queuedLocalInstigatorCounts;
        private readonly int _capacity;
        private InteractionRequest _current;

        public InteractionQueue(int capacity = 64)
        {
            _capacity = capacity > 0 ? capacity : 64;
            _queue = new Queue<InteractionRequest>(_capacity);
            _queuedRequestKeys = new HashSet<QueuedRequestKey>();
            _queuedStableInstigatorCounts = new Dictionary<ulong, int>();
            _queuedLocalInstigatorCounts = new Dictionary<int, int>();
        }

        public int Count => _queue.Count;
        public bool IsEmpty => _queue.Count == 0;
        public InteractionRequest Current => _current;
        public int Capacity => _capacity;

        public event Action<InteractionRequest> OnDequeued;

        public void Enqueue(InteractionRequest request)
        {
            if (!TryEnqueue(request))
            {
                throw new InvalidOperationException("Interaction queue rejected the request because it is invalid, duplicated, or full.");
            }
        }

        public bool TryEnqueue(InteractionRequest request)
        {
            if (!request.IsValid || _queue.Count >= _capacity)
            {
                return false;
            }

            QueuedRequestKey requestKey = new QueuedRequestKey(request);
            if (requestKey.IsValid && !_queuedRequestKeys.Add(requestKey))
            {
                return false;
            }

            _queue.Enqueue(request);
            IncrementInstigatorCount(request);
            return true;
        }

        public bool TryDequeue(out InteractionRequest request)
        {
            if (_queue.Count == 0) { request = default; return false; }
            request = _queue.Dequeue();
            RemoveRequestKey(request);
            DecrementInstigatorCount(request);
            _current = request;
            OnDequeued?.Invoke(request);
            return true;
        }

        public int Cancel(int instigatorId)
        {
            int removed = 0;
            int count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                var item = _queue.Dequeue();
                if (item.InstigatorId == instigatorId)
                {
                    removed++;
                    RemoveRequestKey(item);
                    DecrementInstigatorCount(item);
                }
                else
                {
                    _queue.Enqueue(item);
                }
            }
            return removed;
        }

        public int CancelStable(ulong instigatorStableId)
        {
            if (instigatorStableId == InteractionStableId.None)
            {
                return 0;
            }

            int removed = 0;
            int count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                var item = _queue.Dequeue();
                if (item.InstigatorStableId == instigatorStableId)
                {
                    removed++;
                    RemoveRequestKey(item);
                    DecrementInstigatorCount(item);
                }
                else
                {
                    _queue.Enqueue(item);
                }
            }

            return removed;
        }

        public int CountQueuedForInstigator(ulong instigatorStableId)
        {
            if (instigatorStableId == InteractionStableId.None)
            {
                return 0;
            }

            return _queuedStableInstigatorCounts.TryGetValue(instigatorStableId, out int count) ? count : 0;
        }

        public int CountQueuedForInstigator(int instigatorId)
        {
            if (instigatorId == 0)
            {
                return 0;
            }

            return _queuedLocalInstigatorCounts.TryGetValue(instigatorId, out int count) ? count : 0;
        }

        public void Clear()
        {
            _queue.Clear();
            _queuedRequestKeys.Clear();
            _queuedStableInstigatorCounts.Clear();
            _queuedLocalInstigatorCounts.Clear();
            _current = default;
        }

        private void RemoveRequestKey(InteractionRequest request)
        {
            QueuedRequestKey requestKey = new QueuedRequestKey(request);
            if (requestKey.IsValid)
            {
                _queuedRequestKeys.Remove(requestKey);
            }
        }

        private void IncrementInstigatorCount(InteractionRequest request)
        {
            if (request.InstigatorStableId != InteractionStableId.None)
            {
                _queuedStableInstigatorCounts.TryGetValue(request.InstigatorStableId, out int count);
                _queuedStableInstigatorCounts[request.InstigatorStableId] = count + 1;
            }

            if (request.InstigatorId != 0)
            {
                _queuedLocalInstigatorCounts.TryGetValue(request.InstigatorId, out int count);
                _queuedLocalInstigatorCounts[request.InstigatorId] = count + 1;
            }
        }

        private void DecrementInstigatorCount(InteractionRequest request)
        {
            if (request.InstigatorStableId != InteractionStableId.None)
            {
                DecrementCount(_queuedStableInstigatorCounts, request.InstigatorStableId);
            }

            if (request.InstigatorId != 0)
            {
                DecrementCount(_queuedLocalInstigatorCounts, request.InstigatorId);
            }
        }

        private static void DecrementCount<TKey>(Dictionary<TKey, int> counts, TKey key)
        {
            if (!counts.TryGetValue(key, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                counts.Remove(key);
            }
            else
            {
                counts[key] = count - 1;
            }
        }

        private readonly struct QueuedRequestKey : IEquatable<QueuedRequestKey>
        {
            public readonly int RequestId;
            public readonly ulong InstigatorStableId;
            public readonly int InstigatorId;

            public QueuedRequestKey(InteractionRequest request)
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

            public bool IsValid => RequestId > 0;

            public bool Equals(QueuedRequestKey other)
            {
                return RequestId == other.RequestId &&
                    InstigatorStableId == other.InstigatorStableId &&
                    InstigatorId == other.InstigatorId;
            }

            public override bool Equals(object obj)
            {
                return obj is QueuedRequestKey other && Equals(other);
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
    }
}
