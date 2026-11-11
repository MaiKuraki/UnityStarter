using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Baseline for local multiplayer device assignment. These cases record what the current selection path does
    /// when several players compete for the attached devices, which is the behaviour any change to single-player
    /// device pairing must not disturb.
    /// </summary>
    public sealed class InputLocalMultiplayerTests
    {
        private const string KeyboardAndMousePlayers = @"
schemaVersion: 1
schemaFingerprint: """"
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: KeyboardOnly
        bindingGroup: Keyboard
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player0
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: Keyboard
            deviceBindings:
              - ""<Keyboard>/enter""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
  - playerId: 1
    controlSchemes:
      - name: MouseOnly
        bindingGroup: Mouse
        deviceRequirements:
          - controlPath: ""<Mouse>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player1
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: Mouse
            deviceBindings:
              - ""<Mouse>/leftButton""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
";

        private const string KeyboardAndGamepadPlayers = @"
schemaVersion: 1
schemaFingerprint: """"
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: KeyboardOnly
        bindingGroup: Keyboard
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player0
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: Keyboard
            deviceBindings:
              - ""<Keyboard>/enter""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
  - playerId: 1
    controlSchemes:
      - name: GamepadOnly
        bindingGroup: Gamepad
        deviceRequirements:
          - controlPath: ""<Gamepad>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player1
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: Gamepad
            deviceBindings:
              - ""<Gamepad>/buttonSouth""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
";

        private const string SplitKeyboardPlayers = @"
schemaVersion: 1
schemaFingerprint: """"
playerSlots:
  - playerId: 0
    controlSchemes:
      - name: LeftHalf
        bindingGroup: LeftHalf
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player0
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: LeftHalf
            deviceBindings:
              - ""<Keyboard>/q""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
  - playerId: 1
    controlSchemes:
      - name: RightHalf
        bindingGroup: RightHalf
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: Player1
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: RightHalf
            deviceBindings:
              - ""<Keyboard>/p""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
";

        [Test]
        public void TwoPlayers_KeyboardAndMouse_EachPairsItsOwnDevice()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            Mouse mouse = UIS.AddDevice<Mouse>();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(KeyboardAndMousePlayers).IsSuccess, Is.True);

                IInputPlayer first = manager.JoinSinglePlayer(0);
                IInputPlayer second = manager.JoinSinglePlayer(1);

                Assert.That(first, Is.Not.Null, "Player 0 must join with the keyboard.");
                Assert.That(second, Is.Not.Null, "Player 1 must join with the mouse.");

                Assert.That(PairedSummary(first), Does.Contain("Keyboard"), "Player 0 pairing: " + PairedSummary(first));
                Assert.That(PairedSummary(second), Does.Contain("Mouse"), "Player 1 pairing: " + PairedSummary(second));
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(mouse);
                UIS.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void TwoPlayers_KeyboardAndGamepad_EachPairsItsOwnDevice()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            Gamepad gamepad = UIS.AddDevice<Gamepad>();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(KeyboardAndGamepadPlayers).IsSuccess, Is.True);

                IInputPlayer first = manager.JoinSinglePlayer(0);
                IInputPlayer second = manager.JoinSinglePlayer(1);

                Assert.That(first, Is.Not.Null, "Player 0 must join with the keyboard.");
                Assert.That(second, Is.Not.Null, "Player 1 must join with the gamepad.");

                Assert.That(PairedSummary(first), Does.Contain("Keyboard"), "Player 0 pairing: " + PairedSummary(first));
                Assert.That(PairedSummary(second), Does.Contain("Gamepad"), "Player 1 pairing: " + PairedSummary(second));
                Assert.That(
                    PairedSummary(first),
                    Does.Not.Contain("Gamepad"),
                    "The keyboard player must not claim the gamepad. Pairing: " + PairedSummary(first));
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(gamepad);
                UIS.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void TwoPlayers_SameKeyboard_SecondPlayerJoinsOrIsRejected()
        {
            // The host may already have a physical keyboard, so counting the devices before adding a test
            // one is required for a reproducible assertion. The invariant under test is that a single
            // physical device is never paired to two players, not that a second player is always refused.
            int hostKeyboardCount = 0;
            var attached = UIS.devices;
            for (int i = 0; i < attached.Count; i++)
            {
                if (attached[i] is Keyboard) hostKeyboardCount++;
            }

            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(SplitKeyboardPlayers).IsSuccess, Is.True);

                IInputPlayer first = manager.JoinSinglePlayer(0);
                IInputPlayer second = manager.JoinSinglePlayer(1);

                Assert.That(first, Is.Not.Null, "Player 0 must claim a keyboard.");
                if (hostKeyboardCount + 1 < 2)
                {
                    Assert.That(
                        second,
                        Is.Null,
                        "Two players cannot share one physical keyboard; the second join must be refused. " +
                        "Second pairing: " + (second == null ? "null" : PairedSummary(second)));
                    return;
                }

                Assert.That(second, Is.Not.Null, "A second physical keyboard may host player 1.");
                var firstDevices = ((InputPlayer)first).User.pairedDevices;
                var secondDevices = ((InputPlayer)second).User.pairedDevices;
                for (int i = 0; i < firstDevices.Count; i++)
                {
                    for (int j = 0; j < secondDevices.Count; j++)
                    {
                        Assert.That(
                            ReferenceEquals(firstDevices[i], secondDevices[j]),
                            Is.False,
                            "A physical device must not be paired to two players: " + firstDevices[i].displayName);
                    }
                }
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(keyboard);
            }
        }

        [Test]
        public void TwoPlayers_SharedKeyboardViaExplicitSharedJoin()
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(SplitKeyboardPlayers).IsSuccess, Is.True);

                IInputPlayer first = manager.JoinPlayerOnSharedDevice(0);
                IInputPlayer second = manager.JoinPlayerOnSharedDevice(1);

                Assert.That(first, Is.Not.Null, "Player 0 must join on the shared keyboard.");
                Assert.That(
                    second,
                    Is.Not.Null,
                    "Player 1 must join on the same keyboard; the two players only differ by bound keys.");

                Assert.That(PairedSummary(first), Does.Contain("Keyboard"), "Player 0 pairing: " + PairedSummary(first));
                Assert.That(PairedSummary(second), Does.Contain("Keyboard"), "Player 1 pairing: " + PairedSummary(second));
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(keyboard);
            }
        }

        private static string PairedSummary(IInputPlayer player)
        {
            if (player is not InputPlayer concrete) return "<not InputPlayer>";
            var paired = concrete.User.pairedDevices;
            var names = new List<string>(paired.Count);
            for (int i = 0; i < paired.Count; i++) names.Add(paired[i].displayName);
            return string.Join(", ", names);
        }
    }
}
