using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;
using CycloneGames.RPGFoundation.Runtime.Movement.States;
using CycloneGames.Logger;
#if ANIMANCER_PRESENT
using Animancer;
#endif
#if GAMEPLAY_FRAMEWORK_PRESENT
using CycloneGames.GameplayFramework.Runtime;
#endif

namespace CycloneGames.RPGFoundation.Runtime
{
    [RequireComponent(typeof(CharacterController))]
    public class MovementComponent : MonoBehaviour, IMovementStateQuery3D
#if GAMEPLAY_FRAMEWORK_PRESENT
        , IInitialRotationSettable
#endif
    {
        [SerializeField] private MovementConfig config;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;
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
        private Vector3 _previousWorldUp;

        private bool _isUsingAnimancer = false;
        private bool _isUsingHybridAnimancer = false;
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

            if (animancerComponent != null)
            {
                _isUsingAnimancer = true;

#if ANIMANCER_PRESENT
                if (animancerComponent is HybridAnimancerComponent hybridAnimancer)
                {
                    _isUsingHybridAnimancer = true;
                    _hybridAnimancerAnimator = hybridAnimancer.Animator;

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
                    else
                    {
                        CLogger.LogWarning(
                            "[MovementComponent] HybridAnimancerComponent does not have an internal Animator. " +
                            "Root motion will not work. Make sure the HybridAnimancerComponent has an Animator component assigned.");
                    }
                }
                else if (animancerComponent is AnimancerComponent regularAnimancer)
                {
                    if (characterAnimator != null)
                    {
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
                try
                {
                    var animancerType = animancerComponent.GetType();
                    var isHybridAnimancer = animancerType.Name == "HybridAnimancerComponent" ||
                                           animancerType.FullName == "Animancer.HybridAnimancerComponent" ||
                                           animancerType.BaseType?.Name == "HybridAnimancerComponent";

                    if (isHybridAnimancer)
                    {
                        _isUsingHybridAnimancer = true;

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

            CacheTargetAnimator();
        }

        /// <summary>
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

            UpdateWorldUp();
            
            // Initialize previous WorldUp for change detection
            _previousWorldUp = WorldUp;
        }

        /// <summary>
        /// Updates WorldUp from worldUpSource if assigned.
        /// Called in Start() and UpdateContext() to support dynamic world up changes.
        /// </summary>
        private void UpdateWorldUp()
        {
            if (worldUpSource != null)
            {
                WorldUp = worldUpSource.up;
            }
        }

        void Update()
        {
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
            
            // Initialize previous WorldUp for change detection
            _previousWorldUp = WorldUp;
        }

        private void UpdateContext()
        {
            // Update WorldUp from worldUpSource if assigned (supports dynamic changes)
            Vector3 previousWorldUp = WorldUp;
            if (worldUpSource != null)
            {
                WorldUp = worldUpSource.up;
            }

            _worldUpChanged = previousWorldUp != WorldUp;

            if (config == null)
            {
                CLogger.LogError("[MovementComponent] MovementConfig is null. Movement may not work correctly.");
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.IsGrounded = CheckGrounded();

            if (!_context.UseRootMotion && !useRootMotion)
            {
            }
            else if (useRootMotion && !_isUsingAnimancer)
            {
                if (!_context.UseRootMotion && _currentState != null)
                {
                    _context.UseRootMotion = useRootMotion;
                }
            }
            else if (useRootMotion && _isUsingAnimancer)
            {
                if (_isUsingHybridAnimancer)
                {
                    if (!_context.UseRootMotion && _currentState != null)
                    {
                        _context.UseRootMotion = useRootMotion;
                    }
                }
                else
                {
                    _context.UseRootMotion = false;
                }
            }

            // Project previous vertical velocity onto new WorldUp when it changes in air
            if (_worldUpChanged && !_context.IsGrounded)
            {
                Vector3 previousVerticalDirection = _previousWorldUp * _context.VerticalVelocity;
                float projectedVelocity = Vector3.Dot(previousVerticalDirection, WorldUp);
                _context.VerticalVelocity = projectedVelocity;
            }
            
            if (_context.IsGrounded && _context.VerticalVelocity < 0)
            {
                _context.VerticalVelocity = _groundedVerticalVelocity;
                _context.JumpCount = 0;
                _context.JumpPressed = false;
            }
            
            _previousWorldUp = WorldUp;

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                _context.AnimationController.SetBool(hash, _context.IsGrounded);
            }

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

            bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion &&
                                      _cachedTargetAnimator != null && _cachedTargetAnimator.applyRootMotion;

            if (!shouldUseRootMotion)
            {
                if (math.lengthsq(displacement) > _minSqrMagnitudeForMovement)
                {
                    _characterController.Move(displacement);
                }
            }

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

            if (math.lengthsq(_lookDirection) > _minSqrMagnitudeForMovement)
            {
                UpdateRotation();
            }
            else
            {
                UpdateRotationForWorldUp();
            }
        }

        /// <summary>
        /// Called by Unity when root motion is enabled and the Animator has processed an animation frame.
        /// This applies the root motion delta to the CharacterController.
        /// Only called when Animator.applyRootMotion is true.
        /// Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode.
        /// </summary>
        void OnAnimatorMove()
        {
            if (!useRootMotion || !_context.UseRootMotion || _cachedTargetAnimator == null)
                return;

            if (!_cachedTargetAnimator.applyRootMotion)
                return;

            Vector3 rootMotionDelta = _cachedTargetAnimator.deltaPosition;

            if (rootMotionDelta.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
                Vector3 verticalMovement = WorldUp * _context.VerticalVelocity * DeltaTime;
                _characterController.Move(rootMotionDelta + verticalMovement);
            }
        }

        /// <summary>
        /// Checks if the character is grounded using a combination of CharacterController.isGrounded
        /// and a custom raycast check for more accurate ground detection.
        /// Supports WorldUpSource for wall-walking and ceiling-walking scenarios.
        /// </summary>
        private bool CheckGrounded()
        {
            if (config == null) return false;

            if (_characterController.isGrounded)
            {
                return true;
            }

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

            Vector3 controllerCenter = transform.position + _characterController.center;
            Vector3 controllerBottom = controllerCenter - WorldUp * (_characterController.height * 0.5f);

            float sphereRadius = _characterController.radius * 0.9f;
            float startOffset = sphereRadius + _characterController.skinWidth;
            Vector3 rayOrigin = controllerBottom + WorldUp * startOffset;
            Vector3 rayDirection = -WorldUp;

            float effectiveGroundedCheckDistance = Mathf.Max(config.groundedCheckDistance, _characterController.skinWidth);
            float checkDistance = startOffset + effectiveGroundedCheckDistance + sphereRadius * 0.1f;

            if (Physics.SphereCast(rayOrigin, sphereRadius, rayDirection, out RaycastHit hit, checkDistance, config.groundLayer))
            {
                float distanceFromBottom = Vector3.Dot(hit.point - controllerBottom, -WorldUp);

                if (distanceFromBottom >= 0 && distanceFromBottom <= effectiveGroundedCheckDistance)
                {
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

            float rayStartOffset = _characterController.skinWidth + 0.01f;
            Vector3 rayOrigin = controllerBottom + WorldUp * rayStartOffset;
            Vector3 rayDirection = -WorldUp;

            float effectiveGroundedCheckDistance = Mathf.Max(config.groundedCheckDistance, _characterController.skinWidth);
            float checkDistance = rayStartOffset + effectiveGroundedCheckDistance + 0.1f;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, checkDistance, config.groundLayer))
            {
                float distanceFromBottom = Vector3.Dot(hit.point - controllerBottom, -WorldUp);

                if (distanceFromBottom >= 0 && distanceFromBottom <= effectiveGroundedCheckDistance)
                {
                    float angle = Vector3.Angle(hit.normal, WorldUp);
                    if (angle <= config.slopeLimit && distanceFromBottom > 0.001f)
                    {
                        Vector3 snapMovement = -WorldUp * distanceFromBottom;
                        _characterController.Move(snapMovement);
                    }
                }
            }
        }

        private void UpdateRotation()
        {
            if (config == null) return;

            quaternion targetRotation = quaternion.LookRotation(_lookDirection, _context.WorldUp);
            
            float dot = math.dot(_currentRotation.value, targetRotation.value);
            float angleRad = math.acos(math.clamp(math.abs(dot), 0f, 1f)) * 2f;
            float angleDeg = math.degrees(angleRad);
            
            if (angleDeg < 0.5f)
            {
                _currentRotation = targetRotation;
                transform.rotation = _currentRotation;
                return;
            }
            
            float t = config.rotationSpeed * DeltaTime;
            
            // Reduce rotation speed for large angles (>135°) to prevent instant flip during 180° turns
            if (angleDeg > 135f)
            {
                t = t * 0.7f;
            }
            
            _currentRotation = math.slerp(_currentRotation, targetRotation, t);
            transform.rotation = _currentRotation;
        }

        /// <summary>
        /// Updates rotation only to align with WorldUp (for wall/ceiling walking).
        /// Preserves forward direction while adjusting up direction to match WorldUp.
        /// This ensures smooth transitions when WorldUp changes dynamically.
        /// </summary>
        private void UpdateRotationForWorldUp()
        {
            if (config == null) return;

            float3 currentUp = math.mul(_currentRotation, UnityUpVector);
            float3 worldUp = _context.WorldUp;

            if (math.lengthsq(currentUp - worldUp) > 0.001f)
            {
                UnityEngine.Quaternion unityToUp = UnityEngine.Quaternion.FromToRotation(currentUp, worldUp);
                quaternion toUp = unityToUp;
                quaternion targetRotation = math.mul(toUp, _currentRotation);
                _currentRotation = math.slerp(_currentRotation, targetRotation, config.rotationSpeed * DeltaTime);
                transform.rotation = _currentRotation;
            }
        }

        /// <summary>
        /// Sets the input direction in local space (relative to character's forward/right).
        /// The direction will be automatically converted to world space in movement states,
        /// ensuring movement is relative to the character's orientation, not world axes.
        /// This supports WorldUp changes (e.g., standing on walls/ceilings).
        /// </summary>
        /// <param name="localDirection">Input direction in local space (x = right, z = forward, y = up/down)</param>
        public void SetInputDirection(Vector3 localDirection)
        {
            _context.InputDirection = localDirection;
        }

        public void SetJumpPressed(bool pressed)
        {
            bool wasPressed = _context.JumpPressed;
            _context.JumpPressed = pressed;
            
            // Trigger jump on rising edge when grounded, if within jump count limit
            if (pressed && !wasPressed && _context.IsGrounded)
            {
                if (_context.Config != null && _context.JumpCount < _context.Config.maxJumpCount)
                {
                    RequestStateChange(MovementStateType.Jump);
                }
            }
            // Multi-jump is handled in JumpState.EvaluateTransition and FallState.EvaluateTransition
        }

        public void SetSprintHeld(bool held)
        {
            _context.SprintHeld = held;
        }

        public void SetCrouchHeld(bool held)
        {
            _context.CrouchHeld = held;
        }

        /// <summary>
        /// Sets the look direction for the character. The character will rotate to face this direction.
        /// Movement and rotation are decoupled - this controls only rotation, not movement direction.
        /// </summary>
        /// <param name="worldDirection">The world space direction to look at (will be normalized)</param>
        public void SetLookDirection(Vector3 worldDirection)
        {
            if (math.lengthsq(worldDirection) > _minSqrMagnitudeForMovement)
            {
                _lookDirection = math.normalize(worldDirection);
            }
            else
            {
                _lookDirection = float3.zero;
            }
        }

        /// <summary>
        /// Clears the look direction, stopping automatic rotation.
        /// Character will only rotate to align with WorldUp if needed (e.g., wall/ceiling walking).
        /// </summary>
        public void ClearLookDirection()
        {
            _lookDirection = float3.zero;
        }

        /// <summary>
        /// Sets the character's rotation immediately or as a target rotation.
        /// </summary>
        /// <param name="rotation">The target rotation</param>
        /// <param name="immediate">If true, sets rotation immediately. If false, sets as target for smooth rotation.</param>
        public void SetRotation(Quaternion rotation, bool immediate = false)
        {
            if (immediate)
            {
                _currentRotation = rotation;
                transform.rotation = rotation;
            }
            else
            {
                Vector3 forward = rotation * Vector3.forward;
                SetLookDirection(forward);
            }
        }

        /// <summary>
        /// Sets the character's rotation to match a world space direction immediately or as a target.
        /// </summary>
        /// <param name="worldDirection">The world space direction to face (will be normalized)</param>
        /// <param name="immediate">If true, sets rotation immediately. If false, sets as target for smooth rotation.</param>
        public void SetRotation(Vector3 worldDirection, bool immediate = false)
        {
            if (math.lengthsq(worldDirection) <= _minSqrMagnitudeForMovement)
            {
                CLogger.LogWarning("[MovementComponent] Cannot set rotation with zero direction vector.");
                return;
            }

            Vector3 normalizedDirection = worldDirection.normalized;
            Vector3 up = WorldUp;
            Quaternion targetRotation = Quaternion.LookRotation(normalizedDirection, up);

            if (immediate)
            {
                _currentRotation = targetRotation;
                transform.rotation = targetRotation;
            }
            else
            {
                SetLookDirection(normalizedDirection);
            }
        }

#if GAMEPLAY_FRAMEWORK_PRESENT
        /// <summary>
        /// Implementation of IInitialRotationSettable interface.
        /// Called by GameplayFramework when a Pawn is spawned to synchronize initial rotation.
        /// 
        /// IMPORTANT: This implementation is only available when GAMEPLAY_FRAMEWORK_PRESENT is defined.
        /// - If RPGFoundation is installed via Package Manager and GameplayFramework is present, 
        ///   the define symbol is automatically set via versionDefines in asmdef.
        /// - If RPGFoundation is placed directly in Assets folder (not as Package), 
        ///   you must manually set GAMEPLAY_FRAMEWORK_PRESENT in PlayerSettings > Scripting Define Symbols,
        ///   otherwise you will need to manually set the Pawn's rotation after spawning.
        /// </summary>
        void IInitialRotationSettable.SetInitialRotation(Quaternion rotation, bool immediate)
        {
            SetRotation(rotation, immediate);
        }
#endif

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
                // Note: We do NOT reset JumpPressed here to allow multi-jump (air jump)
                // JumpPressed is consumed in JumpState.EvaluateTransition when performing multi-jump
                // It will be reset when landing (in UpdateContext when grounded)
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