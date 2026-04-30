using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Read-only wrapper for MovementConfig that prevents external modification.
    /// </summary>
    public class MovementConfig3DReadOnlyWrapper : IMovementConfig3DReadOnly
    {
        private readonly MovementConfig _config;

        public MovementConfig3DReadOnlyWrapper(MovementConfig config)
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
        public AnimationSystemType animationSystem => _config.AnimationSystem;
        public AnimancerParameterMode animancerParameterMode => _config.AnimancerParameterMode;
        public string movementSpeedParameter => _config.MovementSpeedParameter;
        public string isGroundedParameter => _config.IsGroundedParameter;
        public string jumpTrigger => _config.JumpTrigger;

        // 3D-specific properties
        public float rollDistance => _config.RollDistance;
        public float rollDuration => _config.RollDuration;
        public float climbSpeed => _config.LadderClimbSpeed;
        public float swimSpeed => _config.SwimSpeed;
        public float flySpeed => _config.FlySpeed;
        public float gravity => _config.Gravity;
        public float airControlMultiplier => _config.AirControlMultiplier;
        public float groundedCheckDistance => _config.GroundedCheckDistance;
        public LayerMask groundLayer => _config.GroundLayer;
        public float slopeLimit => _config.SlopeLimit;
        public float stepHeight => _config.StepHeight;
        public float minAirborneTimeForFall => _config.MinAirborneTimeForFall;
        public float rotationSpeed => _config.RotationSpeed;
        public string rollTrigger => _config.RollTrigger;
    }
}