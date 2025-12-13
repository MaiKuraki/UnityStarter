using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class WalkState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Walk;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float speed = context.Config.walkSpeed;
            float3 worldInputDirection = context.GetWorldInputDirection();
            float3 movement = worldInputDirection * speed;

            float3 horizontal = movement * context.DeltaTime;
            float3 vertical = context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            displacement = horizontal + vertical;

            context.CurrentSpeed = speed;
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