using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class RollState : MovementStateBase
    {
        private float _rollTimer;

        public override MovementStateType StateType => MovementStateType.Roll;

        public override void OnEnter(ref MovementContext context)
        {
            _rollTimer = 0f;
            context.VerticalVelocity = 0f;

            float3 worldInput = context.GetWorldInputDirection();
            if (math.lengthsq(worldInput) < 0.0001f)
            {
                // No input direction - roll forward relative to transform
                worldInput = context.Transform.forward;
            }

            context.CurrentVelocity = worldInput;
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

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            _rollTimer += context.DeltaTime;

            float rollDistance = context.Config.RollDistance;
            float rollDuration = context.Config.RollDuration;
            float rollSpeed = rollDuration > 0f ? rollDistance / rollDuration : 5f;

            float3 direction = math.normalize(context.CurrentVelocity);
            displacement = direction * rollSpeed * context.DeltaTime + context.WorldUp * context.VerticalVelocity * context.DeltaTime;

            context.CurrentSpeed = rollSpeed;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                context.AnimationController.SetFloat(hash, rollSpeed);
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (_rollTimer >= context.Config.RollDuration)
            {
                if (!context.IsGrounded)
                {
                    return StatePool<MovementStateBase>.GetState<FallState>();
                }

                if (math.lengthsq(context.InputDirection) > 0.0001f)
                {
                    if (context.SprintHeld)
                        return StatePool<MovementStateBase>.GetState<SprintState>();
                    return StatePool<MovementStateBase>.GetState<RunState>();
                }

                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }

        public override void OnExit(ref MovementContext context)
        {
        }
    }
}
