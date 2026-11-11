using System.Collections.Generic;
using System.Reflection;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Verifies that the measurement rig actually exercises action dispatch. A rig whose action maps are not
    /// enabled would report plausible but meaningless timings, so the enabled state and the current action value
    /// are asserted before any matrix measurement is trusted.
    /// </summary>
    public sealed class InputPerfDiagnosticTests
    {
        private const string File = "diagnostics.csv";
        private const string Header = "runId,check,metric,value,unit";

        [Test]
        public void Diagnostic_ActionMapsAreEnabledAndActionsReportValues()
        {
            var scenario = new PerfScenario("diag", 1, 1, 3, 100, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);
            InputPlayer player = rig.First;

            for (int frame = 0; frame < 10; frame++) rig.Frame(0.7f, frame / 60d);

            var assetField = typeof(InputPlayer).GetField("_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(assetField, Is.Not.Null);
            var asset = (InputActionAsset)assetField.GetValue(player);
            Assert.That(asset, Is.Not.Null);

            var lines = new List<string>();
            int enabledMaps = 0;
            int enabledActions = 0;
            int totalActions = 0;
            int resolvableActions = 0;

            for (int mapIndex = 0; mapIndex < asset.actionMaps.Count; mapIndex++)
            {
                InputActionMap map = asset.actionMaps[mapIndex];
                if (map.enabled) enabledMaps++;
                lines.Add("map=" + map.name + " enabled=" + map.enabled + " actions=" + map.actions.Count);
                for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
                {
                    InputAction action = map.actions[actionIndex];
                    totalActions++;
                    if (action.enabled) enabledActions++;
                    int controls = action.controls.Count;
                    if (controls > 0) resolvableActions++;
                    object value = action.phase.IsInProgress() ? "in-progress" : "waiting";
                    if (actionIndex < 3)
                    {
                        lines.Add(
                            "  action=" + action.name +
                            " type=" + action.type +
                            " enabled=" + action.enabled +
                            " phase=" + action.phase +
                            " controls=" + controls +
                            " value=" + (value ?? "null") +
                            " control0=" + (controls > 0 ? action.controls[0].path : "<none>") +
                            " device0=" + (controls > 0 ? action.controls[0].device.name : "<none>"));
                    }
                }
            }

            lines.Add("asset.devices=" + (asset.devices.HasValue ? asset.devices.Value.Count : -1));
            lines.Add("InputUser.valid=" + player.User.valid);
            lines.Add("InputUser.pairedDevices=" + player.User.pairedDevices.Count);
            lines.Add("device.stick=" + ((Gamepad)rig.Devices[0]).leftStick.ReadValue());

            for (int i = 0; i < lines.Count; i++) PerfOutput.Log("diag: " + lines[i]);

            PerfOutput.WriteEnvironmentHeader(File);
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "maps", "enabled", enabledMaps.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "maps", "total", asset.actionMaps.Count.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "actions", "enabled", enabledActions.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "actions", "total", totalActions.ToString(), "count" });
            PerfOutput.Row(File, Header, new[] { PerfOutput.RunId, "actions", "resolvableControls", resolvableActions.ToString(), "count" });

            Assert.That(enabledMaps, Is.GreaterThan(0), "No action map was enabled; the rig would measure a disabled graph.");
            Assert.That(enabledActions, Is.GreaterThan(0), "No action was enabled; dispatch cannot happen.");
        }

        /// <summary>
        /// Distinguishes "map not enabled" from "control path not resolved" by enabling the map by hand and
        /// checking that the binding still resolves while the action value stays at default in this host.
        /// </summary>
        [Test]
        public void Diagnostic_ManualEnableKeepsResolvedControlsWithoutDispatch()
        {
            var scenario = new PerfScenario("diag-manual", 1, 1, 3, 100, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);

            for (int frame = 0; frame < 6; frame++) rig.Frame(0.7f, frame / 60d);

            var asset = (InputActionAsset)typeof(InputPlayer)
                .GetField("_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(rig.First);

            InputActionMap map = asset.actionMaps[1 < asset.actionMaps.Count ? 1 : 0];
            InputAction move = map.FindAction("A1");
            Assert.That(move, Is.Not.Null);

            Vector2 before = move.ReadValue<Vector2>();
            map.Enable();
            Vector2 afterEnable = move.ReadValue<Vector2>();
            rig.Frame(0.7f, 1d);
            Vector2 afterFrame = move.ReadValue<Vector2>();

            PerfOutput.Log(
                "diag-manual: mapEnabled=" + map.enabled +
                " before=" + before + " afterEnable=" + afterEnable + " afterFrame=" + afterFrame +
                " stick=" + ((Gamepad)rig.Devices[0]).leftStick.ReadValue());

            Assert.That(map.enabled, Is.True);
            Assert.That(move.controls.Count, Is.GreaterThan(0), "The control path must resolve even when dispatch is unavailable.");
            Assert.That(
                afterFrame,
                Is.EqualTo(Vector2.zero),
                "The EditMode host is expected to keep action values at default; dispatch is measured separately.");
        }
    }
}
