using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class FallState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.runSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.airControlMultiplier);
            float maxSpeed = runSpeed * airControl;

            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            float actualSpeed = maxSpeed * inputMagnitude;
            float3 desiredVelocity = worldInputDirection * actualSpeed;

            float gravity = context.GetAttributeValue(MovementAttribute.Gravity, context.Config.gravity);
            float3 worldUp = context.WorldUp;

            // Handle non-walkable slope sliding
            if (context.IsOnNonWalkableSlope)
            {
                float3 groundNormal = context.GroundNormal;

                // If moving into the slope, limit contribution
                if (math.dot(desiredVelocity, groundNormal) < 0f)
                {
                    // Allow movement parallel to the slope, but not into it
                    float3 groundNormal2D = math.normalize((float3)Vector3.ProjectOnPlane(groundNormal, worldUp));
                    desiredVelocity = (float3)Vector3.ProjectOnPlane(desiredVelocity, groundNormal2D);
                }

                // Make velocity calculations planar by projecting up vector onto non-walkable surface
                // This causes the character to slide down the slope
                worldUp = math.normalize((float3)Vector3.ProjectOnPlane(worldUp, groundNormal));
            }

            // Separate velocity into vertical and lateral components
            float3 verticalVelocity = worldUp * context.VerticalVelocity;
            context.VerticalVelocity += gravity * context.DeltaTime;
            verticalVelocity = worldUp * context.VerticalVelocity;

            float3 horizontal = desiredVelocity * context.DeltaTime;
            float3 vertical = verticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = math.length(desiredVelocity);
            context.CurrentVelocity = desiredVelocity;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                context.AnimationController.SetFloat(hash, context.CurrentSpeed);
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.IsGrounded)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return StatePool<MovementStateBase>.GetState<SprintState>();
                    else
                        return StatePool<MovementStateBase>.GetState<RunState>();
                }
                else
                {
                    return StatePool<MovementStateBase>.GetState<IdleState>();
                }
            }

            // Multi-jump: Check JumpCount < maxJumpCount before transitioning (JumpCount increments in JumpState.OnEnter)
            // Consume JumpPressed immediately to prevent it from persisting if jump cannot be performed
            if (context.JumpPressed)
            {
                context.JumpPressed = false;
                if (context.Config != null && context.JumpCount < context.Config.maxJumpCount)
                {
                    return StatePool<MovementStateBase>.GetState<JumpState>();
                }
            }

            return null;
        }
    }
}