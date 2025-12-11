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
        [Header("Configuration")]
        [SerializeField] private MovementConfig config;

        [Header("Dependencies")]
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private UnityEngine.Object animancerComponent;

        [Header("Gravity & Alignment")]
        [SerializeField] private Transform worldUpSource;

        [Header("Settings")]
        [SerializeField] private bool useRootMotion = false;
        [SerializeField] private bool ignoreTimeScale = false;

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
                                $"[MovementComponent] AnimancerComponent and manually assigned Animator reference different components on {gameObject.name}. " +
                                $"Animancer's Animator: {animancerAnimator.name}, Manual Animator: {characterAnimator.name}. " +
                                "Animancer will use its internal Animator. Consider removing the manual Animator assignment.",
                                this);
                        }
                        else if (animancerAnimator == null)
                        {
                            Debug.LogWarning(
                                $"[MovementComponent] AnimancerComponent on {gameObject.name} does not have an internal Animator. " +
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
                VerticalVelocity = _groundedVerticalVelocity
            };
        }

        private void UpdateContext()
        {
            _context.DeltaTime = DeltaTime;
            _context.WorldUp = WorldUp;
            _context.IsGrounded = _characterController.isGrounded;

            if (_context.IsGrounded && _context.VerticalVelocity < 0)
            {
                _context.VerticalVelocity = _groundedVerticalVelocity;
            }

            if (_context.AnimationController != null && _context.AnimationController.IsValid)
            {
                int hash = AnimationParameterCache.GetHash(config.isGroundedParameter);
                _context.AnimationController.SetBool(hash, _context.IsGrounded);
            }
        }

        private void ExecuteStateMachine()
        {
            float3 displacement;
            _currentState.OnUpdate(ref _context, out displacement);

            if (math.lengthsq(displacement) > _minSqrMagnitudeForMovement)
            {
                _characterController.Move(displacement);
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

        private void UpdateRotation()
        {
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

        public void MoveWithVelocity(Vector3 worldVelocity)
        {
            SetInputDirection(worldVelocity.normalized);

            if (useRootMotion && characterAnimator != null)
            {
                characterAnimator.applyRootMotion = true;
                float speed = worldVelocity.magnitude;
                if (_context.AnimationController != null && _context.AnimationController.IsValid)
                {
                    int hash = AnimationParameterCache.GetHash(config.movementSpeedParameter);
                    _context.AnimationController.SetFloat(hash, speed);
                }
            }
        }

        void OnDestroy()
        {
            StatePool<MovementStateBase>.Clear();
        }
    }
}