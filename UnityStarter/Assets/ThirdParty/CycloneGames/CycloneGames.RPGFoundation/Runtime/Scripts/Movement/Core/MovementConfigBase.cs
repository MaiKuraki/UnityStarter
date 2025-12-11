using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Base configuration shared between 2D and 3D movement configs.
    /// Animation parameter names are stored as strings for flexibility with different animation systems.
    /// </summary>
    public abstract class MovementConfigBase : ScriptableObject
    {
        [Header("Ground Movement")]
        public float walkSpeed = 3f;
        public float runSpeed = 5f;
        public float sprintSpeed = 8f;
        public float crouchSpeed = 1.5f;

        [Header("Jump")]
        public float jumpForce = 10f;
        public int maxJumpCount = 1;

        [Header("Animation Parameters")]
        [Tooltip("Parameter name for movement speed (Float)")]
        public string movementSpeedParameter = "MovementSpeed";

        [Tooltip("Parameter name for grounded state (Bool)")]
        public string isGroundedParameter = "IsGrounded";

        [Tooltip("Parameter name for jump trigger (Trigger)")]
        public string jumpTrigger = "Jump";
    }
}