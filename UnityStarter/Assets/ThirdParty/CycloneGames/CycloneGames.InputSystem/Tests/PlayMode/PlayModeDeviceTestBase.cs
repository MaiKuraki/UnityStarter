using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.PlayMode
{
    /// <summary>
    /// Hides host devices for the duration of a test so synthetic devices are the only claimable candidates,
    /// then restores them.
    /// </summary>
    public abstract class PlayModeDeviceTestBase
    {
        private List<InputDevice> _hostDevices;

        [SetUp]
        public void HideHostDevices()
        {
            _hostDevices = new List<InputDevice>(UIS.devices.Count);
            for (int i = 0; i < UIS.devices.Count; i++)
            {
                _hostDevices.Add(UIS.devices[i]);
            }

            for (int i = _hostDevices.Count - 1; i >= 0; i--)
            {
                InputDevice device = _hostDevices[i];
                if (device != null && device.added)
                {
                    UIS.RemoveDevice(device);
                }
            }
        }

        [TearDown]
        public void RestoreHostDevices()
        {
            List<InputDevice> hostDevices = _hostDevices;
            _hostDevices = null;
            if (hostDevices == null) return;

            for (int i = 0; i < hostDevices.Count; i++)
            {
                InputDevice device = hostDevices[i];
                if (device != null && !device.added)
                {
                    UIS.AddDevice(device);
                }
            }
        }
    }
}
