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
        // Ground Movement - displayed in Custom Editor
        public float walkSpeed = 3f;
        public float runSpeed = 5f;
        public float sprintSpeed = 8f;
        public float crouchSpeed = 1.5f;

        // Jump - displayed in Custom Editor
        public float jumpForce = 8f;
        public int maxJumpCount = 1;

        // Animation System Configuration - displayed in Custom Editor
        [Tooltip("Animation system to use for character animations.\n" +
                 "• Unity Animator: Standard Unity Animator Controller\n" +
                 "• Animancer: Animancer animation system")]
        public AnimationSystemType animationSystem = AnimationSystemType.UnityAnimator;

        [Tooltip("Parameter mode for Animancer (only used when Animation System is Animancer).\n" +
                 "• Animator Hash: Use Animator hash values (for HybridAnimancerComponent with Animator Controller)\n" +
                 "• String Parameter: Use direct string parameters (for AnimancerComponent Parameters mode)")]
        public AnimancerParameterMode animancerParameterMode = AnimancerParameterMode.StringParameter;

        // Animation Parameters - displayed in Custom Editor
        [Tooltip("Parameter name for movement speed (Float)")]
        public string movementSpeedParameter = "MovementSpeed";

        [Tooltip("Parameter name for grounded state (Bool)")]
        public string isGroundedParameter = "IsGrounded";

        [Tooltip("Parameter name for jump trigger (Trigger)")]
        public string jumpTrigger = "Jump";
    }
}