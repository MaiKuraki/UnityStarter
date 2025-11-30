using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public interface IMovementStateQuery
    {
        MovementStateType CurrentState { get; }
        bool IsGrounded { get; }
        float CurrentSpeed { get; }
        Vector3 Velocity { get; }
        bool IsMoving { get; }
    }
}
