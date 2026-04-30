using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class SwimState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Swim;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float swimSpeed = context.GetAttributeValue(MovementAttribute.SwimSpeed, context.Config.SwimSpeed);

            float3 worldInputDirection = context.GetWorldInputDirection();
            float inputMagnitude = context.InputMagnitude;

            float verticalInput = context.InputDirection.y;
            float3 worldUp = context.WorldUp;

            float3 horizontalMovement = worldInputDirection * swimSpeed * inputMagnitude;
            float3 verticalMovement = worldUp * verticalInput * swimSpeed;

            displacement = (horizontalMovement + verticalMovement) * context.DeltaTime;
            context.CurrentSpeed = math.length(horizontalMovement + verticalMovement);
            context.CurrentVelocity = horizontalMovement + verticalMovement;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.MovementSpeedParameter);
                context.AnimationController.SetFloat(hash, context.CurrentSpeed);
            }

            if (!context.IsGrounded)
            {
                float gravity = context.GetAttributeValue(MovementAttribute.Gravity, context.Config.Gravity);
                context.VerticalVelocity += gravity * context.DeltaTime * 0.3f; // Reduced gravity in water
                displacement += context.WorldUp * context.VerticalVelocity * context.DeltaTime;
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (!context.IsGrounded && context.VerticalVelocity > context.Config.JumpForce * 0.5f)
            {
                return StatePool<MovementStateBase>.GetState<FallState>();
            }

            if (math.lengthsq(context.InputDirection) < 0.0001f)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }
    }
}
