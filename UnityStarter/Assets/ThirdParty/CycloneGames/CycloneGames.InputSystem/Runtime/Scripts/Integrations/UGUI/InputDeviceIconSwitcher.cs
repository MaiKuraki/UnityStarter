using System;

using UnityEngine;
using UnityEngine.UI;

using R3;

using CycloneGames.Logger;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Updates a cached UGUI Image sprite when player zero changes input device.
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
            if (_image == null)
            {
                enabled = false;
                return;
            }

            if (iconSet == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CLogger.LogWarning($"[InputDeviceIconSwitcher] {name}: IconSet is not assigned.");
#endif
                return;
            }

            InputManager inputManager = InputManager.Instance;
            IInputPlayer player = inputManager.GetInputPlayer(0);
            if (player == null)
            {
                return;
            }

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
            if (_isDestroyed || _image == null || iconSet == null)
            {
                return;
            }

            _image.sprite = iconSet.GetIcon(kind);
        }
    }
}
