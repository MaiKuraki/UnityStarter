using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    /// <summary>
    /// Read-only wrapper for MovementConfig2D that prevents external modification.
    /// </summary>
    public class MovementConfig2DReadOnlyWrapper : IMovementConfig2DReadOnly
    {
        private readonly MovementConfig2D _config;

        public MovementConfig2DReadOnlyWrapper(MovementConfig2D config)
        {
            _config = config;
        }

        // Base config properties
        public float walkSpeed => _config.walkSpeed;
        public float runSpeed => _config.runSpeed;
        public float sprintSpeed => _config.sprintSpeed;
        public float crouchSpeed => _config.crouchSpeed;
        public float jumpForce => _config.jumpForce;
        public int maxJumpCount => _config.maxJumpCount;
        public Movement.AnimationSystemType animationSystem => _config.animationSystem;
        public Movement.AnimancerParameterMode animancerParameterMode => _config.animancerParameterMode;
        public string movementSpeedParameter => _config.movementSpeedParameter;
        public string isGroundedParameter => _config.isGroundedParameter;
        public string jumpTrigger => _config.jumpTrigger;

        // 2D-specific properties
        public MovementType2D movementType => _config.movementType;
        public float airControlMultiplier => _config.airControlMultiplier;
        public float coyoteTime => _config.coyoteTime;
        public float jumpBufferTime => _config.jumpBufferTime;
        public float gravity => _config.gravity;
        public float maxFallSpeed => _config.maxFallSpeed;
        public float groundCheckDistance => _config.groundCheckDistance;
        public LayerMask groundLayer => _config.groundLayer;
        public Vector2 groundCheckSize => _config.groundCheckSize;
        public Vector2 groundCheckOffset => _config.groundCheckOffset;
        public bool lockZAxis => _config.lockZAxis;
        public float slideSpeed => _config.slideSpeed;
        public float wallJumpForceX => _config.wallJumpForceX;
        public float wallJumpForceY => _config.wallJumpForceY;
        public bool facingRight => _config.facingRight;
        public string verticalSpeedParameter => _config.verticalSpeedParameter;
        public string rollTrigger => _config.rollTrigger;
        public string inputXParameter => _config.inputXParameter;
        public string inputYParameter => _config.inputYParameter;
    }
}
