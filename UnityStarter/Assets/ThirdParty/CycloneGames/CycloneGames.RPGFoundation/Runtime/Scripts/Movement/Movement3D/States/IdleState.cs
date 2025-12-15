using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class IdleState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Idle;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            displacement = float3.zero;

            // Reset speed and velocity when idle
            context.CurrentSpeed = 0f;
            context.CurrentVelocity = float3.zero;

            if (!context.IsGrounded && context.VerticalVelocity < 0)
            {
                displacement = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            }

            // Update animation parameter
            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                context.AnimationController.SetFloat(hash, 0f);
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                if (context.SprintHeld)
                    return StatePool<MovementStateBase>.GetState<SprintState>();

                return StatePool<MovementStateBase>.GetState<RunState>();
            }

            return null;
        }
    }
}