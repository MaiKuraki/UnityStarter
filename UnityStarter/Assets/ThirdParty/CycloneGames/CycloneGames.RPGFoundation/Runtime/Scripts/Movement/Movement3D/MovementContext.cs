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

        public float3 InputDirection; // Local space direction (relative to character's forward/right)
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
        /// </summary>
        public bool UseRootMotion;

        /// <summary>
        /// Converts InputDirection from local space to world space, projecting onto plane perpendicular to WorldUp.
        /// Supports wall/ceiling walking by using dynamic WorldUp.
        /// </summary>
        public float3 GetWorldInputDirection()
        {
            if (Transform == null || math.lengthsq(InputDirection) < 0.0001f)
                return float3.zero;

            Vector3 localInput = new Vector3(InputDirection.x, 0, InputDirection.z);
            Vector3 worldDirection = Transform.TransformDirection(localInput);
            
            // Project onto plane perpendicular to WorldUp: projected = direction - dot(direction, normal) * normal
            float3 projected = (float3)worldDirection - math.dot((float3)worldDirection, WorldUp) * WorldUp;
            float originalMagnitude = math.length(InputDirection);
            float projectedSqrLen = math.lengthsq(projected);
            
            if (projectedSqrLen > 0.0001f)
            {
                return math.normalize(projected) * originalMagnitude;
            }
            else
            {
                return float3.zero;
            }
        }
    }
}