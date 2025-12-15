using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class JumpState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Jump;

        public override void OnEnter(ref MovementContext2D context)
        {
            // Check jump count limit before allowing jump
            if (context.Config != null && context.JumpCount >= context.Config.maxJumpCount)
            {
                return;
            }

            float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.jumpForce);
            float horizontalVelocity = context.Rigidbody.velocity.x;
            context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity, jumpForce);
            context.JumpCount++;
            context.JumpPressed = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.jumpTrigger);
                context.AnimationController.SetTrigger(hash);
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.runSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.airControlMultiplier);
            float airControlSpeed = runSpeed * airControl;
            float horizontalVelocity = context.InputDirection.x * airControlSpeed;

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

                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return StatePool<MovementStateBase2D>.GetState<SprintState2D>();
                    else
                        return StatePool<MovementStateBase2D>.GetState<RunState2D>();
                }
                else
                {
                    return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                }
            }

            // Transition to FallState2D when reaching apex or descending
            // This must be checked BEFORE multi-jump to prevent extra jumps at apex
            if (context.Rigidbody.velocity.y <= 0)
            {
                return StatePool<MovementStateBase2D>.GetState<FallState2D>();
            }

            // Multi-jump: Allow additional jumps while rising (velocity.y > 0) if within jump count limit
            if (context.JumpPressed && context.Config != null && context.JumpCount < context.Config.maxJumpCount)
            {
                float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.jumpForce);
                float horizontalVelocity = context.Rigidbody.velocity.x;
                context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity, jumpForce);
                context.JumpCount++;
                context.JumpPressed = false;

                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int hash = AnimationParameterCache.GetHash(context.Config.jumpTrigger);
                    context.AnimationController.SetTrigger(hash);
                }
            }

            return null;
        }

        public override void OnExit(ref MovementContext2D context)
        {
        }
    }
}