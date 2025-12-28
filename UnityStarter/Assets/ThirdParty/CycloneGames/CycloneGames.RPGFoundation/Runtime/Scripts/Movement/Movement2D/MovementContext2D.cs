using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    /// <summary>
    /// Context struct passed between 2D movement states. 
    /// </summary>
    public struct MovementContext2D
    {
        public Rigidbody2D Rigidbody;
        public IAnimationController AnimationController;
        public Transform Transform;
        public MovementConfig2D Config;

        public float2 InputDirection;
        public float DeltaTime;
        public float FixedDeltaTime;

        public bool IsGrounded;
        public float GroundAngle;
        public float2 GroundNormal;

        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool RollPressed;

        public int JumpCount;

        public float2 CurrentVelocity;
        public float CurrentSpeed;
        public float VerticalVelocity;

        // Wall Jump state
        public bool IsWallJumping;
        public Vector2 WallJumpDirection;
        public int LastWallSide;
        public float LastWallJumpTime;

        public IMovementAuthority MovementAuthority;

        /// <summary>
        /// Gets final value for an attribute after applying modifiers.
        /// </summary>
        public float GetAttributeValue(MovementAttribute attribute, float configValue)
        {
            return MovementAttributeHelper.GetFinalValue(attribute, configValue, MovementAuthority);
        }

        /// <summary>
        /// Gets final speed for a movement state. Kept for backward compatibility.
        /// </summary>
        public float GetFinalSpeed(float baseSpeed, Movement.MovementStateType stateType)
        {
            MovementAttribute attr = stateType switch
            {
                Movement.MovementStateType.Walk => MovementAttribute.WalkSpeed,
                Movement.MovementStateType.Run => MovementAttribute.RunSpeed,
                Movement.MovementStateType.Sprint => MovementAttribute.SprintSpeed,
                Movement.MovementStateType.Crouch => MovementAttribute.CrouchSpeed,
                _ => MovementAttribute.RunSpeed
            };
            return GetAttributeValue(attr, baseSpeed);
        }
    }
}