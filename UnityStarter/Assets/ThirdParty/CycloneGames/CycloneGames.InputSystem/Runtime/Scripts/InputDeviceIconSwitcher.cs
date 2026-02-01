using System;
using UnityEngine;
using UnityEngine.UI;
using R3;
using CycloneGames.Logger;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Automatically updates a UI Image sprite based on the current input device.
    /// Attach to any GameObject with an Image component.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class InputDeviceIconSwitcher : MonoBehaviour
    {
        [SerializeField] private InputDeviceIconSet iconSet;

        private Image _image;
        private IDisposable _subscription;
        private bool _isDestroyed;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Start()
        {
            if (iconSet == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogWarning($"[InputDeviceIconSwitcher] {name}: IconSet is not assigned.");
#endif
                return;
            }

            var inputManager = InputManager.Instance;
            if (inputManager == null) return;

            var player = inputManager.GetInputPlayer(0);
            if (player == null) return;

            _subscription = player.ActiveDeviceKind.Subscribe(OnDeviceChanged);
            OnDeviceChanged(player.ActiveDeviceKind.CurrentValue);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnDeviceChanged(InputDeviceKind kind)
        {
            if (_isDestroyed || _image == null || iconSet == null) return;

            _image.sprite = iconSet.GetIcon(kind);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_image == null)
                _image = GetComponent<Image>();

            if (_image != null && iconSet != null)
            {
                _image.sprite = iconSet.KeyboardMouseIcon;
            }
        }
#endif
    }
}
