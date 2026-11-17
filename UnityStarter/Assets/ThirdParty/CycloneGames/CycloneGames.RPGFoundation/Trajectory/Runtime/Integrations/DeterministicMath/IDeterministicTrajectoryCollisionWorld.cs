using System;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    /// <summary>
    /// Fixed-point counterpart of <see cref="Core.ITrajectoryCollisionWorld"/>. The results span is a
    /// caller-owned loan with a hard write limit; it cannot be retained because it is a ref struct.
    /// </summary>
    public interface IDeterministicTrajectoryCollisionWorld
    {
        int Cast(
            in DeterministicTrajectoryCastQuery query,
            Span<DeterministicTrajectoryHit> results);
    }
}
