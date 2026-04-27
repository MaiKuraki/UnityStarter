using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public sealed class InteractionQueue
    {
        private readonly Queue<InteractionRequest> _queue = new();
        private InteractionRequest _current;

        public int Count => _queue.Count;
        public bool IsEmpty => _queue.Count == 0;
        public InteractionRequest Current => _current;

        public event Action<InteractionRequest> OnDequeued;

        public void Enqueue(InteractionRequest request)
        {
            _queue.Enqueue(request);
        }

        public bool TryDequeue(out InteractionRequest request)
        {
            if (_queue.Count == 0) { request = default; return false; }
            request = _queue.Dequeue();
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
                if (item.InstigatorId == instigatorId) { removed++; }
                else { _queue.Enqueue(item); }
            }
            return removed;
        }

        public void Clear()
        {
            _queue.Clear();
            _current = default;
        }
    }
}
