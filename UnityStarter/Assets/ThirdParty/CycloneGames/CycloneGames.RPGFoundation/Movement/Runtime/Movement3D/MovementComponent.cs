using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime.States;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class MovementComponent : MonoBehaviour, IMovementStateQuery3D, IMovementSnapshotProvider
    {
        private static readonly LogChannel Log = RpgMovementLog.Channel;

        public const int MaximumIgnoredColliderCount = 65_536;

        #region Serialized Fields

        [SerializeField] private MovementConfig config;
        [SerializeField] private Transform worldUpSource;
        [Tooltip("Consume root motion deltas from a bound IRootMotionSource and feed them "
                 + "through the ordinary collision step.")]
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

        private const float kContactOffset = 0.01f;
        private const float kPenetrationOffset = 0.001f;

        private const float kMinGroundDistance = 0.001f;
        private const float kMaxGroundDistance = 0.05f;
        private const float kAvgGroundDistance = 0.02f;

        private const float kHemisphereLimit = 0.01f;

        private const int kMaxMovementIterations = 4;
        private const float kMinMoveDistanceSqr = 0.0001f;
        private const int kMaxHitCount = 16;
        private const int kMaxOverlapCount = 8;

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
        private IMovementAuthority _movementAuthority;

        public IMovementAuthority MovementAuthority
        {
            get
            {
                if (_movementAuthority != null && !MovementUnityLifetime.IsAlive(_movementAuthority))
                    _movementAuthority = null;
                return _movementAuthority;
            }
            set { _movementAuthority = MovementUnityLifetime.Alive(value); }
        }
        public IMovementConfig3DReadOnly Config => _configReadOnly ??= new MovementConfig3DReadOnlyWrapper(config);
        public void BindAnimation(IMovementLocomotionSink locomotionSink, IMovementActionSink actionSink)
        {
            _locomotionSink = locomotionSink;
            _actionSink = actionSink;
        }

        // Root motion is an input to the physics step, never a transform write.
        public void BindRootMotion(IRootMotionSource source)
        {
            _rootMotionSource = source;
            _animatorRootMotionSource = source as AnimatorRootMotionSource;
            SyncRootMotionTimeSource();
        }

        private void SyncRootMotionTimeSource()
        {
            if (_animatorRootMotionSource != null)
                _animatorRootMotionSource.SetTimeScale(LocalTimeScale, ignoreTimeScale);
        }

        public void BindSemantics(MovementExtensionRegistry registry) { _semantics = registry; }

        public void BindPresentationBudget(IMovementPresentationBudget budget) { _presentationBudget = budget; }

        public void SetSemanticStateId(uint id) { _semanticStateId = id; }

        public MovementExtensionRegistry Semantics { get { return _semantics; } }

        public void BindMovementLayers(MovementLayerArbiter arbiter) { _movementLayers = arbiter; }

        public MovementLayerArbiter MovementLayers { get { return _movementLayers; } }

        // Only the outermost window saves layer state and restores it. Nested
        // corrections must not replay actions or reset timers twice.
        public void BeginReplay()
        {
            if (_replayDepth == 0 && _movementLayers != null) _movementLayers.SaveReplayState();
            if (_replayDepth < int.MaxValue) _replayDepth++;
        }

        public void EndReplay()
        {
            if (_replayDepth <= 0) return;
            _replayDepth--;
            if (_replayDepth == 0 && _movementLayers != null) _movementLayers.RestoreReplayState();
        }

        public void BindDeterministicStep(IMovementDeterministicStep driver) { _deterministicStep = driver; }

        public void SetPresentationEnabled(bool enabled)
        {
            _presentationEnabled = enabled;
        }

        public MovementAnimationState LastPublishedAnimationState
        {
            get { return _lastPublishedState; }
        }

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
        private readonly MovementStatePool<MovementStateBase> _statePool = new MovementStatePool<MovementStateBase>();
        private IMovementLocomotionSink _locomotionSink;
        private IMovementActionSink _actionSink;
        private IRootMotionSource _rootMotionSource;
        private readonly MovementAnimationFrameBuilder _animationFrame = new MovementAnimationFrameBuilder();
        private readonly MovementAnimationAction[] _actionBuffer =
            new MovementAnimationAction[MovementAnimationActionWriter.MaximumActionsPerStep];
        private MovementAnimationState _lastPublishedState;
        private bool _presentationEnabled = true;
        private bool _rootMotionAppliedThisStep;
        private float _rootMotionYawThisStep;
        private MovementExtensionRegistry _semantics;
        private IMovementPresentationBudget _presentationBudget;
        private uint _semanticStateId;
        private bool _presentationPaused;
        private MovementLayerArbiter _movementLayers;
        private IMovementDeterministicStep _deterministicStep;
        private int _replayDepth;
        private uint _layerSequence;
        private static readonly Unity.Profiling.ProfilerMarker AnimationPublishMarker =
            new Unity.Profiling.ProfilerMarker("CycloneGames.Movement.PublishAnimationFrame");
        private AnimatorRootMotionSource _animatorRootMotionSource;
        private readonly MovementRootMotionAccumulator _rootMotionAccumulator =
            new MovementRootMotionAccumulator();

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
        private long _rejectedIgnoredColliderAdmissionCount;
        private readonly CollisionResult[] _collisionResults = new CollisionResult[kMaxHitCount];
        private int _collisionCount;

        #endregion

        #region Private Fields - Rotation

        private static readonly float3 UnityUpVector = new float3(0, 1, 0);

        #endregion

        #region Private Fields - Platform

        private MovingPlatformData _movingPlatform;
        private Vector3 _inheritedPlatformVelocity;
        private Vector3 _lastGroundVelocity;

        private Vector3 _lastPlatformPosAtFixedUpdate;
        private Quaternion _lastPlatformRotAtFixedUpdate;

        #endregion

        #region Private Fields - Forces

        private Vector3 _pendingImpulse;
        private Vector3 _pendingForce;
        private float _gapBridgeCooldown;

        private float _coyoteTimeCounter;
        private float _jumpBufferCounter;

        #endregion

        private float DeltaTime => (ignoreTimeScale ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime) * LocalTimeScale;

        private LayerMask CollisionLayers => config != null && config.CollisionLayer != 0
            ? config.CollisionLayer
            : (config != null ? config.GroundLayer : Physics.DefaultRaycastLayers);

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
            InitializeContext();

            _currentState = _statePool.Get<IdleState>();
            _currentRotation = transform.rotation;

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
            SyncRootMotionTimeSource();
            if (_deterministicStep != null && _deterministicStep.Step(DeltaTime)) return;
            if (_currentState == null)
            {
                _currentState = _statePool.Get<IdleState>();
                _currentState?.OnEnter(ref _context);
            }

            float deltaTime = DeltaTime;

            if (_gapBridgeCooldown > 0)
                _gapBridgeCooldown -= deltaTime;

            if (_unconstrainedTimer > 0)
                _unconstrainedTimer -= deltaTime;

            HandleCoyoteTime(deltaTime);
            HandleJumpBuffer(deltaTime);

            _updatedPosition = transform.position;

            ApplyMovingPlatform();

            UpdateContext();

            UpdateMovingPlatformTracking();

            if (_context.IsGrounded && config != null && config.EnableGapBridging)
                TryBridgeGap();

            ExecuteStateMachine();
            ApplyPendingForces();

            UpdateMovingPlatformLocalPosition();

            if (_movingPlatform.isOnPlatform && _movingPlatform.platformTransform != null)
            {
                _lastPlatformPosAtFixedUpdate = _movingPlatform.platformTransform.position;
                _lastPlatformRotAtFixedUpdate = _movingPlatform.platformTransform.rotation;
            }
        }

        private void LateUpdate()
        {
            if (!smoothPlatformFollow) return;
            if (!_movingPlatform.isOnPlatform || _movingPlatform.platformTransform == null) return;

            Vector3 currentPlatformPos = _movingPlatform.platformTransform.position;

            Vector3 platformPosDelta = currentPlatformPos - _lastPlatformPosAtFixedUpdate;

            if (platformPosDelta.sqrMagnitude < 0.0000001f)
                return;

            Vector3 newPos = transform.position + platformPosDelta;
            transform.position = newPos;
            _rigidbody.position = newPos;

            _lastPlatformPosAtFixedUpdate = currentPlatformPos;

            _movingPlatform.UpdateLocalPosition(newPos);
        }

        private void OnDestroy()
        {
            OnStateChanged = null;
            OnLanded = null;
            OnJumpStart = null;
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
            _capsuleCollider.direction = 1;

            if (config != null)
            {
                _minSlopeLimit = Mathf.Cos(config.SlopeLimit * Mathf.Deg2Rad);
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
                MovementAuthority = null,
                StatePool = _statePool
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
                Log.Error("[MovementComponent] MovementConfig is null.");
                return;
            }

            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.WasGrounded = _context.IsGrounded;
            _context.Config = config;

            FindGround();
            _context.IsGrounded = _isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0;
            _context.IsOnNonWalkableSlope = _isOnNonWalkableSlope;
            _context.GroundNormal = _groundNormal;

            _context.UseRootMotion = useRootMotion;

            if (_worldUpChanged && !_context.IsGrounded)
            {
                Vector3 prevVertical = _previousWorldUp * _context.VerticalVelocity;
                _context.VerticalVelocity = Vector3.Dot(prevVertical, WorldUp);
            }

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

            _context.MovementAuthority = MovementAuthority;

#if UNITY_EDITOR
            _debugConfigAssigned = config != null;
            _debugConfigRunSpeed = config != null ? config.RunSpeed : -1f;
            _debugCurrentSpeed = _context.CurrentSpeed;
            _debugDeltaTime = DeltaTime;
            int debugStateId = _currentState != null ? (int)_currentState.StateType : -1;
            if (debugStateId != _lastDebugStateId)
            {
                _lastDebugStateId = debugStateId;
                _debugCurrentState = debugStateId >= 0
                    ? ((MovementStateType)debugStateId).ToString()
                    : "None";
            }
            _debugInputDirection = _context.InputDirection;
            _debugInputMagnitude = math.length(_context.InputDirection);

            _debugOnPlatform = _movingPlatform.isOnPlatform;
            Transform platformTransform = _movingPlatform.platformTransform;
            if (platformTransform != _lastDebugPlatformTransform)
            {
                _lastDebugPlatformTransform = platformTransform;
                _debugPlatformName = platformTransform != null ? platformTransform.name : "None";
            }
            _debugPlatformVelocity = _movingPlatform.platformVelocity;
            _debugLocalPosition = _movingPlatform.localPosition;
            if (_groundCollider != _lastDebugGroundCollider)
            {
                _lastDebugGroundCollider = _groundCollider;
                _debugGroundColliderName = _groundCollider != null ? _groundCollider.name : "None";
            }
            _debugGroundHasRigidbody = _groundCollider != null && _groundCollider.attachedRigidbody != null;
            if (_groundCollider != null && config != null)
            {
                LayerMask platformMask = config.PlatformLayer != 0 ? config.PlatformLayer : config.GroundLayer;
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
                _currentState = _statePool.Get<IdleState>();
                _currentState?.OnEnter(ref _context);
                if (_currentState == null) return;
            }

            float3 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            MovementRootMotionDelta captured = default;
            if (_rootMotionSource != null && _rootMotionSource.TryConsume(out captured))
                _rootMotionAccumulator.Push(in captured);
            else if (!useRootMotion)
                _rootMotionAccumulator.Discard();
            MovementRootMotionDelta stepDelta = default;
            bool rootMotionAvailable = _rootMotionAccumulator.TryConsumeStep(DeltaTime, out stepDelta);
            bool shouldUseRootMotion = useRootMotion && rootMotionAvailable;
            _rootMotionAppliedThisStep = shouldUseRootMotion;
            _rootMotionYawThisStep = shouldUseRootMotion ? stepDelta.YawVelocity : 0f;

            Vector3 desiredVelocity;
            if (shouldUseRootMotion)
            {
                MovementVector3 rootVelocity = stepDelta.LinearVelocity;
                desiredVelocity = new Vector3(rootVelocity.X, rootVelocity.Y, rootVelocity.Z)
                    + WorldUp * _context.VerticalVelocity;
            }
            else
            {
                desiredVelocity = (Vector3)displacement / Mathf.Max(DeltaTime, 0.0001f);

                if (_context.IsGrounded)
                {
                    Vector3 horizontal = new Vector3(displacement.x, 0, displacement.z);
                    _lastGroundVelocity = horizontal / Mathf.Max(DeltaTime, 0.0001f);
                }
                else
                {
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

            }

            if (_movementLayers != null)
            {
                var layerFrame = new MovementLayerFrame(
                    ++_layerSequence, DeltaTime,
                    new MovementVector3(desiredVelocity.x, desiredVelocity.y, desiredVelocity.z),
                    new MovementVector3(WorldUp.x, WorldUp.y, WorldUp.z),
                    _context.IsGrounded);
                MovementLayerOutput layers = _movementLayers.Resolve(in layerFrame);
                desiredVelocity += new Vector3(
                    layers.VelocityDelta.X, layers.VelocityDelta.Y, layers.VelocityDelta.Z);
                desiredVelocity += new Vector3(
                    layers.PositionDelta.X, layers.PositionDelta.Y, layers.PositionDelta.Z)
                    / Mathf.Max(DeltaTime, 0.0001f);
            }

            PerformMovement(desiredVelocity, DeltaTime);

#if UNITY_EDITOR
            _debugActualVelocity = _velocity;
            _debugActualSpeed = _velocity.magnitude;
#endif

            MovementStateBase nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
                RequestStateChangeInternal(nextState);

            if (math.lengthsq(_lookDirection) > kMinSqrMagnitude)
                UpdateRotation();
            else
                UpdateRotationForWorldUp();

            PublishAnimationFrame();
        }

        #endregion

        #region Physics Movement

        private void PerformMovement(Vector3 desiredVelocity, float deltaTime)
        {
            _velocity = desiredVelocity;
            _collisionCount = 0;
            _hasLanded = false;

            ResolveOverlaps();

            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                _velocity = Vector3.ProjectOnPlane(_velocity, WorldUp);
            }

            Vector3 displacement = _velocity * deltaTime;

            if (displacement.sqrMagnitude < kMinMoveDistanceSqr)
            {
                if (_isGrounded && _isConstrainedToGround)
                {
                    AdjustGroundHeight();

                    if (_context.VerticalVelocity < 0)
                    {
                        _context.VerticalVelocity = kGroundedVerticalVelocity;
                        _unconstrainedTimer = 0;
                    }
                }

                _rigidbody.position = _updatedPosition;
                transform.position = _updatedPosition;
                return;
            }

            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                Vector3 probeDir = Vector3.ProjectOnPlane(displacement, WorldUp).normalized;
                if (probeDir.sqrMagnitude > kKindaSmallNumber)
                {
                    if (ProbeForNonWalkableSlope(probeDir, displacement.magnitude, out Vector3 blockingNormal))
                    {
                        displacement = Vector3.ProjectOnPlane(displacement, blockingNormal);
                        _velocity = Vector3.ProjectOnPlane(_velocity, blockingNormal);
                    }
                }
            }

            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                displacement = ComputeTangentToSurface(displacement, _groundNormal);
            }

            Vector3 inputDisplacement = displacement;

            int iteration = 0;
            Vector3 prevNormal = Vector3.zero;
            int maxSlideCount = kMaxMovementIterations;

            while (maxSlideCount-- > 0 && displacement.sqrMagnitude > kMinMoveDistanceSqr)
            {
                bool collided = MovementSweepTest(_updatedPosition, _velocity, displacement, out CollisionResult collisionResult);

                if (!collided)
                {
                    _updatedPosition += displacement;
                    break;
                }

                _updatedPosition += collisionResult.displacementToHit;
                displacement = collisionResult.remainingDisplacement;

                if (_isGrounded && !collisionResult.isWalkable && collisionResult.hitLocation == HitLocation.Sides)
                {
                    if (config != null && config.StepHeight > 0f)
                    {
                        if (TryStepUp(ref displacement, collisionResult))
                        {
                            displacement = Vector3.zero;
                            break;
                        }
                    }
                }

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
                        _foundGround.SetFromSweepResult(true, false, _updatedPosition,
                            collisionResult.hitResult.distance, ref collisionResult.hitResult, collisionResult.surfaceNormal);
                    }
                }

                iteration = SlideAlongSurface(iteration, inputDisplacement, ref _velocity, ref displacement,
                    ref collisionResult, ref prevNormal);

                if (_collisionCount < kMaxHitCount)
                {
                    _collisionResults[_collisionCount++] = collisionResult;
                }
            }

            if (displacement.sqrMagnitude > kMinMoveDistanceSqr)
            {
                _updatedPosition += displacement;
            }

            bool canSnapToGround = _isConstrainedToGround &&
                ((_isGrounded && _unconstrainedTimer <= 0) || _hasLanded);

            if (canSnapToGround)
            {
                AdjustGroundHeight();

                _velocity = Vector3.ProjectOnPlane(_velocity, WorldUp);

                if (_hasLanded && _unconstrainedTimer > 0)
                {
                    _unconstrainedTimer = 0;
                }
            }

            bool shouldSlide = _isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0;
            if (shouldSlide)
            {
                if (!_currentGround.isWalkable && _currentGround.hitGround)
                {
                    ApplySlopeSliding(deltaTime, _currentGround.surfaceNormal);
                }
                else
                {
                    for (int i = 0; i < _collisionCount; i++)
                    {
                        ref CollisionResult col = ref _collisionResults[i];
                        if (!col.isWalkable && col.hitLocation == HitLocation.Sides)
                        {
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

            _rigidbody.position = _updatedPosition;
            transform.position = _updatedPosition;
        }

        private bool MovementSweepTest(Vector3 characterPosition, Vector3 inVelocity, Vector3 displacement,
            out CollisionResult collisionResult)
        {
            collisionResult = default;

            Vector3 sweepDirection = displacement.normalized;
            float sweepDistance = displacement.magnitude;

            if (sweepDistance < kKindaSmallNumber)
                return false;

            float sweepRadius = _radius - kContactOffset;

            Vector3 top = characterPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = characterPosition + transform.rotation * _capsuleBottomCenter;

            int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, sweepRadius, sweepDirection, _hits,
                sweepDistance + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore);

            if (hitCount == 0)
                return false;

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
                return startPenetrating;
            }

            RaycastHit hitResult = _hits[closestIdx];
            HitLocation hitLocation = ComputeHitLocation(hitResult.normal);

            float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
            Vector3 displacementToHit = sweepDirection * safeDistance;
            Vector3 remainingDisplacement = displacement - displacementToHit;

            Vector3 surfaceNormal = hitResult.normal;
            bool isWalkable = false;
            bool hitGround = hitLocation == HitLocation.Below;

            if (hitGround)
            {
                surfaceNormal = FindSurfaceNormal(hitResult);
                isWalkable = IsWalkable(hitResult.collider, surfaceNormal);

                if (_isGrounded && !isWalkable)
                {
                    Vector3 horizontalDisp = Vector3.ProjectOnPlane(displacement, WorldUp);
                    if (horizontalDisp.sqrMagnitude > kKindaSmallNumber)
                    {
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

            collisionResult.normal = ComputeBlockingNormal(collisionResult.normal, isWalkable);

            return true;
        }

        private HitLocation ComputeHitLocation(Vector3 normal)
        {
            float verticalComponent = Vector3.Dot(normal, WorldUp);

            if (verticalComponent > kHemisphereLimit)
                return HitLocation.Below;

            return verticalComponent < -kHemisphereLimit ? HitLocation.Above : HitLocation.Sides;
        }

        private Vector3 ComputeBlockingNormal(Vector3 inNormal, bool isWalkable)
        {
            if ((_isGrounded || _hasLanded) && !isWalkable)
            {
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

        private int SlideAlongSurface(int iteration, Vector3 inputDisplacement, ref Vector3 inVelocity,
            ref Vector3 displacement, ref CollisionResult inHit, ref Vector3 prevNormal)
        {
            Vector3 hitNormal = ComputeBlockingNormal(inHit.normal, inHit.isWalkable);
            inHit.normal = hitNormal;

            if (inHit.isWalkable && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                inVelocity = ComputeSlideVector(inVelocity, hitNormal, true);
                displacement = ComputeSlideVector(displacement, hitNormal, true);
            }
            else
            {
                if (iteration == 0)
                {
                    inVelocity = ComputeSlideVector(inVelocity, hitNormal, inHit.isWalkable);
                    displacement = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                    iteration++;
                }
                else if (iteration == 1)
                {
                    Vector3 crease = Vector3.Cross(prevNormal, hitNormal);

                    if (crease.sqrMagnitude > kKindaSmallNumber)
                    {
                        crease = crease.normalized;

                        Vector3 oVel = Vector3.ProjectOnPlane(inputDisplacement, crease);
                        Vector3 nVel = ComputeSlideVector(displacement, hitNormal, inHit.isWalkable);
                        nVel = Vector3.ProjectOnPlane(nVel, crease);

                        if (Vector3.Dot(oVel, nVel) <= 0f || Vector3.Dot(prevNormal, hitNormal) < 0f)
                        {
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
                    inVelocity = Vector3.zero;
                    displacement = Vector3.zero;
                }

                prevNormal = hitNormal;
            }

            return iteration;
        }

        private Vector3 ComputeSlideVector(Vector3 displacement, Vector3 normal, bool isWalkable)
        {
            if (_isGrounded && _isConstrainedToGround && _unconstrainedTimer <= 0)
            {
                if (isWalkable)
                {
                    return ComputeTangentToSurface(displacement, normal);
                }
                else
                {
                    Vector3 right = Vector3.Cross(normal, _groundNormal);
                    Vector3 up = Vector3.Cross(right, normal);

                    Vector3 result = Vector3.ProjectOnPlane(displacement, normal);

                    result = ComputeTangentToSurface(result, up);

                    return result;
                }
            }
            else
            {
                if (isWalkable)
                {
                    if (_isConstrainedToGround)
                        displacement = Vector3.ProjectOnPlane(displacement, WorldUp);
                    return Vector3.ProjectOnPlane(displacement, normal);
                }
                else
                {
                    Vector3 result = Vector3.ProjectOnPlane(displacement, normal);

                    if (_isConstrainedToGround)
                        result = HandleSlopeBoosting(result, displacement, normal);

                    return result;
                }
            }
        }

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

                    Vector3 lateralRemainder = Vector3.ProjectOnPlane(displacement - slideResult, WorldUp);
                    Vector3 lateralNormal = Vector3.ProjectOnPlane(normal, WorldUp).normalized;
                    Vector3 adjust = Vector3.ProjectOnPlane(lateralRemainder, lateralNormal);
                    slideResult += adjust;
                }
            }
            return slideResult;
        }

        private Vector3 ComputeTangentToSurface(Vector3 displacement, Vector3 surfaceNormal)
        {
            Vector3 right = Vector3.Cross(displacement, surfaceNormal);
            if (right.sqrMagnitude < kKindaSmallNumber)
                return displacement;

            Vector3 tangent = Vector3.Cross(surfaceNormal, right);
            return tangent.normalized * displacement.magnitude;
        }

        private bool TryStepUp(ref Vector3 displacement, CollisionResult obstacleHit)
        {
            if (config == null || config.StepHeight <= 0f) return false;

            float stepHeight = config.StepHeight;
            Vector3 forwardDir = Vector3.ProjectOnPlane(displacement, WorldUp).normalized;

            if (forwardDir.sqrMagnitude < kKindaSmallNumber)
                return false;

            float forwardDist = displacement.magnitude;

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

            Vector3 finalTop = stepForwardPos + transform.rotation * _capsuleTopCenter;
            Vector3 finalBottom = stepForwardPos + transform.rotation * _capsuleBottomCenter;
            float downDist = stepHeight + kMaxGroundDistance * 2f;

            if (!Physics.CapsuleCast(finalBottom, finalTop, sweepRadius, -WorldUp, out RaycastHit downHit, downDist, config.GroundLayer, QueryTriggerInteraction.Ignore))
                return false;

            if (ShouldIgnore(downHit.collider))
                return false;

            Vector3 surfaceNormal = FindSurfaceNormal(downHit);
            if (!IsWalkable(downHit.collider, surfaceNormal))
                return false;

            float downOffset = Mathf.Max(0f, downHit.distance - kContactOffset);
            _updatedPosition = stepForwardPos - WorldUp * downOffset;
            _groundNormal = surfaceNormal;

            displacement = Vector3.zero;
            return true;
        }

        private void ApplySlopeSliding(float deltaTime, Vector3 slopeNormal)
        {
            if (config == null)
                return;

            float slopeDot = Vector3.Dot(slopeNormal, WorldUp);
            float slopeAngle = Mathf.Acos(Mathf.Clamp01(slopeDot)) * Mathf.Rad2Deg;

            if (slopeAngle <= config.SlopeLimit)
                return;

            Vector3 slideDirection = Vector3.ProjectOnPlane(-WorldUp, slopeNormal);
            if (slideDirection.sqrMagnitude < kKindaSmallNumber)
                return;
            slideDirection = slideDirection.normalized;

            float angleRatio = (slopeAngle - config.SlopeLimit) / (90f - config.SlopeLimit);
            angleRatio = Mathf.Clamp01(angleRatio);

            float slideAcceleration = Mathf.Abs(config.Gravity) * angleRatio;
            float slideSpeed = slideAcceleration * deltaTime;

            Vector3 slideDisplacement = slideDirection * slideSpeed;

            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
            float sweepRadius = _radius - kContactOffset;

            if (Physics.CapsuleCast(bottom, top, sweepRadius, slideDirection, out RaycastHit slideHit,
                slideSpeed + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnore(slideHit.collider))
                {
                    float safeDist = Mathf.Max(0f, slideHit.distance - kContactOffset);
                    slideDisplacement = slideDirection * safeDist;
                }
            }

            if (slideDisplacement.sqrMagnitude > kKindaSmallNumber)
            {
                _updatedPosition += slideDisplacement;
            }
        }

        private bool ProbeForNonWalkableSlope(Vector3 probeDirection, float probeDistance, out Vector3 blockingNormal)
        {
            blockingNormal = Vector3.zero;

            float actualProbeDistance = Mathf.Max(_radius + kContactOffset, probeDistance);

            Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
            bottom += WorldUp * (kContactOffset * 2f);

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

                Vector3 surfaceNormal = FindSurfaceNormal(hit);
                float verticalDot = Vector3.Dot(surfaceNormal, WorldUp);

                if (verticalDot > kHemisphereLimit)
                {
                    if (verticalDot < _minSlopeLimit)
                    {
                        Vector3 horizontalNormal = Vector3.ProjectOnPlane(surfaceNormal, WorldUp);
                        if (horizontalNormal.sqrMagnitude > kKindaSmallNumber)
                        {
                            if (Vector3.Dot(horizontalNormal.normalized, probeDirection) < -0.1f)
                            {
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

        private void FindGround()
        {
            _wasGrounded = _isGrounded;
            _isGrounded = false;
            _groundNormal = WorldUp;
            _groundCollider = null;
            _groundDistance = float.MaxValue;
            _currentGround.Clear();

            if (config == null) return;

            float heightCheckAdjust = _wasGrounded ? kMaxGroundDistance + kKindaSmallNumber : -kMaxGroundDistance;
            float sweepDistance = Mathf.Max(kMaxGroundDistance, config.StepHeight + heightCheckAdjust);

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
                _groundNormal = _currentGround.surfaceNormal;
                _groundPoint = _currentGround.point;
                _groundCollider = _currentGround.collider;
                _groundDistance = _currentGround.groundDistance;

                _isGrounded = false;
                _isOnNonWalkableSlope = true;
            }
            else
            {
                _isOnNonWalkableSlope = false;
            }
        }

        private void ComputeGroundDistance(Vector3 characterPosition, float sweepDistance, out FindGroundResult outGroundResult)
        {
            outGroundResult = default;

            float capsuleRadius = _radius;
            float capsuleHalfHeight = _height * 0.5f;

            const float kShrinkScale = 0.9f;
            float shrinkHeight = (capsuleHalfHeight - capsuleRadius) * (1f - kShrinkScale);
            float actualSweepRadius = capsuleRadius - kContactOffset;
            float actualSweepDistance = sweepDistance + shrinkHeight;

            Vector3 top = characterPosition + transform.rotation * _capsuleTopCenter;
            Vector3 bottom = characterPosition + transform.rotation * _capsuleBottomCenter;

            bottom += WorldUp * shrinkHeight;

            bool foundGround = Physics.CapsuleCast(bottom, top, actualSweepRadius, -WorldUp, out RaycastHit hitResult,
                actualSweepDistance, config.GroundLayer, QueryTriggerInteraction.Ignore);

            if (foundGround)
            {
                if (ShouldIgnore(hitResult.collider))
                {
                    foundGround = false;
                }
                else
                {
                    HitLocation hitLocation = ComputeHitLocation(hitResult.normal);
                    if (hitLocation != HitLocation.Below)
                    {
                        foundGround = false;
                    }
                }
            }

            if (foundGround)
            {
                float adjustedDistance = Mathf.Max(-kMaxGroundDistance, hitResult.distance - shrinkHeight);

                Vector3 surfaceNormal = FindSurfaceNormal(hitResult);
                bool isWalkable = IsWalkable(hitResult.collider, surfaceNormal);

                outGroundResult.SetFromSweepResult(true, isWalkable, characterPosition,
                    adjustedDistance, ref hitResult, surfaceNormal);
            }
            else
            {
                float raycastDistance = sweepDistance + capsuleRadius;

                float capsuleBottomY = _capsuleCenter.y - capsuleHalfHeight;
                Vector3 rayOrigin = characterPosition + WorldUp * (capsuleBottomY + capsuleRadius);

                if (Physics.Raycast(rayOrigin, -WorldUp, out RaycastHit rayHit, raycastDistance,
                    config.GroundLayer, QueryTriggerInteraction.Ignore))
                {
                    if (!ShouldIgnore(rayHit.collider))
                    {
                        Vector3 surfaceNormal = FindSurfaceNormal(rayHit);
                        bool isWalkable = IsWalkable(rayHit.collider, surfaceNormal);

                        float groundDist = rayHit.distance - capsuleRadius;

                        outGroundResult.SetFromRaycastResult(true, isWalkable, characterPosition,
                            groundDist, rayHit.distance, ref rayHit, surfaceNormal);
                    }
                }
            }
        }

        private void AdjustGroundHeight()
        {
            if (!_currentGround.isWalkableGround || !_isConstrainedToGround)
                return;

            float lastGroundDistance = _currentGround.groundDistance;

            if (_currentGround.isRaycastResult)
            {
                if (lastGroundDistance < kMinGroundDistance && _currentGround.raycastDistance >= kMinGroundDistance)
                {
                    return;
                }
                else
                {
                    lastGroundDistance = _currentGround.raycastDistance;
                }
            }

            if (lastGroundDistance < kMinGroundDistance || lastGroundDistance > kMaxGroundDistance)
            {
                float initialY = Vector3.Dot(_updatedPosition, WorldUp);
                float moveDistance = kAvgGroundDistance - lastGroundDistance;

                Vector3 displacement = WorldUp * moveDistance;
                Vector3 sweepDirection = displacement.normalized;
                float sweepDistance = displacement.magnitude;

                Vector3 top = _updatedPosition + transform.rotation * _capsuleTopCenter;
                Vector3 bottom = _updatedPosition + transform.rotation * _capsuleBottomCenter;
                float sweepRadius = _radius - kContactOffset;

                bool hit = Physics.CapsuleCast(bottom, top, sweepRadius, sweepDirection, out RaycastHit hitResult,
                    sweepDistance + kContactOffset, CollisionLayers, QueryTriggerInteraction.Ignore);

                bool startPenetrating = false;
                if (hit && hitResult.distance <= 0f)
                {
                    startPenetrating = true;
                    hit = false;
                }

                if (!hit && !startPenetrating)
                {
                    _updatedPosition += displacement;
                    _currentGround.groundDistance += moveDistance;
                }
                else if (hit && moveDistance > 0.0f)
                {
                    float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
                    _updatedPosition += sweepDirection * safeDistance;

                    float currentY = Vector3.Dot(_updatedPosition, WorldUp);
                    _currentGround.groundDistance += currentY - initialY;
                }
                else if (hit && moveDistance < 0.0f)
                {
                    float safeDistance = Mathf.Max(0f, hitResult.distance - kContactOffset);
                    _updatedPosition += sweepDirection * safeDistance;

                    float currentY = Vector3.Dot(_updatedPosition, WorldUp);
                    _currentGround.groundDistance = currentY - initialY;
                }
            }
        }

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

            Vector3 rayOrigin = hit.point + WorldUp * 0.1f;
            if (Physics.Raycast(rayOrigin, -WorldUp, out RaycastHit rayHit, 0.2f, config.GroundLayer, QueryTriggerInteraction.Ignore))
            {
                if (rayHit.collider == hit.collider)
                    return rayHit.normal;
            }

            return hit.normal;
        }

        private bool IsWalkable(Collider inCollider, Vector3 inNormal)
        {
            float verticalComponent = Vector3.Dot(inNormal, WorldUp);
            if (verticalComponent <= kHemisphereLimit)
                return false;

            return verticalComponent >= _minSlopeLimit;
        }

        #endregion

        #region Collision Queries

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

                if (Physics.ComputePenetration(
                    _capsuleCollider, _updatedPosition, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float distance))
                {
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
            if (!TryIgnoreCollision(other, ignore))
            {
                throw new InvalidOperationException(
                    $"Ignored-collider capacity reached the implementation ceiling of {MaximumIgnoredColliderCount}.");
            }
        }

        public bool TryIgnoreCollision(Collider other, bool ignore = true)
        {
            if (!ignore)
            {
                _ignoredColliders.Remove(other);
                return true;
            }

            if (_ignoredColliders.Contains(other))
            {
                return true;
            }

            if (_ignoredColliders.Count >= MaximumIgnoredColliderCount)
            {
                if (_rejectedIgnoredColliderAdmissionCount < long.MaxValue)
                {
                    _rejectedIgnoredColliderAdmissionCount++;
                }

                return false;
            }

            _ignoredColliders.Add(other);
            return true;
        }

        public MovementComponentMemorySnapshot GetMemorySnapshot()
        {
            return new MovementComponentMemorySnapshot(
                _ignoredColliders.Count,
                MaximumIgnoredColliderCount,
                _rejectedIgnoredColliderAdmissionCount);
        }

        #endregion

        #region Moving Platform

        private void ApplyMovingPlatform()
        {
            if (config == null || !config.EnableMovingPlatform || !_movingPlatform.isOnPlatform)
                return;

            if (_movingPlatform.platformTransform == null)
            {
                _movingPlatform.Clear();
                return;
            }

            Vector3 targetWorldPos = _movingPlatform.platformTransform.TransformPoint(_movingPlatform.localPosition);
            Vector3 deltaPos = targetWorldPos - transform.position;

#if UNITY_EDITOR
            _debugPlatformDeltaPos = deltaPos;
#endif

            if (deltaPos.sqrMagnitude > 0.0000001f && deltaPos.sqrMagnitude < 1f)
            {
                _updatedPosition += deltaPos;
            }

            if (config.InheritPlatformRotation)
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
            if (config == null || !config.EnableMovingPlatform)
            {
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }

            if (!_context.IsGrounded)
            {
                if (_movingPlatform.isOnPlatform && config.InheritPlatformMomentum)
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
                LayerMask platformMask = config.PlatformLayer != 0 ? config.PlatformLayer : config.GroundLayer;
                bool isValidPlatform = groundRb != null && ((1 << _groundCollider.gameObject.layer) & platformMask) != 0;

                if (isValidPlatform)
                {
                    if (_movingPlatform.platform != groundRb)
                    {
                        _movingPlatform.SetPlatform(groundRb, transform);
                    }
                    return;
                }
            }

            if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
        }

        private void UpdateMovingPlatformLocalPosition()
        {
            if (!_movingPlatform.isOnPlatform || _movingPlatform.platformTransform == null)
                return;

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

            float rotationSpeed = _context.GetAttributeValue(MovementAttribute.RotationSpeed, config.RotationSpeed);
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
                float rotationSpeed = _context.GetAttributeValue(MovementAttribute.RotationSpeed, config.RotationSpeed);
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

            if (!pressed || wasPressed)
            {
                return;
            }

            if (CanJump())
            {
                RequestStateChange(MovementStateType.Jump);
                return;
            }

            _jumpBufferCounter = config != null ? config.JumpBufferTime : 0f;
        }

        public void SetSprintHeld(bool held) => _context.SprintHeld = held;
        public void SetCrouchHeld(bool held) => _context.CrouchHeld = held;

        public void SetRollPressed(bool pressed)
        {
            bool wasPressed = _context.RollPressed;
            _context.RollPressed = pressed;

            if (pressed && !wasPressed)
            {
                RequestStateChange(MovementStateType.Roll);
            }
        }

        public bool RequestClimb(ClimbingMode climbingMode, Vector3 wallNormal = default, object context = null)
        {
            if (config == null)
            {
                return false;
            }

            switch (climbingMode)
            {
                case ClimbingMode.Ladder:
                    if (!config.EnableLadderClimbing)
                    {
                        return false;
                    }
                    break;
                case ClimbingMode.Wall:
                    if (!config.EnableWallClimbing || wallNormal.sqrMagnitude <= kMinSqrMagnitude)
                    {
                        return false;
                    }
                    _context.WallClimbNormal = wallNormal.normalized;
                    break;
                case ClimbingMode.None:
                    return StopClimb();
                default:
                    return false;
            }

            _context.ClimbingMode = climbingMode;
            return RequestStateChange(MovementStateType.Climb, context ?? climbingMode);
        }

        public bool StopClimb()
        {
            _context.ClimbingMode = ClimbingMode.None;
            _context.WallClimbNormal = Vector3.zero;

            if (CurrentState != MovementStateType.Climb)
            {
                return true;
            }

            return RequestStateChange(_context.IsGrounded ? MovementStateType.Idle : MovementStateType.Fall);
        }

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
                Log.Warning("[MovementComponent] Cannot set rotation with zero direction.");
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
                Log.Warning(
                    targetStateType,
                    static (value, builder) => builder
                        .Append("[MovementComponent] State ")
                        .Append(value)
                        .Append(" not found."));
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
            if (MovementAuthority != null) MovementAuthority.OnStateExited(oldStateType);

            _currentState = newState;
            _currentState.OnEnter(ref _context);
            if (MovementAuthority != null) MovementAuthority.OnStateEntered(newStateType);

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
                MovementStateType.Idle => _statePool.Get<IdleState>(),
                MovementStateType.Walk => _statePool.Get<WalkState>(),
                MovementStateType.Run => _statePool.Get<RunState>(),
                MovementStateType.Sprint => _statePool.Get<SprintState>(),
                MovementStateType.Crouch => _statePool.Get<CrouchState>(),
                MovementStateType.Jump => _statePool.Get<JumpState>(),
                MovementStateType.Fall => _statePool.Get<FallState>(),
                MovementStateType.Roll => _statePool.Get<RollState>(),
                MovementStateType.Swim => _statePool.Get<SwimState>(),
                MovementStateType.Fly => _statePool.Get<FlyState>(),
                MovementStateType.Climb => _context.ClimbingMode switch
                {
                    ClimbingMode.Ladder => _statePool.Get<LadderClimbState>(),
                    ClimbingMode.Wall => _statePool.Get<WallClimbState>(),
                    _ => null
                },
                _ => _statePool.Get<IdleState>()
            };
        }

        #endregion

        #region Gap Bridging

        private bool TryBridgeGap()
        {
            if (config == null || !config.EnableGapBridging) return false;
            if (_gapBridgeCooldown > 0) return false;
            if (_context.CurrentSpeed < config.MinSpeedForGapBridge) return false;

            Vector3 moveDir = new Vector3(_context.CurrentVelocity.x, 0, _context.CurrentVelocity.z).normalized;
            if (moveDir.sqrMagnitude < 0.01f) return false;

            Vector3 frontPoint = transform.position + moveDir * _radius;
            if (Physics.Raycast(frontPoint, Vector3.down, config.GroundedCheckDistance * 3, config.GroundLayer))
                return false;

            for (float dist = 0.5f; dist <= config.MaxGapDistance; dist += 0.3f)
            {
                Vector3 checkPoint = frontPoint + moveDir * dist + Vector3.up * 0.5f;

                if (Physics.Raycast(checkPoint, Vector3.down, out RaycastHit hit,
                    1f + config.MaxGapHeightDiff, config.GroundLayer))
                {
                    float heightDiff = Mathf.Abs(hit.point.y - transform.position.y);
                    if (heightDiff <= config.MaxGapHeightDiff)
                    {
                        float jumpHeight = 0.3f + heightDiff * 0.5f;
                        float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(config.Gravity) * jumpHeight);

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

        private void HandleCoyoteTime(float deltaTime)
        {
            if (config == null) return;

            bool isAirState = _currentState != null &&
                             (_currentState.StateType == MovementStateType.Jump ||
                              _currentState.StateType == MovementStateType.Fall);

            if (!_context.WasGrounded && _context.IsGrounded)
            {
                _coyoteTimeCounter = 0f;
            }
            else if (_context.WasGrounded && !_context.IsGrounded && !isAirState)
            {
                _coyoteTimeCounter = config.CoyoteTime;
            }
            else if (_coyoteTimeCounter > 0f)
            {
                _coyoteTimeCounter -= deltaTime;
            }
        }

        private void HandleJumpBuffer(float deltaTime)
        {
            if (config == null) return;

            if (_jumpBufferCounter > 0f)
            {
                _jumpBufferCounter -= deltaTime;
            }

            if (_jumpBufferCounter > 0f && _context.IsGrounded)
            {
                if (RequestStateChange(MovementStateType.Jump))
                {
                    _jumpBufferCounter = 0f;
                }
            }
        }

        private bool CanJump()
        {
            if (config == null) return false;

            if (_context.IsGrounded) return true;
            if (_coyoteTimeCounter > 0f) return true;

            int resolvedMaxJump = (int)_context.GetAttributeValue(MovementAttribute.MaxJumpCount, config.MaxJumpCount);
            return _context.JumpCount < resolvedMaxJump;
        }

        public void MoveWithVelocity(Vector3 worldVelocity)
        {
            if (config == null) return;
            SetInputDirection(worldVelocity.normalized);
        }

        public void SetUseRootMotion(bool enable)
        {
            useRootMotion = enable;
            _context.UseRootMotion = enable;
        }

        #region Animation Presentation

        protected virtual void PublishAnimationFrame()
        {
            MovementStepEvent events = _context.StepEvents;
            _context.StepEvents = MovementStepEvent.None;

            MovementStateType state = _currentState != null
                ? _currentState.StateType
                : MovementStateType.Idle;

            events |= _animationFrame.DeriveEdges(
                _context.IsGrounded,
                state == MovementStateType.Climb,
                _context.IsWallSliding,
                state == MovementStateType.Swim,
                state == MovementStateType.Fly);

            if (!_presentationEnabled || _presentationPaused || _replayDepth > 0) return;

            uint sequence = _animationFrame.AdvanceSequence();
            MovementAnimationState frame = BuildAnimationFrame(state, sequence)
                .WithSemanticState(_semanticStateId);
            _lastPublishedState = frame;

            if (_presentationBudget != null && !_presentationBudget.ShouldPublish(in frame, 0f)) return;

            using (AnimationPublishMarker.Auto())
            {
                if (_locomotionSink != null) _locomotionSink.Apply(in frame);

                if (_actionSink == null) return;
                int count = MovementAnimationActionWriter.Write(events, sequence, _actionBuffer);
                for (int i = 0; i < count; i++)
                {
                    MovementAnimationAction action = _actionBuffer[i];
                    _actionSink.Play(in action);
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            _presentationPaused = paused;
            if (paused) _rootMotionAccumulator.Discard();
        }

        private void OnApplicationFocus(bool focused)
        {
            _presentationPaused = !focused;
        }

        protected virtual MovementAnimationState BuildAnimationFrame(MovementStateType state, uint sequence)
        {
            MovementAnimationFlags flags = MovementAnimationFlags.None;
            if (_context.IsGrounded) flags |= MovementAnimationFlags.Grounded;
            if (_context.SprintHeld) flags |= MovementAnimationFlags.Sprinting;
            if (_context.CrouchHeld) flags |= MovementAnimationFlags.Crouching;
            if (state == MovementStateType.Climb) flags |= MovementAnimationFlags.Climbing;
            if (_context.IsWallSliding) flags |= MovementAnimationFlags.WallSliding;
            if (state == MovementStateType.Swim) flags |= MovementAnimationFlags.Swimming;
            if (state == MovementStateType.Fly) flags |= MovementAnimationFlags.Flying;
            if (state == MovementStateType.Roll) flags |= MovementAnimationFlags.Rolling;
            if (_context.IsWallJumping) flags |= MovementAnimationFlags.WallJumping;
            if (_rootMotionAppliedThisStep) flags |= MovementAnimationFlags.RootMotionDriven;

            MovementAnimationState frame = new MovementAnimationState(
                sequence,
                DeltaTime,
                state,
                flags,
                new MovementVector3(_velocity.x, _velocity.y, _velocity.z),
                new MovementVector3(_context.InputDirection.x, _context.InputDirection.z, 0f),
                new MovementVector3(WorldUp.x, WorldUp.y, WorldUp.z),
                new MovementVector3(_groundNormal.x, _groundNormal.y, _groundNormal.z),
                new MovementVector3(_lookDirection.x, _lookDirection.z, 0f));
            return frame.WithExtensions(_rootMotionAppliedThisStep ? 1u : 0u,
                _rootMotionYawThisStep, 0f, 0f, 0f);
        }

        #endregion

        #endregion

#if UNITY_EDITOR
        private int _lastDebugStateId = -1;
        private Transform _lastDebugPlatformTransform;
        private Collider _lastDebugGroundCollider;
#endif

        #region Network Snapshot

        public long NetworkTick { get; set; }
        public double NetworkTimeSeconds { get; set; }

        public MovementSnapshot GetSnapshot()
        {
            MovementSnapshot snapshot = default;
            snapshot.Position = transform.position.ToMovementVector3();
            snapshot.Velocity = _velocity.ToMovementVector3();
            snapshot.WorldUp = WorldUp.ToMovementVector3();
            snapshot.StateId = (ushort)CurrentState;
            snapshot.Flags = 0;
            int jumps = _context.JumpCount;
            snapshot.JumpCount = (byte)(jumps < 0 ? 0 : (jumps > 32 ? 32 : jumps));
            snapshot.VerticalVelocity = _context.VerticalVelocity;
            snapshot.IsGrounded = _context.IsGrounded;
            snapshot.NetworkTick = NetworkTick;
            snapshot.NetworkTimeSeconds = NetworkTimeSeconds;
            return snapshot;
        }

        public MovementFullState CaptureFullState()
        {
            MovementFullState state = default;
            state.NetworkTick = NetworkTick;
            state.NetworkTimeSeconds = NetworkTimeSeconds;
            state.Position = transform.position.ToMovementVector3();
            state.Velocity = _velocity.ToMovementVector3();
            state.WorldUp = WorldUp.ToMovementVector3();
            state.GroundNormal = _context.GroundNormal.ToMovementVector3();
            state.InputDirection = _context.InputDirection.ToMovementVector3();
            state.FacingDirection = _lookDirection.ToMovementVector3();
            state.RollDirection = _context.RollDirection.ToMovementVector3();
            state.WallJumpDirection = _context.WallJumpDirection.ToMovementVector3();
            state.LastWallNormal = _context.LastWallNormal.ToMovementVector3();
            state.WallClimbNormal = _context.WallClimbNormal.ToMovementVector3();
            state.PendingImpulse = _pendingImpulse.ToMovementVector3();
            state.PendingForce = _pendingForce.ToMovementVector3();
            state.PlatformLocalPosition = _movingPlatform.localPosition.ToMovementVector3();
            state.PlatformVelocity = _movingPlatform.platformVelocity.ToMovementVector3();
            state.PlatformLocalRotationX = _movingPlatform.localRotation.x;
            state.PlatformLocalRotationY = _movingPlatform.localRotation.y;
            state.PlatformLocalRotationZ = _movingPlatform.localRotation.z;
            state.PlatformLocalRotationW = _movingPlatform.localRotation.w;
            state.VerticalVelocity = _context.VerticalVelocity;
            state.SlopeAngle = _context.SlopeAngle;
            state.RollTimer = _context.RollTimer;
            state.WallClingTimer = _context.WallClingTimer;
            state.LastWallJumpTime = _context.LastWallJumpTime;
            state.StateElapsed = 0f;
            state.UnconstrainedTimer = _unconstrainedTimer;
            state.CurrentSpeed = _context.CurrentSpeed;
            state.JumpCount = _context.JumpCount;
            state.StateTick = 0;
            state.StateId = (ushort)CurrentState;
            state.ClimbingMode = (byte)_context.ClimbingMode;
            state.IsGrounded = _context.IsGrounded;
            state.WasGrounded = _context.WasGrounded;
            state.IsOnNonWalkableSlope = _context.IsOnNonWalkableSlope;
            state.IsWallJumping = _context.IsWallJumping;
            state.IsWallSliding = _context.IsWallSliding;
            state.IsOnPlatform = _movingPlatform.isOnPlatform;
            state.UseRootMotion = _context.UseRootMotion;
            state.JumpPressed = _context.JumpPressed;
            state.SprintHeld = _context.SprintHeld;
            state.CrouchHeld = _context.CrouchHeld;
            state.RollPressed = _context.RollPressed;
            return state;
        }

        public void ApplySnapshot(in MovementSnapshot snapshot)
        {
            Vector3 position = snapshot.Position.ToVector3();
            _updatedPosition = position;
            _velocity = snapshot.Velocity.ToVector3();
            WorldUp = snapshot.WorldUp.ToVector3();
            _context.VerticalVelocity = snapshot.VerticalVelocity;
            _context.IsGrounded = snapshot.IsGrounded;
            _context.JumpCount = snapshot.JumpCount;
            NetworkTick = snapshot.NetworkTick;
            NetworkTimeSeconds = snapshot.NetworkTimeSeconds;

            transform.position = position;
            _rigidbody.position = position;

            if (_currentState == null || (ushort)_currentState.StateType != snapshot.StateId)
                RequestStateChange((MovementStateType)snapshot.StateId);
        }

        public void ApplyFullState(in MovementFullState state)
        {
            Vector3 position = state.Position.ToVector3();
            _updatedPosition = position;
            _velocity = state.Velocity.ToVector3();
            WorldUp = state.WorldUp.ToVector3();
            transform.position = position;
            _rigidbody.position = position;

            NetworkTick = state.NetworkTick;
            NetworkTimeSeconds = state.NetworkTimeSeconds;
            _context.GroundNormal = state.GroundNormal.ToFloat3();
            _context.InputDirection = state.InputDirection.ToFloat3();
            _lookDirection = state.FacingDirection.ToFloat3();
            _context.RollDirection = state.RollDirection.ToFloat3();
            _context.WallJumpDirection = state.WallJumpDirection.ToVector3();
            _context.LastWallNormal = state.LastWallNormal.ToVector3();
            _context.WallClimbNormal = state.WallClimbNormal.ToVector3();
            _pendingImpulse = state.PendingImpulse.ToVector3();
            _pendingForce = state.PendingForce.ToVector3();
            _movingPlatform.localPosition = state.PlatformLocalPosition.ToVector3();
            _movingPlatform.platformVelocity = state.PlatformVelocity.ToVector3();
            _movingPlatform.localRotation = new Quaternion(
                state.PlatformLocalRotationX, state.PlatformLocalRotationY,
                state.PlatformLocalRotationZ, state.PlatformLocalRotationW);
            _context.VerticalVelocity = state.VerticalVelocity;
            _context.SlopeAngle = state.SlopeAngle;
            _context.RollTimer = state.RollTimer;
            _context.WallClingTimer = state.WallClingTimer;
            _context.LastWallJumpTime = state.LastWallJumpTime;
            _unconstrainedTimer = state.UnconstrainedTimer;
            _context.CurrentSpeed = state.CurrentSpeed;
            _context.CurrentVelocity = state.Velocity.ToFloat3();
            _context.JumpCount = state.JumpCount;
            _context.ClimbingMode = (ClimbingMode)state.ClimbingMode;
            _context.IsGrounded = state.IsGrounded;
            _context.WasGrounded = state.WasGrounded;
            _context.IsOnNonWalkableSlope = state.IsOnNonWalkableSlope;
            _context.IsWallJumping = state.IsWallJumping;
            _context.IsWallSliding = state.IsWallSliding;
            _movingPlatform.isOnPlatform = state.IsOnPlatform;
            _context.UseRootMotion = state.UseRootMotion;
            _context.JumpPressed = state.JumpPressed;
            _context.SprintHeld = state.SprintHeld;
            _context.CrouchHeld = state.CrouchHeld;
            _context.RollPressed = state.RollPressed;

            if (_currentState == null || (ushort)_currentState.StateType != state.StateId)
                RequestStateChange((MovementStateType)state.StateId);

            _context.StepEvents = MovementStepEvent.None;
            _animationFrame.ResetBaseline();
            _rootMotionAccumulator.Discard();
        }

        public void ResetFromFullState(in MovementFullState state)
        {
            ApplyFullState(state);
            _inheritedPlatformVelocity = Vector3.zero;
            _lastGroundVelocity = Vector3.zero;
            _hasLanded = false;
            _collisionCount = 0;
        }

        #endregion

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

            DrawCapsuleGizmo(pos, center, radius, halfHeight, currentWorldUp,
                _isGrounded ? Color.green : Color.red);

            if (showGroundDetection)
            {
                float capsuleBottomY = center.y - halfHeight;
                Vector3 rayOrigin = pos + currentWorldUp * (capsuleBottomY + radius);
                float rayDist = config.StepHeight + 0.1f;

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(rayOrigin, rayOrigin - currentWorldUp * rayDist);

                if (_isGrounded)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(_groundPoint, 0.05f);

                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(_groundPoint, _groundPoint + _groundNormal * 0.3f);

                    float slopeAngle = Vector3.Angle(_groundNormal, currentWorldUp);
                    Gizmos.color = slopeAngle <= config.SlopeLimit ? Color.green : Color.red;
                    DrawAngleArc(_groundPoint, _groundNormal, currentWorldUp, slopeAngle);
                }

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                Vector3 stepBottom = pos + currentWorldUp * capsuleBottomY;
                Vector3 stepTop = stepBottom + currentWorldUp * config.StepHeight;
                Gizmos.DrawLine(stepBottom + transform.right * radius, stepTop + transform.right * radius);
                Gizmos.DrawLine(stepBottom - transform.right * radius, stepTop - transform.right * radius);
                Gizmos.DrawLine(stepBottom + transform.forward * radius, stepTop + transform.forward * radius);
                Gizmos.DrawLine(stepBottom - transform.forward * radius, stepTop - transform.forward * radius);
            }

            if (showVelocity && Application.isPlaying)
            {
                Vector3 velStart = pos + center;
                if (_velocity.sqrMagnitude > 0.01f)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(velStart, velStart + _velocity * 0.5f);

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
                for (int i = 0; i < _collisionCount; i++)
                {
                    ref CollisionResult col = ref _collisionResults[i];

                    Gizmos.color = col.isWalkable ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(col.point, 0.03f);

                    Gizmos.color = Color.white;
                    Gizmos.DrawLine(col.point, col.point + col.normal * 0.2f);

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

            Gizmos.DrawWireSphere(topSphere, radius);
            Gizmos.DrawWireSphere(bottomSphere, radius);

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
                sb.AppendLine($"Slope Angle: {slopeAngle:F1} deg (Limit: {(config != null ? config.SlopeLimit : 45f):F1} deg)");
            }

            sb.AppendLine($"Velocity: {_velocity.magnitude:F2} m/s");
            sb.AppendLine($"Constrained: {_isConstrainedToGround}");
            sb.AppendLine($"Collisions: {_collisionCount}");

            return sb.ToString();
        }
#endif
    }
}
