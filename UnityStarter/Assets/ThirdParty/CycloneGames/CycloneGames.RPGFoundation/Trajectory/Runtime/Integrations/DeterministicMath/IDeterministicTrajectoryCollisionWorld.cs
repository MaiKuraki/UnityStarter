namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public interface IDeterministicTrajectoryCollisionWorld
    {
        int Cast(
            in DeterministicTrajectoryCastQuery query,
            DeterministicTrajectoryHit[] results,
            int maxResults);
    }
}
