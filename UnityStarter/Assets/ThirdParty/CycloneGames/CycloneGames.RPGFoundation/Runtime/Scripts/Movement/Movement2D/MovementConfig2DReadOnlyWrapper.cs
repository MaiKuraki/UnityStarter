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
        public float walkSpeed => _config.WalkSpeed;
        public float runSpeed => _config.RunSpeed;
        public float sprintSpeed => _config.SprintSpeed;
        public float crouchSpeed => _config.CrouchSpeed;
        public float jumpForce => _config.JumpForce;
        public int maxJumpCount => _config.MaxJumpCount;
        public Movement.AnimationSystemType animationSystem => _config.AnimationSystem;
        public Movement.AnimancerParameterMode animancerParameterMode => _config.AnimancerParameterMode;
        public string movementSpeedParameter => _config.MovementSpeedParameter;
        public string isGroundedParameter => _config.IsGroundedParameter;
        public string jumpTrigger => _config.JumpTrigger;

        // 2D-specific properties
        public MovementType2D movementType => _config.MovementType;
        public float airControlMultiplier => _config.AirControlMultiplier;
        public float coyoteTime => _config.CoyoteTime;
        public float jumpBufferTime => _config.JumpBufferTime;
        public float gravity => _config.Gravity;
        public float maxFallSpeed => _config.MaxFallSpeed;
        public float groundCheckDistance => _config.GroundCheckDistance;
        public LayerMask groundLayer => _config.GroundLayer;
        public Vector2 groundCheckSize => _config.GroundCheckSize;
        public Vector2 groundCheckOffset => _config.GroundCheckOffset;
        public bool lockZAxis => _config.LockZAxis;
        public float slideSpeed => _config.SlideSpeed;
        public float wallJumpForceX => _config.WallJumpForceX;
        public float wallJumpForceY => _config.WallJumpForceY;
        public bool facingRight => _config.FacingRight;
        public string verticalSpeedParameter => _config.VerticalSpeedParameter;
        public string rollTrigger => _config.RollTrigger;
        public string inputXParameter => _config.InputXParameter;
        public string inputYParameter => _config.InputYParameter;
    }
}
