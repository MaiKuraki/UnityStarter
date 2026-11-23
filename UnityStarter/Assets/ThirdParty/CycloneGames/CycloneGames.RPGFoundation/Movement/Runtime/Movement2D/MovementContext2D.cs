using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D
{
    public struct MovementContext2D
    {
        public Rigidbody2D Rigidbody;
        public Transform Transform;
        public MovementConfig2D Config;

        public float2 InputDirection;
        public float2 WorldUp;
        public float DeltaTime;
        public float FixedDeltaTime;

        public bool IsGrounded;
        public float GroundAngle;
        public float2 GroundNormal;

        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool RollPressed;
        public float RollTimer;
        public float2 RollDirection;

        public int JumpCount;

        public float2 CurrentVelocity;
        public float CurrentSpeed;
        public float VerticalVelocity;

        public bool IsWallJumping;
        public Vector2 WallJumpDirection;
        public int LastWallSide;
        public float LastWallJumpTime;

        public IMovementAuthority MovementAuthority;

        public MovementStatePool<MovementStateBase2D> StatePool;

        public MovementStepEvent StepEvents;

        public ClimbingMode ClimbingMode;

        public int WallClimbSide;
        public float WallClingTimer;
        public bool IsWallSliding;

        public float PendingDepth;

        public float2 LookDirection;

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
