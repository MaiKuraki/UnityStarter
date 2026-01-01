using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class CrouchState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Crouch;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float speed = context.GetFinalSpeed(context.Config.crouchSpeed, StateType);
            float horizontalVelocity = context.InputDirection.x * speed;

#if UNITY_6000_0_OR_NEWER
            velocity = new float2(horizontalVelocity, context.Rigidbody.linearVelocity.y);
#else
            velocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
#endif
            context.CurrentSpeed = math.abs(horizontalVelocity);
#if UNITY_6000_0_OR_NEWER
            context.CurrentVelocity = new float2(horizontalVelocity, context.Rigidbody.linearVelocity.y);
#else
            context.CurrentVelocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
#endif

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

            if (!context.CrouchHeld)
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