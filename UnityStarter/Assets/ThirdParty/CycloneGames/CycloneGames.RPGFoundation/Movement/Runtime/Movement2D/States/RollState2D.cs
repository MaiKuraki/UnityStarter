using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class RollState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Roll;

        public override void OnEnter(ref MovementContext2D context)
        {
            context.RollTimer = 0f;
            context.RollPressed = false;

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                context.RollDirection = math.normalize(context.InputDirection);
            }
            else
            {
                float facingSign = context.Transform.localScale.x > 0f ? 1f : -1f;
                context.RollDirection = new float2(facingSign, 0f);
            }

            context.CurrentSpeed = context.GetAttributeValue(MovementAttribute.RunSpeed, context.Config.RunSpeed);

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.RollTrigger);
                if (hash != 0)
                {
                    context.AnimationController.SetTrigger(hash);
                }
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            context.RollTimer += context.DeltaTime;

            float rollDistance = context.Config.RollDistance;
            float rollDuration = context.Config.RollDuration;
            float rollSpeed = rollDuration > 0f ? rollDistance / rollDuration : 5f;

            displacement = context.RollDirection * rollSpeed * context.DeltaTime;
            context.CurrentSpeed = rollSpeed;
            context.CurrentVelocity = context.RollDirection * rollSpeed;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                context.AnimationController.SetFloat(hash, rollSpeed);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.RollTimer >= context.Config.RollDuration)
            {
                if (!context.IsGrounded)
                {
                    return StatePool<MovementStateBase2D>.GetState<FallState2D>();
                }

                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return StatePool<MovementStateBase2D>.GetState<SprintState2D>();
                    return StatePool<MovementStateBase2D>.GetState<RunState2D>();
                }

                return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            return null;
        }

        public override void OnExit(ref MovementContext2D context)
        {
            context.RollTimer = 0f;
            context.RollDirection = float2.zero;
        }
    }
}
