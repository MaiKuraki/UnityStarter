using System;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>Sprite override for one device layout, for example <c>DualShockGamepad</c>.</summary>
    [Serializable]
    internal struct DeviceLayoutIcon
    {
        public string layout;
        public Sprite icon;
    }

    /// <summary>Sprite override for one control path, for example <c>&lt;Gamepad&gt;/buttonSouth</c>.</summary>
    [Serializable]
    internal struct DeviceControlIcon
    {
        public string controlPath;
        public Sprite icon;
    }

    /// <summary>
    /// Resolves the icon shown for a device or a control. Exact control match wins, then device layout, then the
    /// coarse device kind. Returns sprites only: display text stays in the product's localization system, keyed by
    /// the same control path.
    /// </summary>
    internal static class InputIconResolver
    {
        internal static bool TryResolve(
            string controlPath,
            InputDeviceKind kind,
            string deviceLayout,
            DeviceControlIcon[] controlIcons,
            DeviceLayoutIcon[] layoutIcons,
            Sprite keyboardMouseIcon,
            Sprite gamepadIcon,
            Sprite touchIcon,
            out Sprite icon)
        {
            if (!string.IsNullOrEmpty(controlPath) && controlIcons != null)
            {
                for (int i = 0; i < controlIcons.Length; i++)
                {
                    if (controlIcons[i].icon != null &&
                        string.Equals(controlIcons[i].controlPath, controlPath, StringComparison.OrdinalIgnoreCase))
                    {
                        icon = controlIcons[i].icon;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(deviceLayout) && layoutIcons != null)
            {
                for (int i = 0; i < layoutIcons.Length; i++)
                {
                    if (layoutIcons[i].icon != null &&
                        string.Equals(layoutIcons[i].layout, deviceLayout, StringComparison.OrdinalIgnoreCase))
                    {
                        icon = layoutIcons[i].icon;
                        return true;
                    }
                }
            }

            icon = ResolveKind(kind, keyboardMouseIcon, gamepadIcon, touchIcon);
            return icon != null;
        }

        internal static Sprite ResolveKind(InputDeviceKind kind, Sprite keyboardMouseIcon, Sprite gamepadIcon, Sprite touchIcon)
        {
            return kind switch
            {
                InputDeviceKind.Gamepad => gamepadIcon != null ? gamepadIcon : keyboardMouseIcon,
                InputDeviceKind.Touchscreen => touchIcon != null ? touchIcon : keyboardMouseIcon,
                _ => keyboardMouseIcon
            };
        }
    }

    /// <summary>
    /// Holds prompt sprites for device kinds, device layouts and control paths. Playback code resolves through
    /// <see cref="TryGetIcon(string, InputDeviceKind, string, out Sprite)"/>; the set owns sprites, not text.
    /// </summary>
    [CreateAssetMenu(fileName = "InputDeviceIconSet", menuName = "CycloneGames/Input/Device Icon Set")]
    public class InputDeviceIconSet : ScriptableObject
    {
        [Header("Device Icons")]
        [Tooltip("Icon for Keyboard/Mouse input")]
        [SerializeField] private Sprite keyboardMouseIcon;

        [Tooltip("Icon for Gamepad input")]
        [SerializeField] private Sprite gamepadIcon;

        [Tooltip("Icon for Touchscreen input")]
        [SerializeField] private Sprite touchIcon;

        [Header("Layout Prompts")]
        [Tooltip("Exact Unity device layout names, for example DualShockGamepad or XInputController. " +
                 "Matched case-insensitively before the coarse InputDeviceKind fallback.")]
        [SerializeField] private DeviceLayoutIcon[] layoutIcons = Array.Empty<DeviceLayoutIcon>();

        [Header("Control Prompts")]
        [Tooltip("Exact control paths, for example <Gamepad>/buttonSouth. Matched before layout and device fallbacks.")]
        [SerializeField] private DeviceControlIcon[] controlIcons = Array.Empty<DeviceControlIcon>();

        public Sprite KeyboardMouseIcon => keyboardMouseIcon;
        public Sprite GamepadIcon => gamepadIcon;
        public Sprite TouchIcon => touchIcon;

        /// <summary>Resolves a device icon from the coarse device kind.</summary>
        public Sprite GetIcon(InputDeviceKind kind)
        {
            return InputIconResolver.ResolveKind(kind, keyboardMouseIcon, gamepadIcon, touchIcon);
        }

        /// <summary>Resolves a device icon from the layout first, then the coarse kind.</summary>
        public Sprite GetIcon(InputDeviceKind kind, string deviceLayout)
        {
            InputIconResolver.TryResolve(
                null, kind, deviceLayout, controlIcons, layoutIcons, keyboardMouseIcon, gamepadIcon, touchIcon, out Sprite icon);
            return icon;
        }

        /// <summary>Resolves the icon of one control. Returns false when only a fallback exists.</summary>
        /// <param name="controlPath">Control path such as <c>&lt;Gamepad&gt;/buttonSouth</c>.</param>
        /// <param name="kind">Coarse device kind used by the final fallback.</param>
        /// <param name="deviceLayout">Concrete layout used before the coarse kind, or null.</param>
        /// <param name="icon">Resolved sprite, or null.</param>
        public bool TryGetIcon(string controlPath, InputDeviceKind kind, string deviceLayout, out Sprite icon)
        {
            return InputIconResolver.TryResolve(
                controlPath, kind, deviceLayout, controlIcons, layoutIcons, keyboardMouseIcon, gamepadIcon, touchIcon, out icon);
        }

        /// <summary>Resolves a device icon from a device activity record.</summary>
        public Sprite GetIcon(InputDeviceActivity activity)
        {
            return GetIcon(activity.PromptKind, activity.DeviceLayout);
        }
    }
}
