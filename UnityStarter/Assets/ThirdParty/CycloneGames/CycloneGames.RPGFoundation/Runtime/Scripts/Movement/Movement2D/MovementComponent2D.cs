using System;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Movement;
using CycloneGames.RPGFoundation.Runtime.Movement2D.States;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovementComponent2D : MonoBehaviour, IMovementStateQuery2D
    {
        [Header("Configuration")]
        [SerializeField] private MovementConfig2D config;

        [Header("Dependencies")]
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;

        [Header("Ground Detection")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Vector2 groundCheckSize = new Vector2(0.8f, 0.1f);

        [Header("Settings")]
        [SerializeField] private bool ignoreTimeScale = false;
        [SerializeField] private bool facingRight = true;

        public float LocalTimeScale { get; set; } = 1f;
        public IMovementAuthority MovementAuthority { get; set; }

        public event Action<MovementStateType, MovementStateType> OnStateChanged;
        public event Action OnLanded;
        public event Action OnJumpStart;

        private Rigidbody2D _rigidbody;
        private MovementStateBase2D _currentState;
        private MovementContext2D _context;

        private float _coyoteTimeCounter;
        private float _jumpBufferCounter;
        private bool _wasGrounded;

        private const float _minSqrMagnitudeForMovement = 0.0001f;

        private float DeltaTime => (ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime) * LocalTimeScale;
        private float FixedDeltaTime => (ignoreTimeScale ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime) * LocalTimeScale;

        #region IMovementStateQuery2D Implementation
        public MovementStateType CurrentState => _currentState?.StateType ?? MovementStateType.Idle;
        public bool IsGrounded => _context.IsGrounded;
        public float CurrentSpeed => _context.CurrentSpeed;
        public Vector2 Velocity => _context.CurrentVelocity;
        public bool IsMoving => math.lengthsq(_context.CurrentVelocity) > _minSqrMagnitudeForMovement;
        #endregion

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            
            IAnimationController animationController = null;
            
            // Priority: Animancer > Manually assigned Animator > Auto-found Animator
            if (animancerComponent != null)
            {
                // Validate Animancer's internal Animator if manual Animator is also assigned
                if (characterAnimator != null)
                {
                    // Try to extract Animator from Animancer to verify consistency
                    var animancerType = animancerComponent.GetType();
                    var animatorProperty = animancerType.GetProperty("Animator",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    
                    if (animatorProperty != null)
                    {
                        var animancerAnimator = animatorProperty.GetValue(animancerComponent) as Animator;
                        
                        if (animancerAnimator != null && animancerAnimator != characterAnimator)
                        {
                            Debug.LogWarning(
                                $"[MovementComponent2D] AnimancerComponent and manually assigned Animator reference different components on {gameObject.name}. " +
                                $"Animancer's Animator: {animancerAnimator.name}, Manual Animator: {characterAnimator.name}. " +
                                "Animancer will use its internal Animator. Consider removing the manual Animator assignment.",
                                this);
                        }
                        else if (animancerAnimator == null)
                        {
                            Debug.LogWarning(
                                $"[MovementComponent2D] AnimancerComponent on {gameObject.name} does not have an internal Animator. " +
                                "It will use Parameters mode instead of Animator mode.",
                                this);
                        }
                    }
                }

                // Create parameter name mapping for Animancer Parameters mode
                var parameterMap = CreateParameterNameMap();
                animationController = new AnimancerAnimationController(animancerComponent, parameterMap);
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
                Debug.LogError($"[MovementComponent2D] MovementConfig2D is not assigned on {gameObject.name}. Creating default config.", this);
                config = ScriptableObject.CreateInstance<MovementConfig2D>();
            }

            if (groundCheck == null)
            {
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                groundCheckObj.transform.localPosition = Vector3.zero;
                groundCheck = groundCheckObj.transform;
            }

            PreWarmAnimationParameters();
            InitializePhysics();
            InitializeContext(animationController);
            _currentState = StatePool<MovementStateBase2D>.GetState<IdleState2D>();
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
            
            if (!string.IsNullOrEmpty(config.verticalSpeedParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.verticalSpeedParameter);
                map[hash] = config.verticalSpeedParameter;
            }
            
            if (!string.IsNullOrEmpty(config.inputXParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.inputXParameter);
                map[hash] = config.inputXParameter;
            }
            
            if (!string.IsNullOrEmpty(config.inputYParameter))
            {
                int hash = AnimationParameterCache.GetHash(config.inputYParameter);
                map[hash] = config.inputYParameter;
            }
            
            if (!string.IsNullOrEmpty(config.rollTrigger))
            {
                int hash = AnimationParameterCache.GetHash(config.rollTrigger);
                map[hash] = config.rollTrigger;
            }
            
            return map;
        }

        private void PreWarmAnimationParameters()
        {
            if (config == null) return;
            
            AnimationParameterCache.PreWarm(
                config.movementSpeedParameter,
                config.isGroundedParameter,
                config.jumpTrigger,
                config.verticalSpeedParameter,
                config.inputXParameter,
                config.inputYParameter
            );
            
            if (!string.IsNullOrEmpty(config.rollTrigger))
            {
                AnimationParameterCache.PreWarm(config.rollTrigger);
            }
        }

        private void InitializePhysics()
        {
            if (config.movementType == MovementType2D.TopDown)
            {
                // TopDown has no gravity
                _rigidbody.gravityScale = 0;
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else if (config.movementType == MovementType2D.Platformer && config.lockZAxis)
            {
                // Rigidbody2D automatically ignores Z position, but we can freeze rotation
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
            else
            {
                _rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
                _rigidbody.gravityScale = config.gravity;
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
                IsGrounded = false
            };
        }

        void Update()
        {
            HandleJumpBuffer();
            UpdateContext();
            ExecuteStateMachine();
            UpdateFacing();
        }

        void FixedUpdate()
        {
            CheckGround();
            HandleCoyoteTime();
        }

        private void CheckGround()
        {
            bool wasGrounded = _context.IsGrounded;
            _context.IsGrounded = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0, config.groundLayer);

            if (!wasGrounded && _context.IsGrounded)
            {
                OnLanded?.Invoke();
            }
            _wasGrounded = _context.IsGrounded;
        }

        private void HandleCoyoteTime()
        {
            if (_context.IsGrounded)
            {
                _coyoteTimeCounter = config.coyoteTime;
            }
            else
            {
                _coyoteTimeCounter -= FixedDeltaTime;
            }
        }

        private void HandleJumpBuffer()
        {
            if (_context.JumpPressed)
            {
                _jumpBufferCounter = config.jumpBufferTime;
            }
            else
            {
                _jumpBufferCounter -= DeltaTime;
            }
        }

        private void UpdateContext()
        {
            _context.DeltaTime = DeltaTime;
            _context.FixedDeltaTime = FixedDeltaTime;

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int groundedHash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                int verticalHash = AnimationParameterCache.GetHash(config.verticalSpeedParameter);
                int inputXHash = AnimationParameterCache.GetHash(config.inputXParameter);
                int inputYHash = AnimationParameterCache.GetHash(config.inputYParameter);
                
                _context.AnimationController.SetBool(groundedHash, _context.IsGrounded);
                _context.AnimationController.SetFloat(verticalHash, _rigidbody.velocity.y);
                _context.AnimationController.SetFloat(inputXHash, _context.InputDirection.x);
                _context.AnimationController.SetFloat(inputYHash, _context.InputDirection.y);
            }
        }

        // ...

        private void ExecuteStateMachine()
        {
            float2 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            // Calculate velocity from displacement
            float dt = DeltaTime > 0 ? DeltaTime : 1f;
            Vector2 targetVelocity = new Vector2(displacement.x / dt, _rigidbody.velocity.y);

            if (config.movementType == MovementType2D.TopDown)
            {
                // TopDown Mode:
                // Input X -> Velocity X
                // Input Y -> Velocity Y (No Gravity)

                // In TopDown, the state (Walk/Run) calculates displacement based on InputDirection.
                // Since we map Input Y to InputDirection.y in SetInputDirection,
                // displacement.y ALREADY contains the vertical movement we want.

                // So we just apply displacement.x and displacement.y to velocity.
                // And we ignore gravity (already set to 0 in InitializePhysics).

                // Note: displacement.y comes from state.OnUpdate -> context.InputDirection * speed * dt
                // So targetVelocity.y = displacement.y / dt = InputDirection.y * speed

                _rigidbody.velocity = new Vector2(displacement.x / dt, displacement.y / dt);
            }
            else if (config.movementType == MovementType2D.BeltScroll)
            {
                // In BeltScroll (DNF) mode, Y input (from state displacement.y) maps to Z velocity
                // But wait, the states (Walk/Run) calculate displacement based on InputDirection.
                // If we mapped Input Y to Z in SetInputDirection, then InputDirection.y is actually Z movement.
                // But standard 2D states calculate displacement = Input * Speed.
                // So displacement.y is the vertical movement.

                // For DNF:
                // displacement.x -> Rigidbody.velocity.x
                // displacement.y -> Rigidbody.velocity.y (This is WRONG, Y is gravity/jump)
                // We need to map displacement.y to Z position change.

                // Since Rigidbody2D doesn't handle Z, we must move Transform.Z manually.
                float zMove = displacement.y;
                transform.position += new Vector3(0, 0, zMove);

                // Keep Y velocity controlled by Physics (Gravity/Jump)
                // Unless the state explicitly set VerticalVelocity (like JumpState)
                // But wait, JumpState sets context.VerticalVelocity.
                // We need to apply context.VerticalVelocity to Rigidbody.Y if it was modified?
                // Actually, let's stick to the pattern:
                // X velocity = displacement.x / dt
                // Y velocity = _rigidbody.velocity.y (Physics)

                _rigidbody.velocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
            }
            else
            {
                // Platformer Mode
                // X velocity = displacement.x / dt
                // Y velocity = _rigidbody.velocity.y (Physics)
                _rigidbody.velocity = new Vector2(targetVelocity.x, _rigidbody.velocity.y);
            }

            MovementStateBase2D nextState = _currentState.EvaluateTransition(ref _context);
            if (nextState != null && nextState != _currentState)
            {
                RequestStateChangeInternal(nextState);
            }
        }

        private void UpdateFacing()
        {
            // In TopDown mode, we might not want simple left/right flipping if we have 4-direction sprites.
            // But if we do want flipping for left/right, we keep it.
            // Usually TopDown uses BlendTrees for facing, so flipping Transform.scale might be undesirable if sprites are asymmetrical.
            // Let's make it optional or just disable it for TopDown if InputDirection.x is small.

            if (config.movementType == MovementType2D.TopDown)
            {
                // For TopDown, we rely on Animator (InputX/InputY) to show facing.
                // We DO NOT flip the transform because that would flip the Y-axis sprite too (upside down? no, just X scale).
                // Flipping X scale is fine for Left/Right, but if we have dedicated Up/Down sprites, flipping might be weird.
                // Let's keep it simple: If user wants 4-direction, they use BlendTree. 
                // If they use side-view sprites for TopDown (like Don't Starve), they might want flip.
                // For "Classic RPG" (FF), we usually don't flip scale, we change sprite.
                return;
            }

            if (math.abs(_context.InputDirection.x) > 0.01f)
            {
                bool shouldFaceRight = _context.InputDirection.x > 0;
                if (shouldFaceRight != facingRight)
                {
                    facingRight = shouldFaceRight;
                    Vector3 scale = transform.localScale;
                    scale.x *= -1;
                    transform.localScale = scale;
                }
            }
        }

        public void SetInputDirection(Vector2 direction)
        {
            if (config.movementType == MovementType2D.BeltScroll || config.movementType == MovementType2D.TopDown)
            {
                // Map Input Y to InputDirection.y
                // BeltScroll: Interpreted as Z movement
                // TopDown: Interpreted as Y movement
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
            _context.JumpPressed = pressed;
            if (pressed && (_coyoteTimeCounter > 0 || _context.IsGrounded))
            {
                RequestStateChange(MovementStateType.Jump);
            }
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

            MovementStateBase2D targetState = GetStateByType(targetStateType);
            if (targetState == null)
            {
                Debug.LogWarning($"[MovementComponent2D] State {targetStateType} not found.");
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
                default:
                    Debug.LogWarning($"[MovementComponent2D] State {stateType} not implemented yet.");
                    return null;
            }
        }

        void OnDestroy()
        {
            StatePool<MovementStateBase2D>.Clear();
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck != null && config != null)
            {
                Gizmos.color = _context.IsGrounded ? Color.green : Color.red;
                Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
            }
        }
    }
}