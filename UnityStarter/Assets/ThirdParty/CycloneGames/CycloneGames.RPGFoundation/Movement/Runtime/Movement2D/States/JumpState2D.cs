using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class JumpState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Jump;

        public override void OnEnter(ref MovementContext2D context)
        {
            int resolvedMaxJumpCount = (int)context.GetAttributeValue(MovementAttribute.MaxJumpCount, context.Config.MaxJumpCount);
            if (context.Config != null && context.JumpCount >= resolvedMaxJumpCount)
            {
                return;
            }

            float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.JumpForce);
            float2 jumpVelocity = context.WorldUp * jumpForce;
#if UNITY_6000_0_OR_NEWER
            float horizontalVelocity = context.Rigidbody.linearVelocity.x;
            context.Rigidbody.linearVelocity = new UnityEngine.Vector2(horizontalVelocity + jumpVelocity.x, jumpVelocity.y);
#else
            float horizontalVelocity = context.Rigidbody.velocity.x;
            context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity + jumpVelocity.x, jumpVelocity.y);
#endif
            context.JumpCount++;
            context.JumpPressed = false;

            context.StepEvents |= MovementStepEvent.JumpImpulse;
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.AirControlMultiplier);
            float airControlSpeed = runSpeed * airControl;
            float horizontalVelocity = context.InputDirection.x * airControlSpeed;

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
#if UNITY_6000_0_OR_NEWER
            if (context.IsGrounded && context.Rigidbody.linearVelocity.y <= 0)
#else
            if (context.IsGrounded && context.Rigidbody.velocity.y <= 0)
#endif
            {
                context.JumpCount = 0;

                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return context.StatePool.Get<SprintState2D>();
                    else
                        return context.StatePool.Get<RunState2D>();
                }
                else
                {
                    return context.StatePool.Get<IdleState2D>();
                }
            }

#if UNITY_6000_0_OR_NEWER
            if (context.Rigidbody.linearVelocity.y <= 0)
#else
            if (context.Rigidbody.velocity.y <= 0)
#endif
            {
                return context.StatePool.Get<FallState2D>();
            }

            int resolvedMaxJumpCount = (int)context.GetAttributeValue(MovementAttribute.MaxJumpCount, context.Config.MaxJumpCount);
            if (context.JumpPressed && context.Config != null && context.JumpCount < resolvedMaxJumpCount)
            {
                float jumpForce = context.GetAttributeValue(MovementAttribute.JumpForce, context.Config.JumpForce);
                float2 jumpVelocity = context.WorldUp * jumpForce;
#if UNITY_6000_0_OR_NEWER
                float horizontalVelocity = context.Rigidbody.linearVelocity.x;
                context.Rigidbody.linearVelocity = new UnityEngine.Vector2(horizontalVelocity + jumpVelocity.x, jumpVelocity.y);
#else
                float horizontalVelocity = context.Rigidbody.velocity.x;
                context.Rigidbody.velocity = new UnityEngine.Vector2(horizontalVelocity + jumpVelocity.x, jumpVelocity.y);
#endif

                context.JumpCount++;
                context.JumpPressed = false;

                context.StepEvents |= MovementStepEvent.AirJumpImpulse;
            }

            return null;
        }

        public override void OnExit(ref MovementContext2D context)
        {
        }
    }
}
