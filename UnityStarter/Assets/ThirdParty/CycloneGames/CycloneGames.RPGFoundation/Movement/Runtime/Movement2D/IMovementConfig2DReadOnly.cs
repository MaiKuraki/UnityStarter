using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D
{
    public interface IMovementConfig2DReadOnly : IMovementConfigReadOnly
    {
        MovementType2D movementType { get; }
        float airControlMultiplier { get; }
        float coyoteTime { get; }
        float jumpBufferTime { get; }
        float gravity { get; }
        float maxFallSpeed { get; }
        float groundCheckDistance { get; }
        LayerMask groundLayer { get; }
        Vector2 groundCheckSize { get; }
        Vector2 groundCheckOffset { get; }
        bool lockZAxis { get; }
        float slideSpeed { get; }
        float wallJumpForceX { get; }
        float wallJumpForceY { get; }
        bool facingRight { get; }
    }
}
