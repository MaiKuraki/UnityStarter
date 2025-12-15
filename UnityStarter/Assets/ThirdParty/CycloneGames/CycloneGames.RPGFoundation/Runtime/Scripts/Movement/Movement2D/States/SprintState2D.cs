using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class SprintState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Sprint;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float speed = context.GetFinalSpeed(context.Config.sprintSpeed, StateType);
            float horizontalVelocity = context.InputDirection.x * speed;

            velocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
            context.CurrentSpeed = math.abs(horizontalVelocity);
            context.CurrentVelocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                context.AnimationController.SetFloat(hash, context.CurrentSpeed);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (!context.IsGrounded)
            {
                return StatePool<MovementStateBase2D>.GetState<FallState2D>();
            }

            if (!context.SprintHeld || math.lengthsq(context.InputDirection) < 0.0001f)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return StatePool<MovementStateBase2D>.GetState<RunState2D>();
                else
                    return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            return null;
        }
    }
}