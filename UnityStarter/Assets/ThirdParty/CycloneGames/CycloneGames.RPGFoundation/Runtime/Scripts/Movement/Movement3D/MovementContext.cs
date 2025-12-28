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

        public bool UseRootMotion;

        // Wall Jump state
        public bool IsWallJumping;
        public Vector3 WallJumpDirection;
        public Vector3 LastWallNormal;
        public float LastWallJumpTime;

        public IMovementAuthority MovementAuthority;

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
        public float GetFinalSpeed(float baseSpeed, MovementStateType stateType)
        {
            MovementAttribute attr = stateType switch
            {
                MovementStateType.Walk => MovementAttribute.WalkSpeed,
                MovementStateType.Run => MovementAttribute.RunSpeed,
                MovementStateType.Sprint => MovementAttribute.SprintSpeed,
                MovementStateType.Crouch => MovementAttribute.CrouchSpeed,
                _ => MovementAttribute.RunSpeed
            };
            return GetAttributeValue(attr, baseSpeed);
        }
    }
}