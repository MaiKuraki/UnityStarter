using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public sealed class TrajectoryTraceBuffer
    {
        private readonly TrajectorySegment[] _segments;
        private readonly TrajectoryHit[] _hits;
        private readonly TrajectoryHit[] _castHits;

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

        internal TrajectoryHit[] CastHits
        {
            get
            {
                return _castHits;
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

        public void Clear()
        {
            SegmentCount = 0;
            HitCount = 0;
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
