using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;
using CycloneGames.RPGFoundation.Runtime.Movement.States;
using CycloneGames.Logger;
#if ANIMANCER_PRESENT
using Animancer;
#endif

namespace CycloneGames.RPGFoundation.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public class MovementComponent : MonoBehaviour, IMovementStateQuery3D
    {
        [SerializeField] private MovementConfig config;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;
        // World Up Source - displayed in Editor, no Header needed (single field)
        [Tooltip("Optional Transform to use as world up direction reference.\n" +
                 "If assigned, character will use this Transform's UP direction (Transform.up) as the world up.\n" +
                 "If null, uses Vector3.up (standard Unity world up).\n" +
                 "Use cases:\n" +
                 "- Characters on rotating/moving platforms: Assign the platform's Transform\n" +
                 "- Wall-walking: Create a Transform on the wall, rotate it so its UP points along the wall's normal (outward)\n" +
                 "- Ceiling-walking: Create a Transform on the ceiling, rotate it so its UP points downward\n" +
                 "- Space games with rotating space stations: Assign the station's Transform\n" +
                 "Important for wall-walking:\n" +
                 "  The Transform's UP direction must point along the wall's normal (perpendicular to wall surface).\n" +
                 "  You may need to rotate the Transform manually or use a helper script to align it with the wall normal.\n" +
                 "Note: WorldUp is updated every frame, so dynamic changes to worldUpSource are supported.")]
        [SerializeField] private Transform worldUpSource;

        [Tooltip("Enable root motion support. When enabled, animations with root motion will drive character movement.\n" +
                 "Note: This is a global setting. Individual states can override this via MovementContext.UseRootMotion.\n" +
                 "Use cases:\n" +
                 "- Attack animations with forward lunge\n" +
                 "- Dodge/roll animations\n" +
                 "- Special movement animations\n" +
                 "⚠️ Requires Animator component and animations with root motion enabled.")]
        [SerializeField] private bool useRootMotion = false;

        [Tooltip("Ignore global Time.timeScale. When enabled, this character will use Time.unscaledDeltaTime instead.\n" +
                 "Use cases:\n" +
                 "- UI characters that should animate during pause\n" +
                 "- Characters that need to move during slow-motion effects\n" +
                 "- Cutscene characters\n" +
                 "- Dynamic switching: Can be changed at runtime to toggle between affected/ignored time scale\n" +
                 "Note: LocalTimeScale still applies even when this is enabled.")]
        [SerializeField] private bool ignoreTimeScale = false;

#if UNITY_EDITOR
        [Tooltip("Show ground detection debug visualization in Scene view.\n" +
                 "When enabled, displays the SphereCast used for ground detection:\n" +
                 "- Green sphere: Ground detected within range\n" +
                 "- Red sphere: No ground detected or out of range\n" +
                 "- Yellow line: Ray direction\n" +
                 "- Blue sphere: Character bottom position\n" +
                 "Editor only - this field is automatically removed in builds.")]
        [SerializeField] private bool showGroundDetectionDebug = false;
#endif

        /// <summary>
        /// Whether this character ignores global Time.timeScale.
        /// Can be set at runtime to dynamically switch between affected/ignored time scale.
        /// </summary>
        public bool IgnoreTimeScale
        {
            get => ignoreTimeScale;
            set => ignoreTimeScale = value;
        }

        public float LocalTimeScale { get; set; } = 1f;
        public Vector3 WorldUp { get; set; } = Vector3.up;
        public IMovementAuthority MovementAuthority { get; set; }

        public event Action<MovementStateType, MovementStateType> OnStateChanged;
        public event Action OnLanded;
        public event Action OnJumpStart;

        private CharacterController _characterController;
        private MovementStateBase _currentState;
        private MovementContext _context;

        private float3 _lookDirection;
        private quaternion _currentRotation;
        private const float _minSqrMagnitudeForMovement = 0.0001f;
        private const float _groundedVerticalVelocity = -2f;

        private bool _isUsingAnimancer = false;
        private bool _isUsingHybridAnimancer = false;
        // Cache the Animator from HybridAnimancerComponent for root motion support
        private Animator _hybridAnimancerAnimator = null;
        private Animator _cachedTargetAnimator = null;
        private System.Collections.Generic.Dictionary<int, string> _cachedParameterMap = null;
        private bool _worldUpChanged = true;
        private static readonly float3 UnityUpVector = new float3(0, 1, 0);

        private float DeltaTime => (ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime) * LocalTimeScale;

        #region IMovementStateQuery Implementation
        public MovementStateType CurrentState => _currentState?.StateType ?? MovementStateType.Idle;
        public bool IsGrounded => _context.IsGrounded;
        public float CurrentSpeed => _context.CurrentSpeed;
        public Vector3 Velocity => _context.CurrentVelocity;
        public bool IsMoving => math.lengthsq(_context.CurrentVelocity) > _minSqrMagnitudeForMovement;
        #endregion

        void Awake()
        {
            _characterController = GetComponent<CharacterController>();

            IAnimationController animationController = null;

            // Priority: Animancer > Manually assigned Animator > Auto-found Animator
            if (animancerComponent != null)
            {
                _isUsingAnimancer = true;

#if ANIMANCER_PRESENT
                // Use direct type checking with zero overhead (compile-time optimization)
                // This supports inheritance: if user inherits HybridAnimancerComponent, 'is' check will work
                if (animancerComponent is HybridAnimancerComponent hybridAnimancer)
                {
                    _isUsingHybridAnimancer = true;
                    _hybridAnimancerAnimator = hybridAnimancer.Animator;

                    if (_hybridAnimancerAnimator != null)
                    {
                        // Use HybridAnimancerComponent's Animator as the characterAnimator for root motion
                        if (characterAnimator == null)
                        {
                            characterAnimator = _hybridAnimancerAnimator;
                        }
                        else if (characterAnimator != _hybridAnimancerAnimator)
                        {
                            CLogger.LogWarning(
                                "[MovementComponent] HybridAnimancerComponent and manually assigned Animator reference different components. " +
                                "HybridAnimancerComponent's Animator will be used for root motion. Consider removing the manual Animator assignment.");
                            characterAnimator = _hybridAnimancerAnimator;
                        }
                    }
                    else
                    {
                        CLogger.LogWarning(
                            "[MovementComponent] HybridAnimancerComponent does not have an internal Animator. " +
                            "Root motion will not work. Make sure the HybridAnimancerComponent has an Animator component assigned.");
                    }
                }
                else if (animancerComponent is AnimancerComponent regularAnimancer)
                {
                    // Regular AnimancerComponent (Parameters mode) - doesn't support root motion
                    // Validate Animancer's internal Animator if manual Animator is also assigned
                    if (characterAnimator != null)
                    {
                        // Check if regular AnimancerComponent has an Animator (some configurations might)
                        var animancerAnimator = regularAnimancer.Animator;

                        if (animancerAnimator != null && animancerAnimator != characterAnimator)
                        {
                            CLogger.LogWarning(
                                "[MovementComponent] AnimancerComponent and manually assigned Animator reference different components. " +
                                "Animancer will use its internal Animator. Consider removing the manual Animator assignment.");
                        }
                        else if (animancerAnimator == null)
                        {
                            CLogger.LogWarning(
                                "[MovementComponent] AnimancerComponent does not have an internal Animator. " +
                                "It will use Parameters mode instead of Animator mode. Root motion is not supported in Parameters mode.");
                        }
                    }
                }
#else
                // Fallback to reflection if Animancer is not available (backward compatibility)
                // This path is only used if ANIMANCER_PRESENT is not defined
                try
                {
                    var animancerType = animancerComponent.GetType();
                    var isHybridAnimancer = animancerType.Name == "HybridAnimancerComponent" ||
                                           animancerType.FullName == "Animancer.HybridAnimancerComponent" ||
                                           animancerType.BaseType?.Name == "HybridAnimancerComponent";

                    if (isHybridAnimancer)
                    {
                        _isUsingHybridAnimancer = true;

                        // Extract Animator from HybridAnimancerComponent using reflection
                        var animatorProperty = animancerType.GetProperty("Animator",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);

                        if (animatorProperty != null)
                        {
                            _hybridAnimancerAnimator = animatorProperty.GetValue(animancerComponent) as Animator;

                            if (_hybridAnimancerAnimator != null)
                            {
                                if (characterAnimator == null)
                                {
                                    characterAnimator = _hybridAnimancerAnimator;
                                }
                                else if (characterAnimator != _hybridAnimancerAnimator)
                                {
                                    CLogger.LogWarning(
                                        "[MovementComponent] HybridAnimancerComponent and manually assigned Animator reference different components. " +
                                        "HybridAnimancerComponent's Animator will be used for root motion. Consider removing the manual Animator assignment.");
                                    characterAnimator = _hybridAnimancerAnimator;
                                }
                            }
                        }
                    }
                }
                catch (System.Exception)
                {
                    CLogger.LogError(
                        "[MovementComponent] Failed to extract Animator from AnimancerComponent. " +
                        "Root motion may not work correctly.");
                }
#endif

                // Create parameter name mapping for Animancer Parameters mode
                _cachedParameterMap = CreateParameterNameMap();
                animationController = new AnimancerAnimationController(animancerComponent, _cachedParameterMap);

                // Warn if root motion is enabled with regular AnimancerComponent (not HybridAnimancerComponent)
                if (useRootMotion && !_isUsingHybridAnimancer)
                {
                    CLogger.LogWarning(
                        "[MovementComponent] Root motion is enabled but regular AnimancerComponent is being used. " +
                        "Root motion via OnAnimatorMove only works with Unity Animator or HybridAnimancerComponent, not AnimancerComponent Parameters mode. " +
                        "Consider using HybridAnimancerComponent with an Animator for root motion support, or disable root motion.");
                }
            }
            else
            {
                if (characterAnimator == null)
                {
                    characterAnimator = GetComponent<Animator>();
                }
                if (characterAnimator != null)
                {
                    animationController = new AnimatorAnimationController(characterAnimator);
                }
            }

            if (config == null)
            {
                CLogger.LogError("[MovementComponent] MovementConfig is not assigned. Creating default config.");
                config = ScriptableObject.CreateInstance<MovementConfig>();
            }

            PreWarmAnimationParameters();
            InitializeContext(animationController);
            _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
            _currentRotation = transform.rotation;

            // Cache target animator once during initialization
            CacheTargetAnimator();
        }

        /// <summary>
        /// Caches the target animator to avoid repeated checks every frame.
        /// </summary>
        private void CacheTargetAnimator()
        {
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                _cachedTargetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                _cachedTargetAnimator = characterAnimator;
            }
            else
            {
                _cachedTargetAnimator = null;
            }
        }

        private System.Collections.Generic.Dictionary<int, string> CreateParameterNameMap()
        {
            if (config == null) return new System.Collections.Generic.Dictionary<int, string>();

            var map = new System.Collections.Generic.Dictionary<int, string>();

            // Map parameter hashes to names for Animancer Parameters mode
            if (!string.IsNullOrEmpty(config.movementSpeedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.movementSpeedParameter);
                map[hash] = config.movementSpeedParameter;
            }

            if (!string.IsNullOrEmpty(config.isGroundedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                map[hash] = config.isGroundedParameter;
            }

            if (!string.IsNullOrEmpty(config.jumpTrigger))
            {
                int hash = AnimationParameterCache.GetHash(config.jumpTrigger);
                map[hash] = config.jumpTrigger;
            }

            if (config is MovementConfig config3D && !string.IsNullOrEmpty(config3D.rollTrigger))
            {
                int hash = AnimationParameterCache.GetHash(config3D.rollTrigger);
                map[hash] = config3D.rollTrigger;
            }

            return map;
        }

        private void PreWarmAnimationParameters()
        {
            if (config == null) return;

            AnimationParameterCache.PreWarm(
                config.movementSpeedParameter,
                config.isGroundedParameter,
                config.jumpTrigger
            );

            if (config is MovementConfig config3D && !string.IsNullOrEmpty(config3D.rollTrigger))
            {
                AnimationParameterCache.PreWarm(config3D.rollTrigger);
            }
        }

        void Start()
        {
            _characterController.minMoveDistance = 0f;

            // Initialize WorldUp from worldUpSource if available
            UpdateWorldUp();
        }

        /// <summary>
        /// Updates WorldUp from worldUpSource if assigned.
        /// Called in Start() and UpdateContext() to support dynamic world up changes.
        /// </summary>
        private void UpdateWorldUp()
        {
            // Only update if worldUpSource is assigned (cached check for performance)
            if (worldUpSource != null)
            {
                WorldUp = worldUpSource.up;
            }
        }

        void Update()
        {
            // Ensure _currentState is initialized (may be null if StatePool was cleared during scene transition)
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
            }

            UpdateContext();
            ExecuteStateMachine();
        }

        private void InitializeContext(IAnimationController animationController)
        {
            _context = new MovementContext
            {
                CharacterController = _characterController,
                AnimationController = animationController,
                Transform = transform,
                Config = config,
                WorldUp = WorldUp,
                VerticalVelocity = _groundedVerticalVelocity,
                UseRootMotion = useRootMotion,
                JumpCount = 0
            };
        }

        private void UpdateContext()
        {
            // Update WorldUp from worldUpSource if assigned (supports dynamic changes)
            Vector3 previousWorldUp = WorldUp;
            if (worldUpSource != null)
            {
                WorldUp = worldUpSource.up;
            }

            // Mark world up as changed if it actually changed
            _worldUpChanged = previousWorldUp != WorldUp;

            if (config == null)
            {
                CLogger.LogError("[MovementComponent] MovementConfig is null. Movement may not work correctly.");
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.IsGrounded = CheckGrounded();

            // Update root motion setting
            // States can override UseRootMotion in OnEnter/OnUpdate, but if not set, use component default
            // This allows per-state control while maintaining a global default
            // Note: Root motion works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode
            if (!_context.UseRootMotion && !useRootMotion)
            {
                // If both are false, keep it false (state explicitly disabled it)
            }
            else if (useRootMotion && !_isUsingAnimancer)
            {
                // If component has root motion enabled and we're using Animator (not Animancer), use context value
                // If state hasn't set it, it defaults to true when component enables it
                if (!_context.UseRootMotion && _currentState != null)
                {
                    // State hasn't explicitly set it, so use component default
                    _context.UseRootMotion = useRootMotion;
                }
            }
            else if (useRootMotion && _isUsingAnimancer)
            {
                // Check if we're using HybridAnimancerComponent (supports root motion)
                if (_isUsingHybridAnimancer)
                {
                    // HybridAnimancerComponent supports root motion
                    // If state hasn't set it, use component default
                    if (!_context.UseRootMotion && _currentState != null)
                    {
                        _context.UseRootMotion = useRootMotion;
                    }
                }
                else
                {
                    // Regular AnimancerComponent (Parameters mode) doesn't support root motion
                    // Disable it to prevent issues
                    _context.UseRootMotion = false;
                }
            }

            if (_context.IsGrounded && _context.VerticalVelocity < 0)
            {
                _context.VerticalVelocity = _groundedVerticalVelocity;
                // Reset jump count when grounded to allow fresh jumps
                _context.JumpCount = 0;
            }

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                _context.AnimationController.SetBool(hash, _context.IsGrounded);
            }

            // Update Animator root motion setting
            // Both component setting and context setting must be true for root motion to work
            // Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode
            // Use cached animator to avoid repeated checks
            if (_cachedTargetAnimator != null)
            {
                bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion;
                if (_cachedTargetAnimator.applyRootMotion != shouldUseRootMotion)
                {
                    _cachedTargetAnimator.applyRootMotion = shouldUseRootMotion;
                }
            }
        }

        private void ExecuteStateMachine()
        {
            // Safety check: reinitialize state if it was cleared (e.g., during scene transition)
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
                else
                {
                    CLogger.LogError("[MovementComponent] Failed to initialize state. Movement will not work.");
                    return;
                }
            }

            float3 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            // Apply root motion if enabled (handled in OnAnimatorMove)
            // Otherwise apply calculated displacement
            // Use cached animator to avoid repeated checks
            bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion &&
                                      _cachedTargetAnimator != null && _cachedTargetAnimator.applyRootMotion;

            if (!shouldUseRootMotion)
            {
                if (math.lengthsq(displacement) > _minSqrMagnitudeForMovement)
                {
                    _characterController.Move(displacement);
                }
            }
            // Note: When root motion is active, movement is handled in OnAnimatorMove

            // Snap to ground if grounded to prevent floating
            if (_context.IsGrounded)
            {
                SnapToGround();
            }

            MovementStateBase nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
            {
                RequestStateChangeInternal(nextState);
            }

            if (math.lengthsq(_context.CurrentVelocity) > _minSqrMagnitudeForMovement)
            {
                _lookDirection = math.normalize(_context.CurrentVelocity);
            }

            UpdateRotation();
        }

        /// <summary>
        /// Called by Unity when root motion is enabled and the Animator has processed an animation frame.
        /// This applies the root motion delta to the CharacterController.
        /// Only called when Animator.applyRootMotion is true.
        /// Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode.
        /// </summary>
        void OnAnimatorMove()
        {
            // Use cached animator to avoid repeated checks
            // Only apply root motion if:
            // 1. Root motion is enabled
            // 2. We have a valid Animator (Unity Animator or HybridAnimancerComponent)
            // 3. Animator has root motion applied
            if (!useRootMotion || !_context.UseRootMotion || _cachedTargetAnimator == null)
                return;

            if (!_cachedTargetAnimator.applyRootMotion)
                return;

            // Get root motion delta from Animator
            Vector3 rootMotionDelta = _cachedTargetAnimator.deltaPosition;
            Quaternion rootRotationDelta = _cachedTargetAnimator.deltaRotation;

            // Apply root motion movement
            // Note: deltaPosition already accounts for deltaTime, so we don't multiply again
            if (rootMotionDelta.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
                // Combine root motion with vertical velocity (gravity/falling)
                // Root motion typically only affects horizontal movement, so we add vertical separately
                Vector3 verticalMovement = WorldUp * _context.VerticalVelocity * DeltaTime;
                _characterController.Move(rootMotionDelta + verticalMovement);
            }

            // Apply root motion rotation (optional - you may want to control this differently)
            // Uncomment if you want root motion to control rotation:
            // transform.rotation *= rootRotationDelta;

            // Note: If you want root motion to only affect position but not rotation,
            // keep the rotation line commented out. The default UpdateRotation() will handle rotation.
        }

        /// <summary>
        /// Checks if the character is grounded using a combination of CharacterController.isGrounded
        /// and a custom raycast check for more accurate ground detection.
        /// Supports WorldUpSource for wall-walking and ceiling-walking scenarios.
        /// </summary>
        private bool CheckGrounded()
        {
            if (config == null) return false;

            // If CharacterController says we're grounded, trust it but do additional verification
            // This helps catch edge cases where CharacterController might give false positives
            if (_characterController.isGrounded)
            {
                // Do a quick verification to ensure we're really on the ground
                // If verification fails, still trust CharacterController (it's usually reliable)
                // But log a warning for debugging
                if (!VerifyGroundedWithRaycast())
                {
                    // CharacterController says grounded but raycast doesn't confirm
                    // This can happen if:
                    // 1. Character is on a slope that's within CharacterController's tolerance
                    // 2. LayerMask is not configured correctly
                    // 3. Character is very close to ground (raycast starts inside collider)
                    // In these cases, we trust CharacterController since it's generally reliable
                    return true;
                }
                return true;
            }

            // If CharacterController says we're not grounded, do a custom check
            // This helps catch cases where CharacterController might miss the ground
            // (e.g., when character is moving very slowly or just landed)
            return VerifyGroundedWithRaycast();
        }

        /// <summary>
        /// Verifies ground contact using SphereCast.
        /// Returns true if ground is detected within the configured distance and slope limit.
        /// 
        /// Note: groundedCheckDistance represents the maximum allowed distance from the character's
        /// bottom to the ground. The raycast starts slightly above the bottom (to avoid starting
        /// inside colliders) and checks downward for ground within this distance.
        /// 
        /// Important: If groundedCheckDistance is smaller than CharacterController's skinWidth,
        /// the effective threshold is adjusted to skinWidth to prevent detection failures.
        /// </summary>
        private bool VerifyGroundedWithRaycast()
        {
            if (config == null) return false;

            // Calculate the bottom of the CharacterController relative to WorldUp
            // Always recalculate since transform.position changes every frame
            // (WorldUp changes are tracked separately, but position always changes)
            Vector3 controllerCenter = transform.position + _characterController.center;
            Vector3 controllerBottom = controllerCenter - WorldUp * (_characterController.height * 0.5f);

            float sphereRadius = _characterController.radius * 0.9f;
            // Start the raycast slightly above the bottom to avoid starting inside colliders
            // This offset ensures the sphere cast doesn't start embedded in the ground
            float startOffset = sphereRadius + _characterController.skinWidth;
            Vector3 rayOrigin = controllerBottom + WorldUp * startOffset;
            Vector3 rayDirection = -WorldUp;

            // Use the larger of groundedCheckDistance or skinWidth as the effective threshold
            // This prevents detection failures when groundedCheckDistance < skinWidth
            // CharacterController maintains at least skinWidth distance from ground,
            // so we need to account for that in our detection threshold
            float effectiveGroundedCheckDistance = Mathf.Max(config.groundedCheckDistance, _characterController.skinWidth);

            // groundedCheckDistance represents the max distance from character bottom to ground
            // Since rayOrigin is startOffset above the bottom, we need to check:
            // - From rayOrigin down to (controllerBottom - groundedCheckDistance)
            // - Total distance = startOffset + groundedCheckDistance
            // However, we want to ensure the hit point is within groundedCheckDistance of the bottom
            // So we check a bit more to account for the sphere radius, but validate the actual distance
            float checkDistance = startOffset + effectiveGroundedCheckDistance + sphereRadius * 0.1f; // Small buffer for sphere cast

            if (Physics.SphereCast(rayOrigin, sphereRadius, rayDirection, out RaycastHit hit, checkDistance, config.groundLayer))
            {
                // Calculate the actual distance from character bottom to hit point
                float distanceFromBottom = Vector3.Dot(hit.point - controllerBottom, -WorldUp);

                // Only consider grounded if the hit point is within the effective distance from bottom
                // and the surface is within the slope limit
                // Use effectiveGroundedCheckDistance to handle cases where config value < skinWidth
                if (distanceFromBottom >= 0 && distanceFromBottom <= effectiveGroundedCheckDistance)
                {
                    // Check if the hit surface is roughly aligned with WorldUp (within slope limit)
                    float angle = Vector3.Angle(hit.normal, WorldUp);
                    if (angle <= config.slopeLimit)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Snaps the character to the ground surface when grounded to prevent floating.
        /// This ensures the character stays close to the ground even if there's a small gap.
        /// Uses a simple Raycast from the character bottom to find the ground and snap to it.
        /// 
        /// Important: Uses effective threshold (max of groundedCheckDistance and skinWidth) to handle
        /// cases where groundedCheckDistance < skinWidth.
        /// </summary>
        private void SnapToGround()
        {
            if (config == null) return;

            Vector3 controllerCenter = transform.position + _characterController.center;
            Vector3 controllerBottom = controllerCenter - WorldUp * (_characterController.height * 0.5f);

            // Use a simple Raycast from the bottom to find ground
            // Start slightly above the bottom to avoid starting inside colliders
            float rayStartOffset = _characterController.skinWidth + 0.01f;
            Vector3 rayOrigin = controllerBottom + WorldUp * rayStartOffset;
            Vector3 rayDirection = -WorldUp;

            // Use effective threshold to handle cases where groundedCheckDistance < skinWidth
            float effectiveGroundedCheckDistance = Mathf.Max(config.groundedCheckDistance, _characterController.skinWidth);

            // Check distance should be slightly more than effectiveGroundedCheckDistance to account for the start offset
            float checkDistance = rayStartOffset + effectiveGroundedCheckDistance + 0.1f;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, checkDistance, config.groundLayer))
            {
                // Calculate the actual distance from character bottom to hit point
                float distanceFromBottom = Vector3.Dot(hit.point - controllerBottom, -WorldUp);

                // Only snap if the hit point is within the effective distance and valid slope
                if (distanceFromBottom >= 0 && distanceFromBottom <= effectiveGroundedCheckDistance)
                {
                    float angle = Vector3.Angle(hit.normal, WorldUp);
                    if (angle <= config.slopeLimit)
                    {
                        // If there's a gap, snap the character down to the ground
                        // Only snap if the distance is significant enough to avoid micro-movements
                        if (distanceFromBottom > 0.001f)
                        {
                            // Move the character down by the distance to ground
                            // CharacterController.Move will handle collision properly
                            Vector3 snapMovement = -WorldUp * distanceFromBottom;
                            _characterController.Move(snapMovement);
                        }
                    }
                }
            }
        }

        private void UpdateRotation()
        {
            if (config == null) return;

            quaternion targetRotation;

            if (math.lengthsq(_lookDirection) > _minSqrMagnitudeForMovement)
            {
                targetRotation = quaternion.LookRotation(_lookDirection, _context.WorldUp);
            }
            else
            {
                float3 currentUp = math.mul(_currentRotation, UnityUpVector);
                float3 worldUp = _context.WorldUp;

                if (math.lengthsq(currentUp - worldUp) > 0.001f)
                {
                    UnityEngine.Quaternion unityToUp = UnityEngine.Quaternion.FromToRotation(currentUp, worldUp);
                    quaternion toUp = unityToUp;
                    targetRotation = math.mul(toUp, _currentRotation);
                }
                else
                {
                    targetRotation = _currentRotation;
                }
            }

            _currentRotation = math.slerp(_currentRotation, targetRotation, config.rotationSpeed * DeltaTime);
            transform.rotation = _currentRotation;
        }

        public void SetInputDirection(Vector3 worldDirection)
        {
            _context.InputDirection = worldDirection;
        }

        public void SetJumpPressed(bool pressed)
        {
            if (pressed && !_context.JumpPressed && _context.IsGrounded)
            {
                RequestStateChange(MovementStateType.Jump);
            }
            _context.JumpPressed = pressed;
        }

        public void SetSprintHeld(bool held)
        {
            _context.SprintHeld = held;
        }

        public void SetCrouchHeld(bool held)
        {
            _context.CrouchHeld = held;
        }

        public bool RequestStateChange(MovementStateType targetStateType, object context = null)
        {
            if (MovementAuthority != null && !MovementAuthority.CanEnterState(targetStateType, context))
            {
                return false;
            }

            MovementStateBase targetState = GetStateByType(targetStateType);
            if (targetState == null)
            {
                CLogger.LogWarning($"[MovementComponent] State {targetStateType} not found.");
                return false;
            }

            RequestStateChangeInternal(targetState);
            return true;
        }

        private void RequestStateChangeInternal(MovementStateBase newState)
        {
            if (newState == _currentState) return;

            MovementStateType oldStateType = _currentState?.StateType ?? MovementStateType.Idle;
            MovementStateType newStateType = newState.StateType;

            _currentState?.OnExit(ref _context);

            MovementAuthority?.OnStateExited(oldStateType);

            _currentState = newState;
            _currentState.OnEnter(ref _context);

            MovementAuthority?.OnStateEntered(newStateType);

            OnStateChanged?.Invoke(oldStateType, newStateType);

            if (newStateType == MovementStateType.Jump)
            {
                OnJumpStart?.Invoke();
            }
            else if (oldStateType == MovementStateType.Fall && _context.IsGrounded)
            {
                OnLanded?.Invoke();
            }
        }

        private MovementStateBase GetStateByType(MovementStateType stateType)
        {
            switch (stateType)
            {
                case MovementStateType.Idle: return StatePool<MovementStateBase>.GetState<IdleState>();
                case MovementStateType.Walk: return StatePool<MovementStateBase>.GetState<WalkState>();
                case MovementStateType.Run: return StatePool<MovementStateBase>.GetState<RunState>();
                case MovementStateType.Sprint: return StatePool<MovementStateBase>.GetState<SprintState>();
                case MovementStateType.Crouch: return StatePool<MovementStateBase>.GetState<CrouchState>();
                case MovementStateType.Jump: return StatePool<MovementStateBase>.GetState<JumpState>();
                case MovementStateType.Fall: return StatePool<MovementStateBase>.GetState<FallState>();
                default:
                    CLogger.LogWarning($"[MovementComponent] State {stateType} not implemented yet.");
                    return null;
            }
        }

        /// <summary>
        /// Move the character with a specific velocity. Useful for external control (e.g., cutscenes, AI).
        /// When root motion is enabled, this will also set the animation speed parameter.
        /// Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode.
        /// </summary>
        public void MoveWithVelocity(Vector3 worldVelocity)
        {
            if (config == null) return;

            SetInputDirection(worldVelocity.normalized);

            Animator targetAnimator = null;
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                targetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                targetAnimator = characterAnimator;
            }

            if (useRootMotion && targetAnimator != null)
            {
                // Enable root motion for this movement
                _context.UseRootMotion = true;
                targetAnimator.applyRootMotion = true;

                float speed = worldVelocity.magnitude;
                if (_context.AnimationController != null && _context.AnimationController.IsValid)
                {
                    int hash = AnimationParameterCache.GetHash(config.movementSpeedParameter);
                    _context.AnimationController.SetFloat(hash, speed);
                }
            }
        }

        /// <summary>
        /// Enable or disable root motion at runtime. Useful for switching between root motion and scripted movement.
        /// Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode.
        /// </summary>
        public void SetUseRootMotion(bool enable)
        {
            useRootMotion = enable;
            _context.UseRootMotion = enable;

            Animator targetAnimator = null;
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                targetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                targetAnimator = characterAnimator;
            }

            if (targetAnimator != null)
            {
                targetAnimator.applyRootMotion = enable;
            }
            else if (enable && _isUsingAnimancer && !_isUsingHybridAnimancer)
            {
                CLogger.LogWarning(
                    "[MovementComponent] Cannot enable root motion when using AnimancerComponent Parameters mode. " +
                    "Root motion requires Unity Animator or HybridAnimancerComponent. Consider using HybridAnimancerComponent with an Animator.");
            }
        }

        void OnDestroy()
        {
            StatePool<MovementStateBase>.Clear();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draws debug visualization for ground detection in Scene view.
        /// Shows the SphereCast used for ground detection with color coding:
        /// - Green: Ground detected within range
        /// - Red: No ground detected or out of range
        /// </summary>
        void OnDrawGizmos()
        {
            if (!showGroundDetectionDebug || config == null) return;
            if (_characterController == null) _characterController = GetComponent<CharacterController>();
            if (_characterController == null) return;

            // Get WorldUp - use worldUpSource if available, otherwise use current WorldUp or default
            Vector3 currentWorldUp = WorldUp;
            if (worldUpSource != null)
            {
                currentWorldUp = worldUpSource.up;
            }
            else if (currentWorldUp == Vector3.zero)
            {
                currentWorldUp = Vector3.up;
            }

            // Calculate the same values used in VerifyGroundedWithRaycast
            Vector3 controllerCenter = transform.position + _characterController.center;
            Vector3 controllerBottom = controllerCenter - currentWorldUp * (_characterController.height * 0.5f);

            float sphereRadius = _characterController.radius * 0.9f;
            float startOffset = sphereRadius + _characterController.skinWidth;
            Vector3 rayOrigin = controllerBottom + currentWorldUp * startOffset;
            Vector3 rayDirection = -currentWorldUp;

            float checkDistance = startOffset + config.groundedCheckDistance + sphereRadius * 0.1f;

            // Draw character bottom position (blue sphere)
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(controllerBottom, 0.05f);

            // Draw ray origin (small yellow sphere)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rayOrigin, 0.03f);

            // Draw ray direction line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(rayOrigin, rayOrigin + rayDirection * checkDistance);

            // Perform the actual SphereCast to determine if ground is detected
            bool isGrounded = false;
            float distanceFromBottom = float.MaxValue;
            float hitDistance = 0f;
            RaycastHit debugHit = default;
            if (Physics.SphereCast(rayOrigin, sphereRadius, rayDirection, out RaycastHit hit, checkDistance, config.groundLayer))
            {
                debugHit = hit;
                hitDistance = hit.distance;
                distanceFromBottom = Vector3.Dot(hit.point - controllerBottom, -currentWorldUp);
                if (distanceFromBottom >= 0 && distanceFromBottom <= config.groundedCheckDistance)
                {
                    float angle = Vector3.Angle(hit.normal, currentWorldUp);
                    if (angle <= config.slopeLimit)
                    {
                        isGrounded = true;
                    }
                }
            }

            // Draw sphere cast visualization
            // Color: Green if grounded, Red if not
            Gizmos.color = isGrounded ? Color.green : Color.red;

            // Draw the sphere at the start position
            Gizmos.DrawWireSphere(rayOrigin, sphereRadius);

            // Draw the sphere at the end position (or hit position if grounded)
            if (isGrounded && distanceFromBottom < float.MaxValue && hitDistance > 0)
            {
                // hitDistance is the distance from rayOrigin to where the sphere center touches the surface
                Vector3 hitSphereCenter = rayOrigin + rayDirection * hitDistance;
                Gizmos.DrawWireSphere(hitSphereCenter, sphereRadius);

                // Draw line connecting start and end spheres
                Gizmos.DrawLine(rayOrigin, hitSphereCenter);

                // Draw hit point on ground surface
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(debugHit.point, 0.05f);

                // Draw normal at hit point
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(debugHit.point, debugHit.point + debugHit.normal * 0.2f);
            }
            else
            {
                // Draw sphere at max check distance
                Vector3 endPosition = rayOrigin + rayDirection * checkDistance;
                Gizmos.DrawWireSphere(endPosition, sphereRadius);
                Gizmos.DrawLine(rayOrigin, endPosition);
            }

            // Draw the grounded check distance range
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f); // Cyan with transparency
            Vector3 rangeStart = controllerBottom;
            Vector3 rangeEnd = controllerBottom - currentWorldUp * config.groundedCheckDistance;
            Gizmos.DrawLine(rangeStart, rangeEnd);
            Gizmos.DrawWireSphere(rangeStart, 0.02f);
            Gizmos.DrawWireSphere(rangeEnd, 0.02f);
        }
#endif
    }
}