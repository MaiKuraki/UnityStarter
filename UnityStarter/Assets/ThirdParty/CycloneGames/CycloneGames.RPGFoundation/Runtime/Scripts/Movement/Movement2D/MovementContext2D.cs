using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    public struct MovementContext2D
    {
        public Rigidbody2D Rigidbody;
        public Animator Animator;
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

        public float2 CurrentVelocity;
        public float CurrentSpeed;
    }
}