using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime.States;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public struct MovementContext
    {
        public Rigidbody Rigidbody;
        public CapsuleCollider CapsuleCollider;
        public Transform Transform;
        public MovementConfig Config;

        public float3 InputDirection;
        public float3 WorldUp;
        public float DeltaTime;

        public bool IsGrounded;
        public bool WasGrounded;
        public bool IsOnNonWalkableSlope;
        public float SlopeAngle;
        public float3 GroundNormal;
        public float VerticalVelocity;

        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool RollPressed;
        public float RollTimer;
        public float3 RollDirection;

        public int JumpCount;

        public float3 CurrentVelocity;
        public float CurrentSpeed;

        public bool UseRootMotion;

        public bool IsWallJumping;
        public Vector3 WallJumpDirection;
        public Vector3 LastWallNormal;
        public float LastWallJumpTime;

        public IMovementAuthority MovementAuthority;

        public MovementStatePool<MovementStateBase> StatePool;

        public MovementStepEvent StepEvents;

        public ClimbingMode ClimbingMode;

        public Vector3 WallClimbNormal;
        public float WallClingTimer;
        public bool IsWallSliding;

        public float3 GetWorldInputDirection()
        {
            if (Transform == null || math.lengthsq(InputDirection) < 0.0001f)
                return float3.zero;

            Vector3 localInput = new Vector3(InputDirection.x, 0, InputDirection.z);
            Vector3 worldDirection = Transform.TransformDirection(localInput);

            float3 projected = (float3)worldDirection - math.dot((float3)worldDirection, WorldUp) * WorldUp;
            float projectedSqrLen = math.lengthsq(projected);

            if (projectedSqrLen > 0.0001f)
            {
                return math.normalize(projected);
            }
            else
            {
                return float3.zero;
            }
        }

        public float InputMagnitude => math.clamp(math.length(InputDirection), 0f, 1f);

        public float GetAttributeValue(MovementAttribute attribute, float configValue)
        {
            return MovementAttributeHelper.GetFinalValue(attribute, configValue, MovementAuthority);
        }

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
