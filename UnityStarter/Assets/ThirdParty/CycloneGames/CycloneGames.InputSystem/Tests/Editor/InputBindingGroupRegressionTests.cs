using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Regression coverage for per-binding control-scheme groups. The decisive property is that the
    /// control-scheme binding mask becomes an independent second line of defence: even when the InputUser
    /// device whitelist is widened to include a gamepad, a KeyboardMouse scheme must not resolve any
    /// gamepad control on a binding that declares only the Gamepad group.
    /// </summary>
    public sealed class InputBindingGroupRegressionTests
    {
        private const string LockedFixture = "input_config.txt";

        private readonly List<InputDevice> _injectedDevices = new List<InputDevice>();

        /// <summary>
        /// EditMode can run headless (-batchmode), where Unity creates no keyboard or mouse. The
        /// KeyboardMouse scheme in these fixtures requires both as non-optional devices, so the test
        /// creates them explicitly; in the interactive editor this is a no-op.
        /// </summary>
        private void EnsureSchemeDevices()
        {
            if (Keyboard.current == null) _injectedDevices.Add(UIS.AddDevice<Keyboard>("HermeticKeyboard"));
            if (Mouse.current == null) _injectedDevices.Add(UIS.AddDevice<Mouse>("HermeticMouse"));
        }

        private void ReleaseSchemeDevices()
        {
            for (int i = 0; i < _injectedDevices.Count; i++)
            {
                InputDevice device = _injectedDevices[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }

            _injectedDevices.Clear();
        }

        private static string Fixture(string name)
        {
            return Path.Combine(
                Application.dataPath,
                "ThirdParty", "CycloneGames", "CycloneGames.InputSystem", "Samples", "Fixtures", name);
        }

        private static InputActionAsset AssetOf(InputPlayer player)
        {
            FieldInfo field = typeof(InputPlayer).GetField(
                "_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(player) as InputActionAsset;
        }

        private static InputAction FindAction(InputActionAsset asset, string name)
        {
            for (int mapIndex = 0; mapIndex < asset.actionMaps.Count; mapIndex++)
            {
                var actions = asset.actionMaps[mapIndex].actions;
                for (int i = 0; i < actions.Count; i++)
                {
                    if (actions[i].name == name) return actions[i];
                }
            }

            return null;
        }

        private static int CountGamepadControls(InputActionAsset asset, string actionName)
        {
            InputAction action = FindAction(asset, actionName);
            Assert.That(action, Is.Not.Null, actionName);
            int count = 0;
            for (int i = 0; i < action.controls.Count; i++)
            {
                if (action.controls[i].device is Gamepad) count++;
            }

            return count;
        }

        [Test]
        public void PerBindingGroups_MaskBlocksGamepadWhenWhitelistOpens()
        {
            var pad = UIS.AddDevice<Gamepad>("MaskPad");
            EnsureSchemeDevices();
            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult init =
                    manager.InitializeWithResult(File.ReadAllText(Fixture(LockedFixture)));
                Assert.That(init.IsSuccess, Is.True, init.Message);

                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                InputActionAsset asset = AssetOf(player);

                Assert.That(CountGamepadControls(asset, "Confirm"), Is.Zero);
                Assert.That(FindAction(asset, "Confirm").bindings[0].groups, Is.EqualTo("Gamepad"));
                Assert.That(FindAction(asset, "JoinGame").bindings[1].groups, Is.EqualTo("Gamepad"));

                Assert.That(manager.PairAdditionalDevice(0, pad), Is.True,
                    "The whitelist must widen to include the gamepad for this test.");
                Assert.That(CountGamepadControls(asset, "Confirm"), Is.Zero,
                    "The KeyboardMouse binding mask must independently block the Gamepad-group binding.");
                Assert.That(CountGamepadControls(asset, "JoinGame"), Is.Zero);
                Assert.That(CountGamepadControls(asset, "Move"), Is.Zero);
            }
            finally
            {
                manager.Dispose();
                if (pad.added) UIS.RemoveDevice(pad);
                ReleaseSchemeDevices();
            }
        }

        [Test]
        public void LegacyConfig_WithoutPerBindingGroups_KeepsExactlyTheOldBehaviour()
        {
            // Without per-binding groups, action-level mixed groups still let a gamepad binding
            // through the KeyboardMouse mask.
            var pad = UIS.AddDevice<Gamepad>("LegacyPad");
            EnsureSchemeDevices();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(SingleSlotYaml(null)).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                InputActionAsset asset = AssetOf(player);

                Assert.That(FindAction(asset, "Confirm").bindings[0].groups,
                    Is.EqualTo("KeyboardMouse;Gamepad"));
                Assert.That(manager.PairAdditionalDevice(0, pad), Is.True);
                Assert.That(CountGamepadControls(asset, "Confirm"), Is.EqualTo(1),
                    "Configurations without per-binding groups must retain the action-level groups.");
            }
            finally
            {
                manager.Dispose();
                if (pad.added) UIS.RemoveDevice(pad);
                ReleaseSchemeDevices();
            }
        }

        [Test]
        public void PerBindingGroups_AreAppliedByIndex()
        {
            EnsureSchemeDevices();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(
                    SingleSlotYaml(new[] { "Gamepad", "KeyboardMouse" })).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                InputAction confirm = FindAction(AssetOf(player), "Confirm");
                Assert.That(confirm.bindings[0].groups, Is.EqualTo("Gamepad"));
                Assert.That(confirm.bindings[1].groups, Is.EqualTo("KeyboardMouse"));
            }
            finally
            {
                manager.Dispose();
                ReleaseSchemeDevices();
            }
        }

        [Test]
        public void EmptyPerBindingGroup_FallsBackToActionLevelGroups()
        {
            EnsureSchemeDevices();
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(
                    SingleSlotYaml(new[] { "Gamepad", string.Empty })).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                InputAction confirm = FindAction(AssetOf(player), "Confirm");
                Assert.That(confirm.bindings[0].groups, Is.EqualTo("Gamepad"));
                Assert.That(confirm.bindings[1].groups, Is.EqualTo("KeyboardMouse;Gamepad"));
            }
            finally
            {
                manager.Dispose();
                ReleaseSchemeDevices();
            }
        }

        [Test]
        public void LengthMismatch_IsRejected()
        {
            InputConfiguration parsed = VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(
                Encoding.UTF8.GetBytes(SingleSlotYaml(new[] { "Gamepad" })));
            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(parsed, null);
            Assert.That(result.IsValid, Is.False);
            Assert.That(HasIssueContaining(result, "deviceBindingGroups"), Is.True);
        }

        [Test]
        public void UndeclaredGroup_IsRejected()
        {
            InputConfiguration parsed = VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(
                Encoding.UTF8.GetBytes(SingleSlotYaml(new[] { "NonExistentScheme", "KeyboardMouse" })));
            InputConfigurationValidationResult result =
                InputConfigurationValidator.ValidateAndPrepare(parsed, null);
            Assert.That(result.IsValid, Is.False);
            Assert.That(HasIssueContaining(result, "does not match a declared"), Is.True);
        }

        [Test]
        public void NewSchemaKey_IsAcceptedByYamlPreflight()
        {
            Assert.That(
                InputConfigurationYamlPreflight.TryValidate(SingleSlotYaml(new[] { "Gamepad", "KeyboardMouse" }),
                    out string error),
                Is.True, error);
        }

        private static bool HasIssueContaining(InputConfigurationValidationResult result, string fragment)
        {
            for (int i = 0; i < result.Issues.Count; i++)
            {
                if (result.Issues[i].Message != null &&
                    result.Issues[i].Message.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Builds a minimal single-slot locked configuration. A null groupEntries omits the key entirely.
        /// </summary>
        private static string SingleSlotYaml(IReadOnlyList<string> groupEntries)
        {
            var b = new StringBuilder(1400);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    controlSchemes:");
            b.AppendLine("      - name: KeyboardMouse");
            b.AppendLine("        bindingGroup: KeyboardMouse");
            b.AppendLine("        deviceRequirements:");
            b.AppendLine("          - controlPath: \"<Keyboard>\"");
            b.AppendLine("            isOptional: false");
            b.AppendLine("            isOr: false");
            b.AppendLine("          - controlPath: \"<Mouse>\"");
            b.AppendLine("            isOptional: false");
            b.AppendLine("            isOr: false");
            b.AppendLine("      - name: Gamepad");
            b.AppendLine("        bindingGroup: Gamepad");
            b.AppendLine("        deviceRequirements:");
            b.AppendLine("          - controlPath: \"<Gamepad>\"");
            b.AppendLine("            isOptional: false");
            b.AppendLine("            isOr: false");
            b.AppendLine("    contexts:");
            b.AppendLine("      - name: Gameplay");
            b.AppendLine("        actionMap: Map0");
            b.AppendLine("        priority: 0");
            b.AppendLine("        blocksLowerPriority: true");
            b.AppendLine("        bindings:");
            b.AppendLine("          - type: Button");
            b.AppendLine("            action: Confirm");
            b.AppendLine("            expectedControlType: Button");
            b.AppendLine("            bindingGroups: KeyboardMouse;Gamepad");
            b.AppendLine("            deviceBindings:");
            b.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            b.AppendLine("              - \"<Keyboard>/space\"");
            if (groupEntries != null)
            {
                b.AppendLine("            deviceBindingGroups:");
                for (int i = 0; i < groupEntries.Count; i++)
                {
                    b.Append("              - ");
                    b.AppendLine(string.IsNullOrEmpty(groupEntries[i])
                        ? "\"\""
                        : "\"" + groupEntries[i] + "\"");
                }
            }

            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: 0");
            b.AppendLine("            longPressValueThreshold: 0.5");
            return b.ToString();
        }
    }
}
