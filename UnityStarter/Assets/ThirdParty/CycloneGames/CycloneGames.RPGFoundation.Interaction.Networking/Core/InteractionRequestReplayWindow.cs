using System;
using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>
    /// Per-instigator sliding window over recently accepted request IDs. Makes
    /// reliable-channel retransmission idempotent: a duplicate request ID inside
    /// the window is rejected instead of executed twice.
    ///
    /// The table is bounded by <see cref="MaxTrackedInstigators"/>. At capacity a new
    /// identity is rejected rather than evicting a live one: eviction would silently
    /// re-open replay protection for the evicted instigator, so a peer that controls
    /// enough identities could push a target out and replay its requests. Callers
    /// release identities explicitly with <see cref="Remove"/> when a session ends.
    /// </summary>
    public sealed class InteractionRequestReplayWindow
    {
        private const int WindowBitCount = 64;
        public const int MaxTrackedInstigators = 4096;

        private struct InstigatorWindow
        {
            public int Highest;
            public ulong SeenMask;
        }

        private readonly Dictionary<ulong, InstigatorWindow> _windows;
        private long _rejectedAdmissionCount;

        public InteractionRequestReplayWindow(int capacity = 64)
        {
            _windows = new Dictionary<ulong, InstigatorWindow>(capacity);
        }

        public int TrackedInstigatorCount => _windows.Count;

        /// <summary>Number of new identities refused because the table was full.</summary>
        public long RejectedAdmissionCount => _rejectedAdmissionCount;

        /// <summary>
        /// Returns true if the request may execute; false if it is a duplicate, too old,
        /// or the identity table is full.
        /// </summary>
        public bool TryAccept(ulong instigatorStableId, int requestId)
        {
            if (instigatorStableId == 0UL || requestId <= 0)
                return false;

            if (!_windows.TryGetValue(instigatorStableId, out InstigatorWindow window))
            {
                if (_windows.Count >= MaxTrackedInstigators)
                {
                    if (_rejectedAdmissionCount < long.MaxValue)
                        _rejectedAdmissionCount++;

                    return false;
                }

                window = new InstigatorWindow { Highest = requestId, SeenMask = 1UL };
                _windows.Add(instigatorStableId, window);
                return true;
            }

            int forward = requestId - window.Highest;
            if (forward > 0)
            {
                window.SeenMask = forward >= WindowBitCount ? 1UL : (window.SeenMask << forward) | 1UL;
                window.Highest = requestId;
                _windows[instigatorStableId] = window;
                return true;
            }

            int age = window.Highest - requestId;
            if (age >= WindowBitCount)
                return false;

            ulong bit = 1UL << age;
            if ((window.SeenMask & bit) != 0UL)
                return false;

            window.SeenMask |= bit;
            _windows[instigatorStableId] = window;
            return true;
        }

        public void Remove(ulong instigatorStableId)
        {
            _windows.Remove(instigatorStableId);
        }

        public void Clear()
        {
            _windows.Clear();
        }
    }
}
