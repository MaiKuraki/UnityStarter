using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public struct MovementContext
    {
        public CharacterController CharacterController;
        public Animator Animator;
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

        public float3 CurrentVelocity;
        public float CurrentSpeed;
    }
}