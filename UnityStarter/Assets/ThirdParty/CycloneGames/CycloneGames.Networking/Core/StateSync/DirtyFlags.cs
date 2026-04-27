using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.StateSync
{
    /// <summary>
    /// Compact bitfield for tracking dirty state of up to 64 network variables per entity.
    /// Thread-safe via Interlocked operations so worker threads can mark variables dirty.
    /// </summary>
    public struct DirtyFlags
    {
        private long _flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDirty(int index)
        {
            long mask = 1L << index;
            long original, updated;
            do
            {
                original = Interlocked.Read(ref _flags);
                updated = original | mask;
            } while (Interlocked.CompareExchange(ref _flags, updated, original) != original);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDirty(int index)
        {
            return (Interlocked.Read(ref _flags) & (1L << index)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAnyDirty()
        {
            return Interlocked.Read(ref _flags) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ClearAndGet()
        {
            return Interlocked.Exchange(ref _flags, 0L);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Interlocked.Exchange(ref _flags, 0L);
        }

        /// <summary>
        /// Iterate over set bits. Returns index of each dirty variable.
        /// </summary>
        public DirtyEnumerator GetDirtyIndices()
        {
            return new DirtyEnumerator(Interlocked.Read(ref _flags));
        }

        public ref struct DirtyEnumerator
        {
            private long _remaining;
            private int _current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal DirtyEnumerator(long flags)
            {
                _remaining = flags;
                _current = -1;
            }

            public int Current => _current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_remaining == 0) return false;

                // Find lowest set bit position using de Bruijn sequence
                _current = TrailingZeroCount(_remaining);
                _remaining &= _remaining - 1; // clear lowest set bit
                return true;
            }

            public DirtyEnumerator GetEnumerator() => this;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int TrailingZeroCount(long x)
            {
                // de Bruijn constant for 64-bit trailing zero count
                const ulong debruijn = 0x03F79D71B4CB0A89UL;
                // Isolate lowest set bit
                ulong isolated = (ulong)(x & -x);
                return DeBruijnTable[(int)((isolated * debruijn) >> 58)];
            }

            private static readonly int[] DeBruijnTable = {
                 0,  1, 56,  2, 57, 49, 28,  3, 61, 58, 42, 50, 38, 29, 17,  4,
                62, 47, 59, 36, 45, 43, 51, 22, 53, 39, 33, 30, 24, 18, 12,  5,
                63, 55, 48, 27, 60, 41, 37, 16, 46, 35, 44, 21, 52, 32, 23, 11,
                54, 26, 40, 15, 34, 20, 31, 10, 25, 14, 19,  9, 13,  8,  7,  6
            };
        }
    }
}
