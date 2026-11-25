using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Writes a canonical snapshot of every action and binding an external configuration resolves to.
    /// Requires CGIS_GAME_CONFIG and CGIS_GAME_SNAPSHOT; otherwise the test is ignored. Diff two
    /// snapshots to check that a configuration rewrite preserved the graph.
    /// </summary>
    public sealed class ExternalConfigurationSnapshotTests
    {
        private static readonly FieldInfo AssetField =
            typeof(InputPlayer).GetField("_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void Write_ExternalConfigurationSnapshot()
        {
            string configPath = Environment.GetEnvironmentVariable("CGIS_GAME_CONFIG");
            string snapshotPath = Environment.GetEnvironmentVariable("CGIS_GAME_SNAPSHOT");
            if (string.IsNullOrEmpty(configPath) || string.IsNullOrEmpty(snapshotPath)) Assert.Ignore("Set CGIS_GAME_CONFIG and CGIS_GAME_SNAPSHOT.");

            string yaml = File.ReadAllText(configPath);
            var lines = new List<string>();
            var devices = new List<InputDevice>();
            InputManager manager = null;
            try
            {
                devices.Add(UIS.AddDevice<Keyboard>("SnapshotKeyboard"));
                devices.Add(UIS.AddDevice<Gamepad>("SnapshotGamepad"));

                manager = new InputManager(PerfScenarioBuilder.BuildLimits());
                InputManagerInitializationResult result = manager.InitializeWithResult(yaml);
                lines.Add("success=" + result.IsSuccess);
                lines.Add("message=" + result.Message);

                if (result.IsSuccess)
                {
                    InputPlayer player = manager.JoinSinglePlayer(0) as InputPlayer;
                    Assert.That(player, Is.Not.Null, "Player 0 must join the external configuration.");
                    var asset = (InputActionAsset)AssetField.GetValue(player);
                    foreach (InputControlScheme scheme in asset.controlSchemes)
                    {
                        var requirement = new StringBuilder();
                        foreach (InputControlScheme.DeviceRequirement binding in scheme.deviceRequirements)
                        {
                            requirement.Append(binding.isOptional ? "?" : "!")
                                .Append(binding.controlPath).Append(';');
                        }
                        lines.Add("scheme=" + scheme.name + "|" + requirement);
                    }

                    foreach (InputActionMap map in asset.actionMaps)
                    {
                        foreach (InputAction action in map.actions)
                        {
                            ReadOnlyArray<InputBinding> bindings = action.bindings;
                            for (int i = 0; i < bindings.Count; i++)
                            {
                                InputBinding binding = bindings[i];
                                lines.Add("action=" + map.name + "/" + action.name + "/" + action.type +
                                    "|binding=" + i +
                                    "|path=" + binding.effectivePath +
                                    "|composite=" + binding.isComposite +
                                    "|part=" + binding.isPartOfComposite +
                                    "|groups=" + binding.groups +
                                    "|interactions=" + binding.interactions +
                                    "|processors=" + binding.processors);
                            }
                        }
                    }
                }
            }
            finally
            {
                manager?.Dispose();
                for (int i = devices.Count - 1; i >= 0; i--)
                {
                    InputDevice device = devices[i];
                    if (device != null && device.added) UIS.RemoveDevice(device);
                }
            }

            lines.Sort(StringComparer.Ordinal);
            File.WriteAllLines(snapshotPath, lines, new UTF8Encoding(false));
        }
    }
}
