using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States
{
    public class LadderClimbState2D : MovementStateBase2D
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        public override void OnEnter(ref MovementContext2D context)
        {
            context.VerticalVelocity = 0f;
            context.JumpCount = 0;
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 displacement)
        {
            float climbSpeed = context.Config.LadderClimbSpeed;
            float verticalInput = context.InputDirection.y;
            float horizontalInput = context.InputDirection.x;
            
            float2 currentVelocity = new float2(horizontalInput, verticalInput) * climbSpeed;
            displacement = currentVelocity * context.DeltaTime;
            context.CurrentSpeed = math.length(currentVelocity);
            context.CurrentVelocity = currentVelocity;
        }

        public override void OnExit(ref MovementContext2D context)
        {
            if (context.ClimbingMode == ClimbingMode.Ladder)
            {
                context.ClimbingMode = ClimbingMode.None;
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.JumpPressed)
            {
                return context.StatePool.Get<JumpState2D>();
            }

            return null;
        }
    }
}
