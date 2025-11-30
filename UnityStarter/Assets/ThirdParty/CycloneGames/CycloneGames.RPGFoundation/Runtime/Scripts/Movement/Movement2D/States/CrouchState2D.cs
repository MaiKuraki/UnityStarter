using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class CrouchState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Crouch;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float speed = context.Config.crouchSpeed;
            float horizontalVelocity = context.InputDirection.x * speed;

            velocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
            context.CurrentSpeed = math.abs(horizontalVelocity);
            context.CurrentVelocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);

            if (context.Animator != null)
            {
                context.Animator.SetFloat(context.Config.AnimIDMovementSpeed, context.CurrentSpeed);
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