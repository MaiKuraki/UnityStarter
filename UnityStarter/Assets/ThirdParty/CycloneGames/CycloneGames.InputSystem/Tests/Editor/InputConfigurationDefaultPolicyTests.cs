using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>Configuration defaults must not depend on how a definition was constructed.</summary>
    public sealed class InputConfigurationDefaultPolicyTests
    {
        private const string MinimalFormat = "schemaVersion: 1\nplayerSlots:\n  - playerId: 0\n    contexts:\n      - name: C0\n        actionMap: M0\n{0}        bindings:\n          - type: button\n            action: A0\n            deviceBindings:\n              - <Gamepad>/buttonSouth\n";

        private static InputConfiguration Deserialize(string extra)
        {
            string yaml = string.Format(MinimalFormat, extra);
            return YamlSerializer.Deserialize<InputConfiguration>(Encoding.UTF8.GetBytes(yaml));
        }

        private static ContextDefinitionConfig FirstContext(InputConfiguration configuration)
        {
            return configuration.PlayerSlots[0].Contexts[0];
        }

        [Test]
        public void OmittedBlocksLowerPriority_MatchesTheProgrammaticDefault()
        {
            bool fromYaml = FirstContext(Deserialize(string.Empty)).BlocksLowerPriority;
            bool fromCode = new ContextDefinitionConfig().BlocksLowerPriority;

            PerfOutput.Log("defaultPolicy omittedYaml=" + fromYaml + " programmatic=" + fromCode);
            Assert.That(fromYaml, Is.EqualTo(fromCode),
                "The YAML path and the C# path must agree on the default. yaml=" + fromYaml + " code=" + fromCode);
            Assert.That(fromCode, Is.False,
                "Contexts are additive unless blocksLowerPriority is enabled explicitly.");
        }

        [Test]
        public void ExplicitBlocksLowerPriority_IsPreserved()
        {
            Assert.That(FirstContext(Deserialize("        blocksLowerPriority: true\n")).BlocksLowerPriority, Is.True);
            Assert.That(FirstContext(Deserialize("        blocksLowerPriority: false\n")).BlocksLowerPriority, Is.False);
        }

        [TestCase(true, 1)]
        [TestCase(false, 2)]
        public void RuntimeProjection_FollowsTheConfigurationValue(bool blocks, int expectedActive)
        {
            Gamepad device = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>("DefaultPolicyPad" + blocks);
            InputPlayer player = null;
            try
            {
                InputUser user = InputUser.PerformPairingWithDevice(device);
                string yamlValue = blocks ? "        blocksLowerPriority: true\n" : "        blocksLowerPriority: false\n";
                InputConfiguration configuration = Deserialize(yamlValue);
                Assert.That(configuration.PlayerSlots.Count, Is.EqualTo(1));
                PlayerSlotConfig slot = configuration.PlayerSlots[0];
                Assert.That(slot.Contexts[0].BlocksLowerPriority, Is.EqualTo(blocks));

                // The two-argument constructor delegates the policy to the configuration.
                player = new InputPlayer(0, user, slot, device);
                player.PushContext(new InputContext("M0", "C0"));
                player.PushContext(new InputContext("M0", "C0"));

                Assert.That(ActiveContextCount(player), Is.EqualTo(expectedActive),
                    "blocksLowerPriority=" + blocks + " must produce " + expectedActive + " active contexts.");
            }
            finally
            {
                if (player != null) player.Dispose();
                if (device != null && device.added) UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
            }
        }

        [Test]
        public void OtherInitializers_DifferOnlyWhereTheRuntimeAlreadyNormalizes()
        {
            PlayerSlotConfig fromYaml = Deserialize(string.Empty).PlayerSlots[0];
            var fromCode = new PlayerSlotConfig
            {
                Contexts = new List<ContextDefinitionConfig>
                {
                    new ContextDefinitionConfig
                    {
                        Name = "C0",
                        ActionMap = "M0",
                        Bindings = new List<ActionBindingConfig>
                        {
                            new ActionBindingConfig
                            {
                                Type = ActionValueType.Button,
                                ActionName = "A0",
                                DeviceBindings = new List<string> { "<Gamepad>/buttonSouth" }
                            }
                        }
                    }
                }
            };

            ActionBindingConfig yamlBinding = fromYaml.Contexts[0].Bindings[0];
            ActionBindingConfig codeBinding = fromCode.Contexts[0].Bindings[0];
            PerfOutput.Log("defaultPolicy otherInitializers maxDevices yaml=" + fromYaml.MaxDevicesPerLayout +
                " code=" + fromCode.MaxDevicesPerLayout +
                " updateMode yaml=" + yamlBinding.UpdateMode + " code=" + codeBinding.UpdateMode +
                " threshold yaml=" + yamlBinding.LongPressValueThreshold +
                " code=" + codeBinding.LongPressValueThreshold);

            // The runtime clamps both values to the same limit.
            Assert.That(fromYaml.MaxDevicesPerLayout, Is.Not.EqualTo(fromCode.MaxDevicesPerLayout),
                "Documents the DTO-level difference.");
            Assert.That(new RuntimePlayerSlotConfig(fromYaml).MaxDevicesPerLayout,
                Is.EqualTo(new RuntimePlayerSlotConfig(fromCode).MaxDevicesPerLayout));

            // EventDriven is the enum's zero value.
            Assert.That(yamlBinding.UpdateMode, Is.EqualTo(InputUpdateMode.EventDriven));
            Assert.That(codeBinding.UpdateMode, Is.EqualTo(InputUpdateMode.EventDriven));

            // Only Float long press reads the threshold, and validation rejects zero there.
            Assert.That(yamlBinding.LongPressValueThreshold, Is.Zero);
            Assert.That(codeBinding.LongPressValueThreshold, Is.EqualTo(0.5f).Within(1e-6f));
        }

        private static int ActiveContextCount(InputPlayer player)
        {
            var field = typeof(InputPlayer).GetField("_activeContexts",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return ((System.Collections.ICollection)field.GetValue(player)).Count;
        }
    }
}
