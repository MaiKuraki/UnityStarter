using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// ScriptableObject that maps InputDeviceKind to sprites for UI icons.
    /// Use this to configure device-specific icons for buttons/prompts.
    /// </summary>
    [CreateAssetMenu(fileName = "InputDeviceIconSet", menuName = "CycloneGames/Input/Device Icon Set")]
    public class InputDeviceIconSet : ScriptableObject
    {
        [Header("Device Icons")]
        [Tooltip("Icon for Keyboard/Mouse input")]
        [SerializeField] private Sprite keyboardMouseIcon;

        [Tooltip("Icon for Gamepad input (Xbox, PlayStation, etc.)")]
        [SerializeField] private Sprite gamepadIcon;

        [Tooltip("Icon for Touchscreen input")]
        [SerializeField] private Sprite touchIcon;

        public Sprite KeyboardMouseIcon => keyboardMouseIcon;
        public Sprite GamepadIcon => gamepadIcon;
        public Sprite TouchIcon => touchIcon;

        /// <summary>
        /// Returns the appropriate sprite for the given device kind.
        /// Falls back to keyboard icon if specific icon is null.
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
    }
}
