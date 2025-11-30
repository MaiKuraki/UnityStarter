using System;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "CycloneGames/RPG Foundation/Movement Config")]
    public class MovementConfig : ScriptableObject
    {
        [Header("Ground Movement")]
        public float walkSpeed = 3f;
        public float runSpeed = 5f;
        public float sprintSpeed = 8f;
        public float crouchSpeed = 1.5f;

        [Header("Air Movement")]
        public float jumpForce = 10f;
        public float airControlMultiplier = 0.5f;
        public int maxJumpCount = 1;

        [Header("Special Movement")]
        public float rollDistance = 5f;
        public float rollDuration = 0.5f;
        public float climbSpeed = 2f;
        public float swimSpeed = 3f;
        public float flySpeed = 6f;

        [Header("Physics")]
        public float gravity = -25f;
        public float groundedCheckDistance = 0.2f;
        public float slopeLimit = 45f;
        public float stepHeight = 0.3f;

        [Header("Rotation")]
        public float rotationSpeed = 20f;

        [Header("Animation")]
        public string movementSpeedParameter = "MovementSpeed";
        public string isGroundedParameter = "IsGrounded";
        public string jumpTrigger = "Jump";
        public string rollTrigger = "Roll";

        [NonSerialized] private int _animIDMovementSpeed = -1;
        [NonSerialized] private int _animIDIsGrounded = -1;
        [NonSerialized] private int _animIDJump = -1;
        [NonSerialized] private int _animIDRoll = -1;

        public int AnimIDMovementSpeed => _animIDMovementSpeed != -1 ? _animIDMovementSpeed : (_animIDMovementSpeed = Animator.StringToHash(movementSpeedParameter));
        public int AnimIDIsGrounded => _animIDIsGrounded != -1 ? _animIDIsGrounded : (_animIDIsGrounded = Animator.StringToHash(isGroundedParameter));
        public int AnimIDJump => _animIDJump != -1 ? _animIDJump : (_animIDJump = Animator.StringToHash(jumpTrigger));
        public int AnimIDRoll => _animIDRoll != -1 ? _animIDRoll : (_animIDRoll = Animator.StringToHash(rollTrigger));
    }
}
