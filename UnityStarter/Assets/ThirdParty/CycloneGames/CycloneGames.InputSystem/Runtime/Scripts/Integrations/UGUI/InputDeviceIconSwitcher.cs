using System;

using UnityEngine;
using UnityEngine.UI;

using R3;

using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Updates a cached UGUI Image sprite when player zero changes input device.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class InputDeviceIconSwitcher : MonoBehaviour
    {
        private static readonly LogChannel Log = InputSystemUguiLog.Channel;

        [SerializeField] private InputDeviceIconSet iconSet;
        [SerializeField] private InputManagerProvider managerProvider;

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
#if DEBUG
                Log.Warning($"[InputDeviceIconSwitcher] {name}: IconSet is not assigned.");
#endif
                return;
            }

            if (managerProvider == null || managerProvider.Manager == null)
            {
#if DEBUG
                Log.Warning($"[InputDeviceIconSwitcher] {name}: ManagerProvider is not assigned.");
#endif
                enabled = false;
                return;
            }

            IInputActionReader player = managerProvider.Manager.GetInputPlayer(0);
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
