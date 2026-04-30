using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;
using CycloneGames.RPGFoundation.Runtime.Movement2D.States;
using CycloneGames.Logger;

#if ANIMANCER_PRESENT
using Animancer;
#endif
#if GAMEPLAY_FRAMEWORK_PRESENT
using CycloneGames.GameplayFramework.Runtime;
#endif

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovementComponent2D : MonoBehaviour, IMovementStateQuery2D
#if GAMEPLAY_FRAMEWORK_PRESENT
        , IInitialRotationSettable
#endif
    {
        [SerializeField] private MovementConfig2D config;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;
        [SerializeField] private Transform worldUpSource;
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
        public IMovementAuthority MovementAuthority { get; set; }

        /// <summary>
        /// Read-only access to movement configuration. Prevents external modification of shared ScriptableObject assets.
        /// </summary>
        public IMovementConfig2DReadOnly Config => _configReadOnly ??= new MovementConfig2DReadOnlyWrapper(config);

        private IMovementConfig2DReadOnly _configReadOnly;

        public event Action<MovementStateType, MovementStateType> OnStateChanged;
        public event Action OnLanded;
        public event Action OnJumpStart;

        private Rigidbody2D _rigidbody;
        private MovementStateBase2D _currentState;
        private MovementContext2D _context;

        private float _coyoteTimeCounter;
        private float _jumpBufferCounter;
        private bool _wasGrounded;
        private bool _facingRight; // Current facing direction (initialized from config)
        private Transform _groundCheck; // Auto-created ground check point (internal use only)

        private const float _minSqrMagnitudeForMovement = 0.0001f;

        private MovingPlatformData2D _movingPlatform;
        private Collider2D _lastGroundCollider;
        private Vector2 _inheritedPlatformVelocity;
        private Vector2 _lastGroundVelocity;

        private Vector2 _pendingImpulse;
        private Vector2 _pendingForce;
        private float _unconstrainedTimer;

        private bool _isUsingAnimancer;
        private bool _isUsingHybridAnimancer;
        private Animator _hybridAnimancerAnimator;
        private Animator _cachedTargetAnimator; // Character's own velocity when last grounded

        private float DeltaTime => (ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime) * LocalTimeScale;
        private float FixedDeltaTime => (ignoreTimeScale ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime) * LocalTimeScale;

        #region IMovementStateQuery2D Implementation
        public MovementStateType CurrentState => _currentState?.StateType ?? MovementStateType.Idle;
        public bool IsGrounded => _context.IsGrounded;
        public float CurrentSpeed => _context.CurrentSpeed;
        public Vector2 Velocity => _context.CurrentVelocity;
        public Vector2 LookDirection => _context.LookDirection;
        public bool IsMoving => math.lengthsq(_context.CurrentVelocity) > _minSqrMagnitudeForMovement;
        #endregion

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();

            IAnimationController animationController = null;

            if (animancerComponent != null)
            {
#if ANIMANCER_PRESENT
                if (animancerComponent is HybridAnimancerComponent hybridAnimancer)
                {
                    var animancerAnimator = hybridAnimancer.Animator;
                    if (characterAnimator != null && animancerAnimator != null && animancerAnimator != characterAnimator)
                    {
                        CLogger.LogWarning(
                            "[MovementComponent2D] HybridAnimancerComponent and manually assigned Animator reference different components. " +
                            "HybridAnimancerComponent's Animator will be used.");
                    }
                }
                else if (animancerComponent is AnimancerComponent regularAnimancer)
                {
                    var animancerAnimator = regularAnimancer.Animator;
                    if (characterAnimator != null && animancerAnimator != null && animancerAnimator != characterAnimator)
                    {
                        CLogger.LogWarning(
                            "[MovementComponent2D] AnimancerComponent and manually assigned Animator reference different components.");
                    }
                }

                var parameterMap = CreateParameterNameMap();
                animationController = new AnimancerAnimationController(animancerComponent, parameterMap);
#else
                CLogger.LogWarning(
                    "[MovementComponent2D] Animancer component assigned but Animancer package is not installed. " +
                    "Falling back to Unity Animator.");
#endif
            }

            if (animationController == null)
            {
                if (characterAnimator == null)
                    characterAnimator = GetComponent<Animator>();
                if (characterAnimator != null)
                    animationController = new AnimatorAnimationController(characterAnimator);
            }

            if (config == null)
            {
                CLogger.LogError("[MovementComponent2D] MovementConfig2D is not assigned. Creating default config.");
                config = ScriptableObject.CreateInstance<MovementConfig2D>();
            }

            if (config.MovementType != MovementType2D.TopDown)
            {
                if (_groundCheck == null)
                {
                    GameObject groundCheckObj = new GameObject("GroundCheck");
                    groundCheckObj.transform.SetParent(transform);
                    groundCheckObj.transform.localPosition = config.GroundCheckOffset;
                    _groundCheck = groundCheckObj.transform;
                }
            }

            PreWarmAnimationParameters();
            InitializePhysics();
            InitializeContext(animationController);
            _currentState = StatePool<MovementStateBase2D>.GetState<IdleState2D>();

            // Initialize facing direction from config
            _facingRight = config.FacingRight;

            CacheTargetAnimator();
        }

        private void CacheTargetAnimator()
        {
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
                _cachedTargetAnimator = _hybridAnimancerAnimator;
            else if (characterAnimator != null && !_isUsingAnimancer)
                _cachedTargetAnimator = characterAnimator;
            else
                _cachedTargetAnimator = null;

            if (_cachedTargetAnimator != null && useRootMotion)
                _cachedTargetAnimator.applyRootMotion = true;
        }

        private System.Collections.Generic.Dictionary<int, string> CreateParameterNameMap()
        {
            if (config == null) return new System.Collections.Generic.Dictionary<int, string>();

            var map = new System.Collections.Generic.Dictionary<int, string>();

            // Map parameter hashes to names for Animancer Parameters mode
            if (!string.IsNullOrEmpty(config.MovementSpeedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.MovementSpeedParameter);
                map[hash] = config.MovementSpeedParameter;
            }

            if (!string.IsNullOrEmpty(config.IsGroundedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.IsGroundedParameter);
                map[hash] = config.IsGroundedParameter;
            }

            if (!string.IsNullOrEmpty(config.JumpTrigger))
            {
                int hash = AnimationParameterCache.GetHash(config.JumpTrigger);
                map[hash] = config.JumpTrigger;
            }

            if (!string.IsNullOrEmpty(config.VerticalSpeedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.VerticalSpeedParameter);
                map[hash] = config.VerticalSpeedParameter;
            }

            if (!string.IsNullOrEmpty(config.InputXParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.InputXParameter);
                map[hash] = config.InputXParameter;
            }

            if (!string.IsNullOrEmpty(config.InputYParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.InputYParameter);
                map[hash] = config.InputYParameter;
            }

            if (!string.IsNullOrEmpty(config.RollTrigger))
            {
                int hash = AnimationParameterCache.GetHash(config.RollTrigger);
                map[hash] = config.RollTrigger;
            }

            return map;
        }

        private void PreWarmAnimationParameters()
        {
            if (config == null) return;

            AnimationParameterCache.PreWarm(
                config.MovementSpeedParameter,
                config.IsGroundedParameter,
                config.JumpTrigger,
                config.VerticalSpeedParameter,
                config.InputXParameter,
                config.InputYParameter
            );

            if (!string.IsNullOrEmpty(config.RollTrigger))
            {
                AnimationParameterCache.PreWarm(config.RollTrigger);
            }
        }

        private void InitializePhysics()
        {
            if (config.MovementType == MovementType2D.TopDown)
            {
                // TopDown has no gravity
                _rigidbody.gravityScale = 0;
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else if (config.MovementType == MovementType2D.Platformer && config.LockZAxis)
            {
                // Rigidbody2D automatically ignores Z position, but we can freeze rotation
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else
            {
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
                _rigidbody.gravityScale = config.Gravity;
            }
        }

        private void InitializeContext(IAnimationController animationController)
        {
            _context = new MovementContext2D
            {
                Rigidbody = _rigidbody,
                AnimationController = animationController,
                Transform = transform,
                Config = config,
                WorldUp = new float2(0, 1),
                IsGrounded = false,
                JumpCount = 0,
                MovementAuthority = null
            };
        }

        void Update()
        {
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
            }

            UpdateWorldUp();

            if (_unconstrainedTimer > 0)
                _unconstrainedTimer -= DeltaTime;

            HandleJumpBuffer();
            ApplyMovingPlatform();
            UpdateContext();
            ExecuteStateMachine();
            ApplyPendingForces();
            UpdateFacing();
            UpdateMovingPlatformTracking();
        }

        private void UpdateWorldUp()
        {
            if (worldUpSource != null)
                _context.WorldUp = new float2(worldUpSource.up.x, worldUpSource.up.y);
            else
                _context.WorldUp = new float2(0, 1);
        }

        void FixedUpdate()
        {
            CheckGround();
            HandleCoyoteTime();
        }

        private void CheckGround()
        {
            if (config == null) return;

            if (config.MovementType == MovementType2D.TopDown)
            {
                _context.IsGrounded = true;
                return;
            }

            bool wasGrounded = _context.IsGrounded;

            Vector2 checkPosition = _groundCheck != null
                ? _groundCheck.position
                : (Vector2)transform.position + config.GroundCheckOffset;

            _lastGroundCollider = Physics2D.OverlapBox(checkPosition, config.GroundCheckSize, 0, config.GroundLayer);
            _context.IsGrounded = _lastGroundCollider != null;

            if (!_context.IsGrounded && config.EnableGapBridging)
            {
                if (TryBridgeGap2D(checkPosition))
                {
                    _context.IsGrounded = true;
                }
            }

            // BeltScroll: restore depth position on landing
            if (config.MovementType == MovementType2D.BeltScroll && !wasGrounded && _context.IsGrounded)
            {
                if (math.abs(_context.PendingDepth) > 0.001f)
                {
                    Vector2 pos = transform.position;
                    pos.y += _context.PendingDepth;
                    transform.position = pos;
                    _context.PendingDepth = 0f;
                }
            }

            bool isInAirState = _currentState != null &&
                                (_currentState.StateType == MovementStateType.Jump ||
                                 _currentState.StateType == MovementStateType.Fall);

            if (!wasGrounded && _context.IsGrounded && !isInAirState)
            {
                OnLanded?.Invoke();
                _context.JumpCount = 0;
                _context.JumpPressed = false;
            }
            _wasGrounded = _context.IsGrounded;
        }

        /// <summary>
        /// Mario-style gap bridging: check if there's ground ahead when running fast.
        /// Maintains grounded state to "slide" across small gaps.
        /// </summary>
        private bool TryBridgeGap2D(Vector2 currentCheckPos)
        {
            if (config == null) return false;

            // Only bridge when moving fast enough
            float currentSpeed = Mathf.Abs(_context.CurrentVelocity.x);
            if (currentSpeed < config.MinSpeedForGapBridge) return false;

            // Get movement direction
            float moveDir = Mathf.Sign(_context.CurrentVelocity.x);
            if (Mathf.Approximately(moveDir, 0)) return false;

            // Check for ground ahead at increasing distances
            for (float dist = 0.3f; dist <= config.MaxGapDistance; dist += 0.2f)
            {
                Vector2 aheadPos = currentCheckPos + new Vector2(moveDir * dist, 0);
                Collider2D ground = Physics2D.OverlapBox(aheadPos, config.GroundCheckSize, 0, config.GroundLayer);

                if (ground != null)
                {
                    // Found ground ahead - bridge the gap
                    _lastGroundCollider = ground;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies movement from the moving platform before processing character movement.
        /// </summary>
        private void ApplyMovingPlatform()
        {
            if (config == null || !config.EnableMovingPlatform || !_movingPlatform.isOnPlatform)
                return;

            if (_movingPlatform.platformTransform == null)
            {
                _movingPlatform.Clear();
                return;
            }

            Vector2 deltaPos = _movingPlatform.GetPlatformDeltaPosition(transform);
            if (deltaPos.sqrMagnitude > 0.0001f)
            {
                transform.position += (Vector3)deltaPos;
            }

            if (config.InheritPlatformRotation)
            {
                float deltaRotZ = _movingPlatform.GetPlatformDeltaRotationZ(transform);
                if (Mathf.Abs(deltaRotZ) > 0.01f)
                {
                    transform.Rotate(0, 0, deltaRotZ);
                }
            }
            // Note: localPosition is updated in UpdateMovingPlatformTracking after character movement
        }

        /// <summary>
        /// Updates moving platform tracking after character movement is processed.
        /// Applies platform momentum when leaving platform (jumping off).
        /// </summary>
        private void UpdateMovingPlatformTracking()
        {
            if (config == null || !config.EnableMovingPlatform)
            {
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }

            if (!_context.IsGrounded)
            {
                // Left platform - apply momentum if configured
                if (_movingPlatform.isOnPlatform && config.InheritPlatformMomentum)
                {
                    _inheritedPlatformVelocity = _movingPlatform.platformVelocity;
                }
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }
            else
            {
                // On ground - clear inherited velocities
                _inheritedPlatformVelocity = Vector2.zero;
                _lastGroundVelocity = Vector2.zero;
            }

            if (_lastGroundCollider != null)
            {
                Rigidbody2D groundRb = _lastGroundCollider.attachedRigidbody;
                LayerMask platformMask = config.PlatformLayer != 0 ? config.PlatformLayer : config.GroundLayer;
                bool isValidPlatform = groundRb != null &&
                                       ((1 << _lastGroundCollider.gameObject.layer) & platformMask) != 0;

                if (isValidPlatform)
                {
                    if (_movingPlatform.platform != groundRb)
                    {
                        _movingPlatform.SetPlatform(groundRb, transform);
                    }
                    else
                    {
                        _movingPlatform.UpdatePlatformVelocity(DeltaTime);
                        // Update localPosition AFTER character movement to include running
                        _movingPlatform.localPosition = _movingPlatform.platformTransform.InverseTransformPoint(transform.position);
                        _movingPlatform.localRotationZ = transform.eulerAngles.z - _movingPlatform.platformTransform.eulerAngles.z;
                    }
                    return;
                }
            }

            if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
        }


        private void HandleCoyoteTime()
        {
            if (config == null) return;

            if (_context.IsGrounded)
            {
                _coyoteTimeCounter = config.CoyoteTime;
            }
            else
            {
                _coyoteTimeCounter -= FixedDeltaTime;
            }
        }

        private void HandleJumpBuffer()
        {
            if (config == null) return;

            if (_context.JumpPressed)
            {
                _jumpBufferCounter = config.JumpBufferTime;
            }
            else
            {
                _jumpBufferCounter -= DeltaTime;
            }
        }

        private void UpdateContext()
        {
            if (config == null)
            {
                CLogger.LogError("[MovementComponent2D] MovementConfig2D is null. Movement may not work correctly.");
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.FixedDeltaTime = FixedDeltaTime;

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int groundedHash = AnimationParameterCache.GetHash(config.IsGroundedParameter);
                int verticalHash = AnimationParameterCache.GetHash(config.VerticalSpeedParameter);
                int inputXHash = AnimationParameterCache.GetHash(config.InputXParameter);
                int inputYHash = AnimationParameterCache.GetHash(config.InputYParameter);

                _context.AnimationController.SetBool(groundedHash, _context.IsGrounded);
#if UNITY_6000_0_OR_NEWER
                _context.AnimationController.SetFloat(verticalHash, _rigidbody.linearVelocity.y);
#else
                _context.AnimationController.SetFloat(verticalHash, _rigidbody.velocity.y);
#endif
                _context.AnimationController.SetFloat(inputXHash, _context.InputDirection.x);
                _context.AnimationController.SetFloat(inputYHash, _context.InputDirection.y);
            }

            _context.MovementAuthority = MovementAuthority;
        }

        private void ExecuteStateMachine()
        {
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
                else
                {
                    CLogger.LogError("[MovementComponent2D] Failed to initialize state. Movement will not work.");
                    return;
                }
            }

            float2 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            bool shouldUseRootMotion = useRootMotion && _cachedTargetAnimator != null && _cachedTargetAnimator.applyRootMotion;

            float dt = DeltaTime > 0 ? DeltaTime : 1f;

            if (!shouldUseRootMotion)
            {
#if UNITY_6000_0_OR_NEWER
            Vector2 targetVelocity = new Vector2(displacement.x / dt, _rigidbody.linearVelocity.y);
#else
            Vector2 targetVelocity = new Vector2(displacement.x / dt, _rigidbody.velocity.y);
#endif

            if (config.MovementType == MovementType2D.TopDown)
            {
                // TopDown: Full X/Y control, no gravity
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = new Vector2(displacement.x / dt, displacement.y / dt);
#else
                _rigidbody.velocity = new Vector2(displacement.x / dt, displacement.y / dt);
#endif
            }
            else if (config.MovementType == MovementType2D.BeltScroll)
            {
                // BeltScroll (DNF-style): 
                // - X axis: horizontal movement
                // - Y axis: BOTH depth (input.y) AND jump height
                // 
                // Unlike true 3D, DNF-style games use Y for depth simulation.
                // The depth movement (input.y → displacement.y) is applied directly.
                // Jump is handled by JumpState setting Rigidbody velocity.y.
                // 
                // Key insight: In DNF, when on ground, input.y moves you in "depth".
                // When jumping, Rigidbody.velocity.y handles the arc.
                // Since we're using Rigidbody2D, both are naturally combined.

                // When grounded, depth movement is controlled by states (WalkState, RunState, etc.)
                // States should output displacement.y for depth movement when MovementType is BeltScroll.
                // When in air (Jump/Fall), states control horizontal only; physics handles vertical.

                bool isAirborne = _currentState != null &&
                                  (_currentState.StateType == Movement.MovementStateType.Jump ||
                                   _currentState.StateType == Movement.MovementStateType.Fall);

                if (isAirborne)
                {
                    // In air: X from input, Y from physics (gravity/jump)
#if UNITY_6000_0_OR_NEWER
                    _rigidbody.linearVelocity = new Vector2(targetVelocity.x, _rigidbody.linearVelocity.y);
#else
                    _rigidbody.velocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
#endif
                }
                else
                {
                    // On ground: Full X/Y control for depth movement, no gravity effect on grounded depth
#if UNITY_6000_0_OR_NEWER
                    _rigidbody.linearVelocity = new Vector2(displacement.x / dt, displacement.y / dt);
#else
                    _rigidbody.velocity = new Vector2(displacement.x / dt, displacement.y / dt);
#endif
                }
            }
            else
            {
                // Platformer: X from input, Y from physics
#if UNITY_6000_0_OR_NEWER
                Vector2 finalVelocity = new Vector2(targetVelocity.x, _rigidbody.linearVelocity.y);
#else
                Vector2 finalVelocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
#endif

                if (_context.IsGrounded)
                {
                    // Save character's ground velocity for jump momentum
                    _lastGroundVelocity = new Vector2(displacement.x / dt, 0);
                }
                else
                {
                    // In air: apply inherited momentum (platform + character's own velocity)
#if UNITY_6000_0_OR_NEWER
                    Vector2 totalInheritedVelocity = _inheritedPlatformVelocity + _lastGroundVelocity;
#else
                    Vector2 totalInheritedVelocity = _inheritedPlatformVelocity + _lastGroundVelocity;
#endif

                    if (totalInheritedVelocity.sqrMagnitude > _minSqrMagnitudeForMovement)
                    {
                        // Use whichever is greater: inherited momentum or player input
                        float inheritedSpeed = Mathf.Abs(totalInheritedVelocity.x);
                        float inputSpeed = Mathf.Abs(targetVelocity.x);

                        if (inheritedSpeed > inputSpeed)
                        {
                            // Inherited momentum is stronger - use it
                            finalVelocity.x = totalInheritedVelocity.x;
                        }
                        // else: player input is stronger, use normal velocity
                    }
                }

#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = finalVelocity;
#else
                _rigidbody.velocity = finalVelocity;
#endif
            }
            } // if (!shouldUseRootMotion)

            MovementStateBase2D nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
            {
                RequestStateChangeInternal(nextState);
            }
        }

        private void UpdateFacing()
        {
            if (config == null) return;

            if (config.MovementType == MovementType2D.TopDown || config.MovementType == MovementType2D.BeltScroll)
            {
                // TopDown/BeltScroll: use LookDirection if set, otherwise use movement direction for facing
                float2 facingSource = math.lengthsq(_context.LookDirection) > _minSqrMagnitudeForMovement
                    ? _context.LookDirection
                    : _context.InputDirection;

                if (math.lengthsq(facingSource) > _minSqrMagnitudeForMovement)
                {
                    // For these modes, facing is tracked in context for external use (animation, combat)
                    // Scale flipping is only for X-facing; Y-facing is handled by the animation system
                    if (config.MovementType == MovementType2D.BeltScroll)
                    {
                        float scaleX = transform.localScale.x;
                        if (facingSource.x > 0.01f && scaleX < 0)
                            transform.localScale = new Vector3(-scaleX, transform.localScale.y, transform.localScale.z);
                        else if (facingSource.x < -0.01f && scaleX > 0)
                            transform.localScale = new Vector3(-scaleX, transform.localScale.y, transform.localScale.z);
                    }
                }
            }
            else
            {
                // Platformer: scale flip based on X input
                if (math.lengthsq(_context.InputDirection) > _minSqrMagnitudeForMovement)
                {
                    Vector3 scale = transform.localScale;
                    if (_context.InputDirection.x > 0 && !_facingRight)
                    {
                        scale.x = -scale.x;
                        _facingRight = true;
                    }
                    else if (_context.InputDirection.x < 0 && _facingRight)
                    {
                        scale.x = -scale.x;
                        _facingRight = false;
                    }
                    if (scale.x != transform.localScale.x)
                        transform.localScale = scale;
                }
            }
        }

        public void SetInputDirection(Vector2 direction)
        {
            if (config.MovementType == MovementType2D.BeltScroll || config.MovementType == MovementType2D.TopDown)
            {
                // Map Input Y to InputDirection.y (BeltScroll: Z movement, TopDown: Y movement)
                _context.InputDirection = new float2(direction.x, direction.y);
            }
            else
            {
                // Platformer: Input Y is usually ignored for movement (unless climbing/flying)
                // Standard Walk/Run only use X.
                _context.InputDirection = new float2(direction.x, 0);
            }
        }

        public void SetJumpPressed(bool pressed)
        {
            if (config != null && !config.EnableJump) return;

            bool wasPressed = _context.JumpPressed;
            _context.JumpPressed = pressed;

            // Trigger jump on rising edge when grounded or in coyote time
            // JumpCount check is handled in JumpState2D.OnEnter to ensure consistent counting
            if (pressed && !wasPressed && (_coyoteTimeCounter > 0 || _context.IsGrounded))
            {
                RequestStateChange(MovementStateType.Jump);
            }
            // Multi-jump is handled in JumpState2D.EvaluateTransition and FallState2D.EvaluateTransition
        }

        public void SetSprintHeld(bool held)
        {
            _context.SprintHeld = held;
        }

        public void SetCrouchHeld(bool held)
        {
            _context.CrouchHeld = held;
        }

        public void SetLookDirection(Vector2 direction)
        {
            if (math.lengthsq(direction) > _minSqrMagnitudeForMovement)
                _context.LookDirection = math.normalize(direction);
            else
                _context.LookDirection = float2.zero;
        }

        public void ClearLookDirection()
        {
            _context.LookDirection = float2.zero;
        }

        /// <summary>
        /// Sets the character's rotation. For 2D characters, this primarily affects Z-axis rotation.
        /// For Platformer/BeltScroll modes, the facing direction is controlled by scale flipping,
        /// but initial rotation may still be important for sprite orientation.
        /// </summary>
        /// <param name="rotation">The target rotation</param>
        /// <param name="immediate">If true, sets rotation immediately. If false, has no effect in 2D (2D uses scale flipping for facing).</param>
        public void SetRotation(Quaternion rotation, bool immediate = false)
        {
            if (immediate)
            {
                transform.rotation = rotation;

                // For Platformer/BeltScroll modes, also update facing direction based on rotation
                if (config != null && config.MovementType != MovementType2D.TopDown)
                {
                    Vector3 forward = rotation * Vector3.right; // In 2D, right is typically forward
                    bool shouldFaceRight = forward.x > 0;

                    if (shouldFaceRight != _facingRight)
                    {
                        _facingRight = shouldFaceRight;
                        Vector3 scale = transform.localScale;
                        if (scale.x > 0 && !shouldFaceRight)
                        {
                            scale.x = -scale.x;
                        }
                        else if (scale.x < 0 && shouldFaceRight)
                        {
                            scale.x = -scale.x;
                        }
                        transform.localScale = scale;
                    }
                }
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

            MovementStateBase2D targetState = GetStateByType(targetStateType);
            if (targetState == null)
            {
                CLogger.LogWarning($"[MovementComponent2D] State {targetStateType} not found.");
                return false;
            }

            RequestStateChangeInternal(targetState);
            return true;
        }

        void RequestStateChangeInternal(MovementStateBase2D newState)
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
                _coyoteTimeCounter = 0;
                // Note: We do NOT reset JumpPressed here to allow multi-jump (air jump)
                // JumpPressed is consumed in JumpState2D.EvaluateTransition when performing multi-jump
                // It will be reset when landing (in CheckGround when grounded)
            }
        }

        MovementStateBase2D GetStateByType(MovementStateType stateType)
        {
            switch (stateType)
            {
                case MovementStateType.Idle: return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                case MovementStateType.Walk: return StatePool<MovementStateBase2D>.GetState<WalkState2D>();
                case MovementStateType.Run: return StatePool<MovementStateBase2D>.GetState<RunState2D>();
                case MovementStateType.Sprint: return StatePool<MovementStateBase2D>.GetState<SprintState2D>();
                case MovementStateType.Crouch: return StatePool<MovementStateBase2D>.GetState<CrouchState2D>();
                case MovementStateType.Jump: return StatePool<MovementStateBase2D>.GetState<JumpState2D>();
                case MovementStateType.Fall: return StatePool<MovementStateBase2D>.GetState<FallState2D>();
                case MovementStateType.Roll: return StatePool<MovementStateBase2D>.GetState<RollState2D>();
                case MovementStateType.Swim: return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                case MovementStateType.Fly: return StatePool<MovementStateBase2D>.GetState<IdleState2D>();
                default:
                    CLogger.LogWarning($"[MovementComponent2D] State {stateType} not implemented yet.");
                    return null;
            }
        }

        void OnDestroy()
        {
            OnStateChanged = null;
            OnLanded = null;
            OnJumpStart = null;
        }

        #region Force System

        private void ApplyPendingForces()
        {
            if (_pendingImpulse.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity += _pendingImpulse;
#else
                _rigidbody.velocity += _pendingImpulse;
#endif
                _pendingImpulse = Vector2.zero;
            }

            if (_pendingForce.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity += _pendingForce * DeltaTime;
#else
                _rigidbody.velocity += _pendingForce * DeltaTime;
#endif
                _pendingForce = Vector2.zero;
            }
        }

        public void LaunchCharacter(Vector2 velocity, bool overrideXY = true, bool overrideY = true)
        {
            if (overrideXY)
            {
                _pendingImpulse.x = velocity.x;
            }
            else
            {
                _pendingImpulse.x += velocity.x;
            }

            if (overrideY)
            {
                _pendingImpulse.y = velocity.y;
            }
            else
            {
                _pendingImpulse.y += velocity.y;
            }

            PauseGroundConstraint(0.1f);
        }

        public void AddForce(Vector2 force)
        {
            _pendingForce += force;
        }

        public void AddExplosionForce(float force, Vector2 origin, float radius, float upwardsModifier = 0.5f)
        {
            Vector2 direction = (Vector2)transform.position - origin;
            float distance = direction.magnitude;

            if (distance > radius || distance < 0.001f) return;

            float falloff = 1f - (distance / radius);
            float finalForce = force * falloff;

            direction = direction.normalized;
            direction.y += upwardsModifier;
            direction = direction.normalized;

            LaunchCharacter(direction * finalForce, false, false);
        }

        public void PauseGroundConstraint(float duration = 0.1f)
        {
            _unconstrainedTimer = duration;
        }

        #endregion

        #region Root Motion

        private void OnAnimatorMove()
        {
            if (!useRootMotion || _cachedTargetAnimator == null)
                return;
            if (!_cachedTargetAnimator.applyRootMotion)
                return;

            Vector2 rootMotionDelta = _cachedTargetAnimator.deltaPosition;
            if (rootMotionDelta.sqrMagnitude > _minSqrMagnitudeForMovement)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = rootMotionDelta / Mathf.Max(DeltaTime, 0.0001f);
#else
                _rigidbody.velocity = rootMotionDelta / Mathf.Max(DeltaTime, 0.0001f);
#endif
                // Preserve Y velocity for gravity/jump in Platformer mode
                if (config.MovementType != MovementType2D.TopDown)
                {
#if UNITY_6000_0_OR_NEWER
                    _rigidbody.linearVelocity = new Vector2(_rigidbody.linearVelocity.x, _rigidbody.linearVelocity.y);
#else
                    _rigidbody.velocity = new Vector2(_rigidbody.velocity.x, _rigidbody.velocity.y);
#endif
                }
            }
        }

        #endregion

        void OnDrawGizmosSelected()
        {
            if (config != null && config.MovementType != MovementType2D.TopDown)
            {
                Vector2 checkPosition = _groundCheck != null
                    ? _groundCheck.position
                    : (Vector2)transform.position + config.GroundCheckOffset;

                Gizmos.color = Application.isPlaying && _context.IsGrounded ? Color.green : Color.red;
                Gizmos.DrawWireCube(checkPosition, config.GroundCheckSize);
            }
        }
    }
}