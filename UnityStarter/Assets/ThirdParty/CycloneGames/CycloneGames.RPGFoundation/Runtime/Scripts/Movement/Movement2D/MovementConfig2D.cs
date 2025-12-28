using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    public enum MovementType2D
    {
        Platformer,
        BeltScroll,
        TopDown
    }

    [CreateAssetMenu(fileName = "MovementConfig2D", menuName = "CycloneGames/RPG Foundation/Movement Config 2D")]
    public class MovementConfig2D : Movement.MovementConfigBase
    {
        // Movement Type
        public MovementType2D movementType = MovementType2D.Platformer;

        // Air Movement
        public float airControlMultiplier = 0.5f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.1f;

        // Physics
        public float gravity = 25f;
        public float maxFallSpeed = 20f;
        public float groundCheckDistance = 0.1f;
        public LayerMask groundLayer = 1;

        // Ground Detection
        public Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);
        public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

        // Other Settings
        public bool lockZAxis = true;
        public float slideSpeed = 7f;
        public bool facingRight = true;

        // Moving Platform
        public bool enableMovingPlatform = true;
        public bool inheritPlatformRotation = false;
        public bool inheritPlatformMomentum = true;
        public LayerMask platformLayer = 0;

        // Gap Bridging
        public bool enableGapBridging = true;
        public float minSpeedForGapBridge = 4f;
        public float maxGapDistance = 1.0f;

        // Ladder Climbing
        public bool enableLadderClimbing = true;
        public float ladderClimbSpeed = 3f;
        public LayerMask ladderLayer = 0;

        // Wall Climbing
        public bool enableWallClimbing = false;
        public float wallClimbSpeed = 2f;
        public LayerMask wallLayer = 0;
        public float wallCheckDistance = 0.3f;
        public float wallClingDuration = 0.5f;
        public float wallSlideSpeed = 2f;

        // Wall Jump
        public bool enableWallJump = true;
        public float wallJumpForceX = 8f;
        public float wallJumpForceY = 10f;
        public float wallJumpCooldown = 0.1f;
        [Range(30f, 120f)]
        public float differentWallAngle = 60f;

        // Animation Parameters
        public string verticalSpeedParameter = "VerticalSpeed";
        public string rollTrigger = "Roll";
        public string inputXParameter = "InputX";
        public string inputYParameter = "InputY";
        public string climbingParameter = "IsClimbing";
        public string wallSlidingParameter = "IsWallSliding";
    }
}
