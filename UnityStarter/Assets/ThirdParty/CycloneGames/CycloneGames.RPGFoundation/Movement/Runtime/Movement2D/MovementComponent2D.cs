using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States;

namespace CycloneGames.RPGFoundation.Movement.Runtime.Movement2D
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovementComponent2D : MonoBehaviour, IMovementStateQuery2D, IMovementSnapshotProvider
    {
        private static readonly LogChannel Log = RpgMovementLog.Channel;

        [SerializeField] private MovementConfig2D config;
        [SerializeField] private Transform worldUpSource;
        [Tooltip("Consume root motion deltas from a bound IRootMotionSource and feed them "
                 + "through the ordinary velocity resolution.")]
        [SerializeField] private bool useRootMotion = false;

        [Tooltip("Ignore global Time.timeScale. When enabled, this character will use Time.unscaledDeltaTime instead.\n" +
                 "Use cases:\n" +
                 "- UI characters that should animate during pause\n" +
                 "- Characters that need to move during slow-motion effects\n" +
                 "- Cutscene characters\n" +
                 "- Dynamic switching: Can be changed at runtime to toggle between affected/ignored time scale\n" +
                 "Note: LocalTimeScale still applies even when this is enabled.")]
        [SerializeField] private bool ignoreTimeScale = false;

        public bool IgnoreTimeScale
        {
            get => ignoreTimeScale;
            set => ignoreTimeScale = value;
        }

        public float LocalTimeScale { get; set; } = 1f;
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
        public void BindAnimation(IMovementLocomotionSink locomotionSink, IMovementActionSink actionSink)
        {
            _locomotionSink = locomotionSink;
            _actionSink = actionSink;
        }

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

        public void SetUseRootMotion(bool enable)
        {
            useRootMotion = enable;
            if (!enable) _rootMotionAccumulator.Discard();
        }

        public void BindSemantics(MovementExtensionRegistry registry) { _semantics = registry; }

        public void BindPresentationBudget(IMovementPresentationBudget budget) { _presentationBudget = budget; }

        public void SetSemanticStateId(uint id) { _semanticStateId = id; }

        public MovementExtensionRegistry Semantics { get { return _semantics; } }

        public void BindMovementLayers(MovementLayerArbiter arbiter) { _movementLayers = arbiter; }

        public MovementLayerArbiter MovementLayers { get { return _movementLayers; } }

        // Only the outermost window saves layer state and restores it.
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

        public IMovementConfig2DReadOnly Config => _configReadOnly ??= new MovementConfig2DReadOnlyWrapper(config);

        private IMovementConfig2DReadOnly _configReadOnly;

        public event Action<MovementStateType, MovementStateType> OnStateChanged;
        public event Action OnLanded;
        public event Action OnJumpStart;

        private Rigidbody2D _rigidbody;
        private MovementStateBase2D _currentState;
        private MovementContext2D _context;
        private readonly MovementStatePool<MovementStateBase2D> _statePool = new MovementStatePool<MovementStateBase2D>();
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

        private float _coyoteTimeCounter;
        private float _jumpBufferCounter;
        private bool _wasGrounded;
        private bool _facingRight;
        private Transform _groundCheck;

        private const float _minSqrMagnitudeForMovement = 0.0001f;

        private MovingPlatformData2D _movingPlatform;
        private Collider2D _lastGroundCollider;
        private Vector2 _inheritedPlatformVelocity;
        private Vector2 _lastGroundVelocity;

        private Vector2 _pendingImpulse;
        private Vector2 _pendingForce;
        private float _unconstrainedTimer;

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

        private void Reset()
        {
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null) return;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();

            if (config == null)
            {
                Log.Error("[MovementComponent2D] MovementConfig2D is not assigned. Creating default config.");
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

            InitializePhysics();
            InitializeContext();
            _currentState = _statePool.Get<IdleState2D>();

            _facingRight = config.FacingRight;

            if (MovementAuthority == null)
            {
                MovementAuthority = GetComponent<IMovementAuthority>();
            }
        }

        private void InitializePhysics()
        {
            _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (config.MovementType == MovementType2D.TopDown)
            {
                _rigidbody.gravityScale = 0;
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else if (config.MovementType == MovementType2D.Platformer && config.LockZAxis)
            {
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else
            {
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
                _rigidbody.gravityScale = config.Gravity;
            }
        }

        private void InitializeContext()
        {
            _context = new MovementContext2D
            {
                Rigidbody = _rigidbody,
                Transform = transform,
                Config = config,
                WorldUp = new float2(0, 1),
                IsGrounded = false,
                JumpCount = 0,
                MovementAuthority = null,
                StatePool = _statePool
            };
        }

        void Update()
        {
            UpdateWorldUp();
            UpdateFacing();
        }

        void FixedUpdate()
        {
            SyncRootMotionTimeSource();
            if (_deterministicStep != null && _deterministicStep.Step(FixedDeltaTime)) return;
            if (_currentState == null)
            {
                _currentState = _statePool.Get<IdleState2D>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
            }

            UpdateWorldUp();

            if (_unconstrainedTimer > 0)
            {
                _unconstrainedTimer -= FixedDeltaTime;
            }

            CheckGround();
            HandleCoyoteTime();
            HandleJumpBuffer(FixedDeltaTime);
            ApplyMovingPlatform();
            UpdateContext(FixedDeltaTime);
            ExecuteStateMachine(FixedDeltaTime);
            ApplyPendingForces(FixedDeltaTime);
            UpdateFacing();
            UpdateMovingPlatformTracking(FixedDeltaTime);
        }

        private void UpdateWorldUp()
        {
            if (worldUpSource != null)
                _context.WorldUp = new float2(worldUpSource.up.x, worldUpSource.up.y);
            else
                _context.WorldUp = new float2(0, 1);
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

        private bool TryBridgeGap2D(Vector2 currentCheckPos)
        {
            if (config == null) return false;

            float currentSpeed = Mathf.Abs(_context.CurrentVelocity.x);
            if (currentSpeed < config.MinSpeedForGapBridge) return false;

            float moveDir = Mathf.Sign(_context.CurrentVelocity.x);
            if (Mathf.Approximately(moveDir, 0)) return false;

            for (float dist = 0.3f; dist <= config.MaxGapDistance; dist += 0.2f)
            {
                Vector2 aheadPos = currentCheckPos + new Vector2(moveDir * dist, 0);
                Collider2D ground = Physics2D.OverlapBox(aheadPos, config.GroundCheckSize, 0, config.GroundLayer);

                if (ground != null)
                {
                    _lastGroundCollider = ground;
                    return true;
                }
            }

            return false;
        }

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
        }

        private void UpdateMovingPlatformTracking(float deltaTime)
        {
            if (config == null || !config.EnableMovingPlatform)
            {
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }

            if (!_context.IsGrounded)
            {
                if (_movingPlatform.isOnPlatform && config.InheritPlatformMomentum)
                {
                    _inheritedPlatformVelocity = _movingPlatform.platformVelocity;
                }
                if (_movingPlatform.isOnPlatform) _movingPlatform.Clear();
                return;
            }
            else
            {
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
                        _movingPlatform.UpdatePlatformVelocity(deltaTime);
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

        private void HandleJumpBuffer(float deltaTime)
        {
            if (config == null) return;

            if (_jumpBufferCounter > 0f)
            {
                _jumpBufferCounter -= deltaTime;
            }

            if (_jumpBufferCounter > 0f && (_context.IsGrounded || _coyoteTimeCounter > 0f))
            {
                if (RequestStateChange(MovementStateType.Jump))
                {
                    _jumpBufferCounter = 0f;
                }
            }
        }

        private void UpdateContext(float deltaTime)
        {
            if (config == null)
            {
                Log.Error("[MovementComponent2D] MovementConfig2D is null. Movement may not work correctly.");
                return;
            }

            _context.DeltaTime = deltaTime;
            _context.FixedDeltaTime = FixedDeltaTime;

            _context.MovementAuthority = MovementAuthority;
        }

        private void ExecuteStateMachine(float deltaTime)
        {
            if (_currentState == null)
            {
                _currentState = _statePool.Get<IdleState2D>();
                if (_currentState != null)
                {
                    _currentState.OnEnter(ref _context);
                }
                else
                {
                    Log.Error("[MovementComponent2D] Failed to initialize state. Movement will not work.");
                    return;
                }
            }

            MovementRootMotionDelta captured = default;
            if (_rootMotionSource != null && _rootMotionSource.TryConsume(out captured))
                _rootMotionAccumulator.Push(in captured);
            else if (!useRootMotion)
                _rootMotionAccumulator.Discard();
            MovementRootMotionDelta stepDelta = default;
            bool rootMotionAvailable = _rootMotionAccumulator.TryConsumeStep(deltaTime, out stepDelta);
            bool shouldUseRootMotion = useRootMotion && rootMotionAvailable;
            _rootMotionAppliedThisStep = shouldUseRootMotion;
            _rootMotionYawThisStep = shouldUseRootMotion ? stepDelta.YawVelocity : 0f;

            float2 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            if (shouldUseRootMotion)
            {
                MovementVector3 rootVelocity = stepDelta.LinearVelocity;
                displacement = new float2(rootVelocity.X, rootVelocity.Y) * deltaTime;
            }

            float dt = deltaTime > 0 ? deltaTime : 1f;

            {
#if UNITY_6000_0_OR_NEWER
            Vector2 targetVelocity = new Vector2(displacement.x / dt, _rigidbody.linearVelocity.y);
#else
            Vector2 targetVelocity = new Vector2(displacement.x / dt, _rigidbody.velocity.y);
#endif

            if (config.MovementType == MovementType2D.TopDown)
            {
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = new Vector2(displacement.x / dt, displacement.y / dt);
#else
                _rigidbody.velocity = new Vector2(displacement.x / dt, displacement.y / dt);
#endif
            }
            else if (config.MovementType == MovementType2D.BeltScroll)
            {

                bool isAirborne = _currentState != null &&
                                  (_currentState.StateType == MovementStateType.Jump ||
                                   _currentState.StateType == MovementStateType.Fall);

                if (isAirborne)
                {
#if UNITY_6000_0_OR_NEWER
                    _rigidbody.linearVelocity = new Vector2(targetVelocity.x, _rigidbody.linearVelocity.y);
#else
                    _rigidbody.velocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
#endif
                }
                else
                {
#if UNITY_6000_0_OR_NEWER
                    _rigidbody.linearVelocity = new Vector2(displacement.x / dt, displacement.y / dt);
#else
                    _rigidbody.velocity = new Vector2(displacement.x / dt, displacement.y / dt);
#endif
                }
            }
            else
            {
#if UNITY_6000_0_OR_NEWER
                Vector2 finalVelocity = new Vector2(targetVelocity.x, _rigidbody.linearVelocity.y);
#else
                Vector2 finalVelocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
#endif

                if (_context.IsGrounded)
                {
                    _lastGroundVelocity = new Vector2(displacement.x / dt, 0);
                }
                else
                {
#if UNITY_6000_0_OR_NEWER
                    Vector2 totalInheritedVelocity = _inheritedPlatformVelocity + _lastGroundVelocity;
#else
                    Vector2 totalInheritedVelocity = _inheritedPlatformVelocity + _lastGroundVelocity;
#endif

                    if (totalInheritedVelocity.sqrMagnitude > _minSqrMagnitudeForMovement)
                    {
                        float inheritedSpeed = Mathf.Abs(totalInheritedVelocity.x);
                        float inputSpeed = Mathf.Abs(targetVelocity.x);

                        if (inheritedSpeed > inputSpeed)
                        {
                            finalVelocity.x = totalInheritedVelocity.x;
                        }
                    }
                }

#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = finalVelocity;
#else
                _rigidbody.velocity = finalVelocity;
#endif
            }
            }

            MovementStateBase2D nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
            {
                RequestStateChangeInternal(nextState);
            }

            if (_movementLayers != null)
            {
                Vector2 current = _rigidbody.velocity;
                var layerFrame = new MovementLayerFrame(
                    ++_layerSequence, deltaTime,
                    new MovementVector3(current.x, current.y, 0f),
                    new MovementVector3(_context.WorldUp.x, _context.WorldUp.y, 0f),
                    _context.IsGrounded);
                MovementLayerOutput layers = _movementLayers.Resolve(in layerFrame);
                _rigidbody.velocity += new Vector2(
                    layers.VelocityDelta.X, layers.VelocityDelta.Y);
                _rigidbody.velocity += new Vector2(
                    layers.PositionDelta.X, layers.PositionDelta.Y)
                    / Mathf.Max(deltaTime, 0.0001f);
            }

            PublishAnimationFrame();
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

#if UNITY_6000_0_OR_NEWER
            Vector2 velocity = _rigidbody.linearVelocity;
#else
            Vector2 velocity = _rigidbody.velocity;
#endif
            MovementAnimationState frame = new MovementAnimationState(
                sequence,
                FixedDeltaTime,
                state,
                flags,
                new MovementVector3(velocity.x, velocity.y, 0f),
                new MovementVector3(_context.InputDirection.x, _context.InputDirection.y, 0f),
                new MovementVector3(_context.WorldUp.x, _context.WorldUp.y, 0f),
                new MovementVector3(_context.GroundNormal.x, _context.GroundNormal.y, 0f),
                new MovementVector3(_context.LookDirection.x, _context.LookDirection.y, 0f));
            return frame.WithExtensions(_rootMotionAppliedThisStep ? 1u : 0u,
                _rootMotionYawThisStep, 0f, 0f, 0f);
        }

        #endregion

        private void UpdateFacing()
        {
            if (config == null) return;

            if (config.MovementType == MovementType2D.TopDown || config.MovementType == MovementType2D.BeltScroll)
            {
                float2 facingSource = math.lengthsq(_context.LookDirection) > _minSqrMagnitudeForMovement
                    ? _context.LookDirection
                    : _context.InputDirection;

                if (math.lengthsq(facingSource) > _minSqrMagnitudeForMovement)
                {
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
                _context.InputDirection = new float2(direction.x, direction.y);
            }
            else
            {
                _context.InputDirection = new float2(direction.x, 0);
            }
        }

        public void SetJumpPressed(bool pressed)
        {
            if (config != null && !config.EnableJump) return;

            bool wasPressed = _context.JumpPressed;
            _context.JumpPressed = pressed;

            if (!pressed || wasPressed)
            {
                return;
            }

            if (_coyoteTimeCounter > 0 || _context.IsGrounded)
            {
                RequestStateChange(MovementStateType.Jump);
                return;
            }

            _jumpBufferCounter = config != null ? config.JumpBufferTime : 0f;
        }

        public void SetSprintHeld(bool held)
        {
            _context.SprintHeld = held;
        }

        public void SetCrouchHeld(bool held)
        {
            _context.CrouchHeld = held;
        }

        public void SetRollPressed(bool pressed)
        {
            bool wasPressed = _context.RollPressed;
            _context.RollPressed = pressed;

            if (pressed && !wasPressed)
            {
                RequestStateChange(MovementStateType.Roll);
            }
        }

        public bool RequestClimb(ClimbingMode climbingMode, int wallSide = 0, object context = null)
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
                    if (!config.EnableWallClimbing)
                    {
                        return false;
                    }
                    _context.WallClimbSide = math.clamp(wallSide, -1, 1);
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
            _context.WallClimbSide = 0;

            if (CurrentState != MovementStateType.Climb)
            {
                return true;
            }

            return RequestStateChange(_context.IsGrounded ? MovementStateType.Idle : MovementStateType.Fall);
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

        public void SetRotation(Quaternion rotation, bool immediate = false)
        {
            if (immediate)
            {
                transform.rotation = rotation;

                if (config != null && config.MovementType != MovementType2D.TopDown)
                {
                    Vector3 forward = rotation * Vector3.right;
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

        public bool RequestStateChange(MovementStateType targetStateType, object context = null)
        {
            if (MovementAuthority != null && !MovementAuthority.CanEnterState(targetStateType, context))
            {
                return false;
            }

            MovementStateBase2D targetState = GetStateByType(targetStateType);
            if (targetState == null)
            {
                Log.Warning(
                    targetStateType,
                    static (value, builder) => builder
                        .Append("[MovementComponent2D] State ")
                        .Append(value)
                        .Append(" not found."));
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

            if (MovementAuthority != null) MovementAuthority.OnStateExited(oldStateType);

            _currentState = newState;
            _currentState.OnEnter(ref _context);

            if (MovementAuthority != null) MovementAuthority.OnStateEntered(newStateType);

            OnStateChanged?.Invoke(oldStateType, newStateType);

            if (newStateType == MovementStateType.Jump)
            {
                OnJumpStart?.Invoke();
                _coyoteTimeCounter = 0;
            }
        }

        MovementStateBase2D GetStateByType(MovementStateType stateType)
        {
            switch (stateType)
            {
                case MovementStateType.Idle: return _statePool.Get<IdleState2D>();
                case MovementStateType.Walk: return _statePool.Get<WalkState2D>();
                case MovementStateType.Run: return _statePool.Get<RunState2D>();
                case MovementStateType.Sprint: return _statePool.Get<SprintState2D>();
                case MovementStateType.Crouch: return _statePool.Get<CrouchState2D>();
                case MovementStateType.Jump: return _statePool.Get<JumpState2D>();
                case MovementStateType.Fall: return _statePool.Get<FallState2D>();
                case MovementStateType.Roll: return _statePool.Get<RollState2D>();
                case MovementStateType.Climb:
                    return _context.ClimbingMode switch
                    {
                        ClimbingMode.Ladder => _statePool.Get<LadderClimbState2D>(),
                        ClimbingMode.Wall => _statePool.Get<WallClimbState2D>(),
                        _ => null
                    };
                case MovementStateType.Swim: return _statePool.Get<IdleState2D>();
                case MovementStateType.Fly: return _statePool.Get<IdleState2D>();
                default:
                    Log.Warning(
                        stateType,
                        static (value, builder) => builder
                            .Append("[MovementComponent2D] State ")
                            .Append(value)
                            .Append(" not implemented yet."));
                    return null;
            }
        }

        void OnDestroy()
        {
            OnStateChanged = null;
            OnLanded = null;
            OnJumpStart = null;
        }

        #region Network Snapshot

        public long NetworkTick { get; set; }
        public double NetworkTimeSeconds { get; set; }

        public MovementSnapshot GetSnapshot()
        {
            MovementSnapshot snapshot = default;
            snapshot.Position = transform.position.ToMovementVector3();
            snapshot.Velocity = new MovementVector3(_context.CurrentVelocity.x, _context.CurrentVelocity.y, 0f);
            snapshot.WorldUp = new MovementVector3(_context.WorldUp.x, _context.WorldUp.y, 0f);
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
            state.Velocity = new MovementVector3(_context.CurrentVelocity.x, _context.CurrentVelocity.y, 0f);
            state.WorldUp = new MovementVector3(_context.WorldUp.x, _context.WorldUp.y, 0f);
            state.GroundNormal = new MovementVector3(_context.GroundNormal.x, _context.GroundNormal.y, 0f);
            state.InputDirection = new MovementVector3(_context.InputDirection.x, _context.InputDirection.y, 0f);
            state.FacingDirection = new MovementVector3(_context.LookDirection.x, _context.LookDirection.y, 0f);
            state.RollDirection = new MovementVector3(_context.RollDirection.x, _context.RollDirection.y, 0f);
            state.WallJumpDirection = new MovementVector3(_context.WallJumpDirection.x, _context.WallJumpDirection.y, 0f);
            state.LastWallNormal = MovementVector3.Zero;
            state.WallClimbNormal = MovementVector3.Zero;
            state.PendingImpulse = new MovementVector3(_pendingImpulse.x, _pendingImpulse.y, 0f);
            state.PendingForce = new MovementVector3(_pendingForce.x, _pendingForce.y, 0f);
            state.PlatformLocalPosition = MovementVector3.Zero;
            state.PlatformVelocity = MovementVector3.Zero;
            state.PlatformLocalRotationX = 0f;
            state.PlatformLocalRotationY = 0f;
            state.PlatformLocalRotationZ = 0f;
            state.PlatformLocalRotationW = 1f;
            state.VerticalVelocity = _context.VerticalVelocity;
            state.SlopeAngle = _context.GroundAngle;
            state.RollTimer = _context.RollTimer;
            state.WallClingTimer = _context.WallClingTimer;
            state.LastWallJumpTime = _context.LastWallJumpTime;
            state.StateElapsed = _jumpBufferCounter;
            state.UnconstrainedTimer = _unconstrainedTimer;
            state.CurrentSpeed = _context.CurrentSpeed;
            state.JumpCount = _context.JumpCount;
            state.StateTick = 0;
            state.Extension0 = _coyoteTimeCounter;
            state.Extension1 = _context.PendingDepth;
            state.ExtensionInt0 = _context.LastWallSide;
            state.ExtensionInt1 = _context.WallClimbSide;
            state.StateId = (ushort)CurrentState;
            state.ClimbingMode = (byte)_context.ClimbingMode;
            state.IsGrounded = _context.IsGrounded;
            state.WasGrounded = _context.IsGrounded;
            state.IsOnNonWalkableSlope = false;
            state.IsWallJumping = _context.IsWallJumping;
            state.IsWallSliding = _context.IsWallSliding;
            state.IsOnPlatform = false;
            state.UseRootMotion = false;
            state.JumpPressed = _context.JumpPressed;
            state.SprintHeld = _context.SprintHeld;
            state.CrouchHeld = _context.CrouchHeld;
            state.RollPressed = _context.RollPressed;
            return state;
        }

        public void ApplySnapshot(in MovementSnapshot snapshot)
        {
            Vector3 position = snapshot.Position.ToVector3();
            transform.position = position;
            _rigidbody.position = new Vector2(position.x, position.y);
            _context.CurrentVelocity = new float2(snapshot.Velocity.X, snapshot.Velocity.Y);
            _context.WorldUp = new float2(snapshot.WorldUp.X, snapshot.WorldUp.Y);
            _context.VerticalVelocity = snapshot.VerticalVelocity;
            _context.IsGrounded = snapshot.IsGrounded;
            _context.JumpCount = snapshot.JumpCount;
            NetworkTick = snapshot.NetworkTick;
            NetworkTimeSeconds = snapshot.NetworkTimeSeconds;

            if (_currentState == null || (ushort)_currentState.StateType != snapshot.StateId)
                RequestStateChange((MovementStateType)snapshot.StateId);
        }

        public void ApplyFullState(in MovementFullState state)
        {
            Vector3 position = state.Position.ToVector3();
            transform.position = position;
            _rigidbody.position = new Vector2(position.x, position.y);
            NetworkTick = state.NetworkTick;
            NetworkTimeSeconds = state.NetworkTimeSeconds;
            _context.CurrentVelocity = new float2(state.Velocity.X, state.Velocity.Y);
            _context.WorldUp = new float2(state.WorldUp.X, state.WorldUp.Y);
            _context.GroundNormal = new float2(state.GroundNormal.X, state.GroundNormal.Y);
            _context.InputDirection = new float2(state.InputDirection.X, state.InputDirection.Y);
            _context.LookDirection = new float2(state.FacingDirection.X, state.FacingDirection.Y);
            _context.RollDirection = new float2(state.RollDirection.X, state.RollDirection.Y);
            _context.WallJumpDirection = new Vector2(state.WallJumpDirection.X, state.WallJumpDirection.Y);
            _pendingImpulse = new Vector2(state.PendingImpulse.X, state.PendingImpulse.Y);
            _pendingForce = new Vector2(state.PendingForce.X, state.PendingForce.Y);
            _context.VerticalVelocity = state.VerticalVelocity;
            _context.GroundAngle = state.SlopeAngle;
            _context.RollTimer = state.RollTimer;
            _context.WallClingTimer = state.WallClingTimer;
            _context.LastWallJumpTime = state.LastWallJumpTime;
            _jumpBufferCounter = state.StateElapsed;
            _unconstrainedTimer = state.UnconstrainedTimer;
            _context.CurrentSpeed = state.CurrentSpeed;
            _context.JumpCount = state.JumpCount;
            _coyoteTimeCounter = state.Extension0;
            _context.PendingDepth = state.Extension1;
            _context.LastWallSide = state.ExtensionInt0;
            _context.WallClimbSide = state.ExtensionInt1;
            _context.ClimbingMode = (ClimbingMode)state.ClimbingMode;
            _context.IsGrounded = state.IsGrounded;
            _context.IsWallJumping = state.IsWallJumping;
            _context.IsWallSliding = state.IsWallSliding;
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
            _jumpBufferCounter = 0f;
            _coyoteTimeCounter = 0f;
        }

        #endregion

        #region Force System

        private void ApplyPendingForces(float deltaTime)
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
                _rigidbody.linearVelocity += _pendingForce * deltaTime;
#else
                _rigidbody.velocity += _pendingForce * deltaTime;
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
