using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class RunState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Run;

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float speed = context.GetFinalSpeed(context.Config.RunSpeed, StateType);
            float horizontalVelocity = context.InputDirection.x * speed;

            float verticalVelocity = 0f;
            if (context.Config.MovementType == MovementType2D.BeltScroll ||
                context.Config.MovementType == MovementType2D.TopDown)
            {
                verticalVelocity = context.InputDirection.y * speed;
            }
            else
            {
#if UNITY_6000_0_OR_NEWER
                verticalVelocity = context.Rigidbody.linearVelocity.y;
#else
                verticalVelocity = context.Rigidbody.velocity.y;
#endif
            }

            float2 currentVelocity = new float2(horizontalVelocity, verticalVelocity);
            displacement = currentVelocity * context.DeltaTime;
            context.CurrentSpeed = math.length(currentVelocity);
            context.CurrentVelocity = currentVelocity;
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (!context.IsGrounded)
            {
                return context.StatePool.Get<FallState2D>();
            }

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return context.StatePool.Get<IdleState2D>();
            }

            if (context.SprintHeld)
            {
                return context.StatePool.Get<SprintState2D>();
            }

            if (context.CrouchHeld)
            {
                return context.StatePool.Get<CrouchState2D>();
            }

            return null;
        }
    }
}
