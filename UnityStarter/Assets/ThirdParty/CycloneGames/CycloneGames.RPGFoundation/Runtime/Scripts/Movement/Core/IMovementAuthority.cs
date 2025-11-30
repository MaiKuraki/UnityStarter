namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public interface IMovementAuthority
    {
        bool CanEnterState(MovementStateType stateType, object context = null);
        void OnStateEntered(MovementStateType stateType);
        void OnStateExited(MovementStateType stateType);
    }
}