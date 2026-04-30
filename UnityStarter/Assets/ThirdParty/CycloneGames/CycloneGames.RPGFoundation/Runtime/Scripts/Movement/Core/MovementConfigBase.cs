using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Animation system type for movement configuration.
    /// </summary>
    public enum AnimationSystemType
    {
        UnityAnimator,  // Use Unity's Animator Controller
        Animancer       // Use Animancer animation system
    }

    /// <summary>
    /// Animation parameter mode for Animancer.
    /// </summary>
    public enum AnimancerParameterMode
    {
        AnimatorHash,      // Use Animator hash (for HybridAnimancerComponent with Animator Controller)
        StringParameter    // Use direct string parameter (for AnimancerComponent Parameters mode)
    }

    /// <summary>
    /// Base configuration shared between 2D and 3D movement configs.
    /// Animation parameter names are stored as strings for flexibility with different animation systems.
    /// </summary>
    public abstract class MovementConfigBase : ScriptableObject
    {
        [SerializeField] private float walkSpeed = 1.5f;
        [SerializeField] private float runSpeed = 3f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float crouchSpeed = 1f;

        [SerializeField] private float jumpForce = 6f;
        [SerializeField] private int maxJumpCount = 1;

        [Tooltip("Animation system to use for character animations.\n" +
                 "• Unity Animator: Standard Unity Animator Controller\n" +
                 "• Animancer: Animancer animation system")]
        [SerializeField] private AnimationSystemType animationSystem = AnimationSystemType.UnityAnimator;

        [Tooltip("Parameter mode for Animancer (only used when Animation System is Animancer).\n" +
                 "• Animator Hash: Use Animator hash values (for HybridAnimancerComponent with Animator Controller)\n" +
                 "• String Parameter: Use direct string parameters (for AnimancerComponent Parameters mode)")]
        [SerializeField] private AnimancerParameterMode animancerParameterMode = AnimancerParameterMode.StringParameter;

        [Tooltip("Parameter name for movement speed (Float)")]
        [SerializeField] private string movementSpeedParameter = "MovementSpeed";

        [Tooltip("Parameter name for grounded state (Bool)")]
        [SerializeField] private string isGroundedParameter = "IsGrounded";

        [Tooltip("Parameter name for jump trigger (Trigger)")]
        [SerializeField] private string jumpTrigger = "Jump";

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float SprintSpeed => sprintSpeed;
        public float CrouchSpeed => crouchSpeed;
        public float JumpForce => jumpForce;
        public int MaxJumpCount => maxJumpCount;
        public AnimationSystemType AnimationSystem => animationSystem;
        public AnimancerParameterMode AnimancerParameterMode => animancerParameterMode;
        public string MovementSpeedParameter => movementSpeedParameter;
        public string IsGroundedParameter => isGroundedParameter;
        public string JumpTrigger => jumpTrigger;
    }
}