using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Physics-based movement component using Rigidbody + CapsuleCollider.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class MovementComponent : MonoBehaviour, IMovementStateQuery3D
#if GAMEPLAY_FRAMEWORK_PRESENT
        , IInitialRotationSettable
#endif
    {
        #region Serialized Fields

        [SerializeField] private MovementConfig config;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;
        [SerializeField] private Transform worldUpSource;
        [SerializeField] private bool useRootMotion = false;
        [SerializeField] private bool ignoreTimeScale = false;

        [Header("Moving Platform Smoothing")]
        [Tooltip("Enable smooth visual following on moving platforms. Reduces jitter when platform moves in Update.")]
        [SerializeField] private bool smoothPlatformFollow = true;

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private bool showGroundDetection = true;
        [SerializeField] private bool showCollisionSweep = true;
        [SerializeField] private bool showVelocity = true;

        [Header("Runtime Debug Info (Read Only)")]
        [SerializeField] private float _debugConfigRunSpeed;
        [SerializeField] private float _debugCurrentSpeed;
        [SerializeField] private float _debugDeltaTime;
        [SerializeField] private string _debugCurrentState;
        [SerializeField] private bool _debugConfigAssigned;
        [SerializeField] private Vector3 _debugInputDirection;
        [SerializeField] private float _debugInputMagnitude;
        [SerializeField] private Vector3 _debugActualVelocity;
        [SerializeField] private float _debugActualSpeed;

        [Header("Moving Platform Debug")]
        [SerializeField] private bool _debugOnPlatform;
        [SerializeField] private string _debugPlatformName;
        [SerializeField] private Vector3 _debugPlatformVelocity;
        [SerializeField] private string _debugGroundColliderName;
        [SerializeField] private bool _debugGroundHasRigidbody;
        [SerializeField] private bool _debugGroundLayerMatch;
        [SerializeField] private Vector3 _debugPlatformDeltaPos;
        [SerializeField] private Vector3 _debugLocalPosition;
#endif

        #endregion

        #region Constants

        // Contact and penetration offsets
        private const float kContactOffset = 0.01f;
        private const float kPenetrationOffset = 0.001f;

        // Ground distance thresholds
        private const float kMinGroundDistance = 0.001f;
        private const float kMaxGroundDistance = 0.05f;
        private const float kAvgGroundDistance = 0.02f;

        // Capsule hemisphere detection threshold
        private const float kHemisphereLimit = 0.01f;

        // Movement iteration limits
        private const int kMaxMovementIterations = 4;
        private const float kMinMoveDistanceSqr = 0.0001f; // 0.01^2
        private const int kMaxHitCount = 16;
        private const int kMaxOverlapCount = 8;

        // Misc
        private const float kMinSqrMagnitude = 0.0001f;
        private const float kGroundedVerticalVelocity = -2f;
        private const float kSweepEdgeRejectDistance = 0.015f;
        private const float kKindaSmallNumber = 0.0001f;

        #endregion

        #region Properties

        public bool IgnoreTimeScale
        {
            get => ignoreTimeScale;
            set => ignoreTimeScale = value;
        }

        public float LocalTimeScale { get; set; } = 1f;
        public Vector3 WorldUp { get; set; } = Vector3.up;
        public IMovementAuthority MovementAuthority { get; set; }
        public IMovementConfig3DReadOnly Config => _configReadOnly ??= new MovementConfig3DReadOnlyWrapper(config);

        public float Radius => _radius;
        public float Height => _height;

        #endregion

        #region Events

        public event Action<MovementStateType, MovementStateType> OnStateChanged;
        public event Action OnLanded;
        public event Action OnJumpStart;

        #endregion

        #region Private Fields - Components

        private IMovementConfig3DReadOnly _configReadOnly;
        private Rigidbody _rigidbody;
        private CapsuleCollider _capsuleCollider;
        private MovementStateBase _currentState;
        private MovementContext _context;

        #endregion

        #region Private Fields - Capsule

        private float _radius = 0.5f;
        private float _height = 2.0f;
        private float _minSlopeLimit;
        private Vector3 _capsuleCenter;
        private Vector3 _capsuleTopCenter;
        private Vector3 _capsuleBottomCenter;

        #endregion

        #region Private Fields - Movement

        private float3 _lookDirection;
        private quaternion _currentRotation;
        private Vector3 _previousWorldUp;
        private bool _worldUpChanged;
        private Vector3 _velocity;
        private Vector3 _updatedPosition;

        private bool _isConstrainedToGround = true;
        private float _unconstrainedTimer;

        #endregion

        #region Private Fields - Ground

        private bool _isGrounded;
        private bool _wasGrounded;
        private bool _isOnNonWalkableSlope;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _groundPoint;
        private Collider _groundCollider;
        private float _groundDistance;
        private FindGroundResult _currentGround;
        private FindGroundResult _foundGround;
        private bool _hasLanded;
        private Vector3 _landedVelocity;

        private readonly RaycastHit[] _hits = new RaycastHit[kMaxHitCount];
        private readonly Collider[] _overlaps = new Collider[kMaxOverlapCount];
        private readonly HashSet<Collider> _ignoredColliders = new HashSet<Collider>();
        private readonly CollisionResult[] _collisionResults = new CollisionResult[kMaxHitCount];
        private int _collisionCount;

        #endregion

        #region Private Fields - Animation

        private bool _isUsingAnimancer;
        private bool _isUsingHybridAnimancer;
        private Animator _hybridAnimancerAnimator;
        private Animator _cachedTargetAnimator;
        private Dictionary<int, string> _cachedParameterMap;
        private static readonly float3 UnityUpVector = new float3(0, 1, 0);

        #endregion

        #region Private Fields - Platform

        private MovingPlatformData _movingPlatform;
        private Vector3 _inheritedPlatformVelocity;
        private Vector3 _lastGroundVelocity;

        // For smooth platform following in LateUpdate
        private Vector3 _lastPlatformPosAtFixedUpdate;
        private Quaternion _lastPlatformRotAtFixedUpdate;

        #endregion

        #region Private Fields - Forces

        private Vector3 _pendingImpulse;
        private Vector3 _pendingForce;
        private float _gapBridgeCooldown;

        #endregion

        // Use fixedDeltaTime in FixedUpdate context for physics consistency
        private float DeltaTime => (ignoreTimeScale ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime) * LocalTimeScale;

        // Use collisionLayer if set, otherwise fall back to groundLayer
        private LayerMask CollisionLayers => config != null && config.collisionLayer != 0
            ? config.collisionLayer
            : (config != null ? config.groundLayer : Physics.DefaultRaycastLayers);

        #region IMovementStateQuery

        public MovementStateType CurrentState => _currentState?.StateType ?? MovementStateType.Idle;
        public bool IsGrounded => _context.IsGrounded;
        public float CurrentSpeed => _context.CurrentSpeed;
        public Vector3 Velocity => _context.CurrentVelocity;
        public bool IsMoving => math.lengthsq(_context.CurrentVelocity) > kMinSqrMagnitude;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _capsuleCollider = GetComponent<CapsuleCollider>();

            InitializeRigidbody();
            InitializeCapsule();
            SetupAnimationController();
            PreWarmAnimationParameters();
            InitializeContext();

            _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
            _currentRotation = transform.rotation;
            CacheTargetAnimator();

            // Auto-find MovementAuthority if not set
            if (MovementAuthority == null)
            {
                MovementAuthority = GetComponent<IMovementAuthority>();
            }
        }

        private void Start()
        {
            UpdateWorldUp();
            _previousWorldUp = WorldUp;
        }

        private void FixedUpdate()
        {
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
                _currentState?.OnEnter(ref _context);
            }

            float deltaTime = DeltaTime;

            if (_gapBridgeCooldown > 0)
                _gapBridgeCooldown -= deltaTime;

            if (_unconstrainedTimer > 0)
                _unconstrainedTimer -= deltaTime;

            // Use transform.position for stability (rigidbody.position may be interpolated)
            _updatedPosition = transform.position;

            // Moving platform: First apply platform movement using LAST frame's localPosition
            // This moves the character WITH the platform before any other processing
            ApplyMovingPlatform();

            // Update context (ground detection, etc.) - uses _updatedPosition which now includes platform movement
            UpdateContext();

            // Now update platform tracking with current ground info
            UpdateMovingPlatformTracking();

            if (_context.IsGrounded && config != null && config.enableGapBridging)
                TryBridgeGap();

            ExecuteStateMachine();
            ApplyPendingForces();

            // Update moving platform local position AFTER character movement
            // This ensures next frame's ApplyMovingPlatform uses the correct offset
            UpdateMovingPlatformLocalPosition();

            // Save platform state at end of FixedUpdate for LateUpdate interpolation
            if (_movingPlatform.isOnPlatform && _movingPlatform.platformTransform != null)
            {
                _lastPlatformPosAtFixedUpdate = _movingPlatform.platformTransform.position;
                _lastPlatformRotAtFixedUpdate = _movingPlatform.platformTransform.rotation;
            }
        }

        /// <summary>
        /// LateUpdate handles smooth visual following for moving platforms.
        /// Since platforms often move in Update (e.g., LitMotion, DOTween), 
        /// we need to compensate for platform movement that occurred after FixedUpdate.
        /// </summary>
        private void LateUpdate()
        {
            if (!smoothPlatformFollow) return;
            if (!_movingPlatform.isOnPlatform || _movingPlatform.platformTransform == null) return;

            // Get current platform state
            Vector3 currentPlatformPos = _movingPlatform.platformTransform.position;

            // Calculate how much the platform moved since we last synced
            Vector3 platformPosDelta = currentPlatformPos - _lastPlatformPosAtFixedUpdate;

            // Only apply if platform actually moved (avoid floating point noise)
            if (platformPosDelta.sqrMagnitude < 0.0000001f)
                return;

            // Simply add platform delta to character position
            Vector3 newPos = transform.position + platformPosDelta;
            transform.position = newPos;
            _rigidbody.position = newPos;

            // Update tracking for next frame
            _lastPlatformPosAtFixedUpdate = currentPlatformPos;

            // Keep localPosition in sync so FixedUpdate doesn't fight with us
            _movingPlatform.UpdateLocalPosition(newPos);
        }

        private void OnDestroy()
        {
        }

        #endregion

        #region Initialization

        private void InitializeRigidbody()
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void InitializeCapsule()
        {
            _radius = _capsuleCollider.radius;
            _height = _capsuleCollider.height;
            _capsuleCollider.direction = 1; // Y-axis

            if (config != null)
            {
                // cosine of slope limit angle - surfaces with normal.y >= this are walkable
                _minSlopeLimit = Mathf.Cos(config.slopeLimit * Mathf.Deg2Rad);
            }
            else
            {
                _minSlopeLimit = Mathf.Cos(46f * Mathf.Deg2Rad);
            }

            UpdateCapsuleCache();
        }

        private void UpdateCapsuleCache()
        {
            float halfHeight = _height * 0.5f;
            float halfHeightMinusRadius = Mathf.Max(0f, halfHeight - _radius);

            _capsuleCenter = _capsuleCollider.center;
            _capsuleTopCenter = _capsuleCenter + Vector3.up * halfHeightMinusRadius;
            _capsuleBottomCenter = _capsuleCenter - Vector3.up * halfHeightMinusRadius;
        }

        private void SetupAnimationController()
        {
            IAnimationController animationController = null;

            if (animancerComponent != null)
            {
                _isUsingAnimancer = true;

#if ANIMANCER_PRESENT
                if (animancerComponent is HybridAnimancerComponent hybridAnimancer)
                {
                    _isUsingHybridAnimancer = true;
                    _hybridAnimancerAnimator = hybridAnimancer.Animator;
                    if (_hybridAnimancerAnimator != null && characterAnimator == null)
                        characterAnimator = _hybridAnimancerAnimator;
                }
                else if (animancerComponent is AnimancerComponent)
                {
                    _isUsingHybridAnimancer = false;
                }
#else
                TryExtractAnimatorViaReflection();
#endif

                _cachedParameterMap = CreateParameterNameMap();
                animationController = new AnimancerAnimationController(animancerComponent, _cachedParameterMap);

                if (useRootMotion && !_isUsingHybridAnimancer)
                {
                    CLogger.LogWarning("[MovementComponent] Root motion requires HybridAnimancerComponent.");
                }
            }
            else
            {
                if (characterAnimator == null)
                    characterAnimator = GetComponent<Animator>();
                if (characterAnimator != null)
                    animationController = new AnimatorAnimationController(characterAnimator);
            }

            _context.AnimationController = animationController;
        }

#if !ANIMANCER_PRESENT
        private void TryExtractAnimatorViaReflection()
        {
            try
            {
                var animancerType = animancerComponent.GetType();
                bool isHybrid = animancerType.Name.Contains("Hybrid");

                if (isHybrid)
                {
                    _isUsingHybridAnimancer = true;
                    var animatorProp = animancerType.GetProperty("Animator",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);

                    if (animatorProp != null)
                    {
                        _hybridAnimancerAnimator = animatorProp.GetValue(animancerComponent) as Animator;
                        if (_hybridAnimancerAnimator != null && characterAnimator == null)
                            characterAnimator = _hybridAnimancerAnimator;
                    }
                }
            }
            catch (Exception)
            {
                CLogger.LogError("[MovementComponent] Failed to extract Animator from AnimancerComponent.");
            }
        }
#endif

        private Dictionary<int, string> CreateParameterNameMap()
        {
            var map = new Dictionary<int, string>();
            if (config == null) return map;

            AddParameterToMap(map, config.movementSpeedParameter);
            AddParameterToMap(map, config.isGroundedParameter);
            AddParameterToMap(map, config.jumpTrigger);
            AddParameterToMap(map, config.rollTrigger);

            return map;
        }

        private void AddParameterToMap(Dictionary<int, string> map, string paramName)
        {
            if (!string.IsNullOrEmpty(paramName))
            {
                int hash = AnimationParameterCache.GetHash(paramName);
                map[hash] = paramName;
            }
        }

        private void PreWarmAnimationParameters()
        {
            if (config == null) return;
            AnimationParameterCache.PreWarm(config.movementSpeedParameter, config.isGroundedParameter, config.jumpTrigger);
            if (!string.IsNullOrEmpty(config.rollTrigger))
                AnimationParameterCache.PreWarm(config.rollTrigger);
        }

        private void CacheTargetAnimator()
        {
            if (_isUsingHybridAnimancer && _hybridAnimancerAnimator != null)
                _cachedTargetAnimator = _hybridAnimancerAnimator;
            else if (characterAnimator != null && !_isUsingAnimancer)
                _cachedTargetAnimator = characterAnimator;
            else
                _cachedTargetAnimator = null;
        }

        private void InitializeContext()
        {
            _context = new MovementContext
            {
                Rigidbody = _rigidbody,
                CapsuleCollider = _capsuleCollider,
                Transform = transform,
                Config = config,
                WorldUp = WorldUp,
                VerticalVelocity = kGroundedVerticalVelocity,
                UseRootMotion = useRootMotion,
                JumpCount = 0,
                MovementAuthority = null
            };

            _previousWorldUp = WorldUp;
        }

        #endregion

        #region Update Logic

        private void UpdateWorldUp()
        {
            if (worldUpSource != null)
                WorldUp = worldUpSource.up;
        }

        private void UpdateContext()
        {
            Vector3 previousWorldUp = WorldUp;
            UpdateWorldUp();
            _worldUpChanged = previousWorldUp != WorldUp;

            if (config == null)
            {
                CLogger.LogError("[MovementComponent] MovementConfig is null.");
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.WasGrounded = _context.IsGrounded;
            _context.Config = config; // Ensure config is always up to date

            // Ground detection
            FindGround();
            _context.IsGrounded = _isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0;
            _context.IsOnNonWalkableSlope = _isOnNonWalkableSlope;
            _context.GroundNormal = _groundNormal;

            // Root motion handling
            if (useRootMotion && _isUsingHybridAnimancer)
                _context.UseRootMotion = true;
            else if (useRootMotion && !_isUsingAnimancer && characterAnimator != null)
                _context.UseRootMotion = true;
            else if (!useRootMotion)
                _context.UseRootMotion = false;

            // Project vertical velocity onto new WorldUp when it changes mid-air
            if (_worldUpChanged && !_context.IsGrounded)
            {
                Vector3 prevVertical = _previousWorldUp * _context.VerticalVelocity;
                _context.VerticalVelocity = Vector3.Dot(prevVertical, WorldUp);
            }

            // Reset jump count on landing
            bool isAirState = _currentState != null &&
                             (_currentState.StateType == MovementStateType.Jump ||
                              _currentState.StateType == MovementStateType.Fall);

            if (_context.IsGrounded && _context.VerticalVelocity < 0 && !isAirState)
            {
                _context.VerticalVelocity = kGroundedVerticalVelocity;
                _context.JumpCount = 0;
                _context.JumpPressed = false;

                if (!_context.WasGrounded)
                    OnLanded?.Invoke();
            }

            _previousWorldUp = WorldUp;

            // Update animation grounded parameter
            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                _context.AnimationController.SetBool(hash, _context.IsGrounded);
            }

            // Sync root motion
            if (_cachedTargetAnimator != null)
            {
                bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion;
                if (_cachedTargetAnimator.applyRootMotion != shouldUseRootMotion)
                    _cachedTargetAnimator.applyRootMotion = shouldUseRootMotion;
            }

            _context.MovementAuthority = MovementAuthority;

#if UNITY_EDITOR
            // Update runtime debug info
            _debugConfigAssigned = config != null;
            _debugConfigRunSpeed = config != null ? config.runSpeed : -1f;
            _debugCurrentSpeed = _context.CurrentSpeed;
            _debugDeltaTime = DeltaTime;
            _debugCurrentState = _currentState?.StateType.ToString() ?? "None";
            _debugInputDirection = _context.InputDirection;
            _debugInputMagnitude = math.length(_context.InputDirection);

            // Moving platform debug info
            _debugOnPlatform = _movingPlatform.isOnPlatform;
            _debugPlatformName = _movingPlatform.platformTransform != null ? _movingPlatform.platformTransform.name : "None";
            _debugPlatformVelocity = _movingPlatform.platformVelocity;
            _debugLocalPosition = _movingPlatform.localPosition;
            // _debugPlatformDeltaPos is set in ApplyMovingPlatform
            _debugGroundColliderName = _groundCollider != null ? _groundCollider.name : "None";
            _debugGroundHasRigidbody = _groundCollider != null && _groundCollider.attachedRigidbody != null;
            if (_groundCollider != null && config != null)
            {
                LayerMask platformMask = config.platformLayer != 0 ? config.platformLayer : config.groundLayer;
                _debugGroundLayerMatch = ((1 << _groundCollider.gameObject.layer) & platformMask) != 0;
            }
            else
            {
                _debugGroundLayerMatch = false;
            }
#endif
        }

        private void ExecuteStateMachine()
        {
            if (_currentState == null)
            {
                _currentState = StatePool<MovementStateBase>.GetState<IdleState>();
                _currentState?.OnEnter(ref _context);
                if (_currentState == null) return;
            }

            float3 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            bool shouldUseRootMotion = _context.UseRootMotion && useRootMotion && _cachedTargetAnimator != null && _cachedTargetAnimator.applyRootMotion;

            if (!shouldUseRootMotion)
            {
                Vector3 desiredVelocity = (Vector3)displacement / Mathf.Max(DeltaTime, 0.0001f);

                // Track momentum for air movement
                if (_context.IsGrounded)
                {
                    Vector3 horizontal = new Vector3(displacement.x, 0, displacement.z);
                    _lastGroundVelocity = horizontal / Mathf.Max(DeltaTime, 0.0001f);
                }
                else
                {
                    // Apply inherited momentum in air
                    Vector3 totalInherited = _inheritedPlatformVelocity + _lastGroundVelocity;
                    if (totalInherited.sqrMagnitude > kMinSqrMagnitude)
                    {
                        float inheritedSpeed = totalInherited.magnitude;
                        float inputSpeed = new Vector2(desiredVelocity.x, desiredVelocity.z).magnitude;

                        if (inheritedSpeed > inputSpeed)
                        {
                            desiredVelocity.x = totalInherited.x;
                            desiredVelocity.z = totalInherited.z;
                        }
                    }
                }

                PerformMovement(desiredVelocity, DeltaTime);

#if UNITY_EDITOR
                _debugActualVelocity = _velocity;
                _debugActualSpeed = _velocity.magnitude;
#endif
            }

            // Evaluate state transitions
            MovementStateBase nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
                RequestStateChangeInternal(nextState);

            // Update rotation
            if (math.lengthsq(_lookDirection) > kMinSqrMagnitude)
                UpdateRotation();
            else
                UpdateRotationForWorldUp();
        }

        private void OnAnimatorMove()
        {
            if (!useRootMotion || !_context.UseRootMotion || _cachedTargetAnimator == null)
                return;
            if (!_cachedTargetAnimator.applyRootMotion)
                return;

            Vector3 rootMotionDelta = _cachedTargetAnimator.deltaPosition;
            if (rootMotionDelta.sqrMagnitude > kMinSqrMagnitude)
            {
                Vector3 desiredVelocity = rootMotionDelta / Mathf.Max(DeltaTime, 0.0001f);
                desiredVelocity += WorldUp * _context.VerticalVelocity;
                PerformMovement(desiredVelocity, DeltaTime);
            }
        }

        #endregion

        #region Physics Movement

        private void PerformMovement(Vector3 desiredVelocity, float deltaTime)
        {
            _velocity = desiredVelocity;
            _collisionCount = 0;
            _hasLanded = false;

            // Resolve initial overlaps first
            ResolveOverlaps();

            // If grounded, project velocity onto ground plane
            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                _velocity = Vector3.ProjectOnPlane(_velocity, WorldUp);
            }

            Vector3 displacement = _velocity * deltaTime;

            // Skip movement logic if negligible, but still apply position
            // (important for moving platform - _updatedPosition may have been modified)
            if (displacement.sqrMagnitude < kMinMoveDistanceSqr)
            {
                // Even with small displacement, we need to snap to ground if grounded
                // This is critical for landing from stationary jumps
                if (_isGrounded && _isConstrainedToGround)
                {
                    AdjustGroundHeight();

                    // If we were in air (had vertical velocity), clear it and reset timer
                    if (_context.VerticalVelocity < 0)
                    {
                        _context.VerticalVelocity = kGroundedVerticalVelocity;
                        _unconstrainedTimer = 0;
                    }
                }

                // Apply position for kinematic rigidbody
                _rigidbody.position = _updatedPosition;
                transform.position = _updatedPosition;
                return;
            }

            // Pre-check: If grounded, probe ahead for non-walkable slopes and block early
            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                Vector3 probeDir = Vector3.ProjectOnPlane(displacement, WorldUp).normalized;
                if (probeDir.sqrMagnitude > kKindaSmallNumber)
                {
                    if (ProbeForNonWalkableSlope(probeDir, displacement.magnitude, out Vector3 blockingNormal))
                    {
                        // Block horizontal movement into non-walkable slope
                        displacement = Vector3.ProjectOnPlane(displacement, blockingNormal);
                        _velocity = Vector3.ProjectOnPlane(_velocity, blockingNormal);
                    }
                }
            }

            // If grounded, reorient displacement along ground normal (slope handling)
            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                displacement = ComputeTangentToSurface(displacement, _groundNormal);
            }

            Vector3 inputDisplacement = displacement;

            // Iterative collision detection and slide
            int iteration = 0;
            Vector3 prevNormal = Vector3.zero;
            int maxSlideCount = kMaxMovementIterations;

            while (maxSlideCount-- > 0 && displacement.sqrMagnitude > kMinMoveDistanceSqr)
            {
                bool collided = MovementSweepTest(_updatedPosition, _velocity, displacement, out CollisionResult collisionResult);

                if (!collided)
                {
                    // No collision - apply full displacement
                    _updatedPosition += displacement;
                    break;
                }

                // Move to hit point
                _updatedPosition += collisionResult.displacementToHit;
                displacement = collisionResult.remainingDisplacement;

                // If grounded and hit non-walkable, try step up
                if (_isGrounded && !collisionResult.isWalkable && collisionResult.hitLocation == HitLocation.Sides)
                {
                    if (config != null && config.stepHeight > 0f)
                    {
                        if (TryStepUp(ref displacement, collisionResult))
                        {
                            // Step succeeded, displacement consumed
                            displacement = Vector3.zero;
                            break;
                        }
                    }
                }

                // Check for landing (falling and hit ground)
                if (!_isGrounded && collisionResult.hitLocation == HitLocation.Below)
                {
                    if (collisionResult.isWalkable)
                    {
                        _hasLanded = true;
                        _landedVelocity = collisionResult.velocity;
                        _foundGround.SetFromSweepResult(true, true, _updatedPosition,
                            collisionResult.hitResult.distance, ref collisionResult.hitResult, collisionResult.surfaceNormal);
                    }
                    else
                    {
                        // Hit non-walkable ground - update ground info but don't land
                        _foundGround.SetFromSweepResult(true, false, _updatedPosition,
                            collisionResult.hitResult.distance, ref collisionResult.hitResult, collisionResult.surfaceNormal);
                    }
                }

                // Slide along the surface
                iteration = SlideAlongSurface(iteration, inputDisplacement, ref _velocity, ref displacement,
                    ref collisionResult, ref prevNormal);

                // Cache collision
                if (_collisionCount < kMaxHitCount)
                {
                    _collisionResults[_collisionCount++] = collisionResult;
                }
            }

            // Apply remaining displacement
            if (displacement.sqrMagnitude > kMinMoveDistanceSqr)
            {
                _updatedPosition += displacement;
            }

            // Snap to ground when grounded or just landed
            // Note: _hasLanded overrides _unconstrainedTimer check because landing detection is more reliable
            // during the transition from air to ground (e.g., after jump)
            bool canSnapToGround = _isConstrainedToGround &&
                ((_isGrounded && _unconstrainedTimer <= 0) || _hasLanded);

            if (canSnapToGround)
            {
                AdjustGroundHeight();

                // Discard vertical velocity component but preserve horizontal speed
                _velocity = Vector3.ProjectOnPlane(_velocity, WorldUp);

                // If we just landed, clear the unconstrained timer to re-enable ground constraints
                if (_hasLanded && _unconstrainedTimer > 0)
                {
                    _unconstrainedTimer = 0;
                }
            }

            // Apply slope sliding - on non-walkable ground OR when we hit non-walkable slopes during movement
            bool shouldSlide = _isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0;
            if (shouldSlide)
            {
                // Check if we're on non-walkable ground
                if (!_currentGround.isWalkable && _currentGround.hitGround)
                {
                    ApplySlopeSliding(deltaTime, _currentGround.surfaceNormal);
                }
                else
                {
                    // Check if we hit any non-walkable surfaces during movement
                    for (int i = 0; i < _collisionCount; i++)
                    {
                        ref CollisionResult col = ref _collisionResults[i];
                        if (!col.isWalkable && col.hitLocation == HitLocation.Sides)
                        {
                            // Check if this is a steep slope (has upward component)
                            float verticalDot = Vector3.Dot(col.surfaceNormal, WorldUp);
                            if (verticalDot > kHemisphereLimit && verticalDot < _minSlopeLimit)
                            {
                                ApplySlopeSliding(deltaTime, col.surfaceNormal);
                                break;
                            }
                        }
                    }
                }
            }

            // Apply final position
            _rigidbody.position = _updatedPosition;
            transform.position = _updatedPosition;
        }

        /// <summary>
        /// Sweeps the capsule along displacement vector, detecting collisions.
        /// </summary>
        private bool MovementSweepTest(Vector3 characterPosition, Vector3 inVelocity, Vector3 displacement,
            out CollisionResult collisionResult)
        {
            collisionResult = default;

            Vector3 sweepDirection = displacement.normalized;
            float sweepDistance = displacement.magnitude;

            if (sweepDistance < kKindaSmallNumber)
                return false;

            float sweepRadius = _radius - kContactOffset;

            // Compute capsule endpoints
            Vector3 top = characterPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = characterPosition + transform.rotation * _capsuleBottomCenter;

            int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, sweepRadius, sweepDirection, _hits,
                sweepDistance + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore);

            if (hitCount == 0)
                return false;

            // Find closest valid hit
            float closestDist = float.MaxValue;
            int closestIdx = -1;
            bool startPenetrating = false;

            for (int i = 0; i < hitCount; i++)
            {
                ref RaycastHit hit = ref _hits[i];

                if (ShouldIgnore(hit.collider))
                    continue;

                if (hit.distance <= 0f)
                {
                    startPenetrating = true;
                    continue;
                }

                if (hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    closestIdx = i;
                }
            }

            if (closestIdx < 0)
            {
                // Only penetration, no valid hit
                return startPenetrating;
            }

            RaycastHit hitResult = _hits[closestIdx];
            HitLocation hitLocation = ComputeHitLocation(hitResult.normal);

            // Compute actual displacement to hit
            float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
            Vector3 displacementToHit = sweepDirection * safeDistance;
            Vector3 remainingDisplacement = displacement - displacementToHit;

            // Get surface normal (may differ from hit normal for geometry)
            Vector3 surfaceNormal = hitResult.normal;
            bool isWalkable = false;
            bool hitGround = hitLocation == HitLocation.Below;

            if (hitGround)
            {
                surfaceNormal = FindSurfaceNormal(hitResult);
                isWalkable = IsWalkable(hitResult.collider, surfaceNormal);

                // If we're grounded and hit a non-walkable "ground" while moving horizontally,
                // treat it as a side hit (wall/barrier) to block movement
                if (_isGrounded && !isWalkable)
                {
                    Vector3 horizontalDisp = Vector3.ProjectOnPlane(displacement, WorldUp);
                    if (horizontalDisp.sqrMagnitude > kKindaSmallNumber)
                    {
                        // Check if the slope opposes our movement
                        Vector3 horizontalNormal = Vector3.ProjectOnPlane(surfaceNormal, WorldUp);
                        if (horizontalNormal.sqrMagnitude > kKindaSmallNumber &&
                            Vector3.Dot(horizontalNormal.normalized, horizontalDisp.normalized) < 0f)
                        {
                            hitLocation = HitLocation.Sides;
                        }
                    }
                }
            }

            collisionResult = new CollisionResult
            {
                startPenetrating = startPenetrating,
                hitLocation = hitLocation,
                isWalkable = isWalkable,
                position = characterPosition + displacementToHit,
                velocity = inVelocity,
                point = hitResult.point,
                normal = hitResult.normal,
                surfaceNormal = surfaceNormal,
                displacementToHit = displacementToHit,
                remainingDisplacement = remainingDisplacement,
                collider = hitResult.collider,
                hitResult = hitResult
            };

            // Adjust blocking normal for non-walkable surfaces
            collisionResult.normal = ComputeBlockingNormal(collisionResult.normal, isWalkable);

            return true;
        }

        /// <summary>
        /// Computes where on the capsule a hit occurred based on normal.
        /// </summary>
        private HitLocation ComputeHitLocation(Vector3 normal)
        {
            float verticalComponent = Vector3.Dot(normal, WorldUp);

            if (verticalComponent > kHemisphereLimit)
                return HitLocation.Below;

            return verticalComponent < -kHemisphereLimit ? HitLocation.Above : HitLocation.Sides;
        }

        /// <summary>
        /// Computes the blocking normal for a hit surface.
        /// For non-walkable surfaces when grounded, projects to horizontal to prevent climbing.
        /// </summary>
        private Vector3 ComputeBlockingNormal(Vector3 inNormal, bool isWalkable)
        {
            if ((_isGrounded || _hasLanded) && !isWalkable)
            {
                // Project to horizontal to block vertical movement up unwalkable slopes
                Vector3 actualGroundNormal = _hasLanded ? _foundGround.surfaceNormal : _groundNormal;

                Vector3 forward = Vector3.Cross(actualGroundNormal, inNormal);
                Vector3 blockingNormal = Vector3.Cross(forward, WorldUp);

                if (blockingNormal.sqrMagnitude > kKindaSmallNumber)
                {
                    blockingNormal = blockingNormal.normalized;
                    if (Vector3.Dot(blockingNormal, inNormal) < 0f)
                        blockingNormal = -blockingNormal;
                    return blockingNormal;
                }
            }

            return inNormal;
        }

        /// <summary>
        /// Slides movement along a surface after collision.
        /// </summary>
        private int SlideAlongSurface(int iteration, Vector3 inputDisplacement, ref Vector3 inVelocity,
            ref Vector3 displacement, ref CollisionResult inHit, ref Vector3 prevNormal)
        {
            // Compute blocking normal for non-walkable surfaces
            // This prevents the character from being pushed up steep slopes
            Vector3 hitNormal = ComputeBlockingNormal(inHit.normal, inHit.isWalkable);
            inHit.normal = hitNormal;

            if (inHit.isWalkable && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                // Walkable slope - slide along surface
                inVelocity = ComputeSlideVector(inVelocity, hitNormal, true);
                displacement = ComputeSlideVector(displacement, hitNormal, true);
            }
            else
            {
                // Non-walkable surface or in air
                if (iteration == 0)
                {
                    inVelocity = ComputeSlideVector(inVelocity, hitNormal, inHit.isWalkable);
                    displacement = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                    iteration++;
                }
                else if (iteration == 1)
                {
                    // Hit second surface - find crease between surfaces
                    Vector3 crease = Vector3.Cross(prevNormal, hitNormal);

                    if (crease.sqrMagnitude > kKindaSmallNumber)
                    {
                        crease = crease.normalized;

                        Vector3 oVel = Vector3.ProjectOnPlane(inputDisplacement, crease);
                        Vector3 nVel = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                        nVel = Vector3.ProjectOnPlane(nVel, crease);

                        if (Vector3.Dot(oVel, nVel) <= 0f || Vector3.Dot(prevNormal, hitNormal) < 0f)
                        {
                            // Opposing surfaces - slide along crease only
                            inVelocity = Vector3.Project(inVelocity, crease);
                            displacement = Vector3.Project(displacement, crease);
                            iteration++;
                        }
                        else
                        {
                            inVelocity = ComputeSlideVector(inVelocity, hitNormal, inHit.isWalkable);
                            displacement = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                        }
                    }
                    else
                    {
                        inVelocity = ComputeSlideVector(inVelocity, hitNormal, inHit.isWalkable);
                        displacement = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                    }
                }
                else
                {
                    // Hit too many surfaces - stop
                    inVelocity = Vector3.zero;
                    displacement = Vector3.zero;
                }

                prevNormal = hitNormal;
            }

            return iteration;
        }

        /// <summary>
        /// Computes slide vector along a surface.
        /// </summary>
        private Vector3 ComputeSlideVector(Vector3 displacement, Vector3 normal, bool isWalkable)
        {
            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                if (isWalkable)
                {
                    // Slide along walkable slope - reorient along surface
                    return ComputeTangentToSurface(displacement, normal);
                }
                else
                {
                    // Non-walkable surface (steep slope)
                    // Find the direction along the intersection of ground plane and blocking normal
                    Vector3 right = Vector3.Cross(normal, _groundNormal);
                    Vector3 up = Vector3.Cross(right, normal);

                    // Project displacement onto blocking plane
                    Vector3 result = Vector3.ProjectOnPlane(displacement, normal);

                    // Then reorient along the intersection with ground (tangent to up)
                    result = ComputeTangentToSurface(result, up);

                    return result;
                }
            }
            else
            {
                // In air - standard slide
                if (isWalkable)
                {
                    // Project displacement onto horizontal, then onto surface
                    if (_isConstrainedToGround)
                        displacement = Vector3.ProjectOnPlane(displacement, WorldUp);
                    return Vector3.ProjectOnPlane(displacement, normal);
                }
                else
                {
                    Vector3 result = Vector3.ProjectOnPlane(displacement, normal);

                    // Prevent slope boosting when falling
                    if (_isConstrainedToGround)
                        result = HandleSlopeBoosting(result, displacement, normal);

                    return result;
                }
            }
        }

        /// <summary>
        /// Prevents slope boosting - limits upward deflection when falling.
        /// </summary>
        private Vector3 HandleSlopeBoosting(Vector3 slideResult, Vector3 displacement, Vector3 normal)
        {
            float yResult = Vector3.Dot(slideResult, WorldUp);
            if (yResult > 0f)
            {
                float yLimit = Vector3.Dot(displacement, WorldUp);
                if (yResult - yLimit > kKindaSmallNumber)
                {
                    if (yLimit > 0f)
                    {
                        float upPercent = yLimit / yResult;
                        slideResult *= upPercent;
                    }
                    else
                    {
                        slideResult = Vector3.zero;
                    }

                    // Redistribute remaining horizontal displacement parallel to impact normal
                    Vector3 lateralRemainder = Vector3.ProjectOnPlane(displacement - slideResult, WorldUp);
                    Vector3 lateralNormal = Vector3.ProjectOnPlane(normal, WorldUp).normalized;
                    Vector3 adjust = Vector3.ProjectOnPlane(lateralRemainder, lateralNormal);
                    slideResult += adjust;
                }
            }
            return slideResult;
        }

        /// <summary>
        /// Computes displacement tangent to a surface (for slope walking).
        /// </summary>
        private Vector3 ComputeTangentToSurface(Vector3 displacement, Vector3 surfaceNormal)
        {
            Vector3 right = Vector3.Cross(displacement, surfaceNormal);
            if (right.sqrMagnitude < kKindaSmallNumber)
                return displacement;

            Vector3 tangent = Vector3.Cross(surfaceNormal, right);
            return tangent.normalized * displacement.magnitude;
        }

        /// <summary>
        /// Attempts to step up onto an obstacle.
        /// </summary>
        private bool TryStepUp(ref Vector3 displacement, CollisionResult obstacleHit)
        {
            if (config == null || config.stepHeight <= 0f) return false;

            float stepHeight = config.stepHeight;
            Vector3 forwardDir = Vector3.ProjectOnPlane(displacement, WorldUp).normalized;

            if (forwardDir.sqrMagnitude < kKindaSmallNumber)
                return false;

            float forwardDist = displacement.magnitude;

            // Step 1: Move up
            Vector3 stepUpPos = _updatedPosition + WorldUp * stepHeight;

            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
            float sweepRadius = _radius - kContactOffset;

            if (Physics.CapsuleCast(bottom, top, sweepRadius, WorldUp, out RaycastHit upHit, stepHeight, CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(upHit.collider))
                {
                    stepHeight = Mathf.Max(0f, upHit.distance - kContactOffset);
                    if (stepHeight < 0.01f)
                        return false;
                    stepUpPos = _updatedPosition + WorldUp * stepHeight;
                }
            }

            // Step 2: Move forward at step height
            Vector3 stepTop = stepUpPos + transform.rotation * _capsuleTopCenter;
            Vector3 stepBottom = stepUpPos + transform.rotation * _capsuleBottomCenter;

            float actualForwardDist = forwardDist;
            if (Physics.CapsuleCast(stepBottom, stepTop, sweepRadius, forwardDir, out RaycastHit forwardHit, forwardDist + _radius, CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(forwardHit.collider))
                {
                    if (forwardHit.distance < _radius * 0.5f)
                        return false;
                    actualForwardDist = Mathf.Max(0f, forwardHit.distance - kContactOffset);
                }
            }

            Vector3 stepForwardPos = stepUpPos + forwardDir * actualForwardDist;

            // Step 3: Move down to find ground
            Vector3 finalTop = stepForwardPos + transform.rotation * _capsuleTopCenter;
            Vector3 finalBottom = stepForwardPos + transform.rotation * _capsuleBottomCenter;
            float downDist = stepHeight + kMaxGroundDistance * 2f;

            if (!Physics.CapsuleCast(finalBottom, finalTop, sweepRadius, -WorldUp, out RaycastHit downHit, downDist, config.groundLayer, QueryTriggerInteraction.Ignore))
                return false;

            if (ShouldIgnore(downHit.collider))
                return false;

            Vector3 surfaceNormal = FindSurfaceNormal(downHit);
            if (!IsWalkable(downHit.collider, surfaceNormal))
                return false;

            // Apply step
            float downOffset = Mathf.Max(0f, downHit.distance - kContactOffset);
            _updatedPosition = stepForwardPos - WorldUp * downOffset;
            _groundNormal = surfaceNormal;

            displacement = Vector3.zero;
            return true;
        }

        /// <summary>
        /// Applies sliding force on non-walkable slopes.
        /// Character will slide down slopes steeper than slopeLimit.
        /// </summary>
        private void ApplySlopeSliding(float deltaTime, Vector3 slopeNormal)
        {
            if (config == null)
                return;

            float slopeDot = Vector3.Dot(slopeNormal, WorldUp);
            float slopeAngle = Mathf.Acos(Mathf.Clamp01(slopeDot)) * Mathf.Rad2Deg;

            // Only slide on slopes steeper than limit
            if (slopeAngle <= config.slopeLimit)
                return;

            // Calculate slide direction (down the slope, perpendicular to normal in vertical plane)
            Vector3 slideDirection = Vector3.ProjectOnPlane(-WorldUp, slopeNormal);
            if (slideDirection.sqrMagnitude < kKindaSmallNumber)
                return;
            slideDirection = slideDirection.normalized;

            // Slide speed increases with slope angle
            // At slopeLimit: 0, at 90 degrees: full gravity
            float angleRatio = (slopeAngle - config.slopeLimit) / (90f - config.slopeLimit);
            angleRatio = Mathf.Clamp01(angleRatio);

            // Apply gravity-based sliding
            float slideAcceleration = Mathf.Abs(config.gravity) * angleRatio;
            float slideSpeed = slideAcceleration * deltaTime;

            Vector3 slideDisplacement = slideDirection * slideSpeed;

            // Check for obstacles in slide direction
            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
            float sweepRadius = _radius - kContactOffset;

            if (Physics.CapsuleCast(bottom, top, sweepRadius, slideDirection, out RaycastHit slideHit,
                slideSpeed + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(slideHit.collider))
                {
                    // Hit something - limit slide
                    float safeDist = Mathf.Max(0f, slideHit.distance - kContactOffset);
                    slideDisplacement = slideDirection * safeDist;
                }
            }

            if (slideDisplacement.sqrMagnitude > kKindaSmallNumber)
            {
                _updatedPosition += slideDisplacement;
            }
        }

        /// <summary>
        /// Probes ahead in the horizontal direction to detect non-walkable slopes.
        /// Returns true if a non-walkable slope is found, with the blocking normal.
        /// </summary>
        private bool ProbeForNonWalkableSlope(Vector3 probeDirection, float probeDistance, out Vector3 blockingNormal)
        {
            blockingNormal = Vector3.zero;

            // Use a short probe distance to check for immediate obstacles
            float actualProbeDistance = Mathf.Max(_radius + kContactOffset, probeDistance);

            // Compute capsule points - raise slightly to avoid ground
            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
            bottom += WorldUp * (kContactOffset * 2f); // Raise to avoid current ground

            float sweepRadius = _radius - kContactOffset;

            int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, sweepRadius, probeDirection,
                _hits, actualProbeDistance, CollisionLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                ref RaycastHit hit = ref _hits[i];

                if (ShouldIgnore(hit.collider))
                    continue;

                if (hit.distance <= 0f)
                    continue;

                // Check if this is a steep slope (not walkable) opposing our movement
                Vector3 surfaceNormal = FindSurfaceNormal(hit);
                float verticalDot = Vector3.Dot(surfaceNormal, WorldUp);

                // Is it a slope/ground (normal points somewhat up)?
                if (verticalDot > kHemisphereLimit)
                {
                    // Is it non-walkable (too steep)?
                    if (verticalDot < _minSlopeLimit)
                    {
                        // Does it oppose our movement?
                        Vector3 horizontalNormal = Vector3.ProjectOnPlane(surfaceNormal, WorldUp);
                        if (horizontalNormal.sqrMagnitude > kKindaSmallNumber)
                        {
                            if (Vector3.Dot(horizontalNormal.normalized, probeDirection) < -0.1f)
                            {
                                // This is a non-walkable slope blocking our path
                                // Compute blocking normal (horizontal component only)
                                blockingNormal = horizontalNormal.normalized;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        #endregion

        #region Ground Detection

        /// <summary>
        /// Performs comprehensive ground detection using sweep and raycast.
        /// </summary>
        private void FindGround()
        {
            _wasGrounded = _isGrounded;
            _isGrounded = false;
            _groundNormal = WorldUp;
            _groundCollider = null;
            _groundDistance = float.MaxValue;
            _currentGround.Clear();

            if (config == null) return;

            // Compute sweep distance - larger when previously grounded
            float heightCheckAdjust = _wasGrounded ? kMaxGroundDistance + kKindaSmallNumber : -kMaxGroundDistance;
            float sweepDistance = Mathf.Max(kMaxGroundDistance, config.stepHeight + heightCheckAdjust);

            ComputeGroundDistance(_updatedPosition, sweepDistance, out _currentGround);

            if (_currentGround.isWalkableGround)
            {
                _isGrounded = true;
                _groundNormal = _currentGround.surfaceNormal;
                _groundPoint = _currentGround.point;
                _groundCollider = _currentGround.collider;
                _groundDistance = _currentGround.groundDistance;
                _isOnNonWalkableSlope = false;
            }
            else if (_currentGround.hitGround)
            {
                // Hit non-walkable ground (steep slope)
                // Treat as NOT grounded so character will slide/fall
                _groundNormal = _currentGround.surfaceNormal;
                _groundPoint = _currentGround.point;
                _groundCollider = _currentGround.collider;
                _groundDistance = _currentGround.groundDistance;

                // NOT grounded on non-walkable slopes - this makes character fall/slide
                _isGrounded = false;
                _isOnNonWalkableSlope = true;
            }
            else
            {
                _isOnNonWalkableSlope = false;
            }
        }

        /// <summary>
        /// Computes ground distance using sweep test with raycast fallback.
        /// </summary>
        private void ComputeGroundDistance(Vector3 characterPosition, float sweepDistance, out FindGroundResult outGroundResult)
        {
            outGroundResult = default;

            float capsuleRadius = _radius;
            float capsuleHalfHeight = _height * 0.5f;

            // Shrink capsule slightly to avoid false positives
            const float kShrinkScale = 0.9f;
            float shrinkHeight = (capsuleHalfHeight - capsuleRadius) * (1f - kShrinkScale);
            float actualSweepRadius = capsuleRadius - kContactOffset;
            float actualSweepDistance = sweepDistance + shrinkHeight;

            // Compute capsule points
            Vector3 top = characterPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = characterPosition + transform.rotation * _capsuleBottomCenter;

            // Shrink bottom up slightly
            bottom += WorldUp * shrinkHeight;

            bool foundGround = Physics.CapsuleCast(bottom, top, actualSweepRadius, -WorldUp, out RaycastHit hitResult,
                actualSweepDistance, config.groundLayer, QueryTriggerInteraction.Ignore);

            if (foundGround)
            {
                if (ShouldIgnore(hitResult.collider))
                {
                    foundGround = false;
                }
                else
                {
                    // Check if hit is on bottom hemisphere
                    HitLocation hitLocation = ComputeHitLocation(hitResult.normal);
                    if (hitLocation != HitLocation.Below)
                    {
                        foundGround = false;
                    }
                }
            }

            if (foundGround)
            {
                // Adjust distance for shrink
                float adjustedDistance = Mathf.Max(-kMaxGroundDistance, hitResult.distance - shrinkHeight);

                Vector3 surfaceNormal = FindSurfaceNormal(hitResult);
                bool isWalkable = IsWalkable(hitResult.collider, surfaceNormal);

                outGroundResult.SetFromSweepResult(true, isWalkable, characterPosition,
                    adjustedDistance, ref hitResult, surfaceNormal);
            }
            else
            {
                // Fallback to raycast for more accurate ground detection
                float raycastDistance = sweepDistance + capsuleRadius;

                // Ray from capsule center bottom
                float capsuleBottomY = _capsuleCenter.y - capsuleHalfHeight;
                Vector3 rayOrigin = characterPosition + WorldUp * (capsuleBottomY + capsuleRadius);

                if (Physics.Raycast(rayOrigin, -WorldUp, out RaycastHit rayHit, raycastDistance,
                    config.groundLayer, QueryTriggerInteraction.Ignore))
                {
                    if (!ShouldIgnore(rayHit.collider))
                    {
                        Vector3 surfaceNormal = FindSurfaceNormal(rayHit);
                        bool isWalkable = IsWalkable(rayHit.collider, surfaceNormal);

                        // Ground distance from capsule bottom sphere
                        float groundDist = rayHit.distance - capsuleRadius;

                        outGroundResult.SetFromRaycastResult(true, isWalkable, characterPosition,
                            groundDist, rayHit.distance, ref rayHit, surfaceNormal);
                    }
                }
            }
        }

        /// <summary>
        /// Adjusts character height to maintain proper ground distance.
        /// Uses sweep test to validate movement before applying.
        /// </summary>
        private void AdjustGroundHeight()
        {
            // If we have a ground check that hasn't hit anything walkable, don't adjust height.
            if (!_currentGround.isWalkableGround || !_isConstrainedToGround)
                return;

            float lastGroundDistance = _currentGround.groundDistance;

            if (_currentGround.isRaycastResult)
            {
                if (lastGroundDistance < kMinGroundDistance && _currentGround.raycastDistance >= kMinGroundDistance)
                {
                    // This would cause us to scale unwalkable walls
                    return;
                }
                else
                {
                    // Falling back to a raycast means the sweep was unwalkable (or in penetration).
                    // Use the ray distance for the vertical adjustment.
                    lastGroundDistance = _currentGround.raycastDistance;
                }
            }

            // Move up or down to maintain ground height.
            if (lastGroundDistance < kMinGroundDistance || lastGroundDistance > kMaxGroundDistance)
            {
                float initialY = Vector3.Dot(_updatedPosition, WorldUp);
                float moveDistance = kAvgGroundDistance - lastGroundDistance;

                Vector3 displacement = WorldUp * moveDistance;
                Vector3 sweepDirection = displacement.normalized;
                float sweepDistance = displacement.magnitude;

                // Compute capsule endpoints for sweep
                Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
                Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
                float sweepRadius = _radius - kContactOffset;

                // Perform sweep test to validate movement
                bool hit = Physics.CapsuleCast(bottom, top, sweepRadius, sweepDirection, out RaycastHit hitResult,
                    sweepDistance + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore);

                // Check for penetration
                bool startPenetrating = false;
                if (hit && hitResult.distance <= 0f)
                {
                    startPenetrating = true;
                    hit = false;
                }

                if (!hit && !startPenetrating)
                {
                    // No collision, apply full displacement
                    _updatedPosition += displacement;
                    _currentGround.groundDistance += moveDistance;
                }
                else if (hit && moveDistance > 0.0f)
                {
                    // Moving up - apply displacement up to hit
                    float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
                    _updatedPosition += sweepDirection * safeDistance;

                    float currentY = Vector3.Dot(_updatedPosition, WorldUp);
                    _currentGround.groundDistance += currentY - initialY;
                }
                else if (hit && moveDistance < 0.0f)
                {
                    // Moving down - apply displacement up to hit
                    float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
                    _updatedPosition += sweepDirection * safeDistance;

                    float currentY = Vector3.Dot(_updatedPosition, WorldUp);
                    _currentGround.groundDistance = currentY - initialY;
                }
            }
        }

        /// <summary>
        /// Gets the true surface normal, handling terrain and mesh surfaces.
        /// </summary>
        private Vector3 FindSurfaceNormal(RaycastHit hit)
        {
#if UNITY_TERRAIN_PHYSICS
            if (hit.collider is TerrainCollider terrain)
            {
                Vector3 localPoint = terrain.transform.InverseTransformPoint(hit.point);
                TerrainData data = terrain.terrainData;
                return data.GetInterpolatedNormal(localPoint.x / data.size.x, localPoint.z / data.size.z);
            }
#endif

            // For mesh surfaces, do a raycast from above for accurate normal
            Vector3 rayOrigin = hit.point + WorldUp * 0.1f;
            if (Physics.Raycast(rayOrigin, -WorldUp, out RaycastHit rayHit, 0.2f, config.groundLayer, QueryTriggerInteraction.Ignore))
            {
                if (rayHit.collider == hit.collider)
                    return rayHit.normal;
            }

            return hit.normal;
        }

        /// <summary>
        /// Determines if a surface is walkable based on slope angle.
        /// </summary>
        private bool IsWalkable(Collider inCollider, Vector3 inNormal)
        {
            // Must be on bottom hemisphere
            float verticalComponent = Vector3.Dot(inNormal, WorldUp);
            if (verticalComponent <= kHemisphereLimit)
                return false;

            // Check against slope limit (use >= to include slopes at exactly the limit)
            return verticalComponent >= _minSlopeLimit;
        }

        #endregion

        #region Collision Queries

        /// <summary>
        /// Resolves overlapping colliders by depenetrating the character.
        /// </summary>
        private void ResolveOverlaps()
        {
            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;

            int overlapCount = Physics.OverlapCapsuleNonAlloc(bottom, top, _radius * 0.99f, _overlaps,
                CollisionLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlapCount; i++)
            {
                Collider other = _overlaps[i];

                if (ShouldIgnore(other))
                    continue;

                // Use ComputePenetration for accurate depenetration
                if (Physics.ComputePenetration(
                    _capsuleCollider, _updatedPosition, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float distance))
                {
                    // Apply separation with safety margin
                    _updatedPosition += direction * (distance + kPenetrationOffset);
                }
            }
        }

        private bool ShouldIgnore(Collider other)
        {
            if (other == _capsuleCollider) return true;
            if (other.attachedRigidbody == _rigidbody) return true;
            if (_ignoredColliders.Contains(other)) return true;
            return false;
        }

        public void IgnoreCollision(Collider other, bool ignore = true)
        {
            if (ignore)
                _ignoredColliders.Add(other);
            else
                _ignoredColliders.Remove(other);
        }

        #endregion

        #region Moving Platform

        private void ApplyMovingPlatform()
        {
            if (config == null || !config.enableMovingPlatform || !_movingPlatform.isOnPlatform)
                return;

            if (_movingPlatform.platformTransform == null)
            {
                _movingPlatform.Clear();
                return;
            }

            // When smoothPlatformFollow is enabled, LateUpdate handles the visual smoothing
            // FixedUpdate still needs to apply delta for correct physics, but we use a simpler approach
            Vector3 targetWorldPos = _movingPlatform.platformTransform.TransformPoint(_movingPlatform.localPosition);
            Vector3 deltaPos = targetWorldPos - transform.position;

#if UNITY_EDITOR
            _debugPlatformDeltaPos = deltaPos;
#endif

            // Apply delta if significant
            if (deltaPos.sqrMagnitude > 0.0000001f && deltaPos.sqrMagnitude < 1f)
            {
                _updatedPosition += deltaPos;
            }

            if (config.inheritPlatformRotation)
            {
                Quaternion deltaRot = _movingPlatform.GetPlatformDeltaRotation(transform);
                if (Quaternion.Angle(Quaternion.identity, deltaRot) > 0.01f)
                {
                    transform.rotation = deltaRot * transform.rotation;
                    _currentRotation = transform.rotation;
                }
            }
        }

        private void UpdateMovingPlatformTracking()
        {
            if (config == null || !config.enableMovingPlatform)
            {
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }

            if (!_context.IsGrounded)
            {
                if (_movingPlatform.isOnPlatform && config.inheritPlatformMomentum)
                    _inheritedPlatformVelocity = _movingPlatform.platformVelocity;
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }
            else
            {
                _inheritedPlatformVelocity = Vector3.zero;
            }

            if (_groundCollider != null)
            {
                Rigidbody groundRb = _groundCollider.attachedRigidbody;
                LayerMask platformMask = config.platformLayer != 0 ? config.platformLayer : config.groundLayer;
                bool isValidPlatform = groundRb != null && ((1 << _groundCollider.gameObject.layer) & platformMask) != 0;

                if (isValidPlatform)
                {
                    if (_movingPlatform.platform != groundRb)
                    {
                        // New platform - set up tracking
                        _movingPlatform.SetPlatform(groundRb, transform);
                    }
                    // Existing platform - delta was already calculated in ApplyMovingPlatform
                    return;
                }
            }

            if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
        }

        /// <summary>
        /// Updates the character's local position on the moving platform.
        /// Called AFTER character movement to ensure next frame uses the correct offset.
        /// </summary>
        private void UpdateMovingPlatformLocalPosition()
        {
            if (!_movingPlatform.isOnPlatform || _movingPlatform.platformTransform == null)
                return;

            // Update local position based on character's final position this frame
            _movingPlatform.UpdateLocalPosition(_updatedPosition);
            _movingPlatform.UpdateLocalRotation(transform.rotation);
        }

        #endregion

        #region Forces

        private void ApplyPendingForces()
        {
            if (_pendingImpulse.sqrMagnitude > kMinSqrMagnitude)
            {
                _velocity += _pendingImpulse;
                _pendingImpulse = Vector3.zero;
            }

            if (_pendingForce.sqrMagnitude > kMinSqrMagnitude)
            {
                _velocity += _pendingForce * DeltaTime;
                _pendingForce = Vector3.zero;
            }
        }

        public void LaunchCharacter(Vector3 velocity, bool overrideXY = true, bool overrideZ = true)
        {
            if (overrideXY)
            {
                _pendingImpulse.x = velocity.x;
                _pendingImpulse.z = velocity.z;
            }
            else
            {
                _pendingImpulse.x += velocity.x;
                _pendingImpulse.z += velocity.z;
            }

            if (overrideZ)
                _context.VerticalVelocity = velocity.y;
            else
                _context.VerticalVelocity += velocity.y;

            PauseGroundConstraint(0.1f);
        }

        public void AddForce(Vector3 force)
        {
            _pendingForce += force;
        }

        public void AddExplosionForce(float force, Vector3 origin, float radius, float upwardsModifier = 0.5f)
        {
            Vector3 direction = transform.position - origin;
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

        #region Rotation

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

            float rotationSpeed = _context.GetAttributeValue(MovementAttribute.RotationSpeed, config.rotationSpeed);
            float t = rotationSpeed * DeltaTime;

            if (angleDeg > 135f)
                t *= 0.25f;

            _currentRotation = math.slerp(_currentRotation, targetRotation, t);
            transform.rotation = _currentRotation;
        }

        private void UpdateRotationForWorldUp()
        {
            if (config == null) return;

            float3 currentUp = math.mul(_currentRotation, UnityUpVector);
            float3 worldUp = _context.WorldUp;

            if (math.lengthsq(currentUp - worldUp) > 0.001f)
            {
                Quaternion toUp = Quaternion.FromToRotation(currentUp, worldUp);
                quaternion targetRotation = math.mul((quaternion)toUp, _currentRotation);
                float rotationSpeed = _context.GetAttributeValue(MovementAttribute.RotationSpeed, config.rotationSpeed);
                _currentRotation = math.slerp(_currentRotation, targetRotation, rotationSpeed * DeltaTime);
                transform.rotation = _currentRotation;
            }
        }

        #endregion

        #region Input API

        public void SetInputDirection(Vector3 localDirection)
        {
            _context.InputDirection = localDirection;
        }

        public void SetJumpPressed(bool pressed)
        {
            bool wasPressed = _context.JumpPressed;
            _context.JumpPressed = pressed;

            if (pressed && !wasPressed && _context.IsGrounded)
                RequestStateChange(MovementStateType.Jump);
        }

        public void SetSprintHeld(bool held) => _context.SprintHeld = held;
        public void SetCrouchHeld(bool held) => _context.CrouchHeld = held;

        public void SetLookDirection(Vector3 worldDirection)
        {
            if (math.lengthsq(worldDirection) > kMinSqrMagnitude)
                _lookDirection = math.normalize(worldDirection);
            else
                _lookDirection = float3.zero;
        }

        public void ClearLookDirection() => _lookDirection = float3.zero;

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

        public void SetRotation(Vector3 worldDirection, bool immediate = false)
        {
            if (math.lengthsq(worldDirection) <= kMinSqrMagnitude)
            {
                CLogger.LogWarning("[MovementComponent] Cannot set rotation with zero direction.");
                return;
            }

            Vector3 normalizedDir = worldDirection.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(normalizedDir, WorldUp);

            if (immediate)
            {
                _currentRotation = targetRotation;
                transform.rotation = targetRotation;
            }
            else
            {
                SetLookDirection(normalizedDir);
            }
        }

        #endregion

        #region State Management

        public bool RequestStateChange(MovementStateType targetStateType, object context = null)
        {
            if (MovementAuthority != null && !MovementAuthority.CanEnterState(targetStateType, context))
                return false;

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
                PauseGroundConstraint(0.15f);
            }
            else if (oldStateType == MovementStateType.Fall && _context.IsGrounded)
            {
                OnLanded?.Invoke();
            }
        }

        private MovementStateBase GetStateByType(MovementStateType stateType)
        {
            return stateType switch
            {
                MovementStateType.Idle => StatePool<MovementStateBase>.GetState<IdleState>(),
                MovementStateType.Walk => StatePool<MovementStateBase>.GetState<WalkState>(),
                MovementStateType.Run => StatePool<MovementStateBase>.GetState<RunState>(),
                MovementStateType.Sprint => StatePool<MovementStateBase>.GetState<SprintState>(),
                MovementStateType.Crouch => StatePool<MovementStateBase>.GetState<CrouchState>(),
                MovementStateType.Jump => StatePool<MovementStateBase>.GetState<JumpState>(),
                MovementStateType.Fall => StatePool<MovementStateBase>.GetState<FallState>(),
                _ => null
            };
        }

        #endregion

        #region Gap Bridging

        private bool TryBridgeGap()
        {
            if (config == null || !config.enableGapBridging) return false;
            if (_gapBridgeCooldown > 0) return false;
            if (_context.CurrentSpeed < config.minSpeedForGapBridge) return false;

            Vector3 moveDir = new Vector3(_context.CurrentVelocity.x, 0, _context.CurrentVelocity.z).normalized;
            if (moveDir.sqrMagnitude < 0.01f) return false;

            Vector3 frontPoint = transform.position + moveDir * _radius;
            if (Physics.Raycast(frontPoint, Vector3.down, config.groundedCheckDistance * 3, config.groundLayer))
                return false;

            for (float dist = 0.5f; dist <= config.maxGapDistance; dist += 0.3f)
            {
                Vector3 checkPoint = frontPoint + moveDir * dist + Vector3.up * 0.5f;

                if (Physics.Raycast(checkPoint, Vector3.down, out RaycastHit hit,
                    1f + config.maxGapHeightDiff, config.groundLayer))
                {
                    float heightDiff = Mathf.Abs(hit.point.y - transform.position.y);
                    if (heightDiff <= config.maxGapHeightDiff)
                    {
                        float jumpHeight = 0.3f + heightDiff * 0.5f;
                        float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(config.gravity) * jumpHeight);

                        _context.VerticalVelocity = jumpVelocity;
                        _gapBridgeCooldown = 0.3f;

                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region External Movement

        public void MoveWithVelocity(Vector3 worldVelocity)
        {
            if (config == null) return;

            SetInputDirection(worldVelocity.normalized);

            if (useRootMotion && _cachedTargetAnimator != null)
            {
                _context.UseRootMotion = true;
                _cachedTargetAnimator.applyRootMotion = true;

                float speed = worldVelocity.magnitude;
                if (_context.AnimationController != null && _context.AnimationController.IsValid)
                {
                    int hash = AnimationParameterCache.GetHash(config.movementSpeedParameter);
                    _context.AnimationController.SetFloat(hash, speed);
                }
            }
        }

        public void SetUseRootMotion(bool enable)
        {
            useRootMotion = enable;
            _context.UseRootMotion = enable;

            if (_cachedTargetAnimator != null)
                _cachedTargetAnimator.applyRootMotion = enable;
            else if (enable && _isUsingAnimancer && !_isUsingHybridAnimancer)
                CLogger.LogWarning("[MovementComponent] Root motion requires HybridAnimancerComponent.");
        }

        #endregion

#if GAMEPLAY_FRAMEWORK_PRESENT
        void IInitialRotationSettable.SetInitialRotation(Quaternion rotation, bool immediate)
        {
            SetRotation(rotation, immediate);
        }
#endif

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || config == null) return;

            Vector3 currentWorldUp = worldUpSource != null ? worldUpSource.up : (WorldUp != Vector3.zero ? WorldUp : Vector3.up);
            Vector3 pos = Application.isPlaying ? _updatedPosition : transform.position;

            CapsuleCollider capsule = _capsuleCollider != null ? _capsuleCollider : GetComponent<CapsuleCollider>();
            if (capsule == null) return;

            float radius = capsule.radius;
            float halfHeight = capsule.height * 0.5f;
            Vector3 center = capsule.center;

            // Draw capsule outline
            DrawCapsuleGizmo(pos, center, radius, halfHeight, currentWorldUp,
                _isGrounded ? Color.green : Color.red);

            if (showGroundDetection)
            {
                // Ground detection ray
                float capsuleBottomY = center.y - halfHeight;
                Vector3 rayOrigin = pos + currentWorldUp * (capsuleBottomY + radius);
                float rayDist = config.stepHeight + 0.1f;

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(rayOrigin, rayOrigin - currentWorldUp * rayDist);

                // Ground hit point
                if (_isGrounded)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(_groundPoint, 0.05f);

                    // Ground normal
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(_groundPoint, _groundPoint + _groundNormal * 0.3f);

                    // Slope angle indicator
                    float slopeAngle = Vector3.Angle(_groundNormal, currentWorldUp);
                    Gizmos.color = slopeAngle <= config.slopeLimit ? Color.green : Color.red;
                    DrawAngleArc(_groundPoint, _groundNormal, currentWorldUp, slopeAngle);
                }

                // Step height indicator
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                Vector3 stepBottom = pos + currentWorldUp * capsuleBottomY;
                Vector3 stepTop = stepBottom + currentWorldUp * config.stepHeight;
                Gizmos.DrawLine(stepBottom + transform.right * radius, stepTop + transform.right * radius);
                Gizmos.DrawLine(stepBottom - transform.right * radius, stepTop - transform.right * radius);
                Gizmos.DrawLine(stepBottom + transform.forward * radius, stepTop + transform.forward * radius);
                Gizmos.DrawLine(stepBottom - transform.forward * radius, stepTop - transform.forward * radius);
            }

            if (showVelocity && Application.isPlaying)
            {
                // Velocity vector
                Vector3 velStart = pos + center;
                if (_velocity.sqrMagnitude > 0.01f)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(velStart, velStart + _velocity * 0.5f);

                    // Horizontal velocity
                    Vector3 horizontalVel = Vector3.ProjectOnPlane(_velocity, currentWorldUp);
                    if (horizontalVel.sqrMagnitude > 0.01f)
                    {
                        Gizmos.color = new Color(0f, 0.5f, 1f);
                        Gizmos.DrawLine(velStart, velStart + horizontalVel * 0.5f);
                    }
                }
            }

            if (showCollisionSweep && Application.isPlaying)
            {
                // Show recent collision points
                for (int i = 0; i < _collisionCount; i++)
                {
                    ref CollisionResult col = ref _collisionResults[i];

                    Gizmos.color = col.isWalkable ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(col.point, 0.03f);

                    // Collision normal
                    Gizmos.color = Color.white;
                    Gizmos.DrawLine(col.point, col.point + col.normal * 0.2f);

                    // Surface normal (if different)
                    if (col.surfaceNormal != col.normal)
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawLine(col.point, col.point + col.surfaceNormal * 0.15f);
                    }
                }
            }
        }

        private void DrawCapsuleGizmo(Vector3 position, Vector3 center, float radius, float halfHeight, Vector3 up, Color color)
        {
            Gizmos.color = color;

            Vector3 worldCenter = position + center;
            Vector3 topSphere = worldCenter + up * (halfHeight - radius);
            Vector3 bottomSphere = worldCenter - up * (halfHeight - radius);

            // Draw spheres
            Gizmos.DrawWireSphere(topSphere, radius);
            Gizmos.DrawWireSphere(bottomSphere, radius);

            // Draw connecting lines
            Vector3 right = Vector3.Cross(up, transform.forward).normalized * radius;
            Vector3 forward = Vector3.Cross(right, up).normalized * radius;

            Gizmos.DrawLine(topSphere + right, bottomSphere + right);
            Gizmos.DrawLine(topSphere - right, bottomSphere - right);
            Gizmos.DrawLine(topSphere + forward, bottomSphere + forward);
            Gizmos.DrawLine(topSphere - forward, bottomSphere - forward);
        }

        private void DrawAngleArc(Vector3 origin, Vector3 normal, Vector3 up, float angle)
        {
            const int segments = 16;
            float arcLength = 0.15f;

            Vector3 start = Vector3.ProjectOnPlane(up, normal).normalized;
            if (start.sqrMagnitude < 0.001f)
                start = Vector3.Cross(normal, Vector3.right).normalized;

            Vector3 prev = origin + start * arcLength;

            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments * angle;
                Vector3 rotated = Quaternion.AngleAxis(t, Vector3.Cross(normal, up)) * start;
                Vector3 curr = origin + rotated * arcLength;
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }
        }

        // Debug info for Inspector
        public string GetDebugInfo()
        {
            if (!Application.isPlaying)
                return "Not playing";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"State: {CurrentState}");
            sb.AppendLine($"Grounded: {_isGrounded} (Walkable: {_currentGround.isWalkable})");
            sb.AppendLine($"Ground Distance: {_groundDistance:F3}");

            if (_isGrounded)
            {
                float slopeAngle = Vector3.Angle(_groundNormal, WorldUp);
                sb.AppendLine($"Slope Angle: {slopeAngle:F1}° (Limit: {config?.slopeLimit ?? 45}°)");
            }

            sb.AppendLine($"Velocity: {_velocity.magnitude:F2} m/s");
            sb.AppendLine($"Constrained: {_isConstrainedToGround}");
            sb.AppendLine($"Collisions: {_collisionCount}");

            return sb.ToString();
        }
#endif
    }
}
