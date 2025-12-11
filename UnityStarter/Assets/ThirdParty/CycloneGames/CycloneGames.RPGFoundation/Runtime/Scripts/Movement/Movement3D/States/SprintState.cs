using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class SprintState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Sprint;

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float speed = context.Config.sprintSpeed;
            float3 movement = context.InputDirection * speed;

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

            if (!context.SprintHeld || math.lengthsq(context.InputDirection) < 0.0001f)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return StatePool<MovementStateBase>.GetState<RunState>();
                else
                    return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }
    }
}