using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class WallClimbState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
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

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            displacement = float3.zero;

            context.WallClingTimer += context.DeltaTime;

            if (context.WallClingTimer >= context.Config.WallClingDuration)
            {
                context.IsWallSliding = true;
            }

            if (context.IsWallSliding)
            {
                float slideSpeed = context.GetAttributeValue(MovementAttribute.ClimbSpeed, context.Config.WallSlideSpeed);
                displacement = -context.WorldUp * slideSpeed * context.DeltaTime;
                context.CurrentSpeed = slideSpeed;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int slideHash = AnimationParameterCache.GetHash(context.Config.WallSlidingParameter);
                    if (slideHash != 0) context.AnimationController.SetBool(slideHash, true);
                }
            }
            else
            {
                float climbSpeed = context.GetAttributeValue(MovementAttribute.ClimbSpeed, context.Config.WallClimbSpeed);
                float verticalInput = context.InputDirection.z;
                float horizontalInput = context.InputDirection.x;

                float3 rayOrigin = (float3)context.Transform.position + context.WorldUp * (context.Config.StepHeight + 0.1f);
                float3 rayDir = -context.WallClimbNormal;

                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit,
                    context.Config.WallCheckDistance + 0.5f, context.Config.WallLayer))
                {
                    context.WallClimbNormal = hit.normal;
                }

                float3 wallRight = math.cross(context.WorldUp, context.WallClimbNormal);

                if (math.lengthsq(wallRight) < 0.001f)
                {
                    wallRight = math.cross(context.Transform.forward, context.WallClimbNormal);
                }

                wallRight = math.normalize(wallRight);
                float3 wallUp = math.cross(wallRight, context.WallClimbNormal);

                float3 moveDir = wallUp * verticalInput + wallRight * horizontalInput;
                if (math.lengthsq(moveDir) > 0.001f)
                {
                    moveDir = math.normalize(moveDir);
                }

                float3 adhesion = -context.WallClimbNormal * 0.5f;

                displacement = (moveDir * climbSpeed + adhesion) * context.DeltaTime;
                context.CurrentSpeed = math.length(new float2(horizontalInput, verticalInput)) * climbSpeed;
            }

            context.CurrentVelocity = displacement / math.max(context.DeltaTime, 0.0001f);
        }

        public override void OnExit(ref MovementContext context)
        {
            context.WallClimbNormal = Vector3.zero;
            context.WallClingTimer = 0f;
            context.IsWallSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, false);
                }

                int slideHash = AnimationParameterCache.GetHash(context.Config.WallSlidingParameter);
                if (slideHash != 0)
                {
                    context.AnimationController.SetBool(slideHash, false);
                }
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.JumpPressed && context.Config.EnableWallJump)
            {
                float3 jumpDir = math.normalize((float3)context.WallClimbNormal + context.WorldUp);

                context.WallJumpDirection = new Vector3(
                    context.WallClimbNormal.x * context.Config.WallJumpForceHorizontal,
                    context.Config.WallJumpForceVertical,
                    context.WallClimbNormal.z * context.Config.WallJumpForceHorizontal
                );
                context.IsWallJumping = true;
                context.LastWallNormal = context.WallClimbNormal;
                context.LastWallJumpTime = Time.time;

                return StatePool<MovementStateBase>.GetState<JumpState>();
            }

            if (context.IsGrounded)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }
    }
}
