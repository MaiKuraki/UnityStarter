using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public readonly struct DeterministicTrajectorySegment
    {
        public readonly int Index;
        public readonly int HitIndex;
        public readonly FPVector3 From;
        public readonly FPVector3 To;
        public readonly FPVector3 Direction;
        public readonly FPInt64 Distance;

        public DeterministicTrajectorySegment(
            int index,
            int hitIndex,
            FPVector3 from,
            FPVector3 to,
            FPVector3 direction,
            FPInt64 distance)
        {
            Index = index;
            HitIndex = hitIndex;
            From = from;
            To = to;
            Direction = direction;
            Distance = distance;
        }
    }
}
