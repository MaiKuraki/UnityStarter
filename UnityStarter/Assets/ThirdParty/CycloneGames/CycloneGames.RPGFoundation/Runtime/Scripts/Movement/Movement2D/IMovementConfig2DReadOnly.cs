using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    /// <summary>
    /// Read-only interface for accessing 2D movement configuration values.
    /// Extends base config with 2D-specific properties.
    /// </summary>
    public interface IMovementConfig2DReadOnly : Movement.IMovementConfigReadOnly
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
        string verticalSpeedParameter { get; }
        string rollTrigger { get; }
        string inputXParameter { get; }
        string inputYParameter { get; }
    }
}
