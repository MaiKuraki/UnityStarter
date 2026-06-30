namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public interface ITrajectoryCollisionWorld
    {
        int Cast(
            in TrajectoryCastQuery query,
            TrajectoryHit[] results,
            int maxResults);
    }
}
