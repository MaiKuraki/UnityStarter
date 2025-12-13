using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class JumpState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Jump;

        public override void OnEnter(ref MovementContext context)
        {
            // Vertical velocity uses WorldUp for wall/ceiling walking support
            context.VerticalVelocity = context.Config.jumpForce;
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
            float3 worldInputDirection = context.GetWorldInputDirection();
            float3 movement = worldInputDirection * context.Config.runSpeed * context.Config.airControlMultiplier;

            context.VerticalVelocity += context.Config.gravity * context.DeltaTime;

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
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            if (context.VerticalVelocity < 0)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            // Multi-jump: Allow additional jumps while rising if within jump count limit
            if (context.JumpPressed && context.Config != null && context.JumpCount < context.Config.maxJumpCount)
            {
                context.VerticalVelocity = context.Config.jumpForce;
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