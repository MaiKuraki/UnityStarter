using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D
{
    public enum MovementType2D
    {
        Platformer,
        BeltScroll,
        TopDown
    }

    [CreateAssetMenu(fileName = "MovementConfig2D", menuName = "CycloneGames/RPG Foundation/Movement/Movement Config 2D")]
    public class MovementConfig2D : MovementConfigBase
    {
        [SerializeField] private MovementType2D movementType = MovementType2D.Platformer;

        [Tooltip("Enable jump mechanic. Disable for TopDown games that don't use jumping.")]
        [SerializeField] private bool enableJump = true;

        [Min(0.0f)]
        [SerializeField] private float airControlMultiplier = 0.5f;
        [Min(0.0f)]
        [Tooltip("Unit: s. Valid range: 0 .. 2.")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Min(0.0f)]
        [Tooltip("Unit: s. Valid range: 0 .. 2.")]
        [SerializeField] private float jumpBufferTime = 0.1f;

        [Min(0.0f)]
        [Tooltip("Unit: m/s^2. Valid range: 0 .. 200.")]
        [SerializeField] private float gravity = 25f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 200.")]
        [SerializeField] private float maxFallSpeed = 20f;
        [Min(0.001f)]
        [Tooltip("Unit: m. Valid range: 0.001 .. 2.")]
        [SerializeField] private float groundCheckDistance = 0.1f;
        [SerializeField] private LayerMask groundLayer = 1;

        [SerializeField] private Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);
        [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

        [SerializeField] private bool lockZAxis = true;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float slideSpeed = 7f;
        [Min(0.0f)]
        [Tooltip("Unit: m. Valid range: 0 .. 50.")]
        [SerializeField] private float rollDistance = 5f;
        [Min(0.01f)]
        [Tooltip("Unit: s. Valid range: 0.01 .. 10.")]
        [SerializeField] private float rollDuration = 0.5f;
        [SerializeField] private bool facingRight = true;

        [SerializeField] private bool enableMovingPlatform = true;
        [SerializeField] private bool inheritPlatformRotation = false;
        [SerializeField] private bool inheritPlatformMomentum = true;
        [SerializeField] private LayerMask platformLayer = 0;

        [SerializeField] private bool enableGapBridging = true;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float minSpeedForGapBridge = 4f;
        [Min(0.0f)]
        [Tooltip("Unit: m. Valid range: 0 .. 20.")]
        [SerializeField] private float maxGapDistance = 1.0f;

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
        [SerializeField] private float wallCheckDistance = 0.3f;
        [Min(0.01f)]
        [Tooltip("Unit: s. Valid range: 0.01 .. 10.")]
        [SerializeField] private float wallClingDuration = 0.5f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float wallSlideSpeed = 2f;

        [SerializeField] private bool enableWallJump = true;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 100.")]
        [SerializeField] private float wallJumpForceX = 8f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 100.")]
        [SerializeField] private float wallJumpForceY = 10f;
        [Min(0.0f)]
        [Tooltip("Unit: s. Valid range: 0 .. 5.")]
        [SerializeField] private float wallJumpCooldown = 0.1f;
        [Range(30f, 120f)]
        [Min(0.0f)]
        [Tooltip("Unit: deg. Valid range: 0 .. 180.")]
        [SerializeField] private float differentWallAngle = 60f;

        public MovementType2D MovementType => movementType;
        public bool EnableJump => enableJump;
        public float AirControlMultiplier => airControlMultiplier;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;
        public float Gravity => gravity;
        public float MaxFallSpeed => maxFallSpeed;
        public float GroundCheckDistance => groundCheckDistance;
        public LayerMask GroundLayer => groundLayer;
        public Vector2 GroundCheckSize => groundCheckSize;
        public Vector2 GroundCheckOffset => groundCheckOffset;
        public bool LockZAxis => lockZAxis;
        public float SlideSpeed => slideSpeed;
        public float RollDistance => rollDistance;
        public float RollDuration => rollDuration;
        public bool FacingRight => facingRight;
        public bool EnableMovingPlatform => enableMovingPlatform;
        public bool InheritPlatformRotation => inheritPlatformRotation;
        public bool InheritPlatformMomentum => inheritPlatformMomentum;
        public LayerMask PlatformLayer => platformLayer;
        public bool EnableGapBridging => enableGapBridging;
        public float MinSpeedForGapBridge => minSpeedForGapBridge;
        public float MaxGapDistance => maxGapDistance;
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
        public float WallJumpForceX => wallJumpForceX;
        public float WallJumpForceY => wallJumpForceY;
        public float WallJumpCooldown => wallJumpCooldown;
        public float DifferentWallAngle => differentWallAngle;

        private void OnValidate()
        {
            var rules = new MovementConfigRules();
            MovementConfigIssueCode ignored;
            SanitizeBaseFields(rules);
            airControlMultiplier = rules.Sanitize(MovementConfigField.AirControl2D, airControlMultiplier, out ignored);
            coyoteTime = rules.Sanitize(MovementConfigField.CoyoteTime2D, coyoteTime, out ignored);
            jumpBufferTime = rules.Sanitize(MovementConfigField.JumpBufferTime2D, jumpBufferTime, out ignored);
            gravity = rules.Sanitize(MovementConfigField.Gravity2D, gravity, out ignored);
            maxFallSpeed = rules.Sanitize(MovementConfigField.MaxFallSpeed, maxFallSpeed, out ignored);
            groundCheckDistance = rules.Sanitize(MovementConfigField.GroundCheckDistance2D, groundCheckDistance, out ignored);
            slideSpeed = rules.Sanitize(MovementConfigField.SlideSpeed, slideSpeed, out ignored);
            rollDistance = rules.Sanitize(MovementConfigField.RollDistance2D, rollDistance, out ignored);
            rollDuration = rules.Sanitize(MovementConfigField.RollDuration2D, rollDuration, out ignored);
            minSpeedForGapBridge = rules.Sanitize(MovementConfigField.MinSpeedForGapBridge2D, minSpeedForGapBridge, out ignored);
            maxGapDistance = rules.Sanitize(MovementConfigField.MaxGapDistance2D, maxGapDistance, out ignored);
            ladderClimbSpeed = rules.Sanitize(MovementConfigField.LadderClimbSpeed2D, ladderClimbSpeed, out ignored);
            wallClimbSpeed = rules.Sanitize(MovementConfigField.WallClimbSpeed2D, wallClimbSpeed, out ignored);
            wallCheckDistance = rules.Sanitize(MovementConfigField.WallCheckDistance2D, wallCheckDistance, out ignored);
            wallClingDuration = rules.Sanitize(MovementConfigField.WallClingDuration2D, wallClingDuration, out ignored);
            wallSlideSpeed = rules.Sanitize(MovementConfigField.WallSlideSpeed2D, wallSlideSpeed, out ignored);
            wallJumpForceX = rules.Sanitize(MovementConfigField.WallJumpForceX, wallJumpForceX, out ignored);
            wallJumpForceY = rules.Sanitize(MovementConfigField.WallJumpForceY, wallJumpForceY, out ignored);
            wallJumpCooldown = rules.Sanitize(MovementConfigField.WallJumpCooldown2D, wallJumpCooldown, out ignored);
            differentWallAngle = rules.Sanitize(MovementConfigField.DifferentWallAngle2D, differentWallAngle, out ignored);
            groundCheckSize.x = rules.Sanitize(MovementConfigField.GroundCheckSizeX, groundCheckSize.x, out ignored);
        }

        protected override void WriteNumericValues(float[] destination)
        {
            base.WriteNumericValues(destination);
            destination[(int)MovementConfigField.AirControl2D] = airControlMultiplier;
            destination[(int)MovementConfigField.CoyoteTime2D] = coyoteTime;
            destination[(int)MovementConfigField.JumpBufferTime2D] = jumpBufferTime;
            destination[(int)MovementConfigField.Gravity2D] = gravity;
            destination[(int)MovementConfigField.MaxFallSpeed] = maxFallSpeed;
            destination[(int)MovementConfigField.GroundCheckDistance2D] = groundCheckDistance;
            destination[(int)MovementConfigField.SlideSpeed] = slideSpeed;
            destination[(int)MovementConfigField.RollDistance2D] = rollDistance;
            destination[(int)MovementConfigField.RollDuration2D] = rollDuration;
            destination[(int)MovementConfigField.MinSpeedForGapBridge2D] = minSpeedForGapBridge;
            destination[(int)MovementConfigField.MaxGapDistance2D] = maxGapDistance;
            destination[(int)MovementConfigField.LadderClimbSpeed2D] = ladderClimbSpeed;
            destination[(int)MovementConfigField.WallClimbSpeed2D] = wallClimbSpeed;
            destination[(int)MovementConfigField.WallCheckDistance2D] = wallCheckDistance;
            destination[(int)MovementConfigField.WallClingDuration2D] = wallClingDuration;
            destination[(int)MovementConfigField.WallSlideSpeed2D] = wallSlideSpeed;
            destination[(int)MovementConfigField.WallJumpForceX] = wallJumpForceX;
            destination[(int)MovementConfigField.WallJumpForceY] = wallJumpForceY;
            destination[(int)MovementConfigField.WallJumpCooldown2D] = wallJumpCooldown;
            destination[(int)MovementConfigField.DifferentWallAngle2D] = differentWallAngle;
            destination[(int)MovementConfigField.GroundCheckSizeX] = groundCheckSize.x;
            destination[(int)MovementConfigField.GroundCheckSizeY] = groundCheckSize.y;
        }

    }
}
