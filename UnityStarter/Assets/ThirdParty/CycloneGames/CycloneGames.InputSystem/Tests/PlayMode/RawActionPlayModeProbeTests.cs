using System.Collections;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.PlayMode
{
    /// <summary>
    /// Confirms that a queued device state reaches action callbacks inside a real player loop. The same experiment
    /// in EditMode produces no callback, which makes EditMode unusable for end-to-end input assertions.
    /// </summary>
    public sealed class RawActionPlayModeProbeTests
    {
        [UnityTest]
        [Explicit("Requires a host where queued device state reaches action callbacks; batchmode does not.")]
        public IEnumerator RawAction_FiresFromQueuedKeyboardState()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            var action = new InputAction("probe", InputActionType.Button, "<Keyboard>/space");
            int fired = 0;
            action.performed += _ => fired++;
            try
            {
                action.Enable();
                yield return null;

                UIS.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                yield return null;
                yield return null;

                Assert.That(
                    fired,
                    Is.GreaterThan(0),
                    "A plain Unity action bound to <Keyboard>/space did not fire in the player loop.");
            }
            finally
            {
                action.Disable();
                action.Dispose();
                UIS.RemoveDevice(keyboard);
            }
        }
    }
}
