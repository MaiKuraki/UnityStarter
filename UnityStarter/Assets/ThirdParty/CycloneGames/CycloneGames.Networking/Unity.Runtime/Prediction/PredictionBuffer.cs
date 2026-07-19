using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Prediction
{
    /// <summary>
    /// Fixed-size ring buffer for storing input/state history per tick.
    /// Used by client prediction and server reconciliation.
    /// Storage is preallocated at construction; benchmark the concrete generic type and caller
    /// before making a zero-allocation claim for a product hot path.
    /// </summary>
    public sealed class PredictionBuffer<T> where T : struct
    {
        private readonly T[] _buffer;
        private readonly long[] _ticks;
        private readonly bool[] _valid;
        private readonly int _capacity;
        private readonly int _mask;

        public int Capacity => _capacity;

        public PredictionBuffer(int capacity = NetworkConstants.MaxSnapshotBufferSize)
        {
            if (capacity <= 0 || capacity > 1 << 30)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            // Round up to power of 2 for fast modulo via bitwise AND
            _capacity = NextPowerOfTwo(capacity);
            _mask = _capacity - 1;
            _buffer = new T[_capacity];
            _ticks = new long[_capacity];
            _valid = new bool[_capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(NetworkTickId tick, in T value)
        {
            int index = GetIndex(tick);
            _buffer[index] = value;
            _ticks[index] = tick.Value;
            _valid[index] = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(NetworkTickId tick, out T value)
        {
            int index = GetIndex(tick);
            if (_valid[index] && _ticks[index] == tick.Value)
            {
                value = _buffer[index];
                return true;
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(NetworkTickId tick)
        {
            int index = GetIndex(tick);
            if (!_valid[index] || _ticks[index] != tick.Value)
            {
                throw new InvalidOperationException("The requested prediction tick is not retained in this buffer slot.");
            }

            return ref _buffer[index];
        }

        public void Clear()
        {
            Array.Clear(_valid, 0, _capacity);
        }

        public void Invalidate(NetworkTickId tick)
        {
            int index = GetIndex(tick);
            _valid[index] = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetIndex(NetworkTickId tick)
        {
            if (!tick.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            return (int)(tick.Value & _mask);
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1; v |= v >> 2; v |= v >> 4;
            v |= v >> 8; v |= v >> 16;
            return v + 1;
        }
    }
}
