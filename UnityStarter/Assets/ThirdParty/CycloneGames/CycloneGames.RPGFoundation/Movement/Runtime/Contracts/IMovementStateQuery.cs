using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public interface IMovementStateQuery
    {
        MovementStateType CurrentState { get; }
        bool IsGrounded { get; }
        float CurrentSpeed { get; }
        bool IsMoving { get; }
    }

    public interface IMovementStateQuery3D : IMovementStateQuery
    {
        Vector3 Velocity { get; }
    }

    public interface IMovementStateQuery2D : IMovementStateQuery
    {
        Vector2 Velocity { get; }
        Vector2 LookDirection { get; }
    }
}