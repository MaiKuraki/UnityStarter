using System.Runtime.CompilerServices;
using CycloneGames.Networking.Simulation;
using UnityEngine;

namespace CycloneGames.Networking.Prediction
{
    /// <summary>
    /// Server-side lag compensation for hit detection in shooter-style games.
    /// Stores historical positions for each networked entity to rewind the world
    /// to the time a client fired, compensating for network latency.
    /// 
    /// Use cases: PUBG, Fortnite, Counter-Strike-style hit registration
    /// </summary>
    public sealed class LagCompensationBuffer
    {
        private readonly Vector3[] _positions;
        private readonly Quaternion[] _rotations;
        private readonly Bounds[] _bounds;
        private readonly uint[] _ticks;
        private readonly int _capacity;
        private readonly int _mask;
        private int _writeIndex;
        private int _count;

        public LagCompensationBuffer(int capacity = 128)
        {
            // Power of 2 for fast masking
            int pow2 = 1;
            while (pow2 < capacity) pow2 <<= 1;
            _capacity = pow2;
            _mask = _capacity - 1;
            _positions = new Vector3[_capacity];
            _rotations = new Quaternion[_capacity];
            _bounds = new Bounds[_capacity];
            _ticks = new uint[_capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Record(NetworkTick tick, Vector3 position, Quaternion rotation, Bounds bounds)
        {
            int index = _writeIndex & _mask;
            _positions[index] = position;
            _rotations[index] = rotation;
            _bounds[index] = bounds;
            _ticks[index] = tick.Value;
            _writeIndex++;
            if (_count < _capacity) _count++;
        }

        /// <summary>
        /// Sample position/rotation at a specific tick, with interpolation between stored frames.
        /// </summary>
        public bool Sample(NetworkTick targetTick, out Vector3 position, out Quaternion rotation, out Bounds bounds)
        {
            uint target = targetTick.Value;

            // Use count-based iteration to avoid signed overflow of _writeIndex
            int oldest = (_writeIndex - _count) & _mask;

            for (int i = 0; i < _count - 1; i++)
            {
                int cur = (oldest + i) & _mask;
                int next = (oldest + i + 1) & _mask;

                if (_ticks[cur] <= target && _ticks[next] >= target)
                {
                    if (_ticks[cur] == _ticks[next])
                    {
                        position = _positions[cur];
                        rotation = _rotations[cur];
                        bounds = _bounds[cur];
                        return true;
                    }

                    float t = (float)(target - _ticks[cur]) / (_ticks[next] - _ticks[cur]);
                    position = Vector3.Lerp(_positions[cur], _positions[next], t);
                    rotation = Quaternion.Slerp(_rotations[cur], _rotations[next], t);
                    bounds = new Bounds(
                        Vector3.Lerp(_bounds[cur].center, _bounds[next].center, t),
                        Vector3.Lerp(_bounds[cur].size, _bounds[next].size, t));
                    return true;
                }
            }

            // Exact match on last entry
            if (_count > 0)
            {
                int last = (_writeIndex - 1) & _mask;
                if (_ticks[last] == target)
                {
                    position = _positions[last];
                    rotation = _rotations[last];
                    bounds = _bounds[last];
                    return true;
                }
            }

            position = default;
            rotation = Quaternion.identity;
            bounds = default;
            return false;
        }

        /// <summary>
        /// Perform a hit test against the entity's historical position at the given tick.
        /// </summary>
        public bool HitTest(NetworkTick tick, Ray ray, float maxDistance, out float hitDistance)
        {
            hitDistance = 0f;
            if (!Sample(tick, out _, out _, out var historicalBounds))
                return false;

            return historicalBounds.IntersectRay(ray, out hitDistance) && hitDistance <= maxDistance;
        }

        public void Clear()
        {
            _writeIndex = 0;
            _count = 0;
        }
    }
}
