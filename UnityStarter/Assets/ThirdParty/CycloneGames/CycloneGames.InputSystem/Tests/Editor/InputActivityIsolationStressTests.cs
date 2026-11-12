using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine.InputSystem;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Stress and matrix coverage for the device-activity ownership boundary: a player may only be driven by
    /// devices it owns, in both the debounced kind and the raw activity stream.
    /// </summary>
    public sealed class InputActivityIsolationStressTests : InputDeviceTestBase
    {
        [Test]
        public void Matrix_TwoPlayers_ForeignInjectionNeverChangesKind()
        {
            RunMatrix(playerCount: 2, rounds: 40);
        }

        [Test]
        public void Matrix_ThreePlayers_ForeignInjectionNeverChangesKind()
        {
            RunMatrix(playerCount: 3, rounds: 40);
        }

        [Test]
        public void Soak_TwoThousandMixedInjections_NoDrift()
        {
            const int Iterations = 2000;
            var devices = AttachDevices("SoakK", "SoakM", "SoakG1", "SoakG2");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(BuildYaml(2)).IsSuccess, Is.True);

                var players = JoinAll(manager, 2);
                var random = new Random(20261113);
                int violations = 0;
                int foreignAttempts = 0;

                for (int i = 0; i < Iterations; i++)
                {
                    int index = random.Next(players.Count);
                    InputPlayer owner = players[index];
                    InputPlayer other = players[(index + 1) % players.Count];

                    InputDevice mine = owner.User.pairedDevices.Count > 0
                        ? owner.User.pairedDevices[random.Next(owner.User.pairedDevices.Count)]
                        : null;
                    InputDevice foreign = other.User.pairedDevices.Count > 0
                        ? other.User.pairedDevices[random.Next(other.User.pairedDevices.Count)]
                        : null;

                    bool sendForeign = foreign != null && random.Next(2) == 0;
                    InputDevice target = sendForeign ? foreign : mine;
                    if (target == null) continue;
                    if (sendForeign) foreignAttempts++;

                    owner.NotifyDeviceActivity(target);
                    owner.UpdateRuntimeState(0.001d * (i + 1));

                    if (!IsKindWithinOwnDevices(owner))
                    {
                        violations++;
                    }
                }

                Assert.That(foreignAttempts, Is.GreaterThan(0), "The soak must exercise foreign injections.");
                Assert.That(
                    violations,
                    Is.Zero,
                    "Foreign injections leaked into another player's kind " + violations + " time(s) out of " +
                    Iterations + " iterations.");
            }
            finally
            {
                manager.Dispose();
                DetachDevices(devices);
            }
        }

        [Test]
        public void Soak_ActivityStreamNeverEmitsForeignDevices()
        {
            const int Iterations = 800;
            var devices = AttachDevices("StreamK", "StreamM", "StreamG1", "StreamG2");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(BuildYaml(2)).IsSuccess, Is.True);

                var players = JoinAll(manager, 2);
                InputPlayer first = players[0];
                InputPlayer second = players[1];

                var observedDeviceIds = new HashSet<int>();
                IDisposable subscription = first.DeviceActivity.Subscribe(activity => observedDeviceIds.Add(activity.DeviceId));

                for (int i = 0; i < Iterations; i++)
                {
                    if (second.User.pairedDevices.Count > 0)
                    {
                        first.NotifyDeviceActivity(second.User.pairedDevices[0]);
                    }

                    first.UpdateRuntimeState(0.001d * (i + 1));
                }

                for (int i = 0; i < second.User.pairedDevices.Count; i++)
                {
                    Assert.That(
                        observedDeviceIds.Contains(second.User.pairedDevices[i].deviceId),
                        Is.False,
                        "The raw activity stream must not carry a device owned by another player.");
                }

                subscription.Dispose();
            }
            finally
            {
                manager.Dispose();
                DetachDevices(devices);
            }
        }

        [Test]
        public void Fuzz_InvalidAndRemovedDevices_DoNotThrow()
        {
            var devices = AttachDevices("FuzzK", "FuzzM", "FuzzG1", "FuzzG2");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(BuildYaml(2)).IsSuccess, Is.True);

                var players = JoinAll(manager, 2);
                InputPlayer first = players[0];
                first.UpdateRuntimeState(0d);
                InputDeviceKind baseline = first.ActiveDeviceKind.CurrentValue;

                var random = new Random(424242);
                for (int i = 0; i < 500; i++)
                {
                    InputDevice candidate;
                    switch (random.Next(4))
                    {
                        case 0:
                            candidate = null;
                            break;
                        case 1:
                            candidate = UIS.AddDevice<Gamepad>("FuzzEphemeral" + i);
                            UIS.RemoveDevice(candidate);
                            break;
                        default:
                            candidate = players[random.Next(players.Count)].User.pairedDevices.Count > 0
                                ? players[random.Next(players.Count)].User.pairedDevices[0]
                                : null;
                            break;
                    }

                    Assert.DoesNotThrow(() => first.NotifyDeviceActivity(candidate), "Iteration " + i);
                    first.UpdateRuntimeState(0.001d * (i + 1));
                }

                Assert.That(
                    first.ActiveDeviceKind.CurrentValue,
                    Is.EqualTo(baseline),
                    "Random invalid input must not disturb the owned-device kind.");
            }
            finally
            {
                manager.Dispose();
                DetachDevices(devices);
            }
        }

        private static void RunMatrix(int playerCount, int rounds)
        {
            var devices = AttachDevices("MxK", "MxM", "MxG1", "MxG2");
            var manager = new InputManager();
            try
            {
                Assert.That(manager.InitializeWithResult(BuildYaml(playerCount)).IsSuccess, Is.True);

                var players = JoinAll(manager, playerCount);
                for (int round = 0; round < rounds; round++)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        InputPlayer owner = players[i];
                        InputPlayer other = players[(i + 1) % players.Count];

                        if (other.User.pairedDevices.Count > 0)
                        {
                            owner.NotifyDeviceActivity(other.User.pairedDevices[0]);
                            owner.UpdateRuntimeState(0.05d * (round + 1));
                        }

                        Assert.That(
                            IsKindWithinOwnDevices(owner),
                            Is.True,
                            "Round " + round + ", player " + owner.PlayerId +
                            " kind=" + owner.ActiveDeviceKind.CurrentValue);
                    }
                }
            }
            finally
            {
                manager.Dispose();
                DetachDevices(devices);
            }
        }

        private static bool IsKindWithinOwnDevices(InputPlayer player)
        {
            InputDeviceKind kind = player.ActiveDeviceKind.CurrentValue;
            if (kind == InputDeviceKind.Unknown) return false;

            var paired = player.User.pairedDevices;
            for (int i = 0; i < paired.Count; i++)
            {
                InputDevice device = paired[i];
                bool matches =
                    (kind == InputDeviceKind.KeyboardMouse && (device is Keyboard || device is Mouse)) ||
                    (kind == InputDeviceKind.Gamepad && device is Gamepad) ||
                    (kind == InputDeviceKind.Touchscreen && device is Touchscreen);
                if (matches) return true;
            }

            return false;
        }

        private static List<InputPlayer> JoinAll(InputManager manager, int playerCount)
        {
            var players = new List<InputPlayer>(playerCount);
            for (int i = 0; i < playerCount; i++)
            {
                var player = manager.JoinSinglePlayer(i) as InputPlayer;
                Assert.That(player, Is.Not.Null, "Player " + i + " must join.");
                players.Add(player);
            }

            return players;
        }

        private static string BuildYaml(int playerCount)
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine("schemaVersion: 1");
            builder.AppendLine("playerSlots:");

            for (int i = 0; i < playerCount; i++)
            {
                builder.AppendLine("  - playerId: " + i);
                if (i == 0)
                {
                    builder.AppendLine("    controlSchemes:");
                    builder.AppendLine("      - name: KeyboardMouse");
                    builder.AppendLine("        bindingGroup: KM");
                    builder.AppendLine("        deviceRequirements:");
                    builder.AppendLine("          - controlPath: \"<Keyboard>\"");
                    builder.AppendLine("            isOptional: false");
                    builder.AppendLine("            isOr: false");
                    builder.AppendLine("          - controlPath: \"<Mouse>\"");
                    builder.AppendLine("            isOptional: false");
                    builder.AppendLine("            isOr: false");
                    AppendContext(builder, "Map0", "KM", "\"<Keyboard>/space\"", "\"<Mouse>/leftButton\"");
                }
                else
                {
                    builder.AppendLine("    controlSchemes:");
                    builder.AppendLine("      - name: Gamepad");
                    builder.AppendLine("        bindingGroup: GP");
                    builder.AppendLine("        deviceRequirements:");
                    builder.AppendLine("          - controlPath: \"<Gamepad>\"");
                    builder.AppendLine("            isOptional: false");
                    builder.AppendLine("            isOr: false");
                    AppendContext(builder, "Map" + i, "GP", "\"<Gamepad>/buttonSouth\"");
                }
            }

            return builder.ToString();
        }

        private static void AppendContext(StringBuilder builder, string map, string groups, params string[] paths)
        {
            builder.AppendLine("    contexts:");
            builder.AppendLine("      - name: Gameplay");
            builder.AppendLine("        actionMap: " + map);
            builder.AppendLine("        priority: 0");
            builder.AppendLine("        blocksLowerPriority: true");
            builder.AppendLine("        bindings:");
            builder.AppendLine("          - type: Button");
            builder.AppendLine("            action: Confirm");
            builder.AppendLine("            expectedControlType: Button");
            builder.AppendLine("            bindingGroups: " + groups);
            builder.AppendLine("            deviceBindings:");
            for (int i = 0; i < paths.Length; i++)
            {
                builder.AppendLine("              - " + paths[i]);
            }

            builder.AppendLine("            compositeBindings: []");
            builder.AppendLine("            updateMode: EventDriven");
            builder.AppendLine("            longPressMs: 0");
            builder.AppendLine("            longPressValueThreshold: 0.5");
        }

        private static List<InputDevice> AttachDevices(string k, string m, string g1, string g2)
        {
            return new List<InputDevice>(4)
            {
                UIS.AddDevice<Keyboard>(k),
                UIS.AddDevice<Mouse>(m),
                UIS.AddDevice<Gamepad>(g1),
                UIS.AddDevice<Gamepad>(g2)
            };
        }

        private static void DetachDevices(List<InputDevice> devices)
        {
            for (int i = devices.Count - 1; i >= 0; i--)
            {
                if (devices[i] != null && devices[i].added) UIS.RemoveDevice(devices[i]);
            }
        }
    }
}
