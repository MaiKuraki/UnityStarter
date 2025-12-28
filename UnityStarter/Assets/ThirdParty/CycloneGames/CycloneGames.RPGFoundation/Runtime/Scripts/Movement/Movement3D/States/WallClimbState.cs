using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public class WallClimbState : MovementStateBase
    {
        public override MovementStateType StateType => MovementStateType.Climb;

        private float3 _wallNormal;
        private float _clingTimer;
        private bool _isSliding;

        public void SetWallNormal(float3 normal)
        {
            _wallNormal = normal;
        }

        public override void OnEnter(ref MovementContext context)
        {
            context.VerticalVelocity = 0f;
            _clingTimer = 0f;
            _isSliding = false;

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
            displacement = float3.zero;
            
            _clingTimer += context.DeltaTime;
            
            if (_clingTimer >= context.Config.wallClingDuration)
            {
                _isSliding = true;
            }

            if (_isSliding)
            {
                // Simple sliding down (always world down)
                float slideSpeed = context.Config.wallSlideSpeed;
                displacement = -context.WorldUp * slideSpeed * context.DeltaTime;
                context.CurrentSpeed = slideSpeed;
                
                if (context.AnimationController != null && context.AnimationController.IsValid)
                {
                    int slideHash = AnimationParameterCache.GetHash(context.Config.wallSlidingParameter);
                    if (slideHash != 0) context.AnimationController.SetBool(slideHash, true);
                }
            }
            else
            {
                // Advanced Surface Climbing Logic
                float climbSpeed = context.Config.wallClimbSpeed;
                float verticalInput = context.InputDirection.z;
                float horizontalInput = context.InputDirection.x;

                // 1. Raycast to find the current wall normal (Dynamic Surface Detection)
                // Cast from slightly above center to avoid ground issues, in the direction of the wall
                float3 rayOrigin = (float3)context.Transform.position + context.WorldUp * (context.Config.stepHeight + 0.1f);
                float3 rayDir = -_wallNormal; // Start by looking opposite to last known normal
                
                // If we have input, biases detection towards input direction slightly? 
                // No, sticking to known wall is safer.
                
                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, context.Config.wallCheckDistance + 0.5f, context.Config.wallLayer))
                {
                    _wallNormal = hit.normal;
                }
                
                // 2. Calculate Tangent Space Movement
                // We need a "Right" and "Up" vector relative to the wall surface
                
                // Safe Right: Cross WorldUp with Normal. 
                // If Normal is parallel to WorldUp (Ceiling/Floor), this fails.
                float3 wallRight = math.cross(context.WorldUp, _wallNormal);
                
                // Handle Ceiling/Floor case (Normal is vertical)
                if (math.lengthsq(wallRight) < 0.001f)
                {
                    // If normal is Up/Down, we use the character's forward as reference for "Up" on surface
                    // Actually, if on ceiling, "Up" input should move forward?
                    // For typical FPS/TPS, "Forward/Up" input maps to Character Forward projected on plane
                     wallRight = math.cross(context.Transform.forward, _wallNormal);
                }
                
                wallRight = math.normalize(wallRight);
                
                // wallUp is perpendicular to normal and right
                // Using Right Hand Rule (Unity uses Left Hand, but Cross behavior is standard)
                // Right x Normal = Up
                // Example: Right(1,0,0) x Normal(0,0,-1) = Up(0,1,0)
                float3 wallUp = math.cross(wallRight, _wallNormal);

                // 3. Calculate Displacement
                // Blend inputs
                float3 moveDir = (wallUp * verticalInput + wallRight * horizontalInput);
                if (math.lengthsq(moveDir) > 0.001f)
                {
                    moveDir = math.normalize(moveDir);
                }

                // 4. Adhesion (Keep sticking to wall)
                // Add a small force towards the wall to prevent flying off curved surfaces
                float3 adhesion = -_wallNormal * 0.5f; // stick force
                
                displacement = (moveDir * climbSpeed + adhesion) * context.DeltaTime;
                
                // Update speed for animation
                context.CurrentSpeed = math.length(new float2(horizontalInput, verticalInput)) * climbSpeed;
            }

            context.CurrentVelocity = displacement / math.max(context.DeltaTime, 0.0001f);
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
                
                int slideHash = AnimationParameterCache.GetHash(context.Config.wallSlidingParameter);
                if (slideHash != 0)
                {
                    context.AnimationController.SetBool(slideHash, false);
                }
            }
        }

        public override MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            // Wall jump: push away from wall using stored normal
            if (context.JumpPressed && context.Config.enableWallJump)
            {
                // Calculate jump direction based on wall normal (supports non-180° angles)
                float3 jumpDir = math.normalize(_wallNormal + context.WorldUp);
                
                // Store wall jump velocity in context for JumpState to use
                context.WallJumpDirection = new Vector3(
                    _wallNormal.x * context.Config.wallJumpForceHorizontal,
                    context.Config.wallJumpForceVertical,
                    _wallNormal.z * context.Config.wallJumpForceHorizontal
                );
                context.IsWallJumping = true;
                context.LastWallNormal = new Vector3(_wallNormal.x, _wallNormal.y, _wallNormal.z);
                context.LastWallJumpTime = Time.time;
                
                return StatePool<MovementStateBase>.GetState<JumpState>();
            }

            // Fall off wall if grounded (landed) or lost wall contact
            if (context.IsGrounded)
            {
                return StatePool<MovementStateBase>.GetState<IdleState>();
            }

            return null;
        }
    }
}
