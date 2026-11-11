using System;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// ScriptableObject that maps InputDeviceKind to sprites for UI icons.
    /// Use this to configure device-specific icons for buttons and prompts.
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

        [Serializable]
        private struct LayoutIcon
        {
            public string layout;
            public Sprite icon;
        }

        [Header("Per-Layout Prompt Overrides")]
        [Tooltip("Exact Unity device layout names, for example DualShockGamepad or XInputController. " +
                 "Matched case-insensitively before the coarse InputDeviceKind fallback.")]
        [SerializeField] private LayoutIcon[] layoutIcons = Array.Empty<LayoutIcon>();

        public Sprite KeyboardMouseIcon => keyboardMouseIcon;
        public Sprite GamepadIcon => gamepadIcon;
        public Sprite TouchIcon => touchIcon;

        /// <summary>
        /// Resolves a prompt icon from the coarse legacy kind. Kept for existing consumers.
        /// </summary>
        public Sprite GetIcon(InputDeviceKind kind)
        {
            return kind switch
            {
                InputDeviceKind.Gamepad => gamepadIcon != null ? gamepadIcon : keyboardMouseIcon,
                InputDeviceKind.Touchscreen => touchIcon != null ? touchIcon : keyboardMouseIcon,
                _ => keyboardMouseIcon
            };
        }

        /// <summary>
        /// Resolves a prompt icon from the concrete device layout first (Xbox versus PlayStation versus
        /// Switch glyphs), falling back to <see cref="GetIcon(InputDeviceKind)"/> when no override matches.
        /// </summary>
        public Sprite GetIcon(InputDeviceKind kind, string deviceLayout)
        {
            if (!string.IsNullOrEmpty(deviceLayout) && layoutIcons != null)
            {
                for (int i = 0; i < layoutIcons.Length; i++)
                {
                    if (layoutIcons[i].icon != null &&
                        string.Equals(layoutIcons[i].layout, deviceLayout, StringComparison.OrdinalIgnoreCase))
                    {
                        return layoutIcons[i].icon;
                    }
                }
            }

            return GetIcon(kind);
        }

        /// <summary>
        /// Convenience overload that consumes the full device activity record.
        /// </summary>
        public Sprite GetIcon(InputDeviceActivity activity)
        {
            return GetIcon(activity.PromptKind, activity.DeviceLayout);
        }
    }
}
