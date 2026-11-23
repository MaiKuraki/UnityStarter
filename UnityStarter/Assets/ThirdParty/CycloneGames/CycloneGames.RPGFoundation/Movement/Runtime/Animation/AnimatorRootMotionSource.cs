using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class AnimatorRootMotionSource : MonoBehaviour, IRootMotionSource,
        IMovementRootMotionCapture
    {
        private static readonly LogChannel Log = RpgMovementLog.Channel;

        [SerializeField] private Animator animator;
        [SerializeField] private Transform worldUpSource;

        [Tooltip("Animator.applyRootMotion must be enabled for Unity to deliver deltaPosition. " +
                 "This component is the single owner of root motion on this character.")]
        [SerializeField] private bool takeOwnershipOfApplyRootMotion = true;

        private Vector3 _pendingPosition;
        private float _pendingYaw;
        private float _pendingTime;
        private int _lastCaptureFrame = -1;
        private uint _consumeSequence;
        private Vector3 _worldUp = Vector3.up;
        private bool _useUnscaledTime;
        private float _localTimeScale = 1f;
        private bool _ownsCapture;

        public uint ConsumedDeltaCount { get { return _consumeSequence; } }

        public void SetTimeScale(float localTimeScale, bool useUnscaledTime)
        {
            _localTimeScale = float.IsNaN(localTimeScale) || float.IsInfinity(localTimeScale)
                || localTimeScale < 0f ? 1f : localTimeScale;
            _useUnscaledTime = useUnscaledTime;
        }

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (animator == null) animator = GetComponent<Animator>();
        }
#endif

        private void OnEnable()
        {
            if (animator == null) animator = GetComponent<Animator>();
            ClearPending();

            _ownsCapture = MovementRootMotionCaptureGuard.TryClaim(this, "AnimatorRootMotionSource");
            if (!_ownsCapture) return;

            Animator localAnimator = GetComponent<Animator>();
            if (animator != null && localAnimator != null && animator != localAnimator)
            {
                Log.Warning(
                    "[AnimatorRootMotionSource] Unity sends OnAnimatorMove to the GameObject that owns " +
                    "the Animator. The bound Animator is on a different GameObject, so no root motion " +
                    "will be captured. Place this component on the Animator's GameObject.");
            }

            if (animator != null && takeOwnershipOfApplyRootMotion && !animator.applyRootMotion)
                animator.applyRootMotion = true;
        }

        private void OnDisable()
        {
            _ownsCapture = false;
            ClearPending();
        }

        // Capture only. Movement converts the accumulated delta into velocity and
        // resolves it through the ordinary collision step.
        private void OnAnimatorMove()
        {
            if (!_ownsCapture || animator == null) return;

            _worldUp = worldUpSource != null ? worldUpSource.up : Vector3.up;
            _pendingPosition += animator.deltaPosition;

            Quaternion rotation = animator.deltaRotation;
            float degrees;
            Vector3 axis;
            rotation.ToAngleAxis(out degrees, out axis);
            _pendingYaw += degrees * Mathf.Deg2Rad * Vector3.Dot(axis, _worldUp);

            int frame = Time.frameCount;
            if (frame != _lastCaptureFrame)
            {
                _lastCaptureFrame = frame;
                float dt = (_useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * _localTimeScale;
                if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt)) _pendingTime += dt;
            }
        }

        public bool TryConsume(out MovementRootMotionDelta delta)
        {
            bool hasMotion = _pendingPosition.sqrMagnitude > 1E-12f || Mathf.Abs(_pendingYaw) > 1E-8f;
            if (_pendingTime <= 0f || !hasMotion)
            {
                delta = default;
                ClearPending();
                return false;
            }

            if (_consumeSequence < uint.MaxValue) _consumeSequence++;
            delta = new MovementRootMotionDelta(
                _consumeSequence,
                new MovementVector3(_pendingPosition.x, _pendingPosition.y, _pendingPosition.z),
                _pendingYaw,
                _pendingTime,
                new MovementVector3(_worldUp.x, _worldUp.y, _worldUp.z));

            ClearPending();
            return delta.IsValid;
        }

        private void ClearPending()
        {
            _pendingPosition = Vector3.zero;
            _pendingYaw = 0f;
            _pendingTime = 0f;
            _lastCaptureFrame = -1;
        }
    }
}
