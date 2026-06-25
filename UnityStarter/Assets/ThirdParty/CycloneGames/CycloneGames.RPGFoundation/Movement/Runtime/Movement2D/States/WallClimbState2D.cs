using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class WallClimbState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        public void SetWallSide(int side) { }

        public override void OnEnter(ref MovementContext2D context)
        {
            context.WallClingTimer = 0f;
            context.IsWallSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, true);
                }
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float2 currentVelocity = float2.zero;

            context.WallClingTimer += context.DeltaTime;

            if (context.WallClingTimer >= context.Config.WallClingDuration)
            {
                context.IsWallSliding = true;
            }

            if (context.IsWallSliding)
            {
                float slideSpeed = context.Config.WallSlideSpeed;
                currentVelocity = new float2(0, -slideSpeed);
                context.CurrentSpeed = slideSpeed;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int slideHash = AnimationParameterCache.GetHash(context.Config.WallSlidingParameter);
                    context.AnimationController.SetBool(slideHash, true);
                }
            }
            else
            {
                float climbSpeed = context.GetAttributeValue(MovementAttribute.ClimbSpeed, context.Config.WallClimbSpeed);
                float verticalInput = context.InputDirection.y;
                float horizontalInput = context.InputDirection.x;

                float2 moveDir = new float2(horizontalInput, verticalInput);
                if (math.lengthsq(moveDir) > 0.001f)
                {
                    moveDir = math.normalize(moveDir);
                }

                currentVelocity = moveDir * climbSpeed;
                context.CurrentSpeed = math.length(moveDir) * climbSpeed;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int climbHash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                    context.AnimationController.SetBool(climbHash, true);
                }
            }

            displacement = currentVelocity * context.DeltaTime;
            context.CurrentVelocity = currentVelocity;
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.JumpPressed && context.Config.EnableWallJump)
            {
                context.IsWallJumping = true;
                context.WallJumpDirection = new Vector2(
                    -context.WallClimbSide * context.Config.WallJumpForceX,
                    context.Config.WallJumpForceY);
                context.LastWallSide = context.WallClimbSide;
                context.LastWallJumpTime = Time.time;

                return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
            }

            if (context.IsGrounded)
            {
                return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            return null;
        }

        public override void OnExit(ref MovementContext2D context)
        {
            if (context.ClimbingMode == ClimbingMode.Wall)
            {
                context.ClimbingMode = ClimbingMode.None;
            }

            context.WallClimbSide = 0;
            context.WallClingTimer = 0f;
            context.IsWallSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, false);
                }
            }
        }
    }
}
