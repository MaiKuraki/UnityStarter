using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>
    /// Per-instigator sliding window over recently accepted request IDs. Makes
    /// reliable-channel retransmission idempotent: a duplicate request ID inside
    /// the window is rejected instead of executed twice. Bounded by
    /// <see cref="MaxTrackedInstigators"/>; the least-recently-touched instigator
    /// is evicted under pressure, which fails closed (new requests rejected until
    /// the next accepted one re-opens the window).
    /// </summary>
    public sealed class InteractionRequestReplayWindow
    {
        private const int WindowBitCount = 64;
        public const int MaxTrackedInstigators = 4096;

        private sealed class InstigatorWindow
        {
            public int Highest;
            public ulong SeenMask;
        }

        private readonly Dictionary<ulong, InstigatorWindow> _windows;
        private readonly LinkedList<ulong> _touchOrder;
        private readonly Dictionary<ulong, LinkedListNode<ulong>> _nodes;

        public InteractionRequestReplayWindow(int capacity = 64)
        {
            _windows = new Dictionary<ulong, InstigatorWindow>(capacity);
            _touchOrder = new LinkedList<ulong>();
            _nodes = new Dictionary<ulong, LinkedListNode<ulong>>(capacity);
        }

        public int TrackedInstigatorCount => _windows.Count;

        /// <summary>
        /// Returns true if the request may execute; false if it is a duplicate or
        /// too old. Monotonic request IDs per instigator are required.
        /// </summary>
        public bool TryAccept(ulong instigatorStableId, int requestId)
        {
            if (instigatorStableId == 0UL || requestId <= 0)
                return false;

            if (!_windows.TryGetValue(instigatorStableId, out InstigatorWindow window))
            {
                if (_windows.Count >= MaxTrackedInstigators)
                    EvictLeastRecentlyTouched();
                if (_windows.Count >= MaxTrackedInstigators)
                    return false;

                window = new InstigatorWindow { Highest = requestId, SeenMask = 1UL };
                _windows.Add(instigatorStableId, window);
                Touch(instigatorStableId);
                return true;
            }

            Touch(instigatorStableId);
            int forward = requestId - window.Highest;
            if (forward > 0)
            {
                window.SeenMask = forward >= WindowBitCount ? 1UL : (window.SeenMask << forward) | 1UL;
                window.Highest = requestId;
                return true;
            }

            int age = window.Highest - requestId;
            if (age >= WindowBitCount)
                return false;

            ulong bit = 1UL << age;
            if ((window.SeenMask & bit) != 0UL)
                return false;

            window.SeenMask |= bit;
            return true;
        }

        public void Remove(ulong instigatorStableId)
        {
            if (!_windows.Remove(instigatorStableId))
                return;
            if (_nodes.TryGetValue(instigatorStableId, out LinkedListNode<ulong> node))
            {
                _nodes.Remove(instigatorStableId);
                _touchOrder.Remove(node);
            }
        }

        public void Clear()
        {
            _windows.Clear();
            _touchOrder.Clear();
            _nodes.Clear();
        }

        private void Touch(ulong instigatorStableId)
        {
            if (_nodes.TryGetValue(instigatorStableId, out LinkedListNode<ulong> node))
            {
                _touchOrder.Remove(node);
                _touchOrder.AddLast(node);
                return;
            }

            _nodes[instigatorStableId] = _touchOrder.AddLast(instigatorStableId);
        }

        private void EvictLeastRecentlyTouched()
        {
            LinkedListNode<ulong> first = _touchOrder.First;
            if (first == null)
                return;

            _touchOrder.RemoveFirst();
            _nodes.Remove(first.Value);
            _windows.Remove(first.Value);
        }
    }
}
