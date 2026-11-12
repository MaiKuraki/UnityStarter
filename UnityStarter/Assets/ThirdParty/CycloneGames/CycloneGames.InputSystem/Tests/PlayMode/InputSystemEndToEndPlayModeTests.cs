using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.TestTools;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.PlayMode
{
    /// <summary>
    /// End-to-end coverage of the module graph inside a real player loop. Every EditMode test in this package
    /// asserts graph *shape* (how many controls an action resolves, which groups a binding declares); none of
    /// them can prove that a signal actually reaches a subscriber, because EditMode dispatch is inert.
    /// These tests close that gap: they queue real device state, let the player loop run, and count emissions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The harness injects its own devices and reserves every pre-existing device of the same layouts with a
    /// placeholder user, so a machine with a real keyboard, mouse or gamepad attached cannot influence device
    /// selection.
    /// </para>
    /// <para>
    /// Keyboard-driven cases are not covered here: a bare Unity action bound to a keyboard control does not
    /// dispatch under the Test Runner in this environment. Gamepad-driven cases exercise the module graph,
    /// context wiring and frame pump end to end.
    /// </para>
    /// </remarks>
    public sealed class InputSystemEndToEndPlayModeTests : PlayModeDeviceTestBase
    {
        /// <summary>
        /// <see cref="GamepadButton"/> is a sequential index enum, not a flags enum: <c>South</c> is 6, not
        /// <c>1 &lt;&lt; 6</c>. <c>GamepadState.buttons</c> is the raw bitfield, so the bit has to be shifted in
        /// explicitly. Casting the enum value directly sets bits 1 and 2 (dpad down and left) instead.
        /// </summary>
        private const uint SouthButton = 1u << (int)GamepadButton.South;

        private readonly List<InputDevice> _injected = new List<InputDevice>();
        private readonly List<InputManager> _managers = new List<InputManager>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly List<InputUser> _reservations = new List<InputUser>();

        [SetUp]
        public void SetUp()
        {
            _injected.Clear();
            _managers.Clear();
            _subscriptions.Clear();
            _reservations.Clear();
            ReservePreexistingDevices();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _subscriptions.Count; i++) _subscriptions[i]?.Dispose();
            _subscriptions.Clear();

            for (int i = 0; i < _managers.Count; i++)
            {
                try { _managers[i]?.Dispose(); }
                catch (Exception) { /* teardown must not mask the assertion that failed */ }
            }

            _managers.Clear();

            for (int i = 0; i < _injected.Count; i++)
            {
                InputDevice device = _injected[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }

            _injected.Clear();

            for (int i = 0; i < _reservations.Count; i++)
            {
                InputUser user = _reservations[i];
                if (user.valid) user.UnpairDevicesAndRemoveUser();
            }

            _reservations.Clear();
        }

        // ------------------------------------------------------------------ harness

        /// <summary>
        /// Pairs every device that already exists into a throwaway user so the module cannot select it. This is what
        /// makes the fixture deterministic on a workstation that has a physical keyboard, mouse or gamepad.
        /// </summary>
        private void ReservePreexistingDevices()
        {
            var existing = new List<InputDevice>();
            var attached = UIS.devices;
            for (int i = 0; i < attached.Count; i++)
            {
                InputDevice device = attached[i];
                if (device is Keyboard || device is Mouse || device is Gamepad) existing.Add(device);
            }

            if (existing.Count == 0) return;

            InputUser user = InputUser.PerformPairingWithDevice(existing[0]);
            for (int i = 1; i < existing.Count; i++) InputUser.PerformPairingWithDevice(existing[i], user);
            _reservations.Add(user);
        }

        private T Inject<T>(string name) where T : InputDevice
        {
            T device = UIS.AddDevice<T>(name);
            Assert.That(device, Is.Not.Null, "Device injection failed for " + name);
            _injected.Add(device);
            return device;
        }

        private InputManager NewManager(string yaml)
        {
            var manager = new InputManager();
            _managers.Add(manager);
            InputManagerInitializationResult result = manager.InitializeWithResult(yaml);
            Assert.That(result.IsSuccess, Is.True, result.Message);
            return manager;
        }

        /// <summary>
        /// Joins a slot and activates its top context. Pushing a context is what makes the module enable the
        /// action map: without it the graph still resolves controls, but no action is ever enabled and nothing
        /// dispatches. That distinction is invisible to EditMode assertions on <c>action.controls</c>.
        /// </summary>
        private InputPlayer Join(InputManager manager, int playerId, string actionMapName)
        {
            var player = manager.JoinSinglePlayer(playerId) as InputPlayer;
            Assert.That(player, Is.Not.Null, "Player " + playerId + " failed to join.");
            player.PushContext(new InputContext(actionMapName, "Gameplay", 0, true));
            return player;
        }

        private void Track(IDisposable subscription)
        {
            _subscriptions.Add(subscription);
        }

        private static bool OwnsDevice(InputPlayer player, InputDevice device)
        {
            var paired = player.User.pairedDevices;
            for (int i = 0; i < paired.Count; i++)
            {
                if (paired[i] == device) return true;
            }

            return false;
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        // ------------------------------------------------------------------ 1. event-driven dispatch

        [UnityTest]
        public IEnumerator EndToEnd_EventDrivenButtonReachesSubscriber()
        {
            Gamepad pad = Inject<Gamepad>("E2E_EventPad");
            InputManager manager = NewManager(GamepadSlotsYaml(1));
            InputPlayer player = Join(manager, 0, "Map0");

            Assert.That(OwnsDevice(player, pad), Is.True,
                "Join must pair the injected gamepad; otherwise the test would measure an unbound graph.");

            int fired = 0;
            Track(player.GetButtonObservable("Confirm").Subscribe(_ => fired++));

            yield return null;
            UIS.QueueStateEvent(pad, new GamepadState { buttons = SouthButton });
            yield return null;
            yield return null;

            Assert.That(fired, Is.EqualTo(1),
                "An EventDriven button bound to <Gamepad>/buttonSouth produced " + fired +
                " emissions. Zero means the module graph never dispatches, which EditMode tests cannot detect.");

            UIS.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);

            Assert.That(fired, Is.EqualTo(1), "Releasing the button must not emit another press.");
        }

        // ------------------------------------------------------------------ 2. frame pump

        [UnityTest]
        public IEnumerator EndToEnd_PollingVector2TracksStickAcrossFrames()
        {
            Gamepad pad = Inject<Gamepad>("E2E_PollPad");
            InputManager manager = NewManager(GamepadSlotsYaml(1));
            InputPlayer player = Join(manager, 0, "Map0");

            Vector2 last = new Vector2(float.NaN, float.NaN);
            int samples = 0;
            Track(player.GetVector2Observable("Move").Subscribe(value =>
            {
                last = value;
                samples++;
            }));

            yield return null;
            var injected = new Vector2(0.75f, -0.5f);
            UIS.QueueStateEvent(pad, new GamepadState { leftStick = injected });
            yield return Frames(5);

            Assert.That(samples, Is.GreaterThan(0),
                "A Polling action emitted nothing across 5 frames. The module drives polling from " +
                "Observable.EveryUpdate, so zero samples means the R3 frame pump is not running in this host.");

            // StickControl carries a stickDeadzone processor (min 0.125, max 0.925), so magnitude is remapped by
            // design: 0.9014 in becomes 0.9705 out. Asserting the raw value would test Unity's processor defaults
            // rather than this module, so the contract checked here is direction plus a non-collapsed magnitude.
            Assert.That(last.magnitude, Is.GreaterThan(0.5f),
                "Polled stick magnitude collapsed toward zero; the sample did not come from the injected state.");
            Assert.That(Vector2.Dot(last.normalized, injected.normalized), Is.GreaterThan(0.999f),
                "Polled stick direction " + last.normalized + " diverged from the injected direction " +
                injected.normalized + ".");
        }

        // ------------------------------------------------------------------ 3. isolation

        [UnityTest]
        public IEnumerator EndToEnd_TwoPlayersReceiveOnlyTheirOwnDevices()
        {
            Gamepad padA = Inject<Gamepad>("E2E_PadA");
            Gamepad padB = Inject<Gamepad>("E2E_PadB");
            InputManager manager = NewManager(GamepadSlotsYaml(2));

            InputPlayer playerA = Join(manager, 0, "Map0");
            InputPlayer playerB = Join(manager, 1, "Map1");

            Assert.That(playerA.User.id, Is.Not.EqualTo(playerB.User.id), "Two slots must create two InputUsers.");
            Assert.That(OwnsDevice(playerA, padA) ^ OwnsDevice(playerA, padB), Is.True,
                "Player 0 must own exactly one of the two injected gamepads.");
            Assert.That(OwnsDevice(playerB, padA) ^ OwnsDevice(playerB, padB), Is.True);
            Assert.That(OwnsDevice(playerA, padA), Is.Not.EqualTo(OwnsDevice(playerB, padA)),
                "The two players must not claim the same device.");

            int firedA = 0;
            int firedB = 0;
            Track(playerA.GetButtonObservable("Confirm").Subscribe(_ => firedA++));
            Track(playerB.GetButtonObservable("Confirm").Subscribe(_ => firedB++));

            yield return null;
            Gamepad ownerOfA = OwnsDevice(playerA, padA) ? padA : padB;
            Gamepad ownerOfB = OwnsDevice(playerB, padA) ? padA : padB;

            UIS.QueueStateEvent(ownerOfA, new GamepadState { buttons = SouthButton });
            yield return Frames(3);

            Assert.That(firedA, Is.EqualTo(1), "Player 0 did not receive its own device press.");
            Assert.That(firedB, Is.Zero, "Player 1 received a press from player 0's device.");

            UIS.QueueStateEvent(ownerOfA, new GamepadState());
            UIS.QueueStateEvent(ownerOfB, new GamepadState { buttons = SouthButton });
            yield return Frames(3);

            Assert.That(firedA, Is.EqualTo(1), "Player 0 received a press from player 1's device.");
            Assert.That(firedB, Is.EqualTo(1), "Player 1 did not receive its own device press.");
        }

        // ------------------------------------------------------------------ 5. debounce across real frames

        [UnityTest]
        public IEnumerator EndToEnd_DeviceKindDebounceSuppressesRapidAlternation()
        {
            const int debounceMs = 200;
            Keyboard keyboard = Inject<Keyboard>("E2E_DebounceKeyboard");
            Gamepad pad = Inject<Gamepad>("E2E_DebouncePad");
            InputManager manager = NewManager(MixedSlotYaml(deviceSwitchDebounceMs: debounceMs));
            InputPlayer player = Join(manager, 0, "Map0");

            Assert.That(OwnsDevice(player, keyboard), Is.True);
            Assert.That(OwnsDevice(player, pad), Is.True, "The mixed slot must pair both injected devices.");

            // Drain the join-time pending kind and let the debounce window expire, so the next swap is a clean
            // "well separated" switch rather than an accidental in-window one.
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Frames(2);

            InputDeviceKind settled = player.ActiveDeviceKind.CurrentValue;
            Assert.That(settled, Is.Not.EqualTo(InputDeviceKind.Unknown),
                "The active device kind never left Unknown, so the frame pump never applies device switches.");

            InputDeviceKind target = settled == InputDeviceKind.Gamepad
                ? InputDeviceKind.KeyboardMouse
                : InputDeviceKind.Gamepad;

            if (target == InputDeviceKind.Gamepad)
            {
                UIS.QueueStateEvent(pad, new GamepadState { buttons = SouthButton });
            }
            else
            {
                UIS.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            }

            yield return Frames(3);

            Assert.That(player.ActiveDeviceKind.CurrentValue, Is.EqualTo(target),
                "A device switch that fell outside the debounce window was not applied on the spot. Settled kind " +
                "was " + settled + ".");

            // Swap straight back inside the window. The kind must stay on the target until the window expires.
            if (target == InputDeviceKind.Gamepad)
            {
                UIS.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            }
            else
            {
                UIS.QueueStateEvent(pad, new GamepadState { buttons = SouthButton });
            }

            yield return Frames(2);

            Assert.That(player.ActiveDeviceKind.CurrentValue, Is.EqualTo(target),
                "A device-kind switch inside the debounce window was applied immediately; the debounce is inert " +
                "when driven by the real player loop.");

            yield return new WaitForSecondsRealtime(debounceMs / 1000f + 0.4f);
            yield return Frames(3);

            Assert.That(player.ActiveDeviceKind.CurrentValue, Is.EqualTo(settled),
                "The pending device kind never flushed back to " + settled + " after the debounce window elapsed.");
        }

        // ------------------------------------------------------------------ 6. device loss and reassignment

        [UnityTest]
        public IEnumerator EndToEnd_DeviceLossThenReassignRestoresDispatch()
        {
            Gamepad first = Inject<Gamepad>("E2E_LossPad");
            InputManager manager = NewManager(GamepadSlotsYaml(1));
            InputPlayer player = Join(manager, 0, "Map0");

            int fired = 0;
            Track(player.GetButtonObservable("Confirm").Subscribe(_ => fired++));

            yield return null;
            UIS.QueueStateEvent(first, new GamepadState { buttons = SouthButton });
            yield return Frames(3);
            Assert.That(fired, Is.EqualTo(1), "Baseline press before unplugging did not dispatch.");

            // Unplug: the player stays registered but has no device left to read.
            UIS.RemoveDevice(first);
            _injected.Remove(first);
            yield return Frames(2);
            Assert.That(manager.ActivePlayerCount, Is.EqualTo(1),
                "Unplugging the only device must not silently drop the registration.");

            Gamepad second = Inject<Gamepad>("E2E_LossPadReplacement");
            yield return null;

            Assert.That(manager.ReassignPlayerDevices(0), Is.True, "Reassignment to the replacement device failed.");
            yield return Frames(2);

            // Reassignment tears the old player down and builds a new one, so context and subscriptions belong to
            // the new instance. JoinSinglePlayer returns the registered player when the slot is already occupied.
            InputPlayer reassigned = Join(manager, 0, "Map0");
            Assert.That(ReferenceEquals(reassigned, player), Is.False,
                "Reassignment was expected to build a fresh player instance.");

            int firedAfter = 0;
            Track(reassigned.GetButtonObservable("Confirm").Subscribe(_ => firedAfter++));
            yield return null;

            UIS.QueueStateEvent(second, new GamepadState { buttons = SouthButton });
            yield return Frames(3);

            Assert.That(firedAfter, Is.EqualTo(1),
                "Dispatch did not resume after reassignment. fired=" + firedAfter +
                ", meaning the player is registered but permanently deaf.");
        }

        // ------------------------------------------------------------------ configuration fixtures

        private static string ControlScheme(string name, params string[] requiredPaths)
        {
            var b = new StringBuilder(256);
            b.AppendLine("      - name: " + name);
            b.AppendLine("        bindingGroup: " + name);
            b.AppendLine("        deviceRequirements:");
            for (int i = 0; i < requiredPaths.Length; i++)
            {
                b.AppendLine("          - controlPath: \"" + requiredPaths[i] + "\"");
                b.AppendLine("            isOptional: false");
                b.AppendLine("            isOr: false");
            }

            return b.ToString();
        }

        private static string ButtonBinding(string action, string groups, int longPressMs, params string[] paths)
        {
            var b = new StringBuilder(256);
            b.AppendLine("          - type: Button");
            b.AppendLine("            action: " + action);
            b.AppendLine("            expectedControlType: Button");
            b.AppendLine("            bindingGroups: " + groups);
            b.AppendLine("            deviceBindings:");
            for (int i = 0; i < paths.Length; i++) b.AppendLine("              - \"" + paths[i] + "\"");
            b.AppendLine("            compositeBindings: []");
            b.AppendLine("            updateMode: EventDriven");
            b.AppendLine("            longPressMs: " + longPressMs);
            b.AppendLine("            longPressValueThreshold: 0.5");
            return b.ToString();
        }

        /// <summary>
        /// Slots that only declare a gamepad scheme, so the fixture never depends on a host keyboard or mouse.
        /// </summary>
        private static string GamepadSlotsYaml(int slotCount)
        {
            var b = new StringBuilder(1600);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            for (int slot = 0; slot < slotCount; slot++)
            {
                b.AppendLine("  - playerId: " + slot);
                b.AppendLine("    controlSchemes:");
                b.Append(ControlScheme("Gamepad", "<Gamepad>"));
                b.AppendLine("    contexts:");
                b.AppendLine("      - name: Gameplay");
                b.AppendLine("        actionMap: Map" + slot);
                b.AppendLine("        priority: 0");
                b.AppendLine("        blocksLowerPriority: true");
                b.AppendLine("        bindings:");
                b.AppendLine("          - type: Vector2");
                b.AppendLine("            action: Move");
                b.AppendLine("            expectedControlType: Vector2");
                b.AppendLine("            bindingGroups: Gamepad");
                b.AppendLine("            deviceBindings:");
                b.AppendLine("              - \"<Gamepad>/leftStick\"");
                b.AppendLine("            compositeBindings: []");
                b.AppendLine("            updateMode: Polling");
                b.AppendLine("            longPressMs: 0");
                b.AppendLine("            longPressValueThreshold: 0.5");
                b.Append(ButtonBinding("Confirm", "Gamepad", 0, "<Gamepad>/buttonSouth"));
            }

            return b.ToString();
        }

        /// <summary>
        /// A single mixed slot that pairs every matching layout at once, which is what makes a keyboard-to-gamepad
        /// device-kind switch observable on one player.
        /// </summary>
        private static string MixedSlotYaml(int deviceSwitchDebounceMs)
        {
            var b = new StringBuilder(1400);
            b.AppendLine("schemaVersion: 1");
            b.AppendLine("playerSlots:");
            b.AppendLine("  - playerId: 0");
            b.AppendLine("    pairAllMatchingDevices: true");
            b.AppendLine("    maxDevicesPerLayout: 1");
            b.AppendLine("    deviceSwitchDebounceMs: " + deviceSwitchDebounceMs);
            b.AppendLine("    controlSchemes:");
            b.Append(ControlScheme("KeyboardMouse", "<Keyboard>"));
            b.Append(ControlScheme("Gamepad", "<Gamepad>"));
            b.AppendLine("    contexts:");
            b.AppendLine("      - name: Gameplay");
            b.AppendLine("        actionMap: Map0");
            b.AppendLine("        priority: 0");
            b.AppendLine("        blocksLowerPriority: true");
            b.AppendLine("        bindings:");
            b.Append(ButtonBinding("Confirm", "KeyboardMouse;Gamepad", 0,
                "<Gamepad>/buttonSouth", "<Keyboard>/space"));
            return b.ToString();
        }
    }
}
