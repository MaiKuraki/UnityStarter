using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class IdleState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Idle;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            velocity = new float2(0, context.Rigidbody.velocity.y);
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (!context.IsGrounded)
            {
                return StatePool<MovementStateBase2D>.GetState<FallState2D>();
            }

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                if (context.SprintHeld)
                    return StatePool<MovementStateBase2D>.GetState<SprintState2D>();

                return StatePool<MovementStateBase2D>.GetState<RunState2D>();
            }

            return null;
        }
    }
}