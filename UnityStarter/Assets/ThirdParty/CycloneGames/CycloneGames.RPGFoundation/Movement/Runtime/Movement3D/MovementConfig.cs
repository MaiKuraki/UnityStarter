using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "CycloneGames/RPG Foundation/Movement/Movement Config")]
    public class MovementConfig : MovementConfigBase
    {
        [Min(0.0f)]
        [Tooltip("Unit: m. Valid range: 0 .. 50.")]
        [SerializeField] private float rollDistance = 5f;
        [Min(0.01f)]
        [Tooltip("Unit: s. Valid range: 0.01 .. 10.")]
        [SerializeField] private float rollDuration = 0.5f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float swimSpeed = 3f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float flySpeed = 6f;

        [Min(-200.0f)]
        [Tooltip("Unit: m/s^2. Valid range: -200 .. 0.")]
        [SerializeField] private float gravity = -20f;
        [Min(0.0f)]
        [Tooltip("Unit: x. Valid range: 0 .. 2.")]
        [SerializeField] private float airControlMultiplier = 0.5f;
        [Min(0.001f)]
        [Tooltip("Unit: m. Valid range: 0.001 .. 2.")]
        [SerializeField] private float groundedCheckDistance = 0.025f;
        [SerializeField] private LayerMask groundLayer = 1;
        [Tooltip("Layer mask for collision detection during movement. If empty, uses groundLayer.")]
        [SerializeField] private LayerMask collisionLayer = 0;
        [Min(0.0f)]
        [SerializeField] private float slopeLimit = 45f;
        [Min(0.0f)]
        [SerializeField] private float stepHeight = 0.3f;
        [Tooltip("Minimum time (seconds) character must be airborne before triggering fall animation. Prevents false triggers on stairs.")]
        [Range(0.05f, 0.3f)]
        [Min(0.01f)]
        [SerializeField] private float minAirborneTimeForFall = 0.1f;

        [Tooltip("Enable moving platform support. Character will move with platforms.")]
        [SerializeField] private bool enableMovingPlatform = true;
        [Tooltip("Inherit platform rotation. Character will rotate with rotating platforms.")]
        [SerializeField] private bool inheritPlatformRotation = true;
        [Tooltip("Inherit platform momentum when jumping off. Character will keep platform velocity.")]
        [SerializeField] private bool inheritPlatformMomentum = true;
        [Tooltip("Layer mask for detecting moving platforms. If empty, uses groundLayer.")]
        [SerializeField] private LayerMask platformLayer = 0;

        [Tooltip("Enable ceiling detection to prevent head clipping during jumps.")]
        [SerializeField] private bool enableCeilingDetection = true;
        [Tooltip("Extra distance above character to check for ceiling.")]
        [Min(0.001f)]
        [SerializeField] private float ceilingCheckDistance = 0.1f;

        [Tooltip("Enable gap bridging to auto-jump across small gaps while running.")]
        [SerializeField] private bool enableGapBridging = true;
        [Tooltip("Minimum speed required to trigger gap bridging (m/s).")]
        [Min(0.0f)]
        [SerializeField] private float minSpeedForGapBridge = 3f;
        [Tooltip("Maximum gap distance that can be bridged (m).")]
        [Min(0.0f)]
        [SerializeField] private float maxGapDistance = 0.5f;
        [Tooltip("Maximum height difference allowed for gap bridging (m).")]
        [Min(0.0f)]
        [SerializeField] private float maxGapHeightDiff = 0.3f;

        [SerializeField] private bool enableLadderClimbing = true;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float ladderClimbSpeed = 3f;
        [SerializeField] private LayerMask ladderLayer = 0;

        [SerializeField] private bool enableWallClimbing = false;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float wallClimbSpeed = 2f;
        [SerializeField] private LayerMask wallLayer = 0;
        [Min(0.001f)]
        [Tooltip("Unit: m. Valid range: 0.001 .. 5.")]
        [SerializeField] private float wallCheckDistance = 0.5f;
        [Min(0.01f)]
        [Tooltip("Unit: s. Valid range: 0.01 .. 10.")]
        [SerializeField] private float wallClingDuration = 0.5f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float wallSlideSpeed = 2f;

        [SerializeField] private bool enableWallJump = true;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 100.")]
        [SerializeField] private float wallJumpForceHorizontal = 8f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 100.")]
        [SerializeField] private float wallJumpForceVertical = 10f;
        [Min(0.0f)]
        [Tooltip("Unit: s. Valid range: 0 .. 5.")]
        [SerializeField] private float wallJumpCooldown = 0.1f;
        [Tooltip("Minimum angle difference (degrees) to consider as different wall for continuous wall jump.")]
        [Range(30f, 120f)]
        [Min(0.0f)]
        [SerializeField] private float differentWallAngle = 60f;

        [Min(0.0f)]
        [SerializeField] private float rotationSpeed = 10f;

        [Tooltip("Time after leaving ground that jump input is still accepted.")]
        [Min(0.0f)]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("Time before landing that jump input is buffered.")]
        [Min(0.0f)]
        [SerializeField] private float jumpBufferTime = 0.1f;

        public float RollDistance => rollDistance;
        public float RollDuration => rollDuration;
        public float SwimSpeed => swimSpeed;
        public float FlySpeed => flySpeed;
        public float Gravity => gravity;
        public float AirControlMultiplier => airControlMultiplier;
        public float GroundedCheckDistance => groundedCheckDistance;
        public LayerMask GroundLayer => groundLayer;
        public LayerMask CollisionLayer => collisionLayer;
        public float SlopeLimit => slopeLimit;
        public float StepHeight => stepHeight;
        public float MinAirborneTimeForFall => minAirborneTimeForFall;
        public bool EnableMovingPlatform => enableMovingPlatform;
        public bool InheritPlatformRotation => inheritPlatformRotation;
        public bool InheritPlatformMomentum => inheritPlatformMomentum;
        public LayerMask PlatformLayer => platformLayer;
        public bool EnableCeilingDetection => enableCeilingDetection;
        public float CeilingCheckDistance => ceilingCheckDistance;
        public bool EnableGapBridging => enableGapBridging;
        public float MinSpeedForGapBridge => minSpeedForGapBridge;
        public float MaxGapDistance => maxGapDistance;
        public float MaxGapHeightDiff => maxGapHeightDiff;
        public bool EnableLadderClimbing => enableLadderClimbing;
        public float LadderClimbSpeed => ladderClimbSpeed;
        public LayerMask LadderLayer => ladderLayer;
        public bool EnableWallClimbing => enableWallClimbing;
        public float WallClimbSpeed => wallClimbSpeed;
        public LayerMask WallLayer => wallLayer;
        public float WallCheckDistance => wallCheckDistance;
        public float WallClingDuration => wallClingDuration;
        public float WallSlideSpeed => wallSlideSpeed;
        public bool EnableWallJump => enableWallJump;
        public float WallJumpForceHorizontal => wallJumpForceHorizontal;
        public float WallJumpForceVertical => wallJumpForceVertical;
        public float WallJumpCooldown => wallJumpCooldown;
        public float DifferentWallAngle => differentWallAngle;
        public float RotationSpeed => rotationSpeed;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;

        private void OnValidate()
        {
            var rules = new MovementConfigRules();
            MovementConfigIssueCode ignored;
            SanitizeBaseFields(rules);
            rollDistance = rules.Sanitize(MovementConfigField.RollDistance3D, rollDistance, out ignored);
            rollDuration = rules.Sanitize(MovementConfigField.RollDuration3D, rollDuration, out ignored);
            swimSpeed = rules.Sanitize(MovementConfigField.SwimSpeed, swimSpeed, out ignored);
            flySpeed = rules.Sanitize(MovementConfigField.FlySpeed, flySpeed, out ignored);
            gravity = rules.Sanitize(MovementConfigField.Gravity3D, gravity, out ignored);
            airControlMultiplier = rules.Sanitize(MovementConfigField.AirControl3D, airControlMultiplier, out ignored);
            groundedCheckDistance = rules.Sanitize(MovementConfigField.GroundedCheckDistance, groundedCheckDistance, out ignored);
            slopeLimit = rules.Sanitize(MovementConfigField.SlopeLimit, slopeLimit, out ignored);
            stepHeight = rules.Sanitize(MovementConfigField.StepHeight, stepHeight, out ignored);
            minAirborneTimeForFall = rules.Sanitize(MovementConfigField.MinAirborneTimeForFall, minAirborneTimeForFall, out ignored);
            ceilingCheckDistance = rules.Sanitize(MovementConfigField.CeilingCheckDistance, ceilingCheckDistance, out ignored);
            minSpeedForGapBridge = rules.Sanitize(MovementConfigField.MinSpeedForGapBridge3D, minSpeedForGapBridge, out ignored);
            maxGapDistance = rules.Sanitize(MovementConfigField.MaxGapDistance3D, maxGapDistance, out ignored);
            maxGapHeightDiff = rules.Sanitize(MovementConfigField.MaxGapHeightDiff, maxGapHeightDiff, out ignored);
            ladderClimbSpeed = rules.Sanitize(MovementConfigField.LadderClimbSpeed3D, ladderClimbSpeed, out ignored);
            wallClimbSpeed = rules.Sanitize(MovementConfigField.WallClimbSpeed3D, wallClimbSpeed, out ignored);
            wallCheckDistance = rules.Sanitize(MovementConfigField.WallCheckDistance3D, wallCheckDistance, out ignored);
            wallClingDuration = rules.Sanitize(MovementConfigField.WallClingDuration3D, wallClingDuration, out ignored);
            wallSlideSpeed = rules.Sanitize(MovementConfigField.WallSlideSpeed3D, wallSlideSpeed, out ignored);
            wallJumpForceHorizontal = rules.Sanitize(MovementConfigField.WallJumpForceHorizontal, wallJumpForceHorizontal, out ignored);
            wallJumpForceVertical = rules.Sanitize(MovementConfigField.WallJumpForceVertical, wallJumpForceVertical, out ignored);
            wallJumpCooldown = rules.Sanitize(MovementConfigField.WallJumpCooldown3D, wallJumpCooldown, out ignored);
            differentWallAngle = rules.Sanitize(MovementConfigField.DifferentWallAngle3D, differentWallAngle, out ignored);
            rotationSpeed = rules.Sanitize(MovementConfigField.RotationSpeed, rotationSpeed, out ignored);
            coyoteTime = rules.Sanitize(MovementConfigField.CoyoteTime3D, coyoteTime, out ignored);
            jumpBufferTime = rules.Sanitize(MovementConfigField.JumpBufferTime3D, jumpBufferTime, out ignored);
        }

        protected override void WriteNumericValues(float[] destination)
        {
            base.WriteNumericValues(destination);
            destination[(int)MovementConfigField.RollDistance3D] = rollDistance;
            destination[(int)MovementConfigField.RollDuration3D] = rollDuration;
            destination[(int)MovementConfigField.SwimSpeed] = swimSpeed;
            destination[(int)MovementConfigField.FlySpeed] = flySpeed;
            destination[(int)MovementConfigField.Gravity3D] = gravity;
            destination[(int)MovementConfigField.AirControl3D] = airControlMultiplier;
            destination[(int)MovementConfigField.GroundedCheckDistance] = groundedCheckDistance;
            destination[(int)MovementConfigField.SlopeLimit] = slopeLimit;
            destination[(int)MovementConfigField.StepHeight] = stepHeight;
            destination[(int)MovementConfigField.MinAirborneTimeForFall] = minAirborneTimeForFall;
            destination[(int)MovementConfigField.CeilingCheckDistance] = ceilingCheckDistance;
            destination[(int)MovementConfigField.MinSpeedForGapBridge3D] = minSpeedForGapBridge;
            destination[(int)MovementConfigField.MaxGapDistance3D] = maxGapDistance;
            destination[(int)MovementConfigField.MaxGapHeightDiff] = maxGapHeightDiff;
            destination[(int)MovementConfigField.LadderClimbSpeed3D] = ladderClimbSpeed;
            destination[(int)MovementConfigField.WallClimbSpeed3D] = wallClimbSpeed;
            destination[(int)MovementConfigField.WallCheckDistance3D] = wallCheckDistance;
            destination[(int)MovementConfigField.WallClingDuration3D] = wallClingDuration;
            destination[(int)MovementConfigField.WallSlideSpeed3D] = wallSlideSpeed;
            destination[(int)MovementConfigField.WallJumpForceHorizontal] = wallJumpForceHorizontal;
            destination[(int)MovementConfigField.WallJumpForceVertical] = wallJumpForceVertical;
            destination[(int)MovementConfigField.WallJumpCooldown3D] = wallJumpCooldown;
            destination[(int)MovementConfigField.DifferentWallAngle3D] = differentWallAngle;
            destination[(int)MovementConfigField.RotationSpeed] = rotationSpeed;
            destination[(int)MovementConfigField.CoyoteTime3D] = coyoteTime;
            destination[(int)MovementConfigField.JumpBufferTime3D] = jumpBufferTime;
        }

    }
}
