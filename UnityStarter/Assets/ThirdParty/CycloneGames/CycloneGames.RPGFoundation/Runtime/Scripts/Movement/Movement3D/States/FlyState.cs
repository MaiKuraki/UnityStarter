using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class FlyState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Fly;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float flySpeed = context.GetAttributeValue(MovementAttribute.FlySpeed, context.Config.FlySpeed);

            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            float verticalInput = context.InputDirection.y;
            float3 worldUp = context.WorldUp;

            float3 horizontalMovement = worldInputDirection * flySpeed * inputMagnitude;
            float3 verticalMovement = worldUp * verticalInput * flySpeed;

            displacement = (horizontalMovement + verticalMovement) * context.DeltaTime;
            context.CurrentSpeed = math.length(horizontalMovement + verticalMovement);
            context.CurrentVelocity = horizontalMovement + verticalMovement;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                context.AnimationController.SetFloat(hash, context.CurrentSpeed);
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }
    }
}
