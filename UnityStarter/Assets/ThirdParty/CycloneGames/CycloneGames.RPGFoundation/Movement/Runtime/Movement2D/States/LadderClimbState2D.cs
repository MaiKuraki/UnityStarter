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

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0) context.AnimationController.SetBool(hash, true);
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float climbSpeed = context.Config.LadderClimbSpeed;
            float verticalInput = context.InputDirection.y;
            float horizontalInput = context.InputDirection.x;
            
            velocity = new float2(horizontalInput, verticalInput) * climbSpeed;
            context.CurrentSpeed = math.length(velocity);
        }

        public override void OnExit(ref MovementContext2D context)
        {
            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(context.Config.ClimbingParameter);
                if (hash != 0) context.AnimationController.SetBool(hash, false);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            if (context.JumpPressed)
            {
                return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
            }

            return null;
        }
    }
}
