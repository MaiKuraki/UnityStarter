using CycloneGames.RPGFoundation.Runtime.Movement;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public class WallClimbState2D : MovementStateBase2D
    {
        public override Movement.MovementStateType StateType => Movement.MovementStateType.Climb;

        private int _wallSide; // 1 = wall on right, -1 = wall on left
        private float _clingTimer;
        private bool _isSliding;

        public void SetWallSide(int side)
        {
            _wallSide = side;
        }

        public override void OnEnter(ref MovementContext2D context)
        {
            context.VerticalVelocity = 0f;
            _clingTimer = 0f;
            _isSliding = false;

            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.climbingParameter);
                if (hash != 0) context.AnimationController.SetBool(hash, true);
            }
        }

        public override void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            velocity = float2.zero;
            
            _clingTimer += context.DeltaTime;
            
            if (_clingTimer >= context.Config.wallClingDuration)
            {
                _isSliding = true;
            }

            if (_isSliding)
            {
                float slideSpeed = context.Config.wallSlideSpeed;
                velocity = new float2(0, -slideSpeed);
                context.CurrentSpeed = slideSpeed;
                
                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int slideHash = Movement.AnimationParameterCache.GetHash(context.Config.wallSlidingParameter);
                    if (slideHash != 0) context.AnimationController.SetBool(slideHash, true);
                }
            }
            else
            {
                float climbSpeed = context.Config.wallClimbSpeed;
                float verticalInput = context.InputDirection.y;
                float horizontalInput = context.InputDirection.x;
                
                // Allow both vertical and horizontal movement while clinging (for vines, nets, etc.)
                velocity = new float2(horizontalInput, verticalInput) * climbSpeed;
                context.CurrentSpeed = math.length(velocity);
            }
        }

        public override void OnExit(ref MovementContext2D context)
        {
            if (context.AnimationController != null && context.AnimationController.IsValid)
            {
                int hash = Movement.AnimationParameterCache.GetHash(context.Config.climbingParameter);
                if (hash != 0) context.AnimationController.SetBool(hash, false);
                
                int slideHash = Movement.AnimationParameterCache.GetHash(context.Config.wallSlidingParameter);
                if (slideHash != 0) context.AnimationController.SetBool(slideHash, false);
            }
        }

        public override MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            // Wall jump with direction based on wall side
            if (context.JumpPressed && context.Config.enableWallJump)
            {
                context.IsWallJumping = true;
                context.WallJumpDirection = new Vector2(-_wallSide * context.Config.wallJumpForceX, context.Config.wallJumpForceY);
                context.LastWallSide = _wallSide;
                context.LastWallJumpTime = Time.time;
                
                return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
            }

            if (context.IsGrounded)
            {
                return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
            }

            return null;
        }
    }
}
