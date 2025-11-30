using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class RunState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Run;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float speed = context.Config.runSpeed;
            float3 movement = context.InputDirection * speed;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = speed;
            context.CurrentVelocity = movement;

            if (context.Animator != null)
            {
                context.Animator.SetFloat(context.Config.AnimIDMovementSpeed, math.length(movement));
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded)
            {
                return StatePool.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool.GetState<IdleState>();
            }

            if (context.SprintHeld)
            {
                return StatePool.GetState<SprintState>();
            }

            if (context.CrouchHeld)
            {
                return StatePool.GetState<CrouchState>();
            }

            return null;
        }
    }
}
