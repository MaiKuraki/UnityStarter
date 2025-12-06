using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace CycloneGames.UIFramework.Runtime
{
    public class UIWindow : MonoBehaviour
    {
        [SerializeField, Header("Priority Override"), Range(-100, 400)] private int priority = 0; // Default priority
        public int Priority => priority;

        private string windowNameInternal;
        public string WindowName => windowNameInternal;

        private IUIWindowState currentState;
        private CancellationTokenSource openCts;
        private CancellationTokenSource closeCts;
        private IUIWindowTransitionDriver _transitionDriver; // Optional external transition driver

        // Shared state instances to avoid per-open allocations
        private static readonly OpeningState OpeningStateShared = new OpeningState();
        private static readonly OpenedState OpenedStateShared = new OpenedState();
        private static readonly ClosingState ClosingStateShared = new ClosingState();
        private static readonly ClosedState ClosedStateShared = new ClosedState();
        private UILayer parentLayerInternal;
        public UILayer ParentLayer => parentLayerInternal; // Public getter

        private CanvasGroup canvasGroup;
        private string sourceAssetPath;
        public System.Action<string> OnReleaseAssetReference;

        public void SetSourceAssetPath(string path) => sourceAssetPath = path;

        private bool _isDestroying = false; // Flag to prevent multiple destruction logic paths

        /// <summary>
        /// Sets the logical name for this UI window.
        /// This name is used by UIManager and UILayer for identification.
        /// </summary>
        public void SetWindowName(string newWindowName)
        {
            if (string.IsNullOrEmpty(newWindowName))
            {
                Debug.LogError("[UIWindow] Window name cannot be null or empty.", this);
                // Fallback to GameObject name if newWindowName is invalid, though this should be avoided.
                windowNameInternal = gameObject.name;
                return;
            }
            windowNameInternal = newWindowName;
            gameObject.name = newWindowName; // Consider if this is always desired
        }

        /// <summary>
        /// Sets the parent UILayer for this window.
        /// </summary>
        public void SetUILayer(UILayer layer)
        {
            parentLayerInternal = layer;
        }

        /// <summary>
        /// Initiates the process of closing and destroying this window.
        /// </summary>
        internal void Close()
        {
            if (_isDestroying) return; // Already in the process of closing/destroying

            if (currentState is ClosingState || currentState is ClosedState)
            {
                return;
            }

            // Transition to ClosingState, which might trigger animations.
            OnStartClose();

            // TODO: Implement actual closing animation.
            // For now, immediately "finish" closing.
            // In a real scenario, OnFinishedClose would be called by an animation event, a timer, or UniTask.Delay.
            // If using animations, ensure OnFinishedClose is reliably called.
            OnFinishedClose();
        }

        /// <summary>
        /// Closes the window asynchronously with cancellation.
        /// </summary>
        public async UniTask CloseAsync(CancellationToken externalToken)
        {
            if (_isDestroying) return;
            // cancel any ongoing open
            openCts?.Cancel();
            openCts?.Dispose();
            openCts = null;

            closeCts?.Dispose();
            if (externalToken == CancellationToken.None)
            {
                closeCts = new CancellationTokenSource();
            }
            else
            {
                closeCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            }
            var ct = closeCts.Token;

            OnStartClose();
            if (_transitionDriver != null)
            {
                await _transitionDriver.PlayCloseAsync(this, ct);
            }
            if (ct.IsCancellationRequested) return;
            OnFinishedClose();
        }

        /// <summary>
        /// Assigns an external transition driver (e.g., DOTween/Animator based) for open/close.
        /// </summary>
        public void SetTransitionDriver(IUIWindowTransitionDriver driver)
        {
            _transitionDriver = driver;
        }

        private void ChangeState(IUIWindowState newState)
        {
            if (currentState == newState && newState != null) return; // Avoid re-entering the same state if logic allows

            currentState?.OnExit(this);
            currentState = newState;
            // Debug.Log($"[UIWindow] {WindowName} changing state to {newState?.GetType().Name ?? "null"}", this);
            currentState?.OnEnter(this);
        }

        protected virtual void OnStartOpen()
        {
            if (_isDestroying) return;
            ChangeState(OpeningStateShared);
        }

        protected virtual void OnFinishedOpen()
        {
            if (_isDestroying) return;
            ChangeState(OpenedStateShared);
        }

        protected virtual void OnStartClose()
        {
            // Check if already closing or closed to prevent duplicate close operations
            if (_isDestroying) return;
            if (currentState is ClosingState || currentState is ClosedState)
            {
                return;
            }
            ChangeState(ClosingStateShared);
        }

        protected virtual void OnFinishedClose()
        {
            if (_isDestroying && currentState is ClosedState) return; // Already fully closed and processed by OnDestroy
            if (_isDestroying && !(currentState is ClosingState)) return; // If already destroying by other means and not in closing state

            _isDestroying = true; // Mark that destruction process has started from logical close

            ChangeState(ClosedStateShared);

            // The window is responsible for destroying its GameObject.
            // UILayer will be notified via this window's OnDestroy method.
            if (gameObject) Destroy(gameObject);
        }

        protected virtual void Awake()
        {
            // If not set by UIManager, it might fallback to GameObject's name or be null.
            if (string.IsNullOrEmpty(windowNameInternal))
            {
                windowNameInternal = gameObject.name; // Fallback, but UIManager should set it.
            }
            canvasGroup = GetComponent<CanvasGroup>();
        }

        /// <summary>
        /// Sets the visibility of the window using CanvasGroup if available, otherwise SetActive.
        /// This is more performant than SetActive for frequent toggling.
        /// </summary>
        public virtual void SetVisible(bool visible)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }

        [ContextMenu("Optimize Hierarchy (Disable RaycastTargets)")]
        public void OptimizeHierarchy()
        {
            var graphics = GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var g in graphics)
            {
                // Skip if it's a button or explicitly interactive (rough heuristic)
                if (g.GetComponent<UnityEngine.UI.Button>() != null ||
                    g.GetComponent<UnityEngine.UI.InputField>() != null ||
                    g.GetComponent<UnityEngine.UI.Toggle>() != null ||
                    g.GetComponent<UnityEngine.UI.ScrollRect>() != null ||
                    g.GetComponent<UnityEngine.UI.Slider>() != null ||
                    g.GetComponent<UnityEngine.UI.Dropdown>() != null)
                {
                    continue;
                }

                // If it's just an Image or Text serving as decoration
                g.raycastTarget = false;
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Asynchronously opens the window. This method should be called by the UIManager after instantiation.
        /// It handles the transition through OpeningState and into OpenedState.
        /// Override this method to implement custom opening animations.
        /// </summary>
        /// <returns>A UniTask that completes when the window's opening transition is finished.</returns>
        internal virtual async Cysharp.Threading.Tasks.UniTask Open()
        {
            // cancel any closing in progress
            closeCts?.Cancel();
            closeCts?.Dispose();
            closeCts = null;
            // set new open CTS
            openCts?.Dispose();
            openCts = new CancellationTokenSource();
            var ct = openCts.Token;

            // The opening process starts.
            OnStartOpen();

            // --- Animation Hook ---
            // The opening animation or transition should happen here.
            // For this example, we simulate an instant transition.
            // In a real implementation, you might await a DOTween sequence, a Unity animation, or a simple delay.
            // e.g., await Cysharp.Threading.Tasks.UniTask.Delay(System.TimeSpan.FromSeconds(0.5f));
            if (_transitionDriver != null)
            {
                await _transitionDriver.PlayOpenAsync(this, ct);
            }

            // Allow derived classes to await custom animations; here it's immediate
            if (ct.IsCancellationRequested) return;
            OnFinishedOpen();

            // The task is completed, signaling that the window is fully open.
            await Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        /// <summary>
        /// Opens the window with an external cancellation token.
        /// </summary>
        public async UniTask OpenAsync(CancellationToken externalToken)
        {
            // cancel any closing in progress
            closeCts?.Cancel();
            closeCts?.Dispose();
            closeCts = null;

            openCts?.Dispose();
            if (externalToken == CancellationToken.None)
            {
                openCts = new CancellationTokenSource();
            }
            else
            {
                openCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            }
            var ct = openCts.Token;

            OnStartOpen();
            if (_transitionDriver != null)
            {
                await _transitionDriver.PlayOpenAsync(this, ct);
            }
            if (ct.IsCancellationRequested) return;
            OnFinishedOpen();
            await UniTask.CompletedTask;
        }

        protected virtual void Update()
        {
            if (!_isDestroying) // Don't update if being destroyed
            {
                currentState?.Update(this);
            }
        }

        protected virtual void OnDestroy()
        {
            _isDestroying = true; // Ensure flag is set if destruction is initiated externally (e.g., scene unload)
            openCts?.Cancel();
            openCts?.Dispose();
            openCts = null;
            closeCts?.Cancel();
            closeCts?.Dispose();
            closeCts = null;

            // Debug.Log($"[UIWindow] OnDestroy called for {WindowName}", this);

            // Notify the parent layer that this window is actually destroyed
            // so it can clean up its internal list of windows.
            parentLayerInternal?.NotifyWindowDestroyed(this);
            parentLayerInternal = null; // Clear reference to prevent further calls

            // Notify UIManager to release asset reference
            if (!string.IsNullOrEmpty(sourceAssetPath))
            {
                OnReleaseAssetReference?.Invoke(sourceAssetPath);
                OnReleaseAssetReference = null;
            }

            // Ensure the current state's OnExit is called if it hasn't been through a normal close.
            // This is important if the GameObject is destroyed externally without going through Close().
            if (currentState != null && !(currentState is ClosedState))
            {
                // Debug.LogWarning($"[UIWindow] {WindowName} destroyed externally, attempting OnExit for state {currentState.GetType().Name}", this);
                currentState.OnExit(this); // Graceful exit for the current state
            }
            currentState = null; // Nullify state
        }
    }
}