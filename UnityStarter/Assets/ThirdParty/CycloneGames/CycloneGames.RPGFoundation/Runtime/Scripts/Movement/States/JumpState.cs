using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class JumpState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Jump;
        
        private int _jumpCount;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = context.Config.jumpForce;
            _jumpCount++;

            if (context.Animator != null)
            {
                context.Animator.SetTrigger(context.Config.AnimIDJump);
            }
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float3 movement = context.InputDirection * context.Config.runSpeed * context.Config.airControlMultiplier;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = math.length(movement);
            context.CurrentVelocity = movement;

            if (context.Animator != null)
            {
                context.Animator.SetFloat(context.Config.AnimIDMovementSpeed, context.CurrentSpeed);
            }

            if (context.VerticalVelocity < 0)
            {
                context.VerticalVelocity += context.Config.gravity * context.DeltaTime;
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.IsGrounded && context.VerticalVelocity <= 0)
            {
                _jumpCount = 0;
                return StatePool.GetState<IdleState>();
            }

            if (context.VerticalVelocity < 0)
            {
                return StatePool.GetState<FallState>();
            }

            if (context.JumpPressed && _jumpCount < context.Config.maxJumpCount)
            {
                context.VerticalVelocity = context.Config.jumpForce;
                _jumpCount++;
            }

            return null;
        }

        public override void OnExit(ref MovementContext context)
        {
            _jumpCount = 0;
        }
    }
}
