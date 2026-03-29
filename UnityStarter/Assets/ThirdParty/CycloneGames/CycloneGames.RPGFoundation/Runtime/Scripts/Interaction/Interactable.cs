using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class Interactable : MonoBehaviour, IInteractable
    {
        [SerializeField] protected string interactionPrompt = "Interact";
        [SerializeField] protected bool isInteractable = true;
        [SerializeField] protected bool autoInteract;
        [SerializeField] protected int priority;
        [SerializeField] protected float interactionDistance = 2f;
        [SerializeField] protected Transform interactionPoint;

        [SerializeField] protected InteractionChannel channel = InteractionChannel.Channel0;

        [SerializeField] protected float interactionCooldown;
        [SerializeField] protected bool resetToIdleOnComplete = true;

        [SerializeField] protected bool useLocalization;
        [SerializeField] protected InteractionPromptData promptData;

        [Tooltip("Available actions on this interactable. Leave empty for single default interaction.")]
        [SerializeField] protected InteractionAction[] actions = Array.Empty<InteractionAction>();

        [Tooltip("Duration in seconds the player must hold to complete the interaction. 0 = instant.")]
        [SerializeField] protected float holdDuration;
        [Tooltip("Maximum distance from the instigator during an active interaction. 0 = no limit.")]
        [SerializeField] protected float maxInteractionRange;

        [SerializeField] protected UnityEvent onInteract;
        [SerializeField] protected UnityEvent onFocus;
        [SerializeField] protected UnityEvent onDefocus;

        private InteractionStateType _currentState = InteractionStateType.Idle;
        private float _lastInteractionTime = float.NegativeInfinity;
        private CancellationTokenSource _interactionCts;
        private int _isInteractingFlag;
        private IInteractionSystem _system;
        private Vector3 _lastRegisteredPosition;

        // Cached position to avoid Transform access in hot paths
        private Vector3 _cachedPosition;
        private int _positionFrame = -1;

        // Interaction progress (0~1) for timed/hold interactions
        private float _interactionProgress;
        private string _pendingActionId;
        private InstigatorHandle _currentInstigator;
        private InteractionCancelReason _lastCancelReason;
        private CancellationTokenSource _distanceCheckCts;

        // Requirements stored as array for 0-GC iteration
        private IInteractionRequirement[] _requirements;
        private static readonly IInteractionRequirement[] s_emptyRequirements = Array.Empty<IInteractionRequirement>();
        private static readonly InteractionAction[] s_emptyActions = Array.Empty<InteractionAction>();

        public string InteractionPrompt => interactionPrompt;
        public InteractionPromptData? PromptData => useLocalization && promptData.IsValid ? promptData : null;
        public bool IsInteractable => isInteractable && !IsInteracting && IsCooldownComplete();
        public bool AutoInteract => autoInteract;
        public bool IsInteracting => _isInteractingFlag == 1;
        public int Priority => priority;
        public float InteractionDistance => interactionDistance;
        public InteractionStateType CurrentState => _currentState;
        public InteractionChannel Channel => channel;
        public float InteractionProgress => _interactionProgress;
        public IReadOnlyList<InteractionAction> Actions => actions != null && actions.Length > 0 ? actions : s_emptyActions;
        public IReadOnlyList<IInteractionRequirement> Requirements => _requirements ?? s_emptyRequirements;
        public InstigatorHandle CurrentInstigator => _currentInstigator;
        public float HoldDuration => holdDuration;
        public float MaxInteractionRange => maxInteractionRange;
        public bool IsBusy => IsInteracting;

        public Vector3 Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int frame = Time.frameCount;
                if (_positionFrame != frame)
                {
                    _cachedPosition = interactionPoint != null ? interactionPoint.position : transform.position;
                    _positionFrame = frame;
                }
                return _cachedPosition;
            }
        }

        public event Action<IInteractable, InteractionStateType> OnStateChanged;
        public event Action<IInteractable, float> OnProgressChanged;
        public event Action<IInteractable, InteractionCancelReason> OnInteractionCancelled;

        protected virtual void Awake()
        {
            _requirements = GetComponents<IInteractionRequirement>();
            if (_requirements.Length == 0) _requirements = s_emptyRequirements;
        }

        protected virtual void OnEnable()
        {
            RegisterWithSystem();
        }

        protected virtual void OnDisable()
        {
            UnregisterFromSystem();
        }

        protected virtual void OnDestroy()
        {
            CancelInteraction(InteractionCancelReason.TargetDestroyed);
            StopDistanceMonitor();
            OnProgressChanged = null;
            OnInteractionCancelled = null;
        }

        protected virtual void RegisterWithSystem()
        {
            if (_system != null) return;
            _system = InteractionSystem.Instance;
            if (_system == null) _system = FindAnyObjectByType<InteractionSystem>();
            if (_system != null)
            {
                _lastRegisteredPosition = Position;
                _system.Register(this);
            }
        }

        protected virtual void UnregisterFromSystem()
        {
            _system?.Unregister(this);
            _system = null;
        }

        // Call from movement systems when position changes significantly
        public void NotifyPositionChanged()
        {
            if (_system == null) return;
            Vector3 pos = Position;
            Vector3 diff = pos - _lastRegisteredPosition;
            // Only update grid if moved more than 1 unit (avoid thrashing)
            if (diff.x * diff.x + diff.y * diff.y + diff.z * diff.z > 1f)
            {
                _lastRegisteredPosition = pos;
                _system.UpdatePosition(this);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCooldownComplete()
        {
            if (interactionCooldown <= 0f) return true;
            return Time.time - _lastInteractionTime >= interactionCooldown;
        }

        public bool CanInteract(InstigatorHandle instigator)
        {
            if (!IsInteractable) return false;
            var reqs = _requirements;
            for (int i = 0; i < reqs.Length; i++)
            {
                if (!reqs[i].IsMet(this, instigator)) return false;
            }
            return true;
        }

        protected bool TrySetState(InteractionStateType newState)
        {
            if (_currentState == newState) return false;

            var currentHandler = InteractionStateHandlers.GetHandler(_currentState);
            if (!currentHandler.CanTransitionTo(newState)) return false;

            currentHandler.OnExit(this);
            _currentState = newState;
            InteractionStateHandlers.GetHandler(newState).OnEnter(this);
            OnStateChanged?.Invoke(this, _currentState);
            return true;
        }

        protected void ForceSetState(InteractionStateType newState)
        {
            if (_currentState == newState) return;
            InteractionStateHandlers.GetHandler(_currentState).OnExit(this);
            _currentState = newState;
            InteractionStateHandlers.GetHandler(newState).OnEnter(this);
            OnStateChanged?.Invoke(this, _currentState);
        }

        public async UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default)
        {
            return await TryInteractAsync(null, null, cancellationToken);
        }

        public async UniTask<bool> TryInteractAsync(string actionId, CancellationToken cancellationToken = default)
        {
            return await TryInteractAsync(null, actionId, cancellationToken);
        }

        public async UniTask<bool> TryInteractAsync(InstigatorHandle instigator, string actionId, CancellationToken cancellationToken = default)
        {
            if (!isInteractable) return false;
            if (!IsCooldownComplete()) return false;

            // Atomic check-and-set to prevent concurrent interactions
            if (Interlocked.CompareExchange(ref _isInteractingFlag, 1, 0) != 0)
                return false;

            _currentInstigator = instigator;
            _pendingActionId = actionId;
            _lastCancelReason = InteractionCancelReason.Manual;
            ReportProgress(0f);

            _interactionCts?.Cancel();
            _interactionCts?.Dispose();
            _interactionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start distance monitoring if configured
            StartDistanceMonitor();

            try
            {
                if (!TrySetState(InteractionStateType.Starting))
                {
                    StopDistanceMonitor();
                    _currentInstigator = null;
                    _interactionCts?.Dispose();
                    _interactionCts = null;
                    Interlocked.Exchange(ref _isInteractingFlag, 0);
                    return false;
                }

                await OnStartInteractAsync(_interactionCts.Token);
                if (_interactionCts.Token.IsCancellationRequested) return false;

                TrySetState(InteractionStateType.InProgress);
                await OnDoInteractAsync(_interactionCts.Token);
                if (_interactionCts.Token.IsCancellationRequested) return false;

                TrySetState(InteractionStateType.Completing);
                await OnEndInteractAsync(_interactionCts.Token);

                ReportProgress(1f);
                TrySetState(InteractionStateType.Completed);
                _lastInteractionTime = Time.time;

                if (resetToIdleOnComplete)
                    ForceSetState(InteractionStateType.Idle);

                return true;
            }
            catch (OperationCanceledException)
            {
                ReportProgress(0f);
                ForceSetState(InteractionStateType.Cancelled);
                OnInteractionCancelled?.Invoke(this, _lastCancelReason);
                ForceSetState(InteractionStateType.Idle);
                return false;
            }
            finally
            {
                StopDistanceMonitor();
                _pendingActionId = null;
                _currentInstigator = null;
                _interactionCts?.Dispose();
                _interactionCts = null;
                Interlocked.Exchange(ref _isInteractingFlag, 0);
            }
        }

        /// <summary>
        /// The action ID currently being executed. Null if using default interaction.
        /// Subclasses can use this in OnDoInteractAsync to branch behavior per action.
        /// </summary>
        protected string PendingActionId => _pendingActionId;

        /// <summary>
        /// Report interaction progress (0.0 ~ 1.0). Call this from OnDoInteractAsync
        /// to update continuous progress for timed/hold interactions.
        /// </summary>
        protected void ReportProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Mathf.Approximately(_interactionProgress, progress)) return;
            _interactionProgress = progress;
            OnProgressChanged?.Invoke(this, progress);
        }

        protected virtual UniTask OnStartInteractAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// Awaits the hold duration timer, reporting progress from 0 to 1.
        /// Call this in overridden <see cref="OnDoInteractAsync"/> to use the built-in hold behavior.
        /// </summary>
        protected async UniTask HoldTimerAsync(CancellationToken ct)
        {
            if (holdDuration <= 0f) return;
            float elapsed = 0f;
            while (elapsed < holdDuration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                ReportProgress(Mathf.Clamp01(elapsed / holdDuration));
                await UniTask.Yield(ct);
            }
        }

        protected virtual async UniTask OnDoInteractAsync(CancellationToken ct)
        {
            await HoldTimerAsync(ct);
            onInteract?.Invoke();
        }

        protected virtual UniTask OnEndInteractAsync(CancellationToken ct) => UniTask.CompletedTask;

        public virtual void OnFocus() => onFocus?.Invoke();
        public virtual void OnDefocus() => onDefocus?.Invoke();

        public void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual)
        {
            CancelInteraction(reason);
        }

        private void CancelInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual)
        {
            _lastCancelReason = reason;
            _interactionCts?.Cancel();
            _interactionCts?.Dispose();
            _interactionCts = null;
        }

        private void StartDistanceMonitor()
        {
            if (maxInteractionRange <= 0f || _currentInstigator == null) return;
            if (!_currentInstigator.TryGetPosition(out _)) return;

            _distanceCheckCts = CancellationTokenSource.CreateLinkedTokenSource(_interactionCts.Token);
            MonitorDistanceAsync(_distanceCheckCts.Token).Forget();
        }

        private void StopDistanceMonitor()
        {
            _distanceCheckCts?.Cancel();
            _distanceCheckCts?.Dispose();
            _distanceCheckCts = null;
        }

        private async UniTaskVoid MonitorDistanceAsync(CancellationToken ct)
        {
            float maxRangeSqr = maxInteractionRange * maxInteractionRange;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await UniTask.Yield(ct);
                    if (_currentInstigator == null) break;
                    if (!_currentInstigator.TryGetPosition(out Vector3 instigatorPos)) break;
                    Vector3 diff = Position - instigatorPos;
                    if (diff.sqrMagnitude > maxRangeSqr)
                    {
                        CancelInteraction(InteractionCancelReason.OutOfRange);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

#if UNITY_EDITOR
        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 pos = interactionPoint != null ? interactionPoint.position : transform.position;
            Gizmos.DrawWireSphere(pos, interactionDistance);
        }
#endif
    }
}