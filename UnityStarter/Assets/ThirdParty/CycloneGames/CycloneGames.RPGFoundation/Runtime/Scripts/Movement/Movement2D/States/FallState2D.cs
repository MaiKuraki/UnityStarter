using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class FallState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float airControl = context.Config.runSpeed * context.Config.airControlMultiplier;
            float horizontalVelocity = context.InputDirection.x * airControl;

            float verticalVelocity = context.Rigidbody.velocity.y;
            verticalVelocity = math.max(verticalVelocity, -context.Config.maxFallSpeed);

            velocity = new float2(horizontalVelocity, verticalVelocity);
            context.CurrentSpeed = math.abs(horizontalVelocity);
            context.CurrentVelocity = velocity;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int speedHash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                int verticalHash = AnimationParameterCache.GetHash(context.Config.verticalSpeedParameter);
                context.AnimationController.SetFloat(speedHash, context.CurrentSpeed);
                context.AnimationController.SetFloat(verticalHash, velocity.y);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.IsGrounded)
            {
                if (math.lengthsq(context.InputDirection) > 0.0001f)
                    return StatePool<MovementStateBase2D>.GetState<RunState2D>();
                else
                    return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            // Multi-jump: Check JumpCount < maxJumpCount before transitioning (JumpCount increments in JumpState2D.OnEnter)
            if (context.JumpPressed && context.Config != null && context.JumpCount < context.Config.maxJumpCount)
            {
                context.JumpPressed = false;
                return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
            }

            return null;
        }
    }
}