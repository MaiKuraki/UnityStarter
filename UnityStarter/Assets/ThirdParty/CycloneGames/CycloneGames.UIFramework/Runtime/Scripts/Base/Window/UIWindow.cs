using UnityEngine;

namespace CycloneGames.UIFramework
{
    public class UIWindow : MonoBehaviour
    {
        [SerializeField, Header("Priority Override"), Range(-100, 400)] private int priority = 0; // Default priority
        public int Priority => priority;
        
        private string windowNameInternal;
        public string WindowName => windowNameInternal;
        
        private IUIWindowState currentState;
        private UILayer parentLayerInternal;
        public UILayer ParentLayer => parentLayerInternal; // Public getter

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
        public void Close()
        {
            if (_isDestroying) return; // Already in the process of closing/destroying

            // Transition to ClosingState, which might trigger animations.
            OnStartClose();

            // TODO: Implement actual closing animation.
            // For now, immediately "finish" closing.
            // In a real scenario, OnFinishedClose would be called by an animation event, a timer, or UniTask.Delay.
            // If using animations, ensure OnFinishedClose is reliably called.
            OnFinishedClose();
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
            ChangeState(new OpeningState());
            // Typically, make the GameObject active if it's not.
            // Handled by OpeningState.OnEnter for this example.
        }

        protected virtual void OnFinishedOpen()
        {
            if (_isDestroying) return;
            ChangeState(new OpenedState());
        }

        protected virtual void OnStartClose()
        {
            // No need to check _isDestroying here as Close() method does it.
            ChangeState(new ClosingState());
        }

        protected virtual void OnFinishedClose()
        {
            if (_isDestroying && currentState is ClosedState) return; // Already fully closed and processed by OnDestroy
            if (_isDestroying && !(currentState is ClosingState)) return; // If already destroying by other means and not in closing state

            _isDestroying = true; // Mark that destruction process has started from logical close

            ChangeState(new ClosedState());
            
            // The window is responsible for destroying its GameObject.
            // UILayer will be notified via this window's OnDestroy method.
            if (gameObject) // Check if not already destroyed by some other means
            {
                Destroy(gameObject);
            }
        }

        protected virtual void Awake()
        {
            // If not set by UIManager, it might fallback to GameObject's name or be null.
            if (string.IsNullOrEmpty(windowNameInternal))
            {
                windowNameInternal = gameObject.name; // Fallback, but UIManager should set it.
            }
        }

        protected virtual void Start()
        {
            // This lifecycle assumes windows are instantiated and immediately start opening.
            // If windows can be instantiated but kept "dormant", this logic would need adjustment.
            
            // TODO: The original code had OnStartOpen and OnFinishedOpen called sequentially here.
            // This implies no actual opening animation time.
            // For a proper animated opening:
            // 1. Call OnStartOpen() -> changes state to OpeningState.
            // 2. OpeningState or an animation system calls OnFinishedOpen() upon animation completion.
            
            OnStartOpen(); // Start the opening process

            // Simulating an immediate open for now, as per original logic.
            // In a real system, OnFinishedOpen would be delayed by an animation/transition.
            OnFinishedOpen();
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
            
            // Debug.Log($"[UIWindow] OnDestroy called for {WindowName}", this);

            // Notify the parent layer that this window is actually destroyed
            // so it can clean up its internal list of windows.
            parentLayerInternal?.NotifyWindowDestroyed(this);
            parentLayerInternal = null; // Clear reference to prevent further calls

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