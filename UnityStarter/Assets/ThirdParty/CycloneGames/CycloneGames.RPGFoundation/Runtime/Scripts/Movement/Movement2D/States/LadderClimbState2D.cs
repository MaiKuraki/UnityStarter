using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class LadderClimbState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Climb;

        public override void OnEnter(ref MovementContext2D context)
        {
            context.VerticalVelocity = 0f;
            context.JumpCount = 0;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.climbingParameter);
                if (hash != 0) context.AnimationController.SetBool(hash, true);
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            float climbSpeed = context.Config.ladderClimbSpeed;
            float verticalInput = context.InputDirection.y;
            float horizontalInput = context.InputDirection.x;
            
            velocity = new float2(horizontalInput, verticalInput) * climbSpeed;
            context.CurrentSpeed = math.length(velocity);
        }

        public override void OnExit(ref MovementContext2D context)
        {
            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.climbingParameter);
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
