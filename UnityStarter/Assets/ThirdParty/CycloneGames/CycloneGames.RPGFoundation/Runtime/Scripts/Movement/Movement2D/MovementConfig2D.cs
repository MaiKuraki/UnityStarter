using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    public enum MovementType2D
    {
        Platformer,
        BeltScroll,
        TopDown
    }

    [CreateAssetMenu(fileName = "MovementConfig2D", menuName = "CycloneGames/RPG Foundation/Movement/Movement Config 2D")]
    public class MovementConfig2D : Movement.MovementConfigBase
    {
        [SerializeField] private MovementType2D movementType = MovementType2D.Platformer;

        [Tooltip("Enable jump mechanic. Disable for TopDown games that don't use jumping.")]
        [SerializeField] private bool enableJump = true;

        [SerializeField] private float airControlMultiplier = 0.5f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.1f;

        [SerializeField] private float gravity = 25f;
        [SerializeField] private float maxFallSpeed = 20f;
        [SerializeField] private float groundCheckDistance = 0.1f;
        [SerializeField] private LayerMask groundLayer = 1;

        [SerializeField] private Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);
        [SerializeField] private Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

        [SerializeField] private bool lockZAxis = true;
        [SerializeField] private float slideSpeed = 7f;
        [SerializeField] private float rollDistance = 5f;
        [SerializeField] private float rollDuration = 0.5f;
        [SerializeField] private bool facingRight = true;

        [SerializeField] private bool enableMovingPlatform = true;
        [SerializeField] private bool inheritPlatformRotation = false;
        [SerializeField] private bool inheritPlatformMomentum = true;
        [SerializeField] private LayerMask platformLayer = 0;

        [SerializeField] private bool enableGapBridging = true;
        [SerializeField] private float minSpeedForGapBridge = 4f;
        [SerializeField] private float maxGapDistance = 1.0f;

        [SerializeField] private bool enableLadderClimbing = true;
        [SerializeField] private float ladderClimbSpeed = 3f;
        [SerializeField] private LayerMask ladderLayer = 0;

        [SerializeField] private bool enableWallClimbing = false;
        [SerializeField] private float wallClimbSpeed = 2f;
        [SerializeField] private LayerMask wallLayer = 0;
        [SerializeField] private float wallCheckDistance = 0.3f;
        [SerializeField] private float wallClingDuration = 0.5f;
        [SerializeField] private float wallSlideSpeed = 2f;

        [SerializeField] private bool enableWallJump = true;
        [SerializeField] private float wallJumpForceX = 8f;
        [SerializeField] private float wallJumpForceY = 10f;
        [SerializeField] private float wallJumpCooldown = 0.1f;
        [Range(30f, 120f)]
        [SerializeField] private float differentWallAngle = 60f;

        [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
        [SerializeField] private string rollTrigger = "Roll";
        [SerializeField] private string inputXParameter = "InputX";
        [SerializeField] private string inputYParameter = "InputY";
        [SerializeField] private string climbingParameter = "IsClimbing";
        [SerializeField] private string wallSlidingParameter = "IsWallSliding";

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
        public string VerticalSpeedParameter => verticalSpeedParameter;
        public string RollTrigger => rollTrigger;
        public string InputXParameter => inputXParameter;
        public string InputYParameter => inputYParameter;
        public string ClimbingParameter => climbingParameter;
        public string WallSlidingParameter => wallSlidingParameter;
    }
}
