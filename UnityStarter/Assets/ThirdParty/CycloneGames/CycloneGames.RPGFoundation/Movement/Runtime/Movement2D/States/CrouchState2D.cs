using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class CrouchState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Crouch;

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float speed = context.GetFinalSpeed(context.Config.CrouchSpeed, StateType);
            float horizontalVelocity = context.InputDirection.x * speed;

#if UNITY_6000_0_OR_NEWER
            float2 currentVelocity = new float2(horizontalVelocity, context.Rigidbody.linearVelocity.y);
#else
            float2 currentVelocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
#endif
            displacement = currentVelocity * context.DeltaTime;
            context.CurrentSpeed = math.abs(horizontalVelocity);
            context.CurrentVelocity = currentVelocity;
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (!context.IsGrounded)
            {
                return context.StatePool.Get<FallState2D>();
            }

            if (!context.CrouchHeld)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return context.StatePool.Get<RunState2D>();
                else
                    return context.StatePool.Get<IdleState2D>();
            }

            return null;
        }
    }
}
