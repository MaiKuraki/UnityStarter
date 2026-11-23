using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class IdleState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Idle;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            displacement = float3.zero;

            context.CurrentSpeed = 0f;
            context.CurrentVelocity = float3.zero;

            if (!context.IsGrounded && context.VerticalVelocity < 0)
            {
                displacement = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            }

        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded || context.IsOnNonWalkableSlope)
            {
                return context.StatePool.Get<FallState>();
            }

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                if (context.SprintHeld)
                    return context.StatePool.Get<SprintState>();

                return context.StatePool.Get<RunState>();
            }

            return null;
        }
    }
}