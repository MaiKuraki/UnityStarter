using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Records a host capability that constrains every measurement in this suite: in a batch-mode EditMode run the
    /// Input System processes state events into device state but does not drive enabled InputAction callbacks,
    /// because the editor update type uses a different state buffer than the action state created for dynamic
    /// updates. These probes therefore assert the observed behaviour instead of asserting dispatch, and the
    /// dispatch-dependent dimensions are reported as not measured in this host.
    /// </summary>
    public sealed class InputPerfDispatchProbeTests
    {
        private const string File = "dispatch-probe.csv";
        private const string Header = "runId,probe,metric,value,unit";

        [Test]
        public void Probe_BareAssetDoesNotDispatchInEditMode()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var gamepad = UIS.AddDevice<Gamepad>("BarePad");
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                InputActionMap map = asset.AddActionMap("Bare");
                InputAction stick = map.AddAction("Stick", InputActionType.Value, "<Gamepad>/leftStick");
                float performed = 0f;
                stick.performed += _ => performed += 1f;
                map.Enable();

                float deviceMagnitude = 0f;
                for (int frame = 0; frame < 5; frame++)
                {
                    float phase = 0.2f + frame * 0.1f;
                    UIS.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(phase, phase) });
                    UIS.Update();
                    deviceMagnitude = Mathf.Max(deviceMagnitude, gamepad.leftStick.ReadValue().magnitude);
                }

                Vector2 actionValue = stick.ReadValue<Vector2>();

                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "bareAsset", "deviceStateMagnitude", PerfOutput.Num(deviceMagnitude, 4), "magnitude" });
                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "bareAsset", "actionMagnitude", PerfOutput.Num(actionValue.magnitude, 4), "magnitude" });
                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "bareAsset", "performedCallbacks", PerfOutput.Num(performed, 0), "count" });
                PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "bareAsset", "actionPhase", stick.phase.ToString(), "phase" });

                PerfOutput.Log(
                    "dispatch probe: deviceStateMagnitude=" + PerfOutput.Num(deviceMagnitude, 4) +
                    " actionMagnitude=" + PerfOutput.Num(actionValue.magnitude, 4) +
                    " performedCallbacks=" + PerfOutput.Num(performed, 0) +
                    " phase=" + stick.phase);

                Assert.That(deviceMagnitude, Is.GreaterThan(0f), "The queued state event must reach device state.");
                Assert.That(
                    performed,
                    Is.Zero,
                    "InputAction callbacks are expected to stay idle in an EditMode batch run; if this now fires, dispatch measurement can be enabled for EditMode as well.");
            }
            finally
            {
                UIS.RemoveDevice(gamepad);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Probe_UserAssociatedAssetDoesNotDispatchInEditMode()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var gamepad = UIS.AddDevice<Gamepad>("UserPad");
            InputUser user = InputUser.PerformPairingWithDevice(gamepad);
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                InputActionMap map = asset.AddActionMap("Bare");
                InputAction stick = map.AddAction("Stick", InputActionType.Value, "<Gamepad>/leftStick");
                user.AssociateActionsWithUser(asset);
                map.Enable();

                for (int frame = 0; frame < 5; frame++)
                {
                    UIS.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(0.5f, 0.5f) });
                    UIS.Update();
                }

                PerfOutput.Row(
                    File,
                    Header,
                    new[] { PerfOutput.RunId, "userAssociatedAsset", "actionMagnitude", PerfOutput.Num(stick.ReadValue<Vector2>().magnitude, 4), "magnitude" });
                PerfOutput.Row(
                    File,
                    Header,
                    new[] { PerfOutput.RunId, "userAssociatedAsset", "assetDevices", (asset.devices.HasValue ? asset.devices.Value.Count : -1).ToString(), "count" });

                PerfOutput.Log(
                    "dispatch probe user-associated: actionMagnitude=" + PerfOutput.Num(stick.ReadValue<Vector2>().magnitude, 4) +
                    " assetDevices=" + (asset.devices.HasValue ? asset.devices.Value.Count : -1));

                Assert.That(stick.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            }
            finally
            {
                if (user.valid) user.UnpairDevicesAndRemoveUser();
                UIS.RemoveDevice(gamepad);
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// Confirms that the module graph builds, enables its maps and resolves control paths in this host, so that
        /// the absence of dispatch is a host limitation rather than a rig defect.
        /// </summary>
        [Test]
        public void Probe_ModuleGraphResolvesControlsEvenWithoutDispatch()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var scenario = new PerfScenario("dispatch-module", 1, 2, 9, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);
            InputPlayer player = rig.First;

            for (int frame = 0; frame < 10; frame++) rig.Frame(0.7f, frame / 60d);

            var asset = (InputActionAsset)typeof(InputPlayer)
                .GetField("_inputActionAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(player);

            int enabledMaps = 0;
            int actions = 0;
            int resolvedControls = 0;
            var detail = new List<string>();
            for (int mapIndex = 0; mapIndex < asset.actionMaps.Count; mapIndex++)
            {
                InputActionMap map = asset.actionMaps[mapIndex];
                if (map.enabled) enabledMaps++;
                for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
                {
                    InputAction action = map.actions[actionIndex];
                    actions++;
                    if (action.controls.Count > 0) resolvedControls++;
                    if (actionIndex == 0)
                    {
                        detail.Add(map.name + "/" + action.name + "=>" + (action.controls.Count > 0 ? action.controls[0].path : "<none>"));
                    }
                }
            }

            for (int i = 0; i < detail.Count; i++) PerfOutput.Log("dispatch probe control: " + detail[i]);

            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "moduleGraph", "enabledMaps", enabledMaps.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "moduleGraph", "actions", actions.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "moduleGraph", "resolvedControls", resolvedControls.ToString(), "count" });

            PerfOutput.Log("dispatch probe module graph: enabledMaps=" + enabledMaps + " actions=" + actions + " resolvedControls=" + resolvedControls);

            Assert.That(enabledMaps, Is.EqualTo(scenario.Contexts));
            Assert.That(resolvedControls, Is.EqualTo(actions));
        }
    }
}
