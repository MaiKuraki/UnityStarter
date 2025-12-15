using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class FallState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.runSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.airControlMultiplier);
            float speed = runSpeed * airControl;
            float3 worldInputDirection = context.GetWorldInputDirection();
            float3 movement = worldInputDirection * speed;

            float gravity = context.GetAttributeValue(MovementAttribute.Gravity, context.Config.gravity);
            context.VerticalVelocity += gravity * context.DeltaTime;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = math.length(movement);
            context.CurrentVelocity = movement;

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