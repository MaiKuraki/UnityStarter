using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "CycloneGames/RPG Foundation/Movement/Movement Config")]
    public class MovementConfig : MovementConfigBase
    {
        [SerializeField] private float rollDistance = 5f;
        [SerializeField] private float rollDuration = 0.5f;
        [SerializeField] private float swimSpeed = 3f;
        [SerializeField] private float flySpeed = 6f;

        [SerializeField] private float gravity = -20f;
        [SerializeField] private float airControlMultiplier = 0.5f;
        [SerializeField] private float groundedCheckDistance = 0.025f;
        [SerializeField] private LayerMask groundLayer = 1;
        [Tooltip("Layer mask for collision detection during movement. If empty, uses groundLayer.")]
        [SerializeField] private LayerMask collisionLayer = 0;
        [SerializeField] private float slopeLimit = 45f;
        [SerializeField] private float stepHeight = 0.3f;
        [Tooltip("Minimum time (seconds) character must be airborne before triggering fall animation. Prevents false triggers on stairs.")]
        [Range(0.05f, 0.3f)]
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
        [SerializeField] private float ceilingCheckDistance = 0.1f;

        [Tooltip("Enable gap bridging to auto-jump across small gaps while running.")]
        [SerializeField] private bool enableGapBridging = true;
        [Tooltip("Minimum speed required to trigger gap bridging (m/s).")]
        [SerializeField] private float minSpeedForGapBridge = 3f;
        [Tooltip("Maximum gap distance that can be bridged (m).")]
        [SerializeField] private float maxGapDistance = 0.5f;
        [Tooltip("Maximum height difference allowed for gap bridging (m).")]
        [SerializeField] private float maxGapHeightDiff = 0.3f;

        [SerializeField] private bool enableLadderClimbing = true;
        [SerializeField] private float ladderClimbSpeed = 3f;
        [SerializeField] private LayerMask ladderLayer = 0;

        [SerializeField] private bool enableWallClimbing = false;
        [SerializeField] private float wallClimbSpeed = 2f;
        [SerializeField] private LayerMask wallLayer = 0;
        [SerializeField] private float wallCheckDistance = 0.5f;
        [SerializeField] private float wallClingDuration = 0.5f;
        [SerializeField] private float wallSlideSpeed = 2f;

        [SerializeField] private bool enableWallJump = true;
        [SerializeField] private float wallJumpForceHorizontal = 8f;
        [SerializeField] private float wallJumpForceVertical = 10f;
        [SerializeField] private float wallJumpCooldown = 0.1f;
        [Tooltip("Minimum angle difference (degrees) to consider as different wall for continuous wall jump.")]
        [Range(30f, 120f)]
        [SerializeField] private float differentWallAngle = 60f;

        [Tooltip("Pathfinding system to use for AI navigation. Requires corresponding package installed.")]
        [SerializeField] private PathfindingSystem pathfindingSystem = PathfindingSystem.None;

        [SerializeField] private float rotationSpeed = 10f;

        // Feel - displayed in Custom Editor
        [Tooltip("Time after leaving ground that jump input is still accepted.")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("Time before landing that jump input is buffered.")]
        [SerializeField] private float jumpBufferTime = 0.1f;

        [SerializeField] private string rollTrigger = "Roll";
        [SerializeField] private string climbingParameter = "IsClimbing";
        [SerializeField] private string wallSlidingParameter = "IsWallSliding";

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
        public PathfindingSystem PathfindingSystem => pathfindingSystem;
        public float RotationSpeed => rotationSpeed;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferTime => jumpBufferTime;
        public string RollTrigger => rollTrigger;
        public string ClimbingParameter => climbingParameter;
        public string WallSlidingParameter => wallSlidingParameter;
    }
}
