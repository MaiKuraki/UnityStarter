using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Ownership boundary for <see cref="InputPlayer.NotifyDeviceActivity(InputDevice)"/>: the explicit hook
    /// must apply the same device whitelist as the automatic <c>onAnyButtonPress</c> path.
    /// </summary>
    public sealed class InputActivityIsolationTests : InputDeviceTestBase
    {
        private const string TwoLockedSlotsYaml = @"
schemaVersion: 1
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: KeyboardMouse
        bindingGroup: KM
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
          - controlPath: ""<Mouse>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Map0
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: KM
            deviceBindings:
              - ""<Keyboard>/space""
              - ""<Mouse>/leftButton""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
  - playerId: 1
    controlSchemes:
      - name: Gamepad
        bindingGroup: GP
        deviceRequirements:
          - controlPath: ""<Gamepad>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Map1
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: GP
            deviceBindings:
              - ""<Gamepad>/buttonSouth""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
";

        [Test]
        public void ExplicitHook_IgnoresDeviceOwnedByAnotherPlayer()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>("IsoK");
            Mouse mouse = UIS.AddDevice<Mouse>("IsoM");
            Gamepad gamepad = UIS.AddDevice<Gamepad>("IsoG");
            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult init = manager.InitializeWithResult(TwoLockedSlotsYaml);
                Assert.That(init.IsSuccess, Is.True, init.Message);

                var first = manager.JoinSinglePlayer(0) as InputPlayer;
                var second = manager.JoinSinglePlayer(1) as InputPlayer;
                Assert.That(first, Is.Not.Null, "Player 0 must take keyboard and mouse.");
                Assert.That(second, Is.Not.Null, "Player 1 must take the gamepad.");

                Assert.That(second.User.pairedDevices.Count, Is.EqualTo(1));
                InputDevice foreign = second.User.pairedDevices[0];
                Assert.That(foreign, Is.SameAs(gamepad), "Precondition: the foreign device is player 1's gamepad.");

                first.UpdateRuntimeState(0d);
                InputDeviceKind before = first.ActiveDeviceKind.CurrentValue;
                Assert.That(
                    before,
                    Is.EqualTo(InputDeviceKind.KeyboardMouse),
                    "Precondition: player 0 starts on its own keyboard/mouse set.");

                first.NotifyDeviceActivity(foreign);
                first.UpdateRuntimeState(1d);

                Assert.That(
                    first.ActiveDeviceKind.CurrentValue,
                    Is.EqualTo(before),
                    "A device owned by another local player must not change this player's ActiveDeviceKind.");
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(gamepad);
                UIS.RemoveDevice(mouse);
                UIS.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void ExplicitHook_AcceptsOwnDevice()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>("OwnK");
            Mouse mouse = UIS.AddDevice<Mouse>("OwnM");
            Gamepad gamepad = UIS.AddDevice<Gamepad>("OwnG");
            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult init = manager.InitializeWithResult(TwoLockedSlotsYaml);
                Assert.That(init.IsSuccess, Is.True, init.Message);

                var first = manager.JoinSinglePlayer(0) as InputPlayer;
                var second = manager.JoinSinglePlayer(1) as InputPlayer;
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);

                InputDevice own = first.User.pairedDevices[0];
                first.UpdateRuntimeState(0d);

                first.NotifyDeviceActivity(own);
                first.UpdateRuntimeState(1d);

                Assert.That(
                    first.ActiveDeviceKind.CurrentValue,
                    Is.EqualTo(InputDeviceKind.KeyboardMouse),
                    "A device the player actually owns must still drive its own kind.");
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(gamepad);
                UIS.RemoveDevice(mouse);
                UIS.RemoveDevice(keyboard);
            }
        }
    }
}
