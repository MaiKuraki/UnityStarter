using System;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public sealed class DeterministicTrajectoryTraceBuffer
    {
        private readonly DeterministicTrajectorySegment[] _segments;
        private readonly DeterministicTrajectoryHit[] _hits;
        private readonly DeterministicTrajectoryHit[] _castHits;
        private int _castHitsValidCount;

        public DeterministicTrajectoryTraceBuffer(
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

            _segments = new DeterministicTrajectorySegment[segmentCapacity];
            _hits = new DeterministicTrajectoryHit[hitCapacity];
            _castHits = new DeterministicTrajectoryHit[castHitCapacity];
        }

        public int SegmentCount { get; private set; }

        public int HitCount { get; private set; }

        public int SegmentCapacity
        {
            get { return _segments.Length; }
        }

        public int HitCapacity
        {
            get { return _hits.Length; }
        }

        public int CastHitCapacity
        {
            get { return _castHits.Length; }
        }

        /// <summary>
        /// Scratch array loaned to <see cref="IDeterministicTrajectoryCollisionWorld.Cast"/>. Its
        /// length is the hard write limit and the reference must not be retained past the call.
        /// </summary>
        internal DeterministicTrajectoryHit[] CastHits
        {
            get { return _castHits; }
        }

        /// <summary>
        /// Recorded hits, exposed so the solver can suppress pierce repeats against targets it has
        /// already passed through. Mirrors TrajectoryTraceBuffer.Hits in the float Core.
        /// </summary>
        internal DeterministicTrajectoryHit[] Hits
        {
            get { return _hits; }
        }

        public DeterministicTrajectorySegment GetSegment(int index)
        {
            if ((uint)index >= (uint)SegmentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _segments[index];
        }

        public DeterministicTrajectoryHit GetHit(int index)
        {
            if ((uint)index >= (uint)HitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _hits[index];
        }

        /// <summary>
        /// Resets the counts and clears the cast-scratch entries written by the previous trace, so an
        /// adapter that overclaims its hit count cannot resurface a stale hit. Segment and hit arrays
        /// hold only value types and reads are bounded by the counts, so they are left untouched.
        /// </summary>
        public void Clear()
        {
            SegmentCount = 0;
            HitCount = 0;

            if (_castHitsValidCount > 0)
            {
                Array.Clear(_castHits, 0, _castHitsValidCount);
                _castHitsValidCount = 0;
            }
        }

        /// <summary>
        /// Records how many cast-scratch entries the adapter claims it wrote, clamped to capacity.
        /// Those entries are cleared by the next <see cref="Clear"/>.
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

        internal bool TryAddSegment(in DeterministicTrajectorySegment segment)
        {
            if (SegmentCount >= _segments.Length)
            {
                return false;
            }

            _segments[SegmentCount] = segment;
            SegmentCount++;
            return true;
        }

        internal bool TryAddHit(in DeterministicTrajectoryHit hit)
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
