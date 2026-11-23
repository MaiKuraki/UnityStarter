using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class SprintState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Sprint;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float maxSpeed = context.GetFinalSpeed(context.Config.SprintSpeed, StateType);
            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            float actualSpeed = maxSpeed * inputMagnitude;
            float3 movement = worldInputDirection * actualSpeed;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = actualSpeed;
            context.CurrentVelocity = movement;
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded || context.IsOnNonWalkableSlope)
            {
                return context.StatePool.Get<FallState>();
            }

            if (!context.SprintHeld || math.lengthsq(context.InputDirection) < 0.0001f)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return context.StatePool.Get<RunState>();
                else
                    return context.StatePool.Get<IdleState>();
            }

            return null;
        }
    }
}