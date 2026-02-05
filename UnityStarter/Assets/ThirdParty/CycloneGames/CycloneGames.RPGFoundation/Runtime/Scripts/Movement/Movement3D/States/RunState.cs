using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class RunState : MovementStateBase
    {
        private const float kDefaultRunSpeed = 5f;

        public override MovementStateType StateType => MovementStateType.Run;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            // Safely get run speed from config
            float configSpeed = context.Config != null ? context.Config.runSpeed : kDefaultRunSpeed;
            float maxSpeed = context.GetFinalSpeed(configSpeed, StateType);

            // Get normalized world direction and input magnitude
            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            // Scale speed by input magnitude (analog stick support)
            float actualSpeed = maxSpeed * inputMagnitude;
            float3 movement = worldInputDirection * actualSpeed;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = actualSpeed;
            context.CurrentVelocity = movement;

            if (context.AnimationController != null && context.AnimationController.IsValid && context.Config != null)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                context.AnimationController.SetFloat(hash, math.length(movement));
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            // Fall when not grounded OR on non-walkable slope
            if (!context.IsGrounded || context.IsOnNonWalkableSlope)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            if (context.SprintHeld)
            {
                return StatePool<MovementStateBase>.GetState<SprintState>();
            }

            if (context.CrouchHeld)
            {
                return StatePool<MovementStateBase>.GetState<CrouchState>();
            }

            return null;
        }
    }
}