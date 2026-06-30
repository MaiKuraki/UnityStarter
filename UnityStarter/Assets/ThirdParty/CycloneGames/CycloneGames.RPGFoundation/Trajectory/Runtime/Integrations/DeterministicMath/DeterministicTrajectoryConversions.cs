using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public static class DeterministicTrajectoryConversions
    {
        public static FPVector3 ToFPVector3(TrajectoryVector3 value)
        {
            return new FPVector3(
                FPInt64.FromFloat(value.X),
                FPInt64.FromFloat(value.Y),
                FPInt64.FromFloat(value.Z));
        }

        public static TrajectoryVector3 ToTrajectoryVector3(FPVector3 value)
        {
            return new TrajectoryVector3(
                value.X.ToFloat(),
                value.Y.ToFloat(),
                value.Z.ToFloat());
        }

        public static DeterministicTrajectoryHit ToDeterministicHit(TrajectoryHit hit)
        {
            return new DeterministicTrajectoryHit(
                hit.TargetEntityId,
                hit.TargetObjectId,
                hit.HitLayerMask,
                FPInt64.FromFloat(hit.Distance),
                ToFPVector3(hit.Position),
                ToFPVector3(hit.Normal),
                hit.Response,
                hit.SegmentIndex);
        }

        public static TrajectoryHit ToTrajectoryHit(DeterministicTrajectoryHit hit)
        {
            return new TrajectoryHit(
                hit.TargetEntityId,
                hit.TargetObjectId,
                hit.HitLayerMask,
                hit.Distance.ToFloat(),
                ToTrajectoryVector3(hit.Position),
                ToTrajectoryVector3(hit.Normal),
                hit.Response,
                hit.SegmentIndex);
        }
    }
}
