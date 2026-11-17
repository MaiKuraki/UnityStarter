using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Traces many queries with pre-allocated per-query buffers, so a shotgun spread or a batch of
    /// AI sight checks does not force the caller to manage an array of trace buffers by hand.
    ///
    /// Allocation happens once in the constructor. <see cref="TraceAll(ReadOnlySpan{TrajectoryQuery},ITrajectoryCollisionWorld,Span{TrajectoryTraceResult})"/>
    /// is allocation-free afterwards.
    ///
    /// Not thread-safe: one tracer belongs to one worker. Split the batch across threads by giving
    /// each thread its own tracer.
    /// </summary>
    public sealed class TrajectoryBatchTracer
    {
        private readonly TrajectoryTraceBuffer[] _buffers;

        public TrajectoryBatchTracer(
            int capacity,
            int segmentCapacity,
            int hitCapacity,
            int castHitCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffers = new TrajectoryTraceBuffer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _buffers[i] = new TrajectoryTraceBuffer(segmentCapacity, hitCapacity, castHitCapacity);
            }
        }

        /// <summary>Number of queries one call can trace.</summary>
        public int Capacity
        {
            get
            {
                return _buffers.Length;
            }
        }

        /// <summary>
        /// Buffer owned by slot <paramref name="index"/>. Read it after a trace to inspect that
        /// query's segments and hits.
        /// </summary>
        public TrajectoryTraceBuffer GetBuffer(int index)
        {
            if ((uint)index >= (uint)_buffers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffers[index];
        }

        /// <summary>
        /// Traces up to <c>min(queries.Length, results.Length, Capacity)</c> queries and writes one
        /// result per query at the same index. Returns how many were traced; anything beyond
        /// <see cref="Capacity"/> is left untouched.
        /// </summary>
        public int TraceAll(
            ReadOnlySpan<TrajectoryQuery> queries,
            ITrajectoryCollisionWorld world,
            Span<TrajectoryTraceResult> results)
        {
            int count = queries.Length;
            if (results.Length < count)
            {
                count = results.Length;
            }

            if (_buffers.Length < count)
            {
                count = _buffers.Length;
            }

            for (int i = 0; i < count; i++)
            {
                TrajectoryQuery query = queries[i];
                results[i] = TrajectorySolver.Trace(in query, world, _buffers[i]);
            }

            return count;
        }

        /// <summary>
        /// Traces without a result sink. Use for warming, benchmarking, or when only the per-slot
        /// buffers are read afterwards.
        /// </summary>
        public int TraceAll(ReadOnlySpan<TrajectoryQuery> queries, ITrajectoryCollisionWorld world)
        {
            int count = queries.Length < _buffers.Length ? queries.Length : _buffers.Length;
            for (int i = 0; i < count; i++)
            {
                TrajectoryQuery query = queries[i];
                TrajectorySolver.Trace(in query, world, _buffers[i]);
            }

            return count;
        }
    }
}
