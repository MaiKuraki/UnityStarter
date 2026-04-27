using System;
using System.Runtime.CompilerServices;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.Networking.Prediction
{
    /// <summary>
    /// Fixed-size ring buffer for storing input/state history per tick.
    /// Used by client prediction and server reconciliation.
    /// Zero allocation after initial construction.
    /// </summary>
    public sealed class PredictionBuffer<T> where T : struct
    {
        private readonly T[] _buffer;
        private readonly uint[] _ticks;
        private readonly int _capacity;
        private readonly int _mask;

        public int Capacity => _capacity;

        public PredictionBuffer(int capacity = NetworkConstants.MaxSnapshotBufferSize)
        {
            // Round up to power of 2 for fast modulo via bitwise AND
            _capacity = NextPowerOfTwo(capacity);
            _mask = _capacity - 1;
            _buffer = new T[_capacity];
            _ticks = new uint[_capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(NetworkTick tick, in T value)
        {
            int index = (int)(tick.Value & _mask);
            _buffer[index] = value;
            _ticks[index] = tick.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(NetworkTick tick, out T value)
        {
            int index = (int)(tick.Value & _mask);
            if (_ticks[index] == tick.Value)
            {
                value = _buffer[index];
                return true;
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(NetworkTick tick)
        {
            return ref _buffer[(int)(tick.Value & _mask)];
        }

        public void Clear()
        {
            Array.Clear(_ticks, 0, _capacity);
        }

        public void Invalidate(NetworkTick tick)
        {
            int index = (int)(tick.Value & _mask);
            // Set tick to 0 which won't match any valid tick (ticks start from 1+)
            _ticks[index] = 0;
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
