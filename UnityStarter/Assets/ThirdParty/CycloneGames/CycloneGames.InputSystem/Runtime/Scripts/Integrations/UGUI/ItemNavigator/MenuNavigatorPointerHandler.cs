using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Handles pointer events for menu navigation items (Button, Toggle, Slider, CustomTransform, etc.)
    /// - For Button: OnPointerEnter sets focus, OnPointerClick triggers OnConfirm via Button's OnClickAsObservable subscription
    /// - For Toggle: OnPointerEnter sets focus, click triggers OnConfirm via onValueChanged subscription
    ///   The Toggle's isOn value is preserved; user manually controls it in OnConfirm callback
    /// - For CustomTransform: OnPointerEnter sets focus, OnPointerClick triggers OnConfirm directly
    ///   This enables click-to-confirm for non-Selectable navigable items like SelectionSwitcher
    /// 
    /// Touch Confirmation Gate:
    /// When transitioning from Gamepad to Touchscreen input, the first touch only focuses the element.
    /// A second touch is required to confirm the action. This prevents accidental triggers when
    /// the cursor position jumps from (-1,-1) to the touch location.
    /// </summary>
    public class MenuNavigatorPointerHandler : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerClickHandler
    {
        private MenuNavigatorHorizontal _horizontalNavigator;
        private MenuNavigatorVertical _verticalNavigator;
        private Toggle _toggle;
        private Button _button;
        private bool _cachedToggleValue;
        private int _index;
        private bool _isInitialized;
        private bool _interceptToggleClick;
        private bool _isProcessingToggleChange;
        private bool _isCustomTransformItem;

        // Touch confirmation gate state
        private IInputPlayer _inputPlayer;
        private InputDeviceKind _previousDeviceKind = InputDeviceKind.Unknown;
        private bool _touchConfirmationRequired;
        private int _focusedIndexOnFirstTouch = -1;
        private System.IDisposable _deviceKindSubscription;

        public void Initialize(MenuNavigatorHorizontal navigator, int index, bool interceptToggleClick = true, IInputPlayer inputPlayer = null)
        {
            CleanupSubscriptions();
            _horizontalNavigator = navigator;
            _verticalNavigator = null;
            _index = index;
            _isInitialized = navigator != null && index >= 0;
            _interceptToggleClick = interceptToggleClick;
            _inputPlayer = inputPlayer;
            SetupComponents();
            SetupDeviceKindSubscription();
        }

        public void Initialize(MenuNavigatorVertical navigator, int index, bool interceptToggleClick = true, IInputPlayer inputPlayer = null)
        {
            CleanupSubscriptions();
            _horizontalNavigator = null;
            _verticalNavigator = navigator;
            _index = index;
            _isInitialized = navigator != null && index >= 0;
            _interceptToggleClick = interceptToggleClick;
            _inputPlayer = inputPlayer;
            SetupComponents();
            SetupDeviceKindSubscription();
        }

        private void SetupDeviceKindSubscription()
        {
            if (_inputPlayer == null) return;

            // Reset gate state before setting up new subscription
            // This ensures the gate is only active when actually starting in Gamepad mode,
            // not when re-initializing from a previous Gamepad session
            _touchConfirmationRequired = false;
            _focusedIndexOnFirstTouch = -1;

            _previousDeviceKind = _inputPlayer.ActiveDeviceKind.CurrentValue;
            
            // If starting in Gamepad mode, first pointer event should be blocked
            if (_previousDeviceKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = true;
            }
            
            _deviceKindSubscription = _inputPlayer.ActiveDeviceKind.Subscribe(OnDeviceKindChanged);
        }

        private void OnDeviceKindChanged(InputDeviceKind newKind)
        {
            // When switching TO Gamepad, set gate for next pointer event
            if (newKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = true;
                _focusedIndexOnFirstTouch = -1;
            }
            // When switching FROM Gamepad to pointer device, reset gate immediately
            // This ensures all handlers' gates are reset at once, not one-by-one via pointer events
            else if (_previousDeviceKind == InputDeviceKind.Gamepad)
            {
                _touchConfirmationRequired = false;
                _focusedIndexOnFirstTouch = -1;
            }

            _previousDeviceKind = newKind;
        }

        private void SetupComponents()
        {
            _toggle = GetComponent<Toggle>();
            _button = GetComponent<Button>();
            
            // Determine if this is a CustomTransform item (no Selectable component)
            _isCustomTransformItem = _toggle == null && _button == null && GetComponent<Slider>() == null;
            
            if (_toggle != null && _interceptToggleClick)
            {
                _cachedToggleValue = _toggle.isOn;
                _toggle.onValueChanged.AddListener(OnToggleValueChanged);
            }
        }

        private void OnToggleValueChanged(bool newValue)
        {
            if (!_isInitialized || !_interceptToggleClick || _isProcessingToggleChange) return;
            
            _isProcessingToggleChange = true;
            try
            {
                // Restore the cached value to prevent automatic isOn change
                _toggle.SetIsOnWithoutNotify(_cachedToggleValue);
                
                // Check touch confirmation gate
                if (ShouldBlockTouchConfirmation())
                {
                    // First touch after gamepad→touch: focus only, don't confirm
                    _focusedIndexOnFirstTouch = _index;
                    return;
                }
                
                // Trigger confirm selection so user can manually control isOn in OnConfirm callback
                if (_horizontalNavigator != null)
                {
                    _horizontalNavigator.ConfirmSelection();
                }
                else if (_verticalNavigator != null)
                {
                    _verticalNavigator.ConfirmSelection();
                }
                
                // After OnConfirm, update cached value to reflect any changes made by user
                _cachedToggleValue = _toggle.isOn;
                
                // Reset gate after successful confirmation
                ResetTouchConfirmationGate();
            }
            finally
            {
                _isProcessingToggleChange = false;
            }
        }

        private void CleanupSubscriptions()
        {
            CleanupToggleSubscription();
            _deviceKindSubscription?.Dispose();
            _deviceKindSubscription = null;
        }

        private void CleanupToggleSubscription()
        {
            if (_toggle != null)
            {
                _toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            }
        }

        private bool IsNavigatorValid()
        {
            if (!_isInitialized) return false;
            if (!gameObject.activeInHierarchy || !enabled) return false;

            if (_horizontalNavigator != null)
            {
                return _horizontalNavigator.gameObject != null && _horizontalNavigator.gameObject.activeInHierarchy;
            }

            if (_verticalNavigator != null)
            {
                return _verticalNavigator.gameObject != null && _verticalNavigator.gameObject.activeInHierarchy;
            }

            return false;
        }

        /// <summary>
        /// Checks if touch confirmation should be blocked (first touch after gamepad→touchscreen transition)
        /// </summary>
        private bool ShouldBlockTouchConfirmation()
        {
            if (_inputPlayer == null) return false;
            if (!_touchConfirmationRequired) return false;
            
            // Only block for touchscreen input
            if (_inputPlayer.ActiveDeviceKind.CurrentValue != InputDeviceKind.Touchscreen) return false;

            // If this is the first touch on this element, block confirmation
            if (_focusedIndexOnFirstTouch != _index)
            {
                return true;
            }

            return false;
        }

        private void ResetTouchConfirmationGate()
        {
            _touchConfirmationRequired = false;
            _focusedIndexOnFirstTouch = -1;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsNavigatorValid()) return;

            // Touch gate: first pointer event after gamepad mode should not change focus
            if (ShouldBlockPointerFocus())
            {
                // Reset gate after first blocked pointer enter
                // This ensures mouse hover works after the first blocked event
                // The gate is primarily designed for touch input where cursor jumps from (-1,-1)
                ResetTouchConfirmationGate();
                return; // Don't change focus on this event
            }

            if (_horizontalNavigator != null)
            {
                _horizontalNavigator.SetFocusByIndex(_index);
            }
            else if (_verticalNavigator != null)
            {
                _verticalNavigator.SetFocusByIndex(_index);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsNavigatorValid()) return;

            // Touch gate: first pointer event after gamepad mode should not change focus
            if (ShouldBlockPointerFocus())
            {
                // Reset gate after first blocked pointer down
                ResetTouchConfirmationGate();
                return; // Don't change focus
            }

            if (_horizontalNavigator != null)
            {
                _horizontalNavigator.SetFocusByIndex(_index);
            }
            else if (_verticalNavigator != null)
            {
                _verticalNavigator.SetFocusByIndex(_index);
            }

            // Track first touch for confirmation gate
            if (_touchConfirmationRequired && _focusedIndexOnFirstTouch == -1)
            {
                _focusedIndexOnFirstTouch = _index;
            }
        }

        /// <summary>
        /// Checks if pointer focus should be blocked (when transitioning from Gamepad to pointer input)
        /// </summary>
        private bool ShouldBlockPointerFocus()
        {
            if (_inputPlayer == null) return false;
            if (!_touchConfirmationRequired) return false;
            
            // Block for both touchscreen and mouse input (Steam Deck touch = mouse)
            var currentKind = _inputPlayer.ActiveDeviceKind.CurrentValue;
            return currentKind == InputDeviceKind.Touchscreen || currentKind == InputDeviceKind.KeyboardMouse;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsNavigatorValid()) return;
            
            // For CustomTransform items, handle click-to-confirm here since there's no Button/Toggle subscription
            // For Buttons, confirmation is handled by MenuNavigator's OnClickAsObservable subscription
            // For Toggles, confirmation is handled via onValueChanged subscription in SetupComponents()
            if (_isCustomTransformItem)
            {
                // Check touch confirmation gate
                if (ShouldBlockTouchConfirmation())
                {
                    // First touch after gamepad→touch: focus only, don't confirm
                    _focusedIndexOnFirstTouch = _index;
                    return;
                }

                if (_horizontalNavigator != null)
                {
                    _horizontalNavigator.ConfirmSelection();
                }
                else if (_verticalNavigator != null)
                {
                    _verticalNavigator.ConfirmSelection();
                }

                // Reset gate after successful confirmation
                ResetTouchConfirmationGate();
            }
        }

        private void OnDisable()
        {
            _isInitialized = false;
            ResetTouchConfirmationGate();
        }

        private void OnDestroy()
        {
            CleanupSubscriptions();
            _horizontalNavigator = null;
            _verticalNavigator = null;
            _toggle = null;
            _button = null;
            _inputPlayer = null;
            _isInitialized = false;
            _isCustomTransformItem = false;
        }
    }
}