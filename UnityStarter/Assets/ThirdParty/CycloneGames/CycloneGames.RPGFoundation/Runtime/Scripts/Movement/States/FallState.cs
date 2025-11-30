using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class FallState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float3 movement = context.InputDirection * context.Config.runSpeed * context.Config.airControlMultiplier;

            context.VerticalVelocity += context.Config.gravity * context.DeltaTime;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = math.length(movement);
            context.CurrentVelocity = movement;

            if (context.Animator != null)
            {
                context.Animator.SetFloat(context.Config.AnimIDMovementSpeed, context.CurrentSpeed);
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.IsGrounded)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return StatePool.GetState<WalkState>();
                else
                    return StatePool.GetState<IdleState>();
            }

            return null;
        }
    }
}
