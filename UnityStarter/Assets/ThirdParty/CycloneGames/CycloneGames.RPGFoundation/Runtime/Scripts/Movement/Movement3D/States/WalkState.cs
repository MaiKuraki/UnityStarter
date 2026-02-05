using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class WalkState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Walk;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float maxSpeed = context.GetFinalSpeed(context.Config.walkSpeed, StateType);
            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;
            
            float actualSpeed = maxSpeed * inputMagnitude;
            float3 movement = worldInputDirection * actualSpeed;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = actualSpeed;
            context.CurrentVelocity = movement;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                context.AnimationController.SetFloat(hash, math.length(movement));
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            if (context.CrouchHeld)
            {
                return StatePool<MovementStateBase>.GetState<CrouchState>();
            }

            return null;
        }
    }
}