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
        public float walkSpeed => _config.walkSpeed;
        public float runSpeed => _config.runSpeed;
        public float sprintSpeed => _config.sprintSpeed;
        public float crouchSpeed => _config.crouchSpeed;
        public float jumpForce => _config.jumpForce;
        public int maxJumpCount => _config.maxJumpCount;
        public AnimationSystemType animationSystem => _config.animationSystem;
        public AnimancerParameterMode animancerParameterMode => _config.animancerParameterMode;
        public string movementSpeedParameter => _config.movementSpeedParameter;
        public string isGroundedParameter => _config.isGroundedParameter;
        public string jumpTrigger => _config.jumpTrigger;

        // 3D-specific properties
        public float rollDistance => _config.rollDistance;
        public float rollDuration => _config.rollDuration;
        public float climbSpeed => _config.ladderClimbSpeed;
        public float swimSpeed => _config.swimSpeed;
        public float flySpeed => _config.flySpeed;
        public float gravity => _config.gravity;
        public float airControlMultiplier => _config.airControlMultiplier;
        public float groundedCheckDistance => _config.groundedCheckDistance;
        public LayerMask groundLayer => _config.groundLayer;
        public float slopeLimit => _config.slopeLimit;
        public float stepHeight => _config.stepHeight;
        public float minAirborneTimeForFall => _config.minAirborneTimeForFall;
        public float rotationSpeed => _config.rotationSpeed;
        public string rollTrigger => _config.rollTrigger;
    }
}