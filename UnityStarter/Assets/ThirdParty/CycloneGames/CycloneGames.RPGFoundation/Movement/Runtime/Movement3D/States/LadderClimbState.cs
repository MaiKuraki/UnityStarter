using Unity.Mathematics;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime.States
{
    public class LadderClimbState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
            context.JumpCount = 0;
        }

        public override void OnUpdate(ref MovementContext context, out float3 displacement)
        {
            float climbSpeed = context.GetAttributeValue(MovementAttribute.ClimbSpeed, context.Config.LadderClimbSpeed);
            
            float verticalInput = context.InputDirection.z;
            float horizontalInput = context.InputDirection.x;
            
            float3 worldUp = context.WorldUp;
            float3 worldRight = math.cross(worldUp, context.Transform.forward);
            
            displacement = (worldUp * verticalInput + worldRight * horizontalInput) * climbSpeed * context.DeltaTime;

            context.CurrentSpeed = math.length(new float2(horizontalInput, verticalInput)) * climbSpeed;
            context.CurrentVelocity = displacement / math.max(context.DeltaTime, 0.0001f);
        }

        public override void OnExit(ref MovementContext context)
        {
            if (context.ClimbingMode == ClimbingMode.Ladder)
            {
                context.ClimbingMode = ClimbingMode.None;
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            if (context.JumpPressed)
            {
                return context.StatePool.Get<JumpState>();
            }

            return null;
        }
    }
}
