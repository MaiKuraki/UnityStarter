namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectorySegment
    {
        public readonly int Index;
        public readonly int HitIndex;
        public readonly TrajectoryVector3 From;
        public readonly TrajectoryVector3 To;
        public readonly TrajectoryVector3 Direction;
        public readonly float Distance;

        public TrajectorySegment(
            int index,
            int hitIndex,
            TrajectoryVector3 from,
            TrajectoryVector3 to,
            TrajectoryVector3 direction,
            float distance)
        {
            Index = index;
            HitIndex = hitIndex;
            From = from;
            To = to;
            Direction = direction;
            Distance = distance;
        }

        public bool EndsWithHit
        {
            get
            {
                return HitIndex >= 0;
            }
        }
    }
}
