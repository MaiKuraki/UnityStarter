using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Caller-owned storage for one trace. Reuse it across calls; do not share an instance between
    /// concurrent traces.
    ///
    /// Invariants maintained by <see cref="TrajectorySolver"/>:
    /// <list type="bullet">
    /// <item><description><c>HitCount &lt;= SegmentCount</c>: every hit has a segment.</description></item>
    /// <item><description><c>0 &lt;= GetHit(i).SegmentIndex &lt; SegmentCount</c> for every recorded hit.</description></item>
    /// <item><description><c>GetSegment(i).HitIndex</c> is either -1 or a valid hit index.</description></item>
    /// </list>
    /// </summary>
    public sealed class TrajectoryTraceBuffer
    {
        private readonly TrajectorySegment[] _segments;
        private readonly TrajectoryHit[] _hits;
        private readonly TrajectoryHit[] _castHits;
        private int _castHitsValidCount;

        public TrajectoryTraceBuffer(
            int segmentCapacity,
            int hitCapacity,
            int castHitCapacity)
        {
            if (segmentCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentCapacity));
            }

            if (hitCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hitCapacity));
            }

            if (castHitCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(castHitCapacity));
            }

            _segments = new TrajectorySegment[segmentCapacity];
            _hits = new TrajectoryHit[hitCapacity];
            _castHits = new TrajectoryHit[castHitCapacity];
        }

        public int SegmentCount { get; private set; }

        public int HitCount { get; private set; }

        public int SegmentCapacity
        {
            get
            {
                return _segments.Length;
            }
        }

        public int HitCapacity
        {
            get
            {
                return _hits.Length;
            }
        }

        public int CastHitCapacity
        {
            get
            {
                return _castHits.Length;
            }
        }

        /// <summary>
        /// Scratch array handed to <see cref="ITrajectoryCollisionWorld.Cast"/>. The solver passes it
        /// as a loan only: an implementation must not retain the reference, must write no more than
        /// <see cref="CastHitCapacity"/> entries, and must return the number of entries it wrote.
        /// </summary>
        internal TrajectoryHit[] CastHits
        {
            get
            {
                return _castHits;
            }
        }

        /// <summary>
        /// Recorded hits, exposed so the solver can suppress pierce repeats against targets it has
        /// already passed through.
        /// </summary>
        internal TrajectoryHit[] Hits
        {
            get
            {
                return _hits;
            }
        }

        public TrajectorySegment GetSegment(int index)
        {
            if ((uint)index >= (uint)SegmentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _segments[index];
        }

        public TrajectoryHit GetHit(int index)
        {
            if ((uint)index >= (uint)HitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _hits[index];
        }

        /// <summary>
        /// Resets the counts and clears the cast-scratch entries written by the previous trace, so a
        /// cast implementation that overclaims its hit count cannot resurface a stale hit.
        /// Segment and hit arrays are left untouched: they hold only value types and reads are
        /// bounded by <see cref="SegmentCount"/> / <see cref="HitCount"/>.
        /// </summary>
        public void Clear()
        {
            SegmentCount = 0;
            HitCount = 0;

            if (_castHitsValidCount > 0)
            {
                // Array.Clear lowers to a vectorised memset. A per-element loop over a multi-field
                // struct does not, and its code generation depends on the JIT.
                Array.Clear(_castHits, 0, _castHitsValidCount);
                _castHitsValidCount = 0;
            }
        }

        /// <summary>
        /// Records how many cast-scratch entries the adapter claims it wrote, clamped to the array
        /// capacity. Those entries are cleared by the next <see cref="Clear"/>.
        /// </summary>
        internal void MarkCastHitsWritten(int count)
        {
            if (count <= 0)
            {
                _castHitsValidCount = 0;
                return;
            }

            _castHitsValidCount = count < _castHits.Length ? count : _castHits.Length;
        }

        internal bool TryAddSegment(in TrajectorySegment segment)
        {
            if (SegmentCount >= _segments.Length)
            {
                return false;
            }

            _segments[SegmentCount] = segment;
            SegmentCount++;
            return true;
        }

        internal bool TryAddHit(in TrajectoryHit hit)
        {
            if (HitCount >= _hits.Length)
            {
                return false;
            }

            _hits[HitCount] = hit;
            HitCount++;
            return true;
        }
    }
}
