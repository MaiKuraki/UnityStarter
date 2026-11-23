
namespace CycloneGames.RPGFoundation.Movement.Core
{
    public interface IPathfindingProvider
    {
        bool SetDestination(MovementVector3 destination);

        void StopNavigation();

        bool IsNavigating { get; }

        bool HasReachedDestination { get; }

        MovementVector3 CurrentDestination { get; }

        MovementVector3 CurrentDirection { get; }

        float RemainingDistance { get; }
    }
}
