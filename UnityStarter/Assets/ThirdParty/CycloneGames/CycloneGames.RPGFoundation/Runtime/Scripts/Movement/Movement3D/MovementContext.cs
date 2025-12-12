using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Context struct passed between movement states. 
    /// </summary>
    public struct MovementContext
    {
        public CharacterController CharacterController;
        public IAnimationController AnimationController;
        public Transform Transform;
        public MovementConfig Config;

        public float3 InputDirection;
        public float3 WorldUp;
        public float DeltaTime;

        public bool IsGrounded;
        public float SlopeAngle;
        public float3 GroundNormal;
        public float VerticalVelocity;

        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool RollPressed;
        
        public int JumpCount;

        public float3 CurrentVelocity;
        public float CurrentSpeed;

        /// <summary>
        /// Whether to use root motion for the current state.
        /// Can be set by states to enable/disable root motion dynamically.
        /// </summary>
        public bool UseRootMotion;
    }
}