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
        // Movement Type - displayed in Custom Editor
        public MovementType2D movementType = MovementType2D.Platformer;

        // Air Movement - displayed in Custom Editor
        public float airControlMultiplier = 0.5f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.1f;

        // Physics - displayed in Custom Editor
        public float gravity = 25f;
        public float maxFallSpeed = 20f;
        public float groundCheckDistance = 0.1f;
        public LayerMask groundLayer = 1;

        // Ground Detection (Platformer/BeltScroll) - displayed in Custom Editor
        [Tooltip("Size of the ground detection box (width, height).\n" +
                 "Larger size = more forgiving ground detection, but may detect walls as ground.\n" +
                 "Smaller size = more precise, but may miss ground when moving fast.\n" +
                 "Recommended: Width = 0.8-1.0 (character width), Height = 0.1-0.2 (detection depth)\n" +
                 "Note: Only used in Platformer and BeltScroll modes. TopDown mode doesn't need ground detection.")]
        public Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);

        [Tooltip("Offset from character position for ground check point.\n" +
                 "Use (0, -0.5) to check at character's feet.\n" +
                 "If character pivot is at center, use negative Y offset.\n" +
                 "Note: Only used in Platformer and BeltScroll modes.")]
        public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

        // Other Settings - displayed in Custom Editor
        [Tooltip("Lock Z axis position. Prevents character from moving on Z axis.\n" +
                 "Useful for 2D games to ensure character stays on the correct layer.")]
        public bool lockZAxis = true;

        [Tooltip("Speed when sliding down slopes or walls.")]
        public float slideSpeed = 7f;

        [Tooltip("Horizontal force applied when performing a wall jump.")]
        public float wallJumpForceX = 8f;

        [Tooltip("Vertical force applied when performing a wall jump.")]
        public float wallJumpForceY = 10f;

        // Facing Direction (Platformer/BeltScroll) - displayed in Custom Editor
        [Tooltip("Initial facing direction for Platformer and BeltScroll modes.\n" +
                 "Platformer: Automatically flips sprite (Transform.scale.x) based on movement direction.\n" +
                 "BeltScroll: Automatically flips sprite (Transform.scale.x) based on movement direction (left/right).\n" +
                 "TopDown: Not used (uses Animator BlendTree for 4-direction sprites).\n" +
                 "Set initial facing: true = right, false = left")]
        public bool facingRight = true;

        // Animation Parameters (Additional) - displayed in Custom Editor
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