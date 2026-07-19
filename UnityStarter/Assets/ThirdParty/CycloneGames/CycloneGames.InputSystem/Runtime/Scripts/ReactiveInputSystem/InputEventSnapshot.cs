using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Safe value copy of InputEventPtr metadata. It never retains the borrowed native event pointer.
    /// </summary>
    public readonly struct InputEventSnapshot
    {
        internal InputEventSnapshot(InputEventPtr source)
        {
            DeviceId = source.deviceId;
            Time = source.time;
            Type = source.type;
            SizeInBytes = source.sizeInBytes;
            Handled = source.handled;
        }

        public int DeviceId { get; }
        public double Time { get; }
        public FourCC Type { get; }
        public uint SizeInBytes { get; }
        public bool Handled { get; }
    }

    public readonly struct UnpairedDeviceUseSnapshot
    {
        internal UnpairedDeviceUseSnapshot(InputControl control, InputEventPtr source)
        {
            Control = control;
            Event = new InputEventSnapshot(source);
        }

        public InputControl Control { get; }
        public InputEventSnapshot Event { get; }
    }
}
