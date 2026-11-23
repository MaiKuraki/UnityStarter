using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class RunState : MovementStateBase
    {
        private const float kDefaultRunSpeed = 5f;

        public override MovementStateType StateType => MovementStateType.Run;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float configSpeed = context.Config != null ? context.Config.RunSpeed : kDefaultRunSpeed;
            float maxSpeed = context.GetFinalSpeed(configSpeed, StateType);

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

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return context.StatePool.Get<IdleState>();
            }

            if (context.SprintHeld)
            {
                return context.StatePool.Get<SprintState>();
            }

            if (context.CrouchHeld)
            {
                return context.StatePool.Get<CrouchState>();
            }

            return null;
        }
    }
}