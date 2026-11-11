using System;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Fine-grained device family for prompt and icon selection. Unlike <see cref="InputDeviceKind"/> this
    /// separates keyboard from mouse and exposes non-gamepad device families explicitly.
    /// </summary>
    public enum InputDeviceFamily
    {
        Unknown,
        Keyboard,
        Mouse,
        Gamepad,
        Touchscreen,
        Joystick,
        Other
    }

    /// <summary>
    /// Immutable record of one observed device activity on a player's own device. Published through
    /// <see cref="InputPlayer.DeviceActivity"/>. Carries enough identity for product UI to select a
    /// per-layout prompt (for example an Xbox "A" glyph versus a PlayStation cross glyph) without
    /// guessing from the coarse <see cref="InputDeviceKind"/> value.
    /// </summary>
    /// <remarks>
    /// <see cref="Timestamp"/> uses the player's frame timestamp, which is advanced by
    /// <c>UpdateRuntimeState</c>. It is frame-accurate and deterministic in tests that supply an explicit
    /// timestamp; it is not a wall-clock value.
    /// </remarks>
    public readonly struct InputDeviceActivity
    {
        public InputDeviceActivity(
            int playerId,
            int deviceId,
            string deviceLayout,
            InputDeviceFamily family,
            InputDeviceKind promptKind,
            string controlPath,
            double timestamp)
        {
            PlayerId = playerId;
            DeviceId = deviceId;
            DeviceLayout = deviceLayout ?? string.Empty;
            Family = family;
            PromptKind = promptKind;
            ControlPath = controlPath ?? string.Empty;
            Timestamp = timestamp;
        }

        /// <summary>Owning player slot identity.</summary>
        public int PlayerId { get; }

        /// <summary>Unity Input System device id, stable for the lifetime of the device instance.</summary>
        public int DeviceId { get; }

        /// <summary>Concrete layout name, for example "Keyboard", "Mouse" or "DualShockGamepad".</summary>
        public string DeviceLayout { get; }

        /// <summary>Fine-grained family used for prompt selection.</summary>
        public InputDeviceFamily Family { get; }

        /// <summary>Legacy coarse kind, kept so existing <see cref="InputDeviceKind"/> consumers still work.</summary>
        public InputDeviceKind PromptKind { get; }

        /// <summary>Full control path of the control that produced the activity, or an empty string.</summary>
        public string ControlPath { get; }

        /// <summary>Player frame timestamp at which the activity was observed.</summary>
        public double Timestamp { get; }
    }
}
