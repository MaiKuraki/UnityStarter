using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Regression coverage for device ownership, the locked-versus-mixed distinction, local-multiplayer
    /// isolation, device-switch debounce and the new configuration fields. Every assertion reads the
    /// synchronous Input System graph state, so it is deterministic in EditMode where callbacks do not fire.
    /// </summary>
    public sealed class InputDevicePolicyRegressionTests : InputDeviceTestBase
    {
        private const string LockedFixture = "input_config.txt";
        private const string MixedFixture = "input_config_mixed.txt";

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

        private static string DeviceFilter(InputPlayer player)
        {
            InputActionAsset asset = AssetOf(player);
            if (asset == null) return "<missing>";
            if (!asset.devices.HasValue) return "<unbounded>";
            var filter = asset.devices.Value;
            var layouts = new List<string>(filter.Count);
            for (int i = 0; i < filter.Count; i++) layouts.Add(filter[i].layout);
            return layouts.Count == 0 ? "<empty>" : string.Join("|", layouts);
        }

        private static int CountControls<TDevice>(InputPlayer player) where TDevice : InputDevice
        {
            InputActionAsset asset = AssetOf(player);
            if (asset == null) return 0;
            int count = 0;
            for (int mapIndex = 0; mapIndex < asset.actionMaps.Count; mapIndex++)
            {
                InputActionMap map = asset.actionMaps[mapIndex];
                for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
                {
                    var controls = map.actions[actionIndex].controls;
                    for (int i = 0; i < controls.Count; i++)
                    {
                        if (controls[i].device is TDevice) count++;
                    }
                }
            }

            return count;
        }

        private static int CountPaired<TDevice>(InputPlayer player) where TDevice : InputDevice
        {
            var paired = player.User.pairedDevices;
            int count = 0;
            for (int i = 0; i < paired.Count; i++)
            {
                if (paired[i] is TDevice) count++;
            }

            return count;
        }

        private static List<InputDevice> Attach(string spec, string prefix)
        {
            var devices = new List<InputDevice>(spec.Length);
            for (int i = 0; i < spec.Length; i++)
            {
                switch (spec[i])
                {
                    case 'K': devices.Add(UIS.AddDevice<Keyboard>(prefix + "K" + i)); break;
                    case 'M': devices.Add(UIS.AddDevice<Mouse>(prefix + "M" + i)); break;
                    case 'G': devices.Add(UIS.AddDevice<Gamepad>(prefix + "G" + i)); break;
                }
            }

            return devices;
        }

        private static void Detach(List<InputDevice> devices)
        {
            for (int i = devices.Count - 1; i >= 0; i--)
            {
                if (devices[i] != null && devices[i].added) UIS.RemoveDevice(devices[i]);
            }

            devices.Clear();
        }

        [Test]
        public void LockedAndMixed_HaveDifferentDeviceReachability()
        {
            var devices = Attach("KMG", "Reach");
            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult lockedInit =
                    manager.InitializeWithResult(File.ReadAllText(Fixture(LockedFixture)));
                Assert.That(lockedInit.IsSuccess, Is.True, lockedInit.Message);
                var locked = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(locked, Is.Not.Null);
                Assert.That(CountControls<Gamepad>(locked), Is.Zero,
                    "A locked slot must not resolve any gamepad control.");
                Assert.That(CountPaired<Gamepad>(locked), Is.Zero);
            }
            finally
            {
                manager.Dispose();
            }

            manager = new InputManager();
            try
            {
                InputManagerInitializationResult mixedInit =
                    manager.InitializeWithResult(File.ReadAllText(Fixture(MixedFixture)));
                Assert.That(mixedInit.IsSuccess, Is.True, mixedInit.Message);
                var mixed = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(mixed, Is.Not.Null);
                Assert.That(CountControls<Gamepad>(mixed), Is.GreaterThan(0),
                    "A mixed slot must keep the gamepad bindings live.");
                Assert.That(CountPaired<Gamepad>(mixed), Is.GreaterThan(0));
                Assert.That(DeviceFilter(mixed), Is.Not.EqualTo("<unbounded>"),
                    "A mixed slot must still be confined to its own device whitelist.");
                Assert.That(DeviceFilter(mixed), Is.EqualTo(string.Join("|", PairedLayouts(mixed))),
                    "The action asset filter must equal the player's paired devices.");
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        private static List<string> PairedLayouts(InputPlayer player)
        {
            var paired = player.User.pairedDevices;
            var layouts = new List<string>(paired.Count);
            for (int i = 0; i < paired.Count; i++) layouts.Add(paired[i].layout);
            return layouts;
        }

        [Test]
        public void Mixed_RespectsMaxDevicesPerLayout()
        {
            var devices = Attach("KKMMGG", "Cap");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(File.ReadAllText(Fixture(MixedFixture))).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                Assert.That(CountPaired<Keyboard>(player), Is.LessThanOrEqualTo(1));
                Assert.That(CountPaired<Mouse>(player), Is.LessThanOrEqualTo(1));
                Assert.That(CountPaired<Gamepad>(player), Is.LessThanOrEqualTo(1));
                Assert.That(player.User.pairedDevices.Count, Is.EqualTo(3),
                    "One device per referenced layout should be claimed, leaving spares for other players.");
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void Mixed_LeavesSpareGamepadForSecondPlayer()
        {
            string yaml = TwoSlotYaml();
            var devices = Attach("KMGG", "Share");
            var manager = new InputManager();
            try
            {
                InputManagerInitializationResult init = manager.InitializeWithResult(yaml);
                Assert.That(init.IsSuccess, Is.True, init.Message);
                var first = manager.JoinSinglePlayer(0) as InputPlayer;
                var second = manager.JoinSinglePlayer(1) as InputPlayer;
                Assert.That(first, Is.Not.Null, "Player 0 must join with the mixed slot.");
                Assert.That(second, Is.Not.Null, "Player 1 must be able to claim the spare gamepad.");
                Assert.That(CountPaired<Gamepad>(first), Is.EqualTo(1));
                Assert.That(CountPaired<Gamepad>(second), Is.EqualTo(1));
                Assert.That(DeviceFilter(first), Is.Not.EqualTo("<unbounded>"));
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(2));
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void Debounce_RapidAlternationCollapsesToOneSwitchPerDwell()
        {
            string yaml = SingleSlotYaml(debounceMs: 120);
            var devices = Attach("KG", "Pulse");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(yaml).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);

                int emits = 0;
                IDisposable subscription = player.ActiveDeviceKind.Subscribe(_ => emits++);
                int baseline = emits;

                player.UpdateRuntimeState(0d);
                player.NotifyDeviceActivity(devices[0]);
                Assert.That(emits - baseline, Is.LessThanOrEqualTo(1), "The first switch must not be delayed.");

                // End on the gamepad so a pending switch is left for the dwell-expiry flush.
                for (int i = 0; i <= 40; i++)
                {
                    player.NotifyDeviceActivity(i % 2 == 0 ? devices[1] : devices[0]);
                    player.UpdateRuntimeState(0.001d * (i + 1));
                }

                int duringBurst = emits - baseline;
                Assert.That(duringBurst, Is.LessThanOrEqualTo(2),
                    "Forty-one alternating pulses inside the dwell window must not produce forty-one UI updates. Actual=" + duringBurst);

                player.UpdateRuntimeState(1d);
                Assert.That(emits - baseline, Is.LessThanOrEqualTo(3),
                    "A pending switch is applied at most once when the dwell expires.");
                Assert.That(player.ActiveDeviceKind.CurrentValue, Is.EqualTo(InputDeviceKind.Gamepad),
                    "The dwell-expiry flush must apply the last requested kind.");
                subscription.Dispose();
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void LegacyConfiguration_KeepsImmediateSwitching()
        {
            string yaml = SingleSlotYaml(debounceMs: 0);
            var devices = Attach("KG", "Legacy");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(yaml).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);

                int changes = 0;
                IDisposable subscription = player.ActiveDeviceKind.Subscribe(_ => changes++);
                int baseline = changes;
                for (int i = 0; i < 8; i++)
                {
                    player.NotifyDeviceActivity(i % 2 == 0 ? devices[1] : devices[0]);
                }

                Assert.That(changes - baseline, Is.GreaterThanOrEqualTo(4),
                    "Zero debounce must preserve the legacy immediate-switch behaviour.");
                subscription.Dispose();
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void PairAdditionalDevice_IsExplicitAndDoesNotCreatePlayers()
        {
            var devices = Attach("KMG", "Extra");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(File.ReadAllText(Fixture(LockedFixture))).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                Assert.That(CountPaired<Gamepad>(player), Is.Zero);

                Assert.That(manager.PairAdditionalDevice(0, devices[2]), Is.True,
                    "An explicitly paired gamepad must be accepted for a declared layout.");
                Assert.That(CountPaired<Gamepad>(player), Is.EqualTo(1));
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(1),
                    "Pairing a device must never create another player.");
                Assert.That(manager.PairAdditionalDevice(0, devices[2]), Is.False,
                    "Pairing the same device twice must fail closed.");
                Assert.That(manager.PairAdditionalDevice(99, devices[2]), Is.False);
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void NewConfigurationFields_SurviveValidationAndAreClamped()
        {
            string yaml = SingleSlotYaml(debounceMs: 999999, maxPerLayout: 999999);
            byte[] bytes = Encoding.UTF8.GetBytes(yaml);
            InputConfiguration parsed = VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(bytes);
            Assert.That(parsed.PlayerSlots[0].MaxDevicesPerLayout, Is.EqualTo(999999));
            Assert.That(parsed.PlayerSlots[0].DeviceSwitchDebounceMs, Is.EqualTo(999999));

            InputConfigurationValidationResult validation =
                InputConfigurationValidator.ValidateAndPrepare(parsed, null);
            Assert.That(validation.IsValid, Is.True);
            RuntimePlayerSlotConfig runtime = validation.RuntimeConfiguration.PlayerSlots[0];
            Assert.That(runtime.MaxDevicesPerLayout, Is.EqualTo(8), "Hostile per-layout values must be clamped.");
            Assert.That(runtime.DeviceSwitchDebounceMs, Is.EqualTo(5000), "Hostile debounce values must be clamped.");

            string legacy = SingleSlotYaml(debounceMs: 0, maxPerLayout: 0);
            var legacyValidation = InputConfigurationValidator.ValidateAndPrepare(
                VYaml.Serialization.YamlSerializer.Deserialize<InputConfiguration>(Encoding.UTF8.GetBytes(legacy)), null);
            Assert.That(legacyValidation.IsValid, Is.True);
            Assert.That(legacyValidation.RuntimeConfiguration.PlayerSlots[0].MaxDevicesPerLayout, Is.EqualTo(1));
            Assert.That(legacyValidation.RuntimeConfiguration.PlayerSlots[0].DeviceSwitchDebounceMs, Is.Zero);
        }

        [Test]
        public void SampleUserKeys_AreModeSpecificAndValid()
        {
            var store = new FileInputConfigurationStore(Application.persistentDataPath);
            string[] modes =
            {
                "AutoJoinLockedSinglePlayer", "AutoJoinSharedKeyboard", "LobbyWithDeviceLocking",
                "LobbyWithSharedDevices", "AutoJoinMixedDevices", "AutoJoinKeyboardMouseVsGamepad"
            };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < modes.Length; i++)
            {
                string key = "user_input_settings_" + modes[i] + ".yaml";
                Assert.That(keys.Add(key), Is.True, "Each startup mode needs a distinct user key.");
                Assert.That(store.TryResolveKey(key, out string _, out string error), Is.True, error);
            }
        }

        private static string KeyboardMouseSlotYaml()
        {
            var b = new StringBuilder(1200);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    pairAllMatchingDevices: true");
            b.AppendLine("    maxDevicesPerLayout: 1");
            b.AppendLine("    contexts:");
            b.AppendLine("      - name: Gameplay");
            b.AppendLine("        actionMap: Map0");
            b.AppendLine("        priority: 0");
            b.AppendLine("        blocksLowerPriority: true");
            b.AppendLine("        bindings:");
            b.AppendLine("          - type: Button");
            b.AppendLine("            action: Confirm");
            b.AppendLine("            expectedControlType: Button");
            b.AppendLine("            bindingGroups: \"\"");
            b.AppendLine("            deviceBindings:");
            b.AppendLine("              - \"<Keyboard>/enter\"");
            b.AppendLine("              - \"<Mouse>/leftButton\"");
            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: 0");
            b.AppendLine("            longPressValueThreshold: 0.5");
            return b.ToString();
        }

        private static string SingleSlotYaml(int debounceMs, int maxPerLayout = 1)
        {
            var b = new StringBuilder(1200);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    pairAllMatchingDevices: true");
            b.AppendLine("    maxDevicesPerLayout: " + maxPerLayout);
            b.AppendLine("    deviceSwitchDebounceMs: " + debounceMs);
            b.AppendLine("    contexts:");
            b.AppendLine("      - name: Gameplay");
            b.AppendLine("        actionMap: Map0");
            b.AppendLine("        priority: 0");
            b.AppendLine("        blocksLowerPriority: true");
            b.AppendLine("        bindings:");
            b.AppendLine("          - type: Button");
            b.AppendLine("            action: Confirm");
            b.AppendLine("            expectedControlType: Button");
            b.AppendLine("            bindingGroups: \"\"");
            b.AppendLine("            deviceBindings:");
            b.AppendLine("              - \"<Keyboard>/enter\"");
            b.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: 0");
            b.AppendLine("            longPressValueThreshold: 0.5");
            return b.ToString();
        }

        private static string TwoSlotYaml()
        {
            var b = new StringBuilder(2400);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    pairAllMatchingDevices: true");
            b.AppendLine("    maxDevicesPerLayout: 1");
            AppendContext(b, "Map0", "\"<Keyboard>/enter\", \"<Mouse>/leftButton\", \"<Gamepad>/buttonSouth\"", "\"\"");
            b.AppendLine("  - playerId: 1");
            b.AppendLine("    controlSchemes:");
            b.AppendLine("      - name: Gamepad");
            b.AppendLine("        bindingGroup: GP");
            b.AppendLine("        deviceRequirements:");
            b.AppendLine("          - controlPath: \"<Gamepad>\"");
            b.AppendLine("            isOptional: false");
            b.AppendLine("            isOr: false");
            AppendContext(b, "Map1", "\"<Gamepad>/buttonSouth\"", "GP");
            return b.ToString();
        }

        private static void AppendContext(StringBuilder b, string map, string paths, string groups)
        {
            b.AppendLine("    contexts:");
            b.AppendLine("      - name: Gameplay");
            b.AppendLine("        actionMap: " + map);
            b.AppendLine("        priority: 0");
            b.AppendLine("        blocksLowerPriority: true");
            b.AppendLine("        bindings:");
            b.AppendLine("          - type: Button");
            b.AppendLine("            action: Confirm");
            b.AppendLine("            expectedControlType: Button");
            b.AppendLine("            bindingGroups: " + (groups.Length == 0 ? "\"\"" : groups));
            b.AppendLine("            deviceBindings:");
            string[] split = paths.Split(new[] { ", " }, StringSplitOptions.None);
            for (int i = 0; i < split.Length; i++) b.AppendLine("              - " + split[i]);
            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: 0");
            b.AppendLine("            longPressValueThreshold: 0.5");
        }

        // ------------------------------------------------------------------
        // Device activity stream: precise identity for prompt UI replacement.
        // ------------------------------------------------------------------
        [Test]
        public void DeviceActivity_CarriesFullIdentity()
        {
            var devices = Attach("K", "Identity");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(SingleSlotYaml(0)).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);
                player.UpdateRuntimeState(12.5d);

                var received = new List<InputDeviceActivity>();
                using IDisposable subscription = player.DeviceActivity.Subscribe(received.Add);
                var keyboard = (Keyboard)devices[0];
                player.NotifyDeviceActivity(keyboard, keyboard.spaceKey);

                Assert.That(received.Count, Is.EqualTo(1));
                InputDeviceActivity activity = received[0];
                Assert.That(activity.PlayerId, Is.EqualTo(0));
                Assert.That(activity.DeviceId, Is.EqualTo(keyboard.deviceId));
                Assert.That(activity.DeviceLayout, Is.EqualTo("Keyboard"));
                Assert.That(activity.Family, Is.EqualTo(InputDeviceFamily.Keyboard));
                Assert.That(activity.PromptKind, Is.EqualTo(InputDeviceKind.KeyboardMouse),
                    "The legacy coarse kind must stay unchanged for existing consumers.");
                Assert.That(activity.ControlPath, Does.Contain("space"));
                Assert.That(activity.Timestamp, Is.EqualTo(12.5d));
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void DeviceActivity_SeparatesKeyboardAndMouseFamilies()
        {
            var devices = Attach("KM", "Family");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(KeyboardMouseSlotYaml()).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);

                var received = new List<InputDeviceActivity>();
                using IDisposable subscription = player.DeviceActivity.Subscribe(received.Add);
                player.NotifyDeviceActivity(devices[0]);
                player.NotifyDeviceActivity(devices[1]);

                Assert.That(received.Count, Is.EqualTo(2));
                Assert.That(received[0].Family, Is.EqualTo(InputDeviceFamily.Keyboard));
                Assert.That(received[1].Family, Is.EqualTo(InputDeviceFamily.Mouse));
                Assert.That(received[0].DeviceLayout, Is.Not.EqualTo(received[1].DeviceLayout));
                Assert.That(received[0].PromptKind, Is.EqualTo(received[1].PromptKind),
                    "ActiveDeviceKind stays coarse; the precise family lives on the activity record.");
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void DeviceActivity_AnyButtonHookIgnoresForeignDevices()
        {
            var devices = Attach("KGM", "Foreign");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(SingleSlotYaml(0)).IsSuccess, Is.True);
                var player = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(player, Is.Not.Null);

                var hook = typeof(InputPlayer).GetMethod(
                    "OnAnyButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(hook, Is.Not.Null);

                var received = new List<InputDeviceActivity>();
                using IDisposable subscription = player.DeviceActivity.Subscribe(received.Add);

                // Mouse is not referenced by SingleSlotYaml, so it is not in this player's whitelist.
                hook.Invoke(player, new object[] { ((Mouse)devices[2]).leftButton });
                Assert.That(received.Count, Is.Zero, "Another player's device must not hijack prompts.");

                // The whitelist may contain the host keyboard rather than the injected one; use whichever
                // keyboard this player actually owns.
                Keyboard ownedKeyboard = null;
                var paired = player.User.pairedDevices;
                for (int i = 0; i < paired.Count; i++)
                {
                    if (paired[i] is Keyboard keyboard) { ownedKeyboard = keyboard; break; }
                }

                Assert.That(ownedKeyboard, Is.Not.Null, "A keyboard must be in the player's whitelist.");
                hook.Invoke(player, new object[] { ownedKeyboard.spaceKey });
                Assert.That(received.Count, Is.EqualTo(1));
                Assert.That(received[0].Family, Is.EqualTo(InputDeviceFamily.Keyboard));
            }
            finally
            {
                manager.Dispose();
                Detach(devices);
            }
        }

        [Test]
        public void IconSet_LayoutOverrideWinsOverCoarseKind()
        {
            Sprite generic = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            Sprite playStation = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var iconSet = ScriptableObject.CreateInstance<InputDeviceIconSet>();
            try
            {
                SetField(iconSet, "gamepadIcon", generic);
                SetField(iconSet, "keyboardMouseIcon", generic);

                Type entryType = typeof(InputDeviceIconSet).GetNestedType(
                    "LayoutIcon", BindingFlags.NonPublic);
                Assert.That(entryType, Is.Not.Null);
                Array entries = Array.CreateInstance(entryType, 1);
                object entry = Activator.CreateInstance(entryType);
                entryType.GetField("layout").SetValue(entry, "DualShockGamepad");
                entryType.GetField("icon").SetValue(entry, playStation);
                entries.SetValue(entry, 0);
                SetField(iconSet, "layoutIcons", entries);

                Assert.That(iconSet.GetIcon(InputDeviceKind.Gamepad, "DualShockGamepad"),
                    Is.SameAs(playStation));
                Assert.That(iconSet.GetIcon(InputDeviceKind.Gamepad, "SomeUnknownLayout"),
                    Is.SameAs(generic));
                Assert.That(iconSet.GetIcon(InputDeviceKind.Gamepad), Is.SameAs(generic),
                    "The legacy single-argument overload must keep its behaviour.");

                var activity = new InputDeviceActivity(
                    0, 1, "DualShockGamepad", InputDeviceFamily.Gamepad,
                    InputDeviceKind.Gamepad, "<Gamepad>/buttonSouth", 1d);
                Assert.That(iconSet.GetIcon(activity), Is.SameAs(playStation));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(iconSet);
                UnityEngine.Object.DestroyImmediate(generic);
                UnityEngine.Object.DestroyImmediate(playStation);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        /// <summary>
        /// Claims every keyboard/mouse that exists before the test injects its own devices. Relying on
        /// Keyboard.current is not deterministic: a previous test may have left another keyboard behind.
        /// </summary>
        private static InputUser ClaimPreExistingKeyboardMice()
        {
            var existing = new List<InputDevice>();
            var attached = UIS.devices;
            for (int i = 0; i < attached.Count; i++)
            {
                if (attached[i] is Keyboard || attached[i] is Mouse) existing.Add(attached[i]);
            }

            if (existing.Count == 0) return default;
            InputUser blocker = InputUser.PerformPairingWithDevice(existing[0]);
            for (int i = 1; i < existing.Count; i++)
            {
                InputUser.PerformPairingWithDevice(existing[i], blocker);
            }

            return blocker;
        }

        // ------------------------------------------------------------------
        // Device loss recovery.
        // ------------------------------------------------------------------
        [Test]
        public void ReassignPlayerDevices_RecoversSchemeMaskAndWhitelist()
        {
            InputUser blocker = ClaimPreExistingKeyboardMice();
            var devices = Attach("KMG", "Reassign");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(File.ReadAllText(Fixture(LockedFixture))).IsSuccess, Is.True);
                var original = manager.JoinSinglePlayer(0) as InputPlayer;
                Assert.That(original, Is.Not.Null);
                Assert.That(CountPaired<Gamepad>(original), Is.Zero);

                UIS.RemoveDevice(devices[0]);
                UIS.RemoveDevice(devices[1]);
                Assert.That(original.User.pairedDevices.Count, Is.Zero);

                Assert.That(manager.ReassignPlayerDevices(0), Is.True);
                var recovered = manager.GetInputPlayer(0) as InputPlayer;
                Assert.That(recovered, Is.Not.Null);
                Assert.That(CountPaired<Gamepad>(recovered), Is.EqualTo(1));
                Assert.That(CountControls<Gamepad>(recovered), Is.GreaterThan(0));
                Assert.That(DeviceFilter(recovered), Is.EqualTo("Gamepad"));
                Assert.That(DeviceMask(recovered), Is.EqualTo("Gamepad"));
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(1));
            }
            finally
            {
                manager.Dispose();
                if (blocker.valid) blocker.UnpairDevicesAndRemoveUser();
                Detach(devices);
            }
        }

        [Test]
        public void AutoReassignOnDeviceLoss_RecoversWithoutProductCode()
        {
            InputUser blocker = ClaimPreExistingKeyboardMice();
            var devices = Attach("KMG", "AutoReassign");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(File.ReadAllText(Fixture(LockedFixture))).IsSuccess, Is.True);
                manager.AutoReassignOnDeviceLoss = true;
                Assert.That(manager.JoinSinglePlayer(0), Is.Not.Null);

                // The recovery has to complete inside the removal notification. InputSystem raises
                // onDeviceChange before InputUser drops the device from pairedDevices (measured: the
                // Removed notification for the second of two paired devices still reports one entry), and
                // no further topology event follows. A player that is only recovered by some later
                // unrelated device event is dead on every platform that stays quiet after an unplug.
                UIS.RemoveDevice(devices[0]);
                UIS.RemoveDevice(devices[1]);

                var recovered = manager.GetInputPlayer(0) as InputPlayer;
                Assert.That(recovered, Is.Not.Null);
                Assert.That(CountPaired<Gamepad>(recovered), Is.EqualTo(1),
                    "Losing the last paired device must immediately re-pair the spare gamepad.");
                Assert.That(DeviceMask(recovered), Is.EqualTo("Gamepad"));
            }
            finally
            {
                manager.Dispose();
                if (blocker.valid) blocker.UnpairDevicesAndRemoveUser();
                Detach(devices);
            }
        }

        [Test]
        public void ReassignPlayerDevices_FailureKeepsPlayerRegistered()
        {
            var pad = UIS.AddDevice<Gamepad>("NoSpare");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(GamepadOnlyYaml()).IsSuccess, Is.True);
                Assert.That(manager.JoinSinglePlayer(0), Is.Not.Null);
                UIS.RemoveDevice(pad);

                Assert.That(manager.GetInputPlayer(0), Is.Not.Null);
                Assert.That(((InputPlayer)manager.GetInputPlayer(0)).User.pairedDevices.Count, Is.Zero);

                Assert.That(manager.ReassignPlayerDevices(0), Is.False,
                    "No spare gamepad exists, so the reassignment must fail closed.");
                Assert.That(manager.ActivePlayerCount, Is.EqualTo(1),
                    "A failed reassignment must not make the player vanish.");
                Assert.That(manager.GetInputPlayer(0), Is.Not.Null);
            }
            finally
            {
                manager.Dispose();
                if (pad.added) UIS.RemoveDevice(pad);
            }
        }

        private static string GamepadOnlyYaml()
        {
            var b = new StringBuilder(900);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    controlSchemes:");
            b.AppendLine("      - name: Gamepad");
            b.AppendLine("        bindingGroup: GP");
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
            b.AppendLine("            bindingGroups: GP");
            b.AppendLine("            deviceBindings:");
            b.AppendLine("              - \"<Gamepad>/buttonSouth\"");
            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: 0");
            b.AppendLine("            longPressValueThreshold: 0.5");
            return b.ToString();
        }

        private static string DeviceMask(InputPlayer player)
        {
            PropertyInfo property = typeof(InputActionAsset).GetProperty(
                "bindingMask", BindingFlags.Instance | BindingFlags.Public);
            object mask = property?.GetValue(AssetOf(player));
            if (mask == null) return "<unset>";
            object groups = typeof(InputBinding).GetProperty("groups")?.GetValue(mask);
            return string.IsNullOrEmpty(groups as string) ? "<none>" : (string)groups;
        }

        // ------------------------------------------------------------------
        // Contract clarity and value-type size.
        // ------------------------------------------------------------------
        [Test]
        public void SchemaVersion_ProfileContractIsIndependentAndAliased()
        {
            Assert.That(InputConfiguration.CurrentSchemaVersion, Is.EqualTo(1));
            Assert.That(InputBindingOverrideProfile.CurrentSchemaVersion, Is.EqualTo(1));
            Assert.That(InputBindingOverrideProfile.ProfileSchemaVersion,
                Is.EqualTo(InputBindingOverrideProfile.CurrentSchemaVersion));
            Assert.That(new InputBindingOverrideProfile().SchemaVersion,
                Is.EqualTo(InputBindingOverrideProfile.ProfileSchemaVersion));
        }

        [Test]
        public void BindingOverrideApplication_IsReferenceType()
        {
            Type type = typeof(InputPlayer).GetNestedType(
                "BindingOverrideApplication", BindingFlags.NonPublic);
            Assert.That(type, Is.Not.Null);
            Assert.That(type.IsValueType, Is.False,
                "A 104-byte value type embedding Unity's InputBinding should not be copied in List<T>.");
        }

        // ------------------------------------------------------------------
        // Property-based invariant: two players never own the same device.
        // ------------------------------------------------------------------
        [TestCase(101)]
        [TestCase(2027)]
        [TestCase(31337)]
        public void Fuzz_RandomTopology_KeepsPlayerDeviceSetsDisjoint(int seed)
        {
            var random = new System.Random(seed);
            var pads = new List<Gamepad>(4);
            for (int i = 0; i < 4; i++) pads.Add(UIS.AddDevice<Gamepad>("Fz" + seed + "_" + i));
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(TwoSlotYaml()).IsSuccess, Is.True);
                for (int step = 0; step < 300; step++)
                {
                    int choice = random.Next(100);
                    if (choice < 35) manager.JoinSinglePlayer(random.Next(2));
                    else if (choice < 60) manager.RemovePlayer(random.Next(2));
                    else if (choice < 80) manager.ReassignPlayerDevices(random.Next(2));
                    else if (pads.Count > 0)
                    {
                        int index = random.Next(pads.Count);
                        if (pads[index] != null && pads[index].added) UIS.RemoveDevice(pads[index]);
                        pads[index] = UIS.AddDevice<Gamepad>("Fz" + seed + "_re" + step);
                    }

                    var seen = new HashSet<int>();
                    for (int playerId = 0; playerId < 2; playerId++)
                    {
                        var player = manager.GetInputPlayer(playerId) as InputPlayer;
                        if (player == null) continue;
                        var paired = player.User.pairedDevices;
                        for (int i = 0; i < paired.Count; i++)
                        {
                            Assert.That(seen.Add(paired[i].deviceId), Is.True,
                                "seed=" + seed + " step=" + step +
                                ": device " + paired[i].deviceId + " was owned by two players.");
                        }
                    }
                }
            }
            finally
            {
                manager.Dispose();
                for (int i = pads.Count - 1; i >= 0; i--)
                {
                    if (pads[i] != null && pads[i].added) UIS.RemoveDevice(pads[i]);
                }
            }
        }
    }
}
