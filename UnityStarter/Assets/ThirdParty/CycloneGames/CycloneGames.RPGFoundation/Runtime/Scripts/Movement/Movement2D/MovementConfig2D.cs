using System;
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
        public string verticalSpeedParameter = "VerticalSpeed";
        public string rollTrigger = "Roll";
        public string inputXParameter = "InputX";
        public string inputYParameter = "InputY";

        [NonSerialized] private int _animIDVerticalSpeed = -1;
        [NonSerialized] private int _animIDRoll = -1;
        [NonSerialized] private int _animIDInputX = -1;
        [NonSerialized] private int _animIDInputY = -1;

        public int AnimIDVerticalSpeed => _animIDVerticalSpeed != -1 ? _animIDVerticalSpeed : (_animIDVerticalSpeed = Animator.StringToHash(verticalSpeedParameter));
        public int AnimIDRoll => _animIDRoll != -1 ? _animIDRoll : (_animIDRoll = Animator.StringToHash(rollTrigger));
        public int AnimIDInputX => _animIDInputX != -1 ? _animIDInputX : (_animIDInputX = Animator.StringToHash(inputXParameter));
        public int AnimIDInputY => _animIDInputY != -1 ? _animIDInputY : (_animIDInputY = Animator.StringToHash(inputYParameter));
    }
}