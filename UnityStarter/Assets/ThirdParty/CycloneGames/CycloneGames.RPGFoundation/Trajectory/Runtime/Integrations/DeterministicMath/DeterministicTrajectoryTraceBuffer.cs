using System;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public sealed class DeterministicTrajectoryTraceBuffer
    {
        private readonly DeterministicTrajectorySegment[] _segments;
        private readonly DeterministicTrajectoryHit[] _hits;
        private readonly DeterministicTrajectoryHit[] _castHits;

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

        public int CastHitCapacity
        {
            get
            {
                return _castHits.Length;
            }
        }

        internal DeterministicTrajectoryHit[] CastHits
        {
            get
            {
                return _castHits;
            }
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

        public void Clear()
        {
            SegmentCount = 0;
            HitCount = 0;
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
