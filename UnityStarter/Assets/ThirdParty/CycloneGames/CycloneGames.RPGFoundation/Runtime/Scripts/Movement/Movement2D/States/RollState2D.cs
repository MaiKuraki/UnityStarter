using Unity.Mathematics;
using CycloneGames.RPGFoundation.Runtime.Movement;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class RollState2D : MovementStateBase2D
    {
        private float _rollTimer;
        private float2 _rollDirection;

        public override Movement.MovementStateType StateType => Movement.MovementStateType.Roll;

        public override void OnEnter(ref MovementContext2D context)
        {
            _rollTimer = 0f;

            if (math.lengthsq(context.InputDirection) > 0.0001f)
            {
                _rollDirection = math.normalize(context.InputDirection);
            }
            else
            {
                float facingSign = context.Transform.localScale.x > 0f ? 1f : -1f;
                _rollDirection = new float2(facingSign, 0f);
            }

            context.CurrentSpeed = context.GetAttributeValue(Movement.MovementAttribute.RunSpeed, context.Config.RunSpeed);

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.RollTrigger);
                if (hash != 0)
                {
                    context.AnimationController.SetTrigger(hash);
                }
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            _rollTimer += context.DeltaTime;

            float rollDistance = context.Config.RollDistance;
            float rollDuration = context.Config.RollDuration;
            float rollSpeed = rollDuration > 0f ? rollDistance / rollDuration : 5f;

            velocity = _rollDirection * rollSpeed * context.DeltaTime;
            context.CurrentSpeed = rollSpeed;
            context.CurrentVelocity = _rollDirection * rollSpeed;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                context.AnimationController.SetFloat(hash, rollSpeed);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (_rollTimer >= context.Config.RollDuration)
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
        }
    }
}
