using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// A single local player with a gamepad and keyboard/mouse attached must receive input from all of them.
    /// The default selection path locks onto one control scheme and therefore pairs one device group; enabling
    /// <c>pairAllMatchingDevices</c> pairs every device the configuration references so all bindings stay live.
    /// </summary>
    public sealed class InputMixedDeviceTests : InputDeviceTestBase
    {
        private const string SchemeConfiguration = @"
schemaVersion: 1
schemaFingerprint: """"
playerSlots:
  - playerId: 0
    pairAllMatchingDevices: {0}
    controlSchemes:
      - name: KeyboardMouse
        bindingGroup: KeyboardMouse
        deviceRequirements:
          - controlPath: ""<Keyboard>""
            isOptional: false
            isOr: false
      - name: Gamepad
        bindingGroup: Gamepad
        deviceRequirements:
          - controlPath: ""<Gamepad>""
            isOptional: false
            isOr: false
    contexts:
      - name: Gameplay
        actionMap: PlayerActions
        priority: 0
        blocksLowerPriority: true
        bindings:
          - type: Button
            action: Confirm
            expectedControlType: Button
            bindingGroups: """"
            deviceBindings:
              - ""<Keyboard>/enter""
              - ""<Gamepad>/buttonSouth""
              - ""<Mouse>/leftButton""
            compositeBindings: []
            updateMode: EventDriven
            longPressMs: 0
            longPressValueThreshold: 0.5
";

        [Test]
        public void Configuration_PairAllMatchingDevicesSurvivesValidation()
        {
            string yaml = string.Format(SchemeConfiguration, "true");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(yaml);
            InputConfiguration parsed = VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(bytes);

            Assert.That(parsed.PlayerSlots, Is.Not.Null);
            Assert.That(
                parsed.PlayerSlots[0].PairAllMatchingDevices,
                Is.True,
                "pairAllMatchingDevices did not survive YAML deserialization.");
        }

        [Test]
        public void SelectDevices_PairAllMatching_PicksEveryReferencedDevice()
        {
            string yaml = string.Format(SchemeConfiguration, "true");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(yaml);
            InputConfiguration parsed = VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(bytes);
            InputConfigurationValidationResult validation =
                InputConfigurationValidator.ValidateAndPrepare(parsed, null);
            Assert.That(validation.IsValid, Is.True);

            RuntimePlayerSlotConfig config = validation.RuntimeConfiguration.PlayerSlots[0];
            Assert.That(config.PairAllMatchingDevices, Is.True, "The runtime configuration dropped the flag.");

            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            Mouse mouse = UIS.AddDevice<Mouse>();
            Gamepad gamepad = UIS.AddDevice<Gamepad>();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(yaml).IsSuccess, Is.True);

                bool selectedOk = manager.TrySelectDevices(
                    config,
                    null,
                    out List<InputDevice> devices,
                    out string scheme);

                var names = new List<string>();
                if (devices != null)
                {
                    for (int i = 0; i < devices.Count; i++) names.Add(devices[i].displayName);
                }

                Assert.That(selectedOk, Is.True, "Selection failed.");
                Assert.That(
                    devices.Count,
                    Is.GreaterThanOrEqualTo(3),
                    "Only " + devices.Count + " device(s) selected: " + string.Join(", ", names) +
                    "; scheme=" + (scheme ?? "<null>"));
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
        public void Join_WithoutPairAllMatching_PairsOneDeviceGroup()
        {
            PairedDevices paired = JoinAndCollect(false);

            Assert.That(
                paired.Count,
                Is.LessThan(3),
                "The default path pairs one control scheme group, not every device. Paired: " + paired.Summary);
        }

        [Test]
        public void Join_WithPairAllMatching_PairsEveryReferencedDevice()
        {
            PairedDevices paired = JoinAndCollect(true);

            Assert.That(paired.HasKeyboard, Is.True, "Keyboard must be paired. " + paired.Summary);
            Assert.That(paired.HasGamepad, Is.True, "Gamepad must be paired. " + paired.Summary);
            Assert.That(paired.HasMouse, Is.True, "Mouse must be paired. " + paired.Summary);
        }

        private static PairedDevices JoinAndCollect(bool pairAllMatchingDevices)
        {
            Keyboard keyboard = UIS.AddDevice<Keyboard>();
            Mouse mouse = UIS.AddDevice<Mouse>();
            Gamepad gamepad = UIS.AddDevice<Gamepad>();
            var manager = new InputManager();
            try
            {
                string yaml = string.Format(
                    SchemeConfiguration,
                    pairAllMatchingDevices ? "true" : "false");

                InputManagerInitializationResult initialization = manager.InitializeWithResult(yaml);
                Assert.That(initialization.IsSuccess, Is.True, initialization.Message);

                IInputPlayer player = manager.JoinSinglePlayer(0);
                Assert.That(player, Is.Not.Null, "The player must join with the declared configuration.");

                var concrete = player as InputPlayer;
                Assert.That(concrete, Is.Not.Null);

                var devices = concrete.User.pairedDevices;
                var result = new PairedDevices();
                var names = new List<string>(devices.Count);
                for (int i = 0; i < devices.Count; i++)
                {
                    names.Add(devices[i].displayName);
                    if (devices[i] is Keyboard) result.HasKeyboard = true;
                    if (devices[i] is Gamepad) result.HasGamepad = true;
                    if (devices[i] is Mouse) result.HasMouse = true;
                }

                result.Count = devices.Count;
                result.Summary = string.Join(", ", names);
                return result;
            }
            finally
            {
                manager.Dispose();
                UIS.RemoveDevice(gamepad);
                UIS.RemoveDevice(mouse);
                UIS.RemoveDevice(keyboard);
            }
        }

        private sealed class PairedDevices
        {
            internal bool HasKeyboard;
            internal bool HasGamepad;
            internal bool HasMouse;
            internal int Count;
            internal string Summary = string.Empty;
        }
    }
}
