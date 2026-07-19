namespace CycloneGames.InputSystem.Runtime
{
    public enum InputPlayerDeviceChangeKind
    {
        Paired = 0,
        Unpaired = 1,
        Lost = 2,
        Regained = 3
    }

    public readonly struct InputPlayerDeviceStatus
    {
        public InputPlayerDeviceStatus(
            InputPlayerDeviceChangeKind changeKind,
            InputDeviceKind deviceKind,
            int deviceId,
            string layout)
        {
            ChangeKind = changeKind;
            DeviceKind = deviceKind;
            DeviceId = deviceId;
            Layout = layout ?? string.Empty;
        }

        public InputPlayerDeviceChangeKind ChangeKind { get; }
        public InputDeviceKind DeviceKind { get; }
        public int DeviceId { get; }
        public string Layout { get; }
    }
}
