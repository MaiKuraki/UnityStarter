using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class IdleState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Idle;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            displacement = float3.zero;

            if (!context.IsGrounded && context.VerticalVelocity < 0)
            {
                displacement = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded)
            {
                return StatePool.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                if (context.SprintHeld)
                    return StatePool.GetState<SprintState>();
                
                return StatePool.GetState<RunState>();
            }

            return null;
        }
    }
}
