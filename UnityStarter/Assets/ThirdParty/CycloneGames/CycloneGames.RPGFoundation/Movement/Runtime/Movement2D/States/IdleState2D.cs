using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class IdleState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Idle;

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float verticalVelocity = 0f;
            if (context.Config.MovementType == MovementType2D.Platformer)
            {
#if UNITY_6000_0_OR_NEWER
                verticalVelocity = context.Rigidbody.linearVelocity.y;
#else
                verticalVelocity = context.Rigidbody.velocity.y;
#endif
            }

            float2 currentVelocity = new float2(0, verticalVelocity);
            displacement = currentVelocity * context.DeltaTime;
            context.CurrentSpeed = 0f;
            context.CurrentVelocity = currentVelocity;

        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (!context.IsGrounded)
            {
                return context.StatePool.Get<FallState2D>();
            }

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                if (context.SprintHeld)
                    return context.StatePool.Get<SprintState2D>();

                return context.StatePool.Get<RunState2D>();
            }

            return null;
        }
    }
}
