using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    /// <summary>
    /// Climbing state for ladder/wall climbing.
    /// Vertical movement only, no gravity, controlled by input.
    /// </summary>
    public class ClimbState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
            context.JumpCount = 0;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.climbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, true);
                }
            }
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float climbSpeed = context.GetAttributeValue(MovementAttribute.ClimbSpeed, context.Config.climbSpeed);
            
            // Y input controls vertical climbing, X input controls horizontal movement on ladder
            float verticalInput = context.InputDirection.z;
            float horizontalInput = context.InputDirection.x;
            
            float3 worldUp = context.WorldUp;
            float3 worldRight = math.cross(worldUp, context.Transform.forward);
            
            displacement = (worldUp * verticalInput + worldRight * horizontalInput) * climbSpeed * context.DeltaTime;

            context.CurrentSpeed = math.length(new float2(horizontalInput, verticalInput)) * climbSpeed;
            context.CurrentVelocity = displacement / math.max(context.DeltaTime, 0.0001f);

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int speedHash = AnimationParameterCache.GetHash(context.Config.movementSpeedParameter);
                if (speedHash != 0)
                {
                    context.AnimationController.SetFloat(speedHash, context.CurrentSpeed);
                }
            }
        }

        public override void OnExit(ref MovementContext context)
        {
            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.climbingParameter);
                if (hash != 0)
                {
                    context.AnimationController.SetBool(hash, false);
                }
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            // Exit climbing via jump (detach from ladder)
            if (context.JumpPressed)
            {
                return StatePool<MovementStateBase>.GetState<JumpState>();
            }

            // External state change request handles returning to other states
            return null;
        }
    }
}
