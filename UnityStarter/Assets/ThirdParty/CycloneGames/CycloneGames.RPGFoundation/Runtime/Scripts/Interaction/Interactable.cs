using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class Interactable : MonoBehaviour, IInteractable
    {
        [Header("Interaction Settings")]
        [SerializeField] protected string interactionPrompt = "Interact";
        [SerializeField] protected bool isInteractable = true;
        [SerializeField] protected bool autoInteract;
        [SerializeField] protected int priority;
        [SerializeField] protected float interactionDistance = 2f;
        [SerializeField] protected Transform interactionPoint;

        [Header("Behavior")]
        [SerializeField] protected float interactionCooldown;
        [SerializeField] protected bool resetToIdleOnComplete = true;

        [Header("Localization")]
        [SerializeField] protected bool useLocalization;
        [SerializeField] protected InteractionPromptData promptData;

        [Header("Events")]
        [SerializeField] protected UnityEvent onInteract;
        [SerializeField] protected UnityEvent onFocus;
        [SerializeField] protected UnityEvent onDefocus;

        private InteractionStateType _currentState = InteractionStateType.Idle;
        private float _lastInteractionTime = float.NegativeInfinity;
        private CancellationTokenSource _interactionCts;
        private int _isInteractingFlag;

        public string InteractionPrompt => interactionPrompt;
        public InteractionPromptData? PromptData => useLocalization && promptData.IsValid ? promptData : null;
        public bool IsInteractable => isInteractable && !IsInteracting && IsCooldownComplete();
        public bool AutoInteract => autoInteract;
        public bool IsInteracting => _isInteractingFlag == 1;
        public int Priority => priority;
        public Vector3 Position => interactionPoint != null ? interactionPoint.position : transform.position;
        public float InteractionDistance => interactionDistance;
        public InteractionStateType CurrentState => _currentState;

        public event Action<IInteractable, InteractionStateType> OnStateChanged;

        protected virtual void Awake() { }

        protected virtual void OnDestroy()
        {
            CancelInteraction();
        }

        private bool IsCooldownComplete()
        {
            if (interactionCooldown <= 0f) return true;
            return Time.time - _lastInteractionTime >= interactionCooldown;
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
            if (!isInteractable) return false;
            if (!IsCooldownComplete()) return false;

            // Atomic check-and-set to prevent concurrent interactions
            if (Interlocked.CompareExchange(ref _isInteractingFlag, 1, 0) != 0)
                return false;

            _interactionCts?.Cancel();
            _interactionCts?.Dispose();
            _interactionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                if (!TrySetState(InteractionStateType.Starting))
                {
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

                TrySetState(InteractionStateType.Completed);
                _lastInteractionTime = Time.time;

                if (resetToIdleOnComplete)
                    ForceSetState(InteractionStateType.Idle);

                return true;
            }
            catch (OperationCanceledException)
            {
                ForceSetState(InteractionStateType.Cancelled);
                ForceSetState(InteractionStateType.Idle);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _isInteractingFlag, 0);
            }
        }

        protected virtual UniTask OnStartInteractAsync(CancellationToken ct) => UniTask.CompletedTask;

        protected virtual UniTask OnDoInteractAsync(CancellationToken ct)
        {
            onInteract?.Invoke();
            return UniTask.CompletedTask;
        }

        protected virtual UniTask OnEndInteractAsync(CancellationToken ct) => UniTask.CompletedTask;

        public virtual void OnFocus() => onFocus?.Invoke();
        public virtual void OnDefocus() => onDefocus?.Invoke();

        public void ForceEndInteraction() => CancelInteraction();

        private void CancelInteraction()
        {
            _interactionCts?.Cancel();
            _interactionCts?.Dispose();
            _interactionCts = null;
        }

#if UNITY_EDITOR
        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Position, interactionDistance);
        }
#endif
    }
}