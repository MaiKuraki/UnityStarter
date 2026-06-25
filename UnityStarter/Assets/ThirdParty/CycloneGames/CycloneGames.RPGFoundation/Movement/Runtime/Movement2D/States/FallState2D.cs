using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class FallState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Fall;

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float runSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);
            float airControl = context.GetAttributeValue(MovementAttribute.AirControlMultiplier, context.Config.AirControlMultiplier);
            float airControlSpeed = runSpeed * airControl;
            float horizontalVelocity = context.InputDirection.x * airControlSpeed;

#if UNITY_6000_0_OR_NEWER
            float verticalVelocity = context.Rigidbody.linearVelocity.y;
#else
            float verticalVelocity = context.Rigidbody.velocity.y;
#endif
            verticalVelocity = math.max(verticalVelocity, -context.Config.MaxFallSpeed);

            float2 currentVelocity = new float2(horizontalVelocity, verticalVelocity);
            displacement = currentVelocity * context.DeltaTime;
            context.CurrentSpeed = math.abs(horizontalVelocity);
            context.CurrentVelocity = currentVelocity;

            // BeltScroll: accumulate Y input as pending depth while airborne
            if (context.Config.MovementType == MovementType2D.BeltScroll)
            {
                float depthSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);
                context.PendingDepth += context.InputDirection.y * depthSpeed * context.DeltaTime;
            }

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int speedHash = AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                int verticalHash = AnimationParameterCache.GetHash(context.Config.VerticalSpeedParameter);
                context.AnimationController.SetFloat(speedHash, context.CurrentSpeed);
                context.AnimationController.SetFloat(verticalHash, currentVelocity.y);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.IsGrounded)
            {
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

            // Multi-jump: Check JumpCount < maxJumpCount before transitioning (JumpCount increments in JumpState2D.OnEnter)
            // Consume JumpPressed immediately to prevent it from persisting if jump cannot be performed
            if (context.JumpPressed)
            {
                context.JumpPressed = false;
                int resolvedMaxJump = (int)context.GetAttributeValue(MovementAttribute.MaxJumpCount, context.Config.MaxJumpCount);
                if (context.Config != null && context.JumpCount < resolvedMaxJump)
                {
                    return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
                }
            }

            return null;
        }
    }
}
