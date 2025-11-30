using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Base interface for querying movement state.
    /// Implemented by both 3D and 2D movement components.
    /// </summary>
    public interface IMovementStateQuery
    {
        MovementStateType CurrentState { get; }
        bool IsGrounded { get; }
        float CurrentSpeed { get; }
        bool IsMoving { get; }
    }

    /// <summary>
    /// 3D-specific movement state query with Vector3 velocity.
    /// </summary>
    public interface IMovementStateQuery3D : IMovementStateQuery
    {
        Vector3 Velocity { get; }
    }

    /// <summary>
    /// 2D-specific movement state query with Vector2 velocity.
    /// </summary>
    public interface IMovementStateQuery2D : IMovementStateQuery
    {
        Vector2 Velocity { get; }
    }
}