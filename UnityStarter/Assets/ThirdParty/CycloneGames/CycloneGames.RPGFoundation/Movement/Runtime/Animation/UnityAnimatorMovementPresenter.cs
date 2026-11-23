using System;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class UnityAnimatorMovementPresenter : MonoBehaviour,
        IMovementLocomotionSink, IMovementActionSink, IMovementAnimationBackend
    {
        private enum LocoSlot : byte
        {
            PlanarSpeed,
            VerticalSpeed,
            InputX,
            InputY,
            Grounded,
            Sprinting,
            Crouching,
            Climbing,
            WallSliding,
            Count
        }

        private const int FloatSlotCount = 4;
        private const int BoolSlotCount = 5;

        private enum ActionSlot : byte
        {
            Jump,
            AirJump,
            Roll,
            Land,
            ClimbEnter,
            ClimbExit,
            WallSlideEnter,
            WallSlideExit,
            SwimEnter,
            SwimExit,
            FlyEnter,
            FlyExit,
            Count
        }

        [SerializeField] private Animator animator;
        [Tooltip("AnimatePhysics aligns the Animator with the fixed physics step; " +
                 "root motion capture is resampled by the movement owner either way.")]
        [SerializeField] private AnimatorUpdateMode configuredUpdateMode = AnimatorUpdateMode.Normal;
        [SerializeField] private AnimatorCullingMode configuredCullingMode = AnimatorCullingMode.CullUpdateTransforms;

        [Tooltip("Half-life of the exponential smoothing applied on render frames, in seconds. " +
                 "Smaller values snap closer to the authoritative step.")]
        [SerializeField] private float smoothingHalfLifeSeconds = 0.05f;

        [Header("Continuous parameters (leave empty to skip)")]
        [SerializeField] private string planarSpeedParameter = "MovementSpeed";
        [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
        [SerializeField] private string inputXParameter = "InputX";
        [SerializeField] private string inputYParameter = "InputY";
        [SerializeField] private string groundedParameter = "IsGrounded";
        [SerializeField] private string sprintingParameter = "IsSprinting";
        [SerializeField] private string crouchingParameter = "IsCrouching";
        [SerializeField] private string climbingParameter = "IsClimbing";
        [SerializeField] private string wallSlidingParameter = "IsWallSliding";

        [Header("One-shot triggers (leave empty to skip)")]
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private string airJumpTrigger = "AirJump";
        [SerializeField] private string rollTrigger = "Roll";
        [SerializeField] private string landTrigger = "Land";
        [SerializeField] private string climbEnterTrigger = "ClimbEnter";
        [SerializeField] private string climbExitTrigger = "ClimbExit";
        [SerializeField] private string wallSlideEnterTrigger = "WallSlideEnter";
        [SerializeField] private string wallSlideExitTrigger = "WallSlideExit";
        [SerializeField] private string swimEnterTrigger = "SwimEnter";
        [SerializeField] private string swimExitTrigger = "SwimExit";
        [SerializeField] private string flyEnterTrigger = "FlyEnter";
        [SerializeField] private string flyExitTrigger = "FlyExit";

        private readonly int[] _locoHashes = new int[(int)LocoSlot.Count];
        private readonly bool[] _locoBound = new bool[(int)LocoSlot.Count];
        private readonly int[] _actionHashes = new int[(int)ActionSlot.Count];
        private readonly bool[] _actionBound = new bool[(int)ActionSlot.Count];
        private readonly float[] _targetFloats = new float[FloatSlotCount];
        private readonly float[] _writtenFloats = new float[FloatSlotCount];
        private readonly bool[] _targetBools = new bool[BoolSlotCount];
        private readonly bool[] _writtenBools = new bool[BoolSlotCount];

        private uint _lastSequence;
        private bool _hasSequence;
        private bool _hasTargets;
        private bool _forceFullWrite;
        private long _totalAnimatorWrites;

        public int BoundLocomotionParameterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _locoBound.Length; i++) if (_locoBound[i]) count++;
                return count;
            }
        }

        public int BoundActionParameterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _actionBound.Length; i++) if (_actionBound[i]) count++;
                return count;
            }
        }

        public long TotalAnimatorWrites { get { return _totalAnimatorWrites; } }

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (smoothingHalfLifeSeconds < 0.001f) smoothingHalfLifeSeconds = 0.001f;
        }
#endif

        private void Awake()
        {
            if (!MovementAnimationBackendGuard.TryClaim(this, nameof(UnityAnimatorMovementPresenter))) return;
            if (animator == null) animator = GetComponent<Animator>();
            Rebind();
        }

        private void OnDisable()
        {
            _hasSequence = false;
        }

        public void Rebind()
        {
            if (animator != null)
            {
                animator.updateMode = configuredUpdateMode;
                animator.cullingMode = configuredCullingMode;
            }
            for (int i = 0; i < _locoBound.Length; i++) _locoBound[i] = false;
            for (int i = 0; i < _actionBound.Length; i++) _actionBound[i] = false;
            _hasSequence = false;
            _forceFullWrite = true;

            if (animator == null || animator.runtimeAnimatorController == null) return;
            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters == null) return;

            BindLoco(parameters, LocoSlot.PlanarSpeed, planarSpeedParameter, AnimatorControllerParameterType.Float);
            BindLoco(parameters, LocoSlot.VerticalSpeed, verticalSpeedParameter, AnimatorControllerParameterType.Float);
            BindLoco(parameters, LocoSlot.InputX, inputXParameter, AnimatorControllerParameterType.Float);
            BindLoco(parameters, LocoSlot.InputY, inputYParameter, AnimatorControllerParameterType.Float);
            BindLoco(parameters, LocoSlot.Grounded, groundedParameter, AnimatorControllerParameterType.Bool);
            BindLoco(parameters, LocoSlot.Sprinting, sprintingParameter, AnimatorControllerParameterType.Bool);
            BindLoco(parameters, LocoSlot.Crouching, crouchingParameter, AnimatorControllerParameterType.Bool);
            BindLoco(parameters, LocoSlot.Climbing, climbingParameter, AnimatorControllerParameterType.Bool);
            BindLoco(parameters, LocoSlot.WallSliding, wallSlidingParameter, AnimatorControllerParameterType.Bool);

            BindAction(parameters, ActionSlot.Jump, jumpTrigger);
            BindAction(parameters, ActionSlot.AirJump, airJumpTrigger);
            BindAction(parameters, ActionSlot.Roll, rollTrigger);
            BindAction(parameters, ActionSlot.Land, landTrigger);
            BindAction(parameters, ActionSlot.ClimbEnter, climbEnterTrigger);
            BindAction(parameters, ActionSlot.ClimbExit, climbExitTrigger);
            BindAction(parameters, ActionSlot.WallSlideEnter, wallSlideEnterTrigger);
            BindAction(parameters, ActionSlot.WallSlideExit, wallSlideExitTrigger);
            BindAction(parameters, ActionSlot.SwimEnter, swimEnterTrigger);
            BindAction(parameters, ActionSlot.SwimExit, swimExitTrigger);
            BindAction(parameters, ActionSlot.FlyEnter, flyEnterTrigger);
            BindAction(parameters, ActionSlot.FlyExit, flyExitTrigger);
        }

        private void BindLoco(AnimatorControllerParameter[] parameters, LocoSlot slot, string name,
            AnimatorControllerParameterType expectedType)
        {
            int index = (int)slot;
            _locoBound[index] = false;
            if (string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter == null) continue;
                if (parameter.type != expectedType) continue;
                if (!string.Equals(parameter.name, name, StringComparison.Ordinal)) continue;
                _locoHashes[index] = parameter.nameHash;
                _locoBound[index] = true;
                return;
            }
        }

        private void BindAction(AnimatorControllerParameter[] parameters, ActionSlot slot, string name)
        {
            int index = (int)slot;
            _actionBound[index] = false;
            if (string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter == null) continue;
                if (parameter.type != AnimatorControllerParameterType.Trigger) continue;
                if (!string.Equals(parameter.name, name, StringComparison.Ordinal)) continue;
                _actionHashes[index] = parameter.nameHash;
                _actionBound[index] = true;
                return;
            }
        }

        public void Apply(in MovementAnimationState state)
        {
            if (_hasSequence && state.Sequence == _lastSequence) return;
            _lastSequence = state.Sequence;
            _hasSequence = true;
            if (!state.IsValid) return;

            _targetFloats[0] = state.PlanarSpeed;
            _targetFloats[1] = state.VerticalSpeed;
            _targetFloats[2] = state.Input.X;
            _targetFloats[3] = state.Input.Y;
            _targetBools[0] = state.IsGrounded;
            _targetBools[1] = state.IsSprinting;
            _targetBools[2] = state.IsCrouching;
            _targetBools[3] = state.IsClimbing;
            _targetBools[4] = state.IsWallSliding;
            _hasTargets = true;
        }

        private void Update()
        {
            if (animator == null || !animator.isActiveAndEnabled || !_hasTargets) return;

            float dt = Time.deltaTime;
            float halfLife = smoothingHalfLifeSeconds < 0.001f ? 0.001f : smoothingHalfLifeSeconds;
            float factor = 1f - Mathf.Exp(-dt / halfLife);
            if (factor < 0f) factor = 0f;
            if (factor > 1f) factor = 1f;

            for (int i = 0; i < FloatSlotCount; i++)
            {
                if (!_locoBound[i]) continue;
                float target = _targetFloats[i];
                float current = _writtenFloats[i];
                float next = current + (target - current) * factor;
                if (!_forceFullWrite && Mathf.Abs(next - current) < 1E-4f) continue;
                animator.SetFloat(_locoHashes[i], next);
                _writtenFloats[i] = next;
                _totalAnimatorWrites++;
            }

            for (int i = 0; i < BoolSlotCount; i++)
            {
                int index = i + FloatSlotCount;
                if (!_locoBound[index]) continue;
                bool target = _targetBools[i];
                if (!_forceFullWrite && target == _writtenBools[i]) continue;
                animator.SetBool(_locoHashes[index], target);
                _writtenBools[i] = target;
                _totalAnimatorWrites++;
            }

            _forceFullWrite = false;
        }

        public void Play(in MovementAnimationAction action)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (!action.IsValid) return;

            ActionSlot slot = MapSlot(action.Kind);
            if (slot == ActionSlot.Count) return;
            int index = (int)slot;
            if (!_actionBound[index]) return;
            animator.SetTrigger(_actionHashes[index]);
            _totalAnimatorWrites++;
        }

        private static ActionSlot MapSlot(MovementAnimationActionKind kind)
        {
            switch (kind)
            {
                case MovementAnimationActionKind.Jump: return ActionSlot.Jump;
                case MovementAnimationActionKind.AirJump: return ActionSlot.AirJump;
                case MovementAnimationActionKind.Roll: return ActionSlot.Roll;
                case MovementAnimationActionKind.Land: return ActionSlot.Land;
                case MovementAnimationActionKind.ClimbEnter: return ActionSlot.ClimbEnter;
                case MovementAnimationActionKind.ClimbExit: return ActionSlot.ClimbExit;
                case MovementAnimationActionKind.WallSlideEnter: return ActionSlot.WallSlideEnter;
                case MovementAnimationActionKind.WallSlideExit: return ActionSlot.WallSlideExit;
                case MovementAnimationActionKind.SwimEnter: return ActionSlot.SwimEnter;
                case MovementAnimationActionKind.SwimExit: return ActionSlot.SwimExit;
                case MovementAnimationActionKind.FlyEnter: return ActionSlot.FlyEnter;
                case MovementAnimationActionKind.FlyExit: return ActionSlot.FlyExit;
                default: return ActionSlot.Count;
            }
        }
    }
}
