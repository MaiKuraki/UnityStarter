using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class JumpState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Jump;

        public override void OnEnter(ref MovementContext2D context)
        {
            float horizontalVelocity = context.Rigidbody.velocity.x;
            context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity, context.Config.jumpForce);
            context.JumpCount++;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.jumpTrigger);
                context.AnimationController.SetTrigger(hash);
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float airControl = context.Config.runSpeed * context.Config.airControlMultiplier;
            float horizontalVelocity = context.InputDirection.x * airControl;

            velocity = new float2(horizontalVelocity, context.Rigidbody.velocity.y);
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
            if (context.IsGrounded && context.Rigidbody.velocity.y <= 0)
            {
                context.JumpCount = 0;
                return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            if (context.Rigidbody.velocity.y < 0)
            {
                return StatePool<MovementStateBase2D>.GetState<FallState2D>();
            }

            if (context.JumpPressed && context.JumpCount < context.Config.maxJumpCount)
            {
                float horizontalVelocity = context.Rigidbody.velocity.x;
                context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity, context.Config.jumpForce);
                context.JumpCount++;
            }

            return null;
        }

        public override void OnExit(ref MovementContext2D context)
        {
            context.JumpCount = 0;
        }
    }
}