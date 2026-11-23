using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class FallState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.AirControlMultiplier);
            float maxSpeed = runSpeed * airControl;

            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            float actualSpeed = maxSpeed * inputMagnitude;
            float3 desiredVelocity = worldInputDirection * actualSpeed;

            float gravity = context.GetAttributeValue(MovementAttribute.Gravity, context.Config.Gravity);
            float3 worldUp = context.WorldUp;

            if (context.IsOnNonWalkableSlope)
            {
                float3 groundNormal = context.GroundNormal;

                if (math.dot(desiredVelocity, groundNormal) < 0f)
                {
                    float3 groundNormal2D = math.normalize((float3)Vector3.ProjectOnPlane(groundNormal, worldUp));
                    desiredVelocity = (float3)Vector3.ProjectOnPlane(desiredVelocity, groundNormal2D);
                }

                worldUp = math.normalize((float3)Vector3.ProjectOnPlane(worldUp, groundNormal));
            }

            float3 verticalVelocity = worldUp * context.VerticalVelocity;
            context.VerticalVelocity += gravity * context.DeltaTime;
            verticalVelocity = worldUp * context.VerticalVelocity;

            float3 horizontal = desiredVelocity * context.DeltaTime;
            float3 vertical = verticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = math.length(desiredVelocity);
            context.CurrentVelocity = desiredVelocity;
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.IsGrounded)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return context.StatePool.Get<SprintState>();
                    else
                        return context.StatePool.Get<RunState>();
                }
                else
                {
                    return context.StatePool.Get<IdleState>();
                }
            }

            if (context.JumpPressed)
            {
                context.JumpPressed = false;
                int resolvedMaxJump = (int)context.GetAttributeValue(MovementAttribute.MaxJumpCount, context.Config.MaxJumpCount);
                if (context.Config != null && context.JumpCount < resolvedMaxJump)
                {
                    return context.StatePool.Get<JumpState>();
                }
            }

            return null;
        }
    }
}