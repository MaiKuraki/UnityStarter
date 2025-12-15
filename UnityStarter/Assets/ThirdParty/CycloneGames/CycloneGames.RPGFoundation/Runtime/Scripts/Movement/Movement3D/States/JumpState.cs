using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class JumpState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Jump;

        public override void OnEnter(ref MovementContext context)
        {
            // Check jump count limit before allowing jump
            if (context.Config != null && context.JumpCount >= context.Config.maxJumpCount)
            {
                return;
            }

            float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.jumpForce);
            context.VerticalVelocity = jumpForce;
            context.JumpCount++;
            context.JumpPressed = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.jumpTrigger);
                context.AnimationController.SetTrigger(hash);
            }
        }

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
            if (context.IsGrounded && context.VerticalVelocity <= 0)
            {
                context.JumpCount = 0;
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

            // Transition to FallState when reaching apex or descending
            // This must be checked BEFORE multi-jump to prevent extra jumps at apex
            if (context.VerticalVelocity <= 0)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            // Multi-jump: Allow additional jumps while rising (VerticalVelocity > 0) if within jump count limit
            if (context.JumpPressed && context.Config != null && context.JumpCount < context.Config.maxJumpCount)
            {
                float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.jumpForce);
                context.VerticalVelocity = jumpForce;
                context.JumpCount++;
                context.JumpPressed = false;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int hash = AnimationParameterCache.GetHash(context.Config.jumpTrigger);
                    context.AnimationController.SetTrigger(hash);
                }
            }

            return null;
        }

        public override void OnExit(ref MovementContext context)
        {
        }
    }
}