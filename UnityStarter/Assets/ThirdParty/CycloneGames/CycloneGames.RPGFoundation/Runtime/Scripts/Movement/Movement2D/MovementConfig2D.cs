using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    public enum MovementType2D
    {
        Platformer, // Standard 2D Side-scroller (X/Y movement, Gravity on Y)
        BeltScroll, // DNF Style (X/Z movement on ground, Y is Jump/Height, Gravity on Y)
        TopDown     // Classic RPG Style (X/Y movement, No Gravity, No Jump)
    }

    [CreateAssetMenu(fileName = "MovementConfig2D", menuName = "CycloneGames/RPG Foundation/Movement Config 2D")]
    public class MovementConfig2D : Movement.MovementConfigBase
    {
        [Header("Movement Type")]
        public MovementType2D movementType = MovementType2D.Platformer;

        [Header("2D Specific - Air Movement")]
        public float airControlMultiplier = 0.5f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.1f;

        [Header("2D Specific - Physics")]
        public float gravity = 25f;
        public float maxFallSpeed = 20f;
        public float groundCheckDistance = 0.1f;
        public LayerMask groundLayer = 1;

        [Header("2D Specific - Other")]
        public bool lockZAxis = true;
        public float slideSpeed = 7f;
        public float wallJumpForceX = 8f;
        public float wallJumpForceY = 10f;

        [Header("2D Animation (Additional)")]
        [Tooltip("Parameter name for vertical speed (Float)")]
        public string verticalSpeedParameter = "VerticalSpeed";

        [Tooltip("Parameter name for roll trigger (Trigger)")]
        public string rollTrigger = "Roll";

        [Tooltip("Parameter name for input X axis (Float)")]
        public string inputXParameter = "InputX";

        [Tooltip("Parameter name for input Y axis (Float)")]
        public string inputYParameter = "InputY";
    }
}