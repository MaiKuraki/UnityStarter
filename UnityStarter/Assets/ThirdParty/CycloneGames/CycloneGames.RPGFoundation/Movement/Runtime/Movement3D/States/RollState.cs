using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class RollState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Roll;

        public override void OnEnter(ref MovementContext context)
        {
            context.RollTimer = 0f;
            context.RollPressed = false;
            context.VerticalVelocity = 0f;

            float3 worldInput = context.GetWorldInputDirection();
            if (math.lengthsq(worldInput) < 0.0001f)
            {
                worldInput = context.Transform.forward;
            }

            context.RollDirection = math.normalize(worldInput);
            context.CurrentVelocity = context.RollDirection;
            context.CurrentSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);

            context.StepEvents |= MovementStepEvent.RollStarted;
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            context.RollTimer += context.DeltaTime;

            float rollDistance = context.Config.RollDistance;
            float rollDuration = context.Config.RollDuration;
            float rollSpeed = rollDuration > 0f ? rollDistance / rollDuration : 5f;

            float3 direction = math.lengthsq(context.RollDirection) > 0.0001f
                ? math.normalize(context.RollDirection)
                : context.GetWorldInputDirection();
            displacement = direction * rollSpeed * context.DeltaTime + context.WorldUp * context.VerticalVelocity * context.DeltaTime;

            context.CurrentSpeed = rollSpeed;
            context.CurrentVelocity = direction * rollSpeed + context.WorldUp * context.VerticalVelocity;
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.RollTimer >= context.Config.RollDuration)
            {
                if (!context.IsGrounded)
                {
                    return context.StatePool.Get<FallState>();
                }

                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return context.StatePool.Get<SprintState>();
                    return context.StatePool.Get<RunState>();
                }

                return context.StatePool.Get<IdleState>();
            }

            return null;
        }

        public override void OnExit(ref MovementContext context)
        {
            context.RollTimer = 0f;
            context.RollDirection = float3.zero;
        }
    }
}
