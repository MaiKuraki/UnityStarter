using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;
using CycloneGames.RPGFoundation.Runtime.Movement.States;

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

        // Cache whether we're using Animancer (which doesn't support root motion via OnAnimatorMove)
        private bool _isUsingAnimancer = false;
        // Cache whether we're using HybridAnimancerComponent (which DOES support root motion)
        private bool _isUsingHybridAnimancer = false;
        // Cache the Animator from HybridAnimancerComponent for root motion support
        private Animator _hybridAnimancerAnimator = null;

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

                // Check if this is a HybridAnimancerComponent (which supports root motion)
                try
                {
                    var animancerType = animancerComponent.GetType();
                    var isHybridAnimancer = animancerType.Name == "HybridAnimancerComponent" ||
                                           animancerType.FullName == "Animancer.HybridAnimancerComponent";

                    if (isHybridAnimancer)
                    {
                        _isUsingHybridAnimancer = true;

                        // Extract Animator from HybridAnimancerComponent
                        var animatorProperty = animancerType.GetProperty("Animator",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (animatorProperty != null)
                        {
                            _hybridAnimancerAnimator = animatorProperty.GetValue(animancerComponent) as Animator;

                            if (_hybridAnimancerAnimator != null)
                            {
                                // Use HybridAnimancerComponent's Animator as the characterAnimator for root motion
                                if (characterAnimator == null)
                                {
                                    characterAnimator = _hybridAnimancerAnimator;
                                }
                                else if (characterAnimator != _hybridAnimancerAnimator)
                                {
                                    Debug.LogWarning(
                                        $"[MovementComponent] HybridAnimancerComponent and manually assigned Animator reference different components on {gameObject.name}. " +
                                        $"HybridAnimancerComponent's Animator: {_hybridAnimancerAnimator.name}, Manual Animator: {characterAnimator.name}. " +
                                        "HybridAnimancerComponent's Animator will be used for root motion. Consider removing the manual Animator assignment.",
                                        this);
                                    characterAnimator = _hybridAnimancerAnimator;
                                }
                            }
                            else
                            {
                                Debug.LogWarning(
                                    $"[MovementComponent] HybridAnimancerComponent on {gameObject.name} does not have an internal Animator. " +
                                    "Root motion will not work. Make sure the HybridAnimancerComponent has an Animator component assigned.",
                                    this);
                            }
                        }
                    }
                    else
                    {
                        // Regular AnimancerComponent (Parameters mode) - doesn't support root motion
                        // Validate Animancer's internal Animator if manual Animator is also assigned
                        if (characterAnimator != null)
                        {
                            // Try to extract Animator from Animancer to verify consistency
                            var animatorProperty = animancerType.GetProperty("Animator",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                            if (animatorProperty != null)
                            {
                                var animancerAnimator = animatorProperty.GetValue(animancerComponent) as Animator;

                                if (animancerAnimator != null && animancerAnimator != characterAnimator)
                                {
                                    Debug.LogWarning(
                                        $"[MovementComponent] AnimancerComponent and manually assigned Animator reference different components on {gameObject.name}. " +
                                        $"Animancer's Animator: {animancerAnimator.name}, Manual Animator: {characterAnimator.name}. " +
                                        "Animancer will use its internal Animator. Consider removing the manual Animator assignment.",
                                        this);
                                }
                                else if (animancerAnimator == null)
                                {
                                    Debug.LogWarning(
                                        $"[MovementComponent] AnimancerComponent on {gameObject.name} does not have an internal Animator. " +
                                        "It will use Parameters mode instead of Animator mode. Root motion is not supported in Parameters mode.",
                                        this);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(
                        $"[MovementComponent] Failed to extract Animator from AnimancerComponent on {gameObject.name}: {ex.Message}. " +
                        "Root motion may not work correctly.",
                        this);
                }

                // Create parameter name mapping for Animancer Parameters mode
                var parameterMap = CreateParameterNameMap();
                animationController = new AnimancerAnimationController(animancerComponent, parameterMap);

                // Warn if root motion is enabled with regular AnimancerComponent (not HybridAnimancerComponent)
                if (useRootMotion && !_isUsingHybridAnimancer)
                {
                    Debug.LogWarning(
                        $"[MovementComponent] Root motion is enabled but regular AnimancerComponent is being used on {gameObject.name}. " +
                        "Root motion via OnAnimatorMove only works with Unity Animator or HybridAnimancerComponent, not AnimancerComponent Parameters mode. " +
                        "Consider using HybridAnimancerComponent with an Animator for root motion support, or disable root motion.",
                        this);
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
                Debug.LogError($"[MovementComponent] MovementConfig is not assigned on {gameObject.name}. Creating default config.", this);
                config = ScriptableObject.CreateInstance<MovementConfig>();
            }

            PreWarmAnimationParameters();
            InitializeContext(animationController);
            _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
            _currentRotation = transform.rotation;
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
                UseRootMotion = useRootMotion
            };
        }

        private void UpdateContext()
        {
            // Update WorldUp from worldUpSource if assigned (supports dynamic changes)
            if (worldUpSource != null)
            {
                WorldUp = worldUpSource.up;
            }

            if (config == null)
            {
                Debug.LogError($"[MovementComponent] MovementConfig is null on {gameObject.name}. Movement may not work correctly.", this);
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.IsGrounded = _characterController.isGrounded;

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
            }

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                _context.AnimationController.SetBool(hash, _context.IsGrounded);
            }

            // Update Animator root motion setting
            // Both component setting and context setting must be true for root motion to work
            // Works with Unity Animator and HybridAnimancerComponent, but not AnimancerComponent Parameters mode
            Animator targetAnimator = null;

            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                // Use HybridAnimancerComponent's Animator
                targetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                // Use regular Unity Animator
                targetAnimator = characterAnimator;
            }

            if (targetAnimator != null)
            {
                bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion;
                if (targetAnimator.applyRootMotion != shouldUseRootMotion)
                {
                    targetAnimator.applyRootMotion = shouldUseRootMotion;
                }
            }
        }

        private void ExecuteStateMachine()
        {
            float3 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            // Apply root motion if enabled (handled in OnAnimatorMove)
            // Otherwise apply calculated displacement
            Animator targetAnimator = null;
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                targetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                targetAnimator = characterAnimator;
            }

            bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion &&
                                      targetAnimator != null && targetAnimator.applyRootMotion;

            if (!shouldUseRootMotion)
            {
                if (math.lengthsq(displacement) > _minSqrMagnitudeForMovement)
                {
                    _characterController.Move(displacement);
                }
            }
            // Note: When root motion is active, movement is handled in OnAnimatorMove

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
            // Determine which Animator to use for root motion
            Animator targetAnimator = null;

            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
            {
                // Use HybridAnimancerComponent's Animator
                targetAnimator = _hybridAnimancerAnimator;
            }
            else if (characterAnimator != null && !_isUsingAnimancer)
            {
                // Use regular Unity Animator
                targetAnimator = characterAnimator;
            }

            // Only apply root motion if:
            // 1. Root motion is enabled
            // 2. We have a valid Animator (Unity Animator or HybridAnimancerComponent)
            // 3. Animator has root motion applied
            if (!useRootMotion || !_context.UseRootMotion || targetAnimator == null)
                return;

            if (!targetAnimator.applyRootMotion)
                return;

            // Get root motion delta from Animator
            Vector3 rootMotionDelta = targetAnimator.deltaPosition;
            Quaternion rootRotationDelta = targetAnimator.deltaRotation;

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
                float3 currentUp = math.mul(_currentRotation, new float3(0, 1, 0));
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
                Debug.LogWarning($"[MovementComponent] State {targetStateType} not found.");
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
                    Debug.LogWarning($"[MovementComponent] State {stateType} not implemented yet.");
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
                Debug.LogWarning(
                    $"[MovementComponent] Cannot enable root motion on {gameObject.name} when using AnimancerComponent Parameters mode. " +
                    "Root motion requires Unity Animator or HybridAnimancerComponent. Consider using HybridAnimancerComponent with an Animator.",
                    this);
            }
        }

        void OnDestroy()
        {
            StatePool<MovementStateBase>.Clear();
        }
    }
}