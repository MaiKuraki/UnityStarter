using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class WallClimbState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Climb;

        public void SetWallSide(int side) { }

        public override void OnEnter(ref MovementContext2D context)
        {
            context.WallClingTimer = 0f;
            context.IsWallSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, true);
                }
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            velocity = float2.zero;

            context.WallClingTimer += context.DeltaTime;

            if (context.WallClingTimer >= context.Config.WallClingDuration)
            {
                context.IsWallSliding = true;
            }

            if (context.IsWallSliding)
            {
                float slideSpeed = context.Config.WallSlideSpeed;
                velocity = new float2(0, -slideSpeed) * context.DeltaTime;
                context.CurrentSpeed = slideSpeed;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int slideHash = Movement.AnimationParameterCache.GetHash(context.Config.WallClimbSpeed.ToString());
                    context.AnimationController.SetBool(slideHash, true);
                }
            }
            else
            {
                float climbSpeed = context.GetAttributeValue(Movement.MovementAttribute.ClimbSpeed, context.Config.WallClimbSpeed);
                float verticalInput = context.InputDirection.y;
                float horizontalInput = context.InputDirection.x;

                float2 moveDir = new float2(horizontalInput, verticalInput);
                if (math.lengthsq(moveDir) > 0.001f)
                {
                    moveDir = math.normalize(moveDir);
                }

                velocity = moveDir * climbSpeed * context.DeltaTime;
                context.CurrentSpeed = math.length(moveDir) * climbSpeed;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int climbHash = Movement.AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                    context.AnimationController.SetBool(climbHash, true);
                }
            }

            context.CurrentVelocity = velocity / math.max(context.DeltaTime, 0.0001f);
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
            context.WallClimbSide = 0;
            context.WallClingTimer = 0f;
            context.IsWallSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, false);
                }
            }
        }
    }
}
