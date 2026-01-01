using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class RunState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Run;

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float speed = context.GetFinalSpeed(context.Config.runSpeed, StateType);
            float horizontalVelocity = context.InputDirection.x * speed;

            // For BeltScroll and TopDown modes, Y input controls depth/vertical movement
            float verticalVelocity = 0f;
            if (context.Config.movementType == MovementType2D.BeltScroll ||
                context.Config.movementType == MovementType2D.TopDown)
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

            velocity = new float2(horizontalVelocity, verticalVelocity);
            context.CurrentSpeed = math.length(new float2(horizontalVelocity, verticalVelocity));
            context.CurrentVelocity = velocity;

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

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            if (context.SprintHeld)
            {
                return StatePool<MovementStateBase2D>.GetState<SprintState2D>();
            }

            if (context.CrouchHeld)
            {
                return StatePool<MovementStateBase2D>.GetState<CrouchState2D>();
            }

            return null;
        }
    }
}