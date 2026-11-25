using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>Runtime context-policy overrides, verified against the final active-context list.</summary>
    public sealed class InputContextPolicyRegressionTests
    {
        private static int _deviceSerial;

        private readonly List<InputPlayer> _players = new List<InputPlayer>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _players.Count - 1; i >= 0; i--) _players[i].Dispose();
            _players.Clear();
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = _devices[i];
                if (device != null && device.added) UIS.RemoveDevice(device);
            }
            _devices.Clear();
        }

        private InputPlayer CreatePlayer(int contextCount, out InputContext[] contexts)
        {
            var slot = new PlayerSlotConfig
            {
                PlayerId = 0,
                Contexts = new List<ContextDefinitionConfig>(contextCount)
            };

            for (int i = 0; i < contextCount; i++)
            {
                slot.Contexts.Add(new ContextDefinitionConfig
                {
                    Name = "C" + i,
                    ActionMap = "M" + i,
                    Bindings = new List<ActionBindingConfig>
                    {
                        new ActionBindingConfig
                        {
                            Type = ActionValueType.Button,
                            ActionName = "A0",
                            DeviceBindings = new List<string> { "<Gamepad>/buttonSouth" }
                        }
                    }
                });
            }

            Gamepad device = UIS.AddDevice<Gamepad>("PolicyPad" + (++_deviceSerial));
            _devices.Add(device);
            InputUser user = InputUser.PerformPairingWithDevice(device);
            var player = new InputPlayer(0, user, slot, device);
            _players.Add(player);

            contexts = new InputContext[contextCount];
            for (int i = 0; i < contextCount; i++) contexts[i] = new InputContext("M" + i, "C" + i, 0, true);
            return player;
        }

        private static InputContext[] ActiveContexts(InputPlayer player)
        {
            FieldInfo field = typeof(InputPlayer).GetField("_activeContexts", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var list = field.GetValue(player) as IEnumerable<InputContext>;
            Assert.That(list, Is.Not.Null);
            var copy = new List<InputContext>(4);
            foreach (InputContext context in list) copy.Add(context);
            return copy.ToArray();
        }

        private static int EnabledMapCount(InputPlayer player)
        {
            FieldInfo field = typeof(InputPlayer).GetField("_inputActionAsset", BindingFlags.Instance | BindingFlags.NonPublic);
            var asset = (InputActionAsset)field.GetValue(player);
            int count = 0;
            foreach (InputActionMap map in asset.actionMaps)
            {
                if (map.enabled) count++;
            }
            return count;
        }

        private static int PolicyOverrideCount(InputPlayer player)
        {
            FieldInfo field = typeof(InputPlayer).GetField("_contextPolicyOverrides", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return ((System.Collections.ICollection)field.GetValue(player)).Count;
        }

        [Test]
        public void SetPolicy_ChangesTheActiveProjectionAndClearRestoresIt()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            player.PushContext(contexts[1]);

            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[1] }), "Blocking top context starts exclusive.");

            Assert.That(player.SetContextPolicy(contexts[1], 0, false), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[1], contexts[0] }),
                "A non-blocking top context must reveal lower contexts without popping itself.");
            Assert.That(PolicyOverrideCount(player), Is.EqualTo(1));

            Assert.That(player.SetContextPolicy(contexts[1], 0, true), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[1] }));

            Assert.That(player.ClearContextPolicy(contexts[1]), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[1] }));
            Assert.That(PolicyOverrideCount(player), Is.Zero);
            Assert.That(contexts[1].Priority, Is.Zero, "The shared context object must stay immutable.");
            Assert.That(contexts[1].BlocksLowerPriority, Is.True, "The shared context object must stay immutable.");
        }

        [Test]
        public void SetPolicy_IsScopedToOnePlayerEvenWhenTheContextIsShared()
        {
            InputPlayer first = CreatePlayer(2, out InputContext[] firstContexts);
            InputPlayer second = CreatePlayer(2, out InputContext[] secondContexts);

            var shared = new InputContext("M0", "C0", 0, true);
            first.PushContext(firstContexts[1]);
            first.PushContext(shared);
            second.PushContext(secondContexts[1]);
            second.PushContext(shared);

            Assert.That(ActiveContexts(first), Is.EqualTo(new[] { shared }));
            Assert.That(ActiveContexts(second), Is.EqualTo(new[] { shared }));

            Assert.That(first.SetContextPolicy(shared, 0, false), Is.True);
            Assert.That(ActiveContexts(first), Is.EqualTo(new[] { shared, firstContexts[1] }));
            Assert.That(ActiveContexts(second), Is.EqualTo(new[] { shared }),
                "Mutating one player must not change another player's projection of the same context.");
            Assert.That(shared.BlocksLowerPriority, Is.True);
        }

        [Test]
        public void PolicyOverride_IsReleasedWithContextOwnership()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            player.PushContext(contexts[1]);
            Assert.That(player.SetContextPolicy(contexts[0], 20, false), Is.True);
            Assert.That(player.SetContextPolicy(contexts[1], 30, false), Is.True);
            Assert.That(PolicyOverrideCount(player), Is.EqualTo(2));

            Assert.That(player.RemoveContext(contexts[1]), Is.True);
            Assert.That(PolicyOverrideCount(player), Is.EqualTo(1));

            player.PopContext();
            Assert.That(PolicyOverrideCount(player), Is.Zero, "Overrides must not outlive their context ownership.");

            using (player.CaptureContext(contexts[0]))
            {
                Assert.That(player.SetContextPolicy(contexts[0], 5, false), Is.True);
                Assert.That(PolicyOverrideCount(player), Is.EqualTo(1));
            }
            Assert.That(PolicyOverrideCount(player), Is.Zero, "Releasing a capture scope must drop its override.");

            player.PushContext(contexts[0]);
            Assert.That(player.SetContextPolicy(contexts[0], 5, false), Is.True);
            contexts[0].Dispose();
            Assert.That(PolicyOverrideCount(player), Is.Zero, "Disposing the context must drop its override.");
        }

        [Test]
        public void SetPolicy_NoOpDoesNotProjectAgain()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            player.PushContext(contexts[1]);

            int notifications = 0;
            player.OnContextChanged += _ => notifications++;
            notifications = 0;

            Assert.That(player.SetContextPolicy(contexts[1], 0, false), Is.True);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(player.SetContextPolicy(contexts[1], 0, false), Is.True);
            Assert.That(notifications, Is.EqualTo(1), "An unchanged policy must not trigger a refresh.");
            Assert.That(player.SetContextPolicy(contexts[1], 7, false), Is.True);
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(player.ClearContextPolicy(contexts[1]), Is.True);
            Assert.That(notifications, Is.EqualTo(3));
            Assert.That(player.ClearContextPolicy(contexts[1]), Is.False);
            Assert.That(notifications, Is.EqualTo(3));
        }

        [Test]
        public void SetPolicy_IsSafeWhenCalledFromInsideASubscription()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            var replay = new BehaviorSubject<Unit>(Unit.Default);
            contexts[1].AddBinding(replay, new ActionCommand(() => player.SetContextPolicy(contexts[1], 0, false)));

            player.PushContext(contexts[0]);
            Assert.DoesNotThrow(() => player.PushContext(contexts[1]));
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[1], contexts[0] }),
                "Re-entrant policy changes converge to the correct projection without an unbounded refresh loop.");
            Assert.That(PolicyOverrideCount(player), Is.EqualTo(1));
            replay.Dispose();
        }

        [Test]
        public void SetPolicy_RejectsNullOrUnownedContextsAndExtremePriorities()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);

            Assert.That(player.SetContextPolicy(null, 0, false), Is.False);
            Assert.That(player.SetContextPolicy(contexts[1], 0, false), Is.False);
            Assert.That(player.ClearContextPolicy(null), Is.False);
            Assert.That(player.ClearContextPolicy(contexts[0]), Is.False);
            Assert.That(PolicyOverrideCount(player), Is.Zero);

            Assert.That(player.SetContextPolicy(contexts[0], int.MinValue, false), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[0] }));
            Assert.That(player.SetContextPolicy(contexts[0], int.MaxValue, false), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[0] }));
        }

        [Test]
        public void SetPolicy_PriorityOverridesStackOrderAsDocumented()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            player.PushContext(contexts[1]);

            Assert.That(player.SetContextPolicy(contexts[0], 100, false), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[0], contexts[1] }),
                "A higher priority is collected first, independent of stack order.");
        }

        [Test]
        public void ThreeLayers_MidOperationSwitchesFromOneToTwoActiveContexts()
        {
            InputPlayer player = CreatePlayer(3, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);

            Assert.That(player.SetContextPolicy(contexts[2], 10, true), Is.True);
            Assert.That(player.SetContextPolicy(contexts[1], 5, true), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[2] }));
            Assert.That(EnabledMapCount(player), Is.EqualTo(1), "Modal phase enables one map.");

            Assert.That(player.SetContextPolicy(contexts[2], 10, false), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[2], contexts[1] }));
            Assert.That(EnabledMapCount(player), Is.EqualTo(2), "Preview phase enables UI + Gameplay only.");

            InputPlayer.ProjectionDiagnostics d0 = player.GetProjectionDiagnostics();
            player.RefreshActiveContext();
            InputPlayer.ProjectionDiagnostics d1 = player.GetProjectionDiagnostics();
            Assert.That(d1.MapEnables - d0.MapEnables + d1.MapDisables - d0.MapDisables, Is.Zero);
            Assert.That(d1.SubscriptionRebuilds - d0.SubscriptionRebuilds, Is.Zero);

            Assert.That(player.SetContextPolicy(contexts[2], 10, true), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[2] }));
            Assert.That(EnabledMapCount(player), Is.EqualTo(1), "Returning to modal disables the extra map.");

            player.ClearContextPolicy(contexts[2]);
            player.ClearContextPolicy(contexts[1]);
            Assert.That(PolicyOverrideCount(player), Is.Zero);
        }

        [Test]
        public void FourLayers_OnlyTwoNonAdjacentLayersReceiveInput()
        {
            InputPlayer player = CreatePlayer(4, out InputContext[] contexts);
            for (int i = 0; i < contexts.Length; i++) player.PushContext(contexts[i]);

            Assert.That(player.SetContextPolicy(contexts[3], 100, false), Is.True);
            Assert.That(player.SetContextPolicy(contexts[1], 90, true), Is.True);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[3], contexts[1] }),
                "Active contexts are the sorted prefix, so a priority pair can select non-adjacent layers.");
            Assert.That(EnabledMapCount(player), Is.EqualTo(2),
                "Exactly the two selected action maps must be enabled.");

            InputPlayer.ProjectionDiagnostics d0 = player.GetProjectionDiagnostics();
            player.RefreshActiveContext();
            player.RefreshActiveContext();
            InputPlayer.ProjectionDiagnostics d1 = player.GetProjectionDiagnostics();
            Assert.That(d1.MapEnables - d0.MapEnables + d1.MapDisables - d0.MapDisables, Is.Zero);
            Assert.That(d1.SubscriptionRebuilds - d0.SubscriptionRebuilds, Is.Zero,
                "An unchanged projection must reuse its subscriptions.");
            Assert.That(d1.SubscriptionReuses - d0.SubscriptionReuses, Is.EqualTo(4));

            player.ClearContextPolicy(contexts[3]);
            player.ClearContextPolicy(contexts[1]);
            Assert.That(ActiveContexts(player), Is.EqualTo(new[] { contexts[3] }));
            Assert.That(EnabledMapCount(player), Is.EqualTo(1));
        }

        [Test]
        public void SetPolicy_DisposedPlayerThrowsLikeOtherContextOperations()
        {
            InputPlayer player = CreatePlayer(2, out InputContext[] contexts);
            player.PushContext(contexts[0]);
            player.Dispose();
            Assert.Throws<ObjectDisposedException>(() => player.SetContextPolicy(contexts[0], 0, false));
            Assert.Throws<ObjectDisposedException>(() => player.ClearContextPolicy(contexts[0]));
        }
    }
}
