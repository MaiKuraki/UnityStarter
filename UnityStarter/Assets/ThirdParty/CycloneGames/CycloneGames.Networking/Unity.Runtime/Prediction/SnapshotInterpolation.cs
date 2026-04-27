using System.Runtime.CompilerServices;
using CycloneGames.Networking.Simulation;
using UnityEngine;

namespace CycloneGames.Networking.Prediction
{
    /// <summary>
    /// Generic snapshot interpolation for smooth visual representation of networked entities.
    /// Buffers received snapshots and interpolates between them with a configurable delay.
    /// 
    /// Use cases:
    /// - Remote player positions (all game types)
    /// - Vehicle positions (GTA, racing games)
    /// - NPC/Monster positions (Monster Hunter, FFXIV, WoW)
    /// </summary>
    public sealed class SnapshotInterpolation<T> where T : struct
    {
        public delegate T LerpFunc(in T from, in T to, float t);
        public delegate double TimestampFunc(in T snapshot);

        private readonly T[] _buffer;
        private readonly double[] _timestamps;
        private readonly int _capacity;
        private readonly int _mask;
        private int _writeIndex;
        private int _count;

        private readonly LerpFunc _lerp;
        private readonly TimestampFunc _getTimestamp;
        private readonly double _interpolationDelay;

        private T _current;
        public ref readonly T Current => ref _current;

        public int BufferedCount => _count;

        /// <param name="interpolationDelay">Delay in seconds behind latest snapshot (typically 2-3 tick intervals)</param>
        public SnapshotInterpolation(
            LerpFunc lerp,
            TimestampFunc getTimestamp,
            double interpolationDelay = 0.1,
            int bufferCapacity = 32)
        {
            _capacity = bufferCapacity;
            _mask = _capacity - 1;
            _buffer = new T[_capacity];
            _timestamps = new double[_capacity];
            _lerp = lerp;
            _getTimestamp = getTimestamp;
            _interpolationDelay = interpolationDelay;
        }

        public void AddSnapshot(in T snapshot)
        {
            int index = _writeIndex & _mask;
            _buffer[index] = snapshot;
            _timestamps[index] = _getTimestamp(snapshot);
            _writeIndex++;
            if (_count < _capacity) _count++;
        }

        /// <summary>
        /// Update interpolation. Call once per visual frame.
        /// </summary>
        /// <param name="currentTime">Current estimated server time on client side</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(double currentTime)
        {
            if (_count < 2) return;

            double renderTime = currentTime - _interpolationDelay;
            FindBracketingSnapshots(renderTime, out int fromIdx, out int toIdx);

            if (fromIdx < 0 || toIdx < 0) return;

            double fromTime = _timestamps[fromIdx];
            double toTime = _timestamps[toIdx];

            float t;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (toTime == fromTime)
                t = 1f;
            else
                t = Mathf.Clamp01((float)((renderTime - fromTime) / (toTime - fromTime)));

            _current = _lerp(_buffer[fromIdx], _buffer[toIdx], t);
        }

        private void FindBracketingSnapshots(double renderTime, out int fromIdx, out int toIdx)
        {
            fromIdx = -1;
            toIdx = -1;

            int start = _writeIndex - _count;
            int end = _writeIndex;

            for (int i = start; i < end - 1; i++)
            {
                int cur = i & _mask;
                int next = (i + 1) & _mask;

                if (_timestamps[cur] <= renderTime && _timestamps[next] >= renderTime)
                {
                    fromIdx = cur;
                    toIdx = next;
                    return;
                }
            }

            // Extrapolation: use last two if render time is ahead
            if (end - start >= 2)
            {
                fromIdx = (end - 2) & _mask;
                toIdx = (end - 1) & _mask;
            }
        }

        public void Clear()
        {
            _writeIndex = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Pre-built snapshot for common Unity transform data.
    /// </summary>
    public struct TransformSnapshot
    {
        public double Timestamp;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static TransformSnapshot Lerp(in TransformSnapshot from, in TransformSnapshot to, float t)
        {
            return new TransformSnapshot
            {
                Timestamp = from.Timestamp + (to.Timestamp - from.Timestamp) * t,
                Position = Vector3.Lerp(from.Position, to.Position, t),
                Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t),
                Scale = Vector3.Lerp(from.Scale, to.Scale, t)
            };
        }

        public static double GetTimestamp(in TransformSnapshot snapshot) => snapshot.Timestamp;
    }
}
