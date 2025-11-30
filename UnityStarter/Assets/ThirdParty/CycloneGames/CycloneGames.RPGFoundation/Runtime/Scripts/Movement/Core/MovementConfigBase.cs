using System;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Base configuration shared between 2D and 3D movement configs.
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
        public string movementSpeedParameter = "MovementSpeed";
        public string isGroundedParameter = "IsGrounded";
        public string jumpTrigger = "Jump";

        [NonSerialized] private int _animIDMovementSpeed = -1;
        [NonSerialized] private int _animIDIsGrounded = -1;
        [NonSerialized] private int _animIDJump = -1;

        public int AnimIDMovementSpeed => _animIDMovementSpeed != -1 ? _animIDMovementSpeed : (_animIDMovementSpeed = Animator.StringToHash(movementSpeedParameter));
        public int AnimIDIsGrounded => _animIDIsGrounded != -1 ? _animIDIsGrounded : (_animIDIsGrounded = Animator.StringToHash(isGroundedParameter));
        public int AnimIDJump => _animIDJump != -1 ? _animIDJump : (_animIDJump = Animator.StringToHash(jumpTrigger));
    }
}