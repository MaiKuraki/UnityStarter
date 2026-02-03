using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public struct NavigableItemSetup
    {
        public Button Button;
        public Toggle Toggle;
        public Slider Slider;
        /// <summary>
        /// Optional custom Transform for non-Selectable navigable items (e.g., SelectionSwitcher).
        /// When set, this Transform is used instead of Button/Toggle/Slider for focus management.
        /// This enables navigation support for custom composite components.
        /// </summary>
        public Transform CustomTransform;
        public Action OnConfirm;
        public Action OnNavigateLeft;
        public Action OnNavigateRight;
        public Action<Transform> OnFocused;
        public Action<Transform> OnUnfocused;
    }

    public class MenuNavigatorVertical : MonoBehaviour
    {
        private class InternalNavItem
        {
            public Button Button;
            public Toggle Toggle;
            public Slider Slider;
            public Transform CustomTransform;
            public Action OnConfirm;
            public Action OnNavigateLeft;
            public Action OnNavigateRight;
            public Action<Transform> OnFocused;
            public Action<Transform> OnUnfocused;
            public Selectable CachedSelectable;
            public Transform CachedTransform;
            public bool CachedIsActive;
            public bool CachedIsInteractable;
            public bool IsCustomTransformItem;

            public void UpdateCache()
            {
                // Priority: CustomTransform > Button > Toggle > Slider
                if (CustomTransform != null && CustomTransform.gameObject != null)
                {
                    IsCustomTransformItem = true;
                    CachedSelectable = null;
                    CachedTransform = CustomTransform;
                    CachedIsActive = CustomTransform.gameObject.activeInHierarchy;
                    CachedIsInteractable = true; // CustomTransform is always "interactable"
                }
                else
                {
                    IsCustomTransformItem = false;
                    CachedSelectable = Button != null ? (Selectable)Button : (Toggle != null ? (Selectable)Toggle : Slider);
                    if (IsSelectableValid())
                    {
                        CachedTransform = CachedSelectable.transform;
                        CachedIsActive = CachedSelectable.gameObject.activeInHierarchy;
                        CachedIsInteractable = CachedSelectable.interactable;
                    }
                    else
                    {
                        CachedSelectable = null;
                        CachedTransform = null;
                        CachedIsActive = false;
                        CachedIsInteractable = false;
                    }
                }
            }

            public bool IsSelectableValid()
            {
                return CachedSelectable != null && CachedSelectable.gameObject != null;
            }

            public bool IsTransformValid()
            {
                return CachedTransform != null && CachedTransform.gameObject != null;
            }

            public bool IsSelectable()
            {
                if (IsCustomTransformItem)
                {
                    return IsTransformValid() && CachedIsActive;
                }
                return IsSelectableValid() && CachedIsActive && CachedIsInteractable;
            }
        }

        private readonly List<InternalNavItem> _navigableItems = new List<InternalNavItem>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private Transform _focusIndicator;
        private bool _allowLooping;
        private bool _focusIndicatorOnTop;
        private int _currentFocusIndex = -1;
        private bool _isInitialized;

        [SerializeField] private bool _requireConfirmToEditSliders = false;
        [SerializeField] private float _sliderStep = 0.1f;

        private bool _isEditingSlider;

        private const float NAVIGATION_THRESHOLD = 0.5f;

        // Touch confirmation gate support
        private IInputPlayer _inputPlayer;
        private InputDeviceKind _previousDeviceKind = InputDeviceKind.Unknown;
        private bool _touchConfirmationRequired;
        private System.IDisposable _deviceKindSubscription;

        /// <summary>
        /// Initializes the vertical menu navigator with the provided setup data.
        /// </summary>
        /// <param name="setupData">List of navigable items (buttons/sliders) with their associated callbacks</param>
        /// <param name="focusIndicator">Optional transform to use as a visual focus indicator</param>
        /// <param name="allowLooping">Whether navigation should loop from first to last item and vice versa</param>
        /// <param name="defaultFocusIndex">Index of the item to focus on initialization (default: 0)</param>
        /// <param name="autoSubscribeButtonClick">
        /// Controls whether MenuNavigatorVertical automatically subscribes to button click events.
        /// Default: false
        /// 
        /// When true:
        /// - MenuNavigatorVertical automatically subscribes to Button.OnClickAsObservable() for all buttons
        /// - Button clicks (mouse, keyboard, gamepad) are handled automatically via OnConfirm callback
        /// - ⚠️ CRITICAL WARNING: Do NOT manually subscribe to Button.OnClickAsObservable() in your UI code when this is true
        /// - ⚠️ CRITICAL WARNING: Do NOT use Unity's Button.onClick.AddListener() or Inspector-assigned onClick events when this is true
        /// - ⚠️ Doing so will cause duplicate event handling, duplicate callback invocations, and potential logic errors
        /// - Recommended for simple menus where you want automatic click handling
        /// 
        /// When false (default):
        /// - MenuNavigatorVertical still subscribes to Button.OnClickAsObservable() but only handles keyboard/gamepad confirm
        /// - When a button is currently focused and keyboard/gamepad submit is pressed, OnConfirm callback is invoked
        /// - Mouse clicks on buttons should be handled manually via your own subscription
        /// - Mouse clicks on focused buttons will trigger both OnConfirm (via MenuNavigatorVertical) and your manual subscription
        ///   - You should guard against duplicate handling in your manual subscription (e.g., use a flag)
        /// - Use this option when you need custom mouse click handling but still want keyboard/gamepad to work via OnConfirm
        /// 
        /// Note: Mouse hover focus is always handled automatically via MenuNavigatorPointerHandler component regardless of this setting
        /// <param name="focusIndicatorOnTop">
        /// Controls whether the focus indicator is placed on top (last sibling) or bottom (first sibling) of the focused item's children.
        /// Default: true (on top, rendered in front)
        /// </param>
        /// <param name="inputPlayer">
        /// Optional IInputPlayer for touch confirmation gate support.
        /// When provided, first touch after switching from gamepad only focuses, second touch confirms.
        /// </param>
        public void Initialize(List<NavigableItemSetup> setupData, Transform focusIndicator, bool allowLooping, int defaultFocusIndex = 0, bool autoSubscribeButtonClick = false, bool focusIndicatorOnTop = true, IInputPlayer inputPlayer = null)
        {
            // Removed early return to ensure cache (active status) is refreshed even if previously initialized
            // if (_isInitialized) return;

            _focusIndicator = focusIndicator;
            _allowLooping = allowLooping;
            _focusIndicatorOnTop = focusIndicatorOnTop;
            _inputPlayer = inputPlayer;

            Cleanup();

            int count = setupData.Count;
            _navigableItems.Capacity = count > _navigableItems.Capacity ? count : _navigableItems.Capacity;

            for (int i = 0; i < count; i++)
            {
                var itemSetup = setupData[i];
                var item = new InternalNavItem
                {
                    Button = itemSetup.Button,
                    Toggle = itemSetup.Toggle,
                    Slider = itemSetup.Slider,
                    CustomTransform = itemSetup.CustomTransform,
                    OnConfirm = itemSetup.OnConfirm,
                    OnNavigateLeft = itemSetup.OnNavigateLeft,
                    OnNavigateRight = itemSetup.OnNavigateRight,
                    OnFocused = itemSetup.OnFocused,
                    OnUnfocused = itemSetup.OnUnfocused
                };
                item.UpdateCache();
                _navigableItems.Add(item);

                int index = i;

                // CustomTransform pointer handler (for non-Selectable navigable items like SelectionSwitcher)
                if (item.IsCustomTransformItem && item.CachedTransform != null && item.CachedTransform.gameObject != null)
                {
                    var customPointerHandler = item.CachedTransform.gameObject.GetComponent<MenuNavigatorPointerHandler>();
                    if (customPointerHandler == null)
                    {
                        customPointerHandler = item.CachedTransform.gameObject.AddComponent<MenuNavigatorPointerHandler>();
                    }
                    customPointerHandler.Initialize(this, index, true, _inputPlayer);
                }
                else if (item.CachedSelectable != null && item.CachedSelectable.gameObject != null)
                {
                    // Button click event subscription
                    if (itemSetup.Button != null && itemSetup.Button.gameObject != null)
                    {
                        if (autoSubscribeButtonClick)
                        {
                            // Full automatic handling: MenuNavigatorVertical handles all button clicks
                            var clickSubscription = itemSetup.Button.OnClickAsObservable().Subscribe(_ => OnButtonClicked(index));
                            _subscriptions.Add(clickSubscription);
                            clickSubscription.AddTo(item.CachedSelectable.gameObject);
                        }
                        else
                        {
                            // Selective handling: Only intercept keyboard/gamepad confirm via Unity's EventSystem
                            var clickSubscription = itemSetup.Button.OnClickAsObservable().Subscribe(_ =>
                            {
                                if (_isInitialized && _currentFocusIndex == index && IsItemSelectable(index))
                                {
                                    ConfirmSelection();
                                }
                            });
                            _subscriptions.Add(clickSubscription);
                            clickSubscription.AddTo(item.CachedSelectable.gameObject);
                        }

                        var buttonPointerHandler = itemSetup.Button.gameObject.GetComponent<MenuNavigatorPointerHandler>();
                        if (buttonPointerHandler == null)
                        {
                            buttonPointerHandler = itemSetup.Button.gameObject.AddComponent<MenuNavigatorPointerHandler>();
                        }
                        buttonPointerHandler.Initialize(this, index, true, _inputPlayer);
                    }
                    
                    // Toggle pointer handler (no onValueChanged subscription needed - 
                    // PointerHandler intercepts clicks and prevents automatic isOn changes)
                    if (itemSetup.Toggle != null && itemSetup.Toggle.gameObject != null)
                    {
                        var togglePointerHandler = itemSetup.Toggle.gameObject.GetComponent<MenuNavigatorPointerHandler>();
                        if (togglePointerHandler == null)
                        {
                            togglePointerHandler = itemSetup.Toggle.gameObject.AddComponent<MenuNavigatorPointerHandler>();
                        }
                        togglePointerHandler.Initialize(this, index, true, _inputPlayer);
                    }

                    // Slider pointer handler
                    if (itemSetup.Slider != null && itemSetup.Slider.gameObject != null)
                    {
                        var sliderPointerHandler = itemSetup.Slider.gameObject.GetComponent<MenuNavigatorPointerHandler>();
                        if (sliderPointerHandler == null)
                        {
                            sliderPointerHandler = itemSetup.Slider.gameObject.AddComponent<MenuNavigatorPointerHandler>();
                        }
                        sliderPointerHandler.Initialize(this, index, true, _inputPlayer);
                    }
                }
            }

            _isInitialized = true;

            try
            {
                // Initialize focus state: ensure all items are unfocused first (except the one we're about to focus)
                // This ensures correct initial visual state when UI elements default to focused appearance
                int targetFocusIndex = -1;
                int itemCount = _navigableItems.Count;
                
                if (IsItemSelectable(defaultFocusIndex))
                {
                    targetFocusIndex = defaultFocusIndex;
                }
                else
                {
                    // Find first available item
                    for (int i = 0; i < itemCount; i++)
                    {
                        if (IsItemSelectable(i))
                        {
                            targetFocusIndex = i;
                            break;
                        }
                    }
                }

                // Set all items to unfocused state first (only if they have OnUnfocused callback)
                if (targetFocusIndex >= 0)
                {
                    for (int i = 0; i < itemCount; i++)
                    {
                        if (i != targetFocusIndex && IsItemSelectable(i))
                        {
                            var item = _navigableItems[i];
                            if (item.CachedTransform != null && item.OnUnfocused != null)
                            {
                                try
                                {
                                    item.OnUnfocused.Invoke(item.CachedTransform);
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }

                // Now set focus on the target item
                if (targetFocusIndex >= 0)
                {
                    SetFocusWithoutUnfocus(targetFocusIndex);
                }
                else
                {
                    _currentFocusIndex = -1;
                    UpdateFocusIndicator();
                }
            }
            catch
            {
                _isInitialized = false;
            }
            
            // Setup device kind subscription for touch confirmation gate
            // Must be called after Cleanup() and after _isInitialized is set
            SetupDeviceKindSubscription();
        }

        public void Navigate(Vector2 direction)
        {
            if (!_isInitialized || this == null || gameObject == null) return;
            if (!gameObject.activeInHierarchy || !enabled) return;

            float absY = direction.y >= 0 ? direction.y : -direction.y;

            if (_isEditingSlider && absY > NAVIGATION_THRESHOLD)
            {
                return;
            }

            try
            {
                if (absY > NAVIGATION_THRESHOLD)
                {
                    MoveSelection(direction.y < 0 ? 1 : -1);
                }
                else
                {
                    float absX = direction.x >= 0 ? direction.x : -direction.x;
                    if (absX > NAVIGATION_THRESHOLD)
                    {
                        TriggerContextualNavigation(direction.x > 0);
                    }
                }
            }
            catch
            {
            }
        }

        public void ConfirmSelection()
        {
            if (!_isInitialized || _currentFocusIndex < 0) return;
            if (!IsItemSelectable(_currentFocusIndex)) return;

            var item = _navigableItems[_currentFocusIndex];

            if (_requireConfirmToEditSliders && !_isEditingSlider && item.Slider != null)
            {
                _isEditingSlider = true;
                return;
            }

            if (item.OnConfirm != null)
            {
                try
                {
                    item.OnConfirm.Invoke();
                }
                catch
                {
                }
            }
        }

        public bool TryCancelEdit()
        {
            if (_isEditingSlider)
            {
                _isEditingSlider = false;
                return true;
            }
            return false;
        }

        private void MoveSelection(int direction)
        {
            _isEditingSlider = false;

            int count = _navigableItems.Count;
            if (count < 2) return;

            int startIndex = _currentFocusIndex < 0 ? 0 : _currentFocusIndex;
            int newIndex = startIndex;
            int searchCount = _allowLooping ? count : count - 1;

            for (int i = 0; i < searchCount; i++)
            {
                newIndex += direction;

                if (_allowLooping)
                {
                    if (newIndex < 0)
                    {
                        newIndex = count - 1;
                    }
                    else if (newIndex >= count)
                    {
                        newIndex = 0;
                    }
                }
                else
                {
                    if (newIndex < 0)
                    {
                        newIndex = 0;
                    }
                    else if (newIndex >= count)
                    {
                        newIndex = count - 1;
                    }
                }

                if (newIndex != _currentFocusIndex && IsItemSelectable(newIndex))
                {
                    SetFocus(newIndex);
                    return;
                }
            }
        }

        private void TriggerContextualNavigation(bool isRight)
        {
            if (!_isInitialized) return;
            if (!IsItemSelectable(_currentFocusIndex)) return;

            var item = _navigableItems[_currentFocusIndex];

            bool canEditSlider = item.Slider != null && item.Slider.gameObject != null && (!_requireConfirmToEditSliders || _isEditingSlider);

            if (canEditSlider)
            {
                float step = item.Slider.wholeNumbers ? 1f : _sliderStep;
                item.Slider.value += isRight ? step : -step;
                return;
            }

            var action = isRight ? item.OnNavigateRight : item.OnNavigateLeft;
            if (action != null)
            {
                try
                {
                    action.Invoke();
                }
                catch
                {
                }
            }
        }

        private void OnButtonClicked(int index)
        {
            if (!_isInitialized) return;
            if (!IsValidIndex(index)) return;
            try
            {
                // Touch confirmation gate: first touch after gamepad→pointer does NOTHING (no focus change, no confirm)
                if (ShouldBlockTouchConfirmation())
                {
                    ResetTouchConfirmationGate();
                    return;
                }
                
                // Normal behavior: focus and confirm
                SetFocus(index);
                ConfirmSelection();
            }
            catch
            {
            }
        }

        private void SetupDeviceKindSubscription()
        {
            _deviceKindSubscription?.Dispose();
            if (_inputPlayer == null) return;

            // Reset gate state before setting up new subscription
            // This ensures the gate is only active when actually starting in Gamepad mode,
            // not when re-initializing from a previous Gamepad session
            _touchConfirmationRequired = false;

            _previousDeviceKind = _inputPlayer.ActiveDeviceKind.CurrentValue;
            
            // If starting in Gamepad mode, first pointer click should be blocked
            if (_previousDeviceKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = true;
            }
            
            _deviceKindSubscription = _inputPlayer.ActiveDeviceKind.Subscribe(OnDeviceKindChanged);
        }

        private void OnDeviceKindChanged(InputDeviceKind newKind)
        {
            // When switching TO Gamepad, set gate for next pointer click
            if (newKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = true;
            }
            // When switching FROM Gamepad to pointer device, reset gate immediately
            else if (_previousDeviceKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = false;
            }
            
            _previousDeviceKind = newKind;
        }

        private bool ShouldBlockTouchConfirmation()
        {
            if (_inputPlayer == null) return false;
            if (!_touchConfirmationRequired) return false;
            
            // Block if gate is set and current input is a pointer device (mouse or touch)
            var currentKind = _inputPlayer.ActiveDeviceKind.CurrentValue;
            return currentKind == InputDeviceKind.Touchscreen || currentKind == InputDeviceKind.KeyboardMouse;
        }

        private void ResetTouchConfirmationGate()
        {
            _touchConfirmationRequired = false;
        }

        private void SetFocus(int index)
        {
            if (!_isInitialized) return;
            if (!IsValidIndex(index)) return;
            if (_currentFocusIndex == index) return;
            if (!IsItemSelectable(index)) return;

            int previousIndex = _currentFocusIndex;
            if (previousIndex >= 0 && IsValidIndex(previousIndex))
            {
                var previousItem = _navigableItems[previousIndex];
                if (previousItem.CachedTransform != null && previousItem.OnUnfocused != null)
                {
                    try
                    {
                        previousItem.OnUnfocused.Invoke(previousItem.CachedTransform);
                    }
                    catch
                    {
                    }
                }
            }

            SetFocusWithoutUnfocus(index);
        }

        // Internal method to set focus without calling OnUnfocused (used during initialization)
        private void SetFocusWithoutUnfocus(int index)
        {
            _isEditingSlider = false;
            _currentFocusIndex = index;
            var item = _navigableItems[index];
            if (item.CachedTransform != null && item.OnFocused != null)
            {
                try
                {
                    item.OnFocused.Invoke(item.CachedTransform);
                }
                catch
                {
                }
            }
            UpdateFocusIndicator();
        }

        private void UpdateFocusIndicator()
        {
            if (!_isInitialized) return;
            if (_focusIndicator == null || _focusIndicator.gameObject == null) return;

            try
            {
                // Refresh cache before checking selectability to ensure current active state
                if (IsValidIndex(_currentFocusIndex))
                {
                    _navigableItems[_currentFocusIndex].UpdateCache();
                }

                if (!IsItemSelectable(_currentFocusIndex))
                {
                    if (_focusIndicator.gameObject != null)
                    {
                        _focusIndicator.gameObject.SetActive(false);
                    }
                    return;
                }

                var item = _navigableItems[_currentFocusIndex];
                if (item.CachedTransform != null && item.CachedTransform.gameObject != null && item.CachedIsActive)
                {
                    _focusIndicator.SetParent(item.CachedTransform, false);
                    _focusIndicator.localPosition = Vector3.zero;
                    _focusIndicator.gameObject.SetActive(true);
                    if (_focusIndicatorOnTop)
                    {
                        _focusIndicator.SetAsLastSibling();
                    }
                    else
                    {
                        _focusIndicator.SetAsFirstSibling();
                    }
                }
                else
                {
                    if (_focusIndicator.gameObject != null)
                    {
                        _focusIndicator.gameObject.SetActive(false);
                    }
                }
            }
            catch
            {
            }
        }

        public void FocusFirstAvailable()
        {
            if (!_isInitialized) return;

            int count = _navigableItems.Count;
            for (int i = 0; i < count; i++)
            {
                if (IsItemSelectable(i))
                {
                    SetFocus(i);
                    return;
                }
            }

            _currentFocusIndex = -1;
            UpdateFocusIndicator();
        }

        public void SetFocusByIndex(int index)
        {
            if (!_isInitialized || this == null || gameObject == null) return;
            if (!gameObject.activeInHierarchy || !enabled) return;
            SetFocus(index);
        }

        private bool IsItemSelectable(int index)
        {
            if (!IsValidIndex(index)) return false;
            return _navigableItems[index].IsSelectable();
        }

        private bool IsValidIndex(int index)
        {
            return index >= 0 && index < _navigableItems.Count;
        }

        private void Cleanup()
        {
            if (!_isInitialized) return;

            _deviceKindSubscription?.Dispose();
            _deviceKindSubscription = null;

            int subscriptionCount = _subscriptions.Count;
            for (int i = 0; i < subscriptionCount; i++)
            {
                _subscriptions[i]?.Dispose();
            }
            _subscriptions.Clear();

            int itemCount = _navigableItems.Count;
            for (int i = 0; i < itemCount; i++)
            {
                try
                {
                    var item = _navigableItems[i];
                    if (item.CachedSelectable != null)
                    {
                        var selectableObj = item.CachedSelectable.gameObject;
                        if (selectableObj != null)
                        {
                            var handler = selectableObj.GetComponent<MenuNavigatorPointerHandler>();
                            if (handler != null)
                            {
                                handler.Initialize((MenuNavigatorVertical)null, -1);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            _navigableItems.Clear();
            _isInitialized = false;
            _currentFocusIndex = -1;
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void OnDisable()
        {
            if (_isInitialized)
            {
                Cleanup();
            }
        }


    }
}
