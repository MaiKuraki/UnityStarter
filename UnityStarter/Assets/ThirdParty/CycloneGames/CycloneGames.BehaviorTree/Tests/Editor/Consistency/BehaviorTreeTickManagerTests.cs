using System;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class BehaviorTreeTickManagerTests
    {
        [Test]
        public void TickManager_RejectsZeroCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BTTickManager(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BTTickManager().TickBudget = 0);
        }

        [Test]
        public void TickManager_RemovesTerminalTreesAfterPass()
        {
            using RuntimeBehaviorTree tree = CreateTree(new CallbackNode(null));
            var manager = new BTTickManager(1);
            manager.Register(tree);

            manager.Tick();

            Assert.That(tree.State, Is.EqualTo(RuntimeState.Success));
            Assert.That(manager.Count, Is.Zero);
        }

        [Test]
        public void TickManager_DefersRegistrationRequestedByNodeCallback()
        {
            using RuntimeBehaviorTree second = CreateTree(new CallbackNode(null));
            var manager = new BTTickManager(1);
            using RuntimeBehaviorTree first = CreateTree(new CallbackNode(() => manager.Register(second)));
            manager.Register(first);

            manager.Tick();

            Assert.That(manager.Count, Is.EqualTo(1));
            Assert.That(second.State, Is.EqualTo(RuntimeState.NotEntered));

            manager.Tick();
            Assert.That(second.State, Is.EqualTo(RuntimeState.Success));
            Assert.That(manager.Count, Is.Zero);
        }

        [Test]
        public void PriorityTickManager_MovesAndRemovesTreesWithoutLinearLookupState()
        {
            using RuntimeBehaviorTree tree = CreateTree(new CallbackNode(null));
            var manager = new BTPriorityTickManager();
            manager.Register(tree, 0);

            manager.UpdatePriority(tree, 7);

            Assert.That(manager.GetTreeCount(0), Is.Zero);
            Assert.That(manager.GetTreeCount(7), Is.EqualTo(1));

            manager.Tick();
            Assert.That(manager.GetTotalCount(), Is.Zero);
        }

        [Test]
        public void PriorityTickManager_RejectsNegativeBudget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BTPriorityTickManager(new[] { -1 }));
        }

        [Test]
        public void TickManagerComponent_LegacyRegisterReportsOnlyFirstCapacityRejection()
        {
            var owner = new GameObject("Legacy Tick Manager Owner");
            BTTickManagerComponent component = owner.AddComponent<BTTickManagerComponent>();
            var boundedManager = new BTTickManager(1, 1, 1);
            SetPrivateField(component, "_manager", boundedManager);
            using RuntimeBehaviorTree accepted = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree tryRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree firstLegacyRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree repeatedLegacyRejected = CreateTree(new CallbackNode(null));
            try
            {
                Assert.That(component.TryRegister(accepted), Is.True);

                // TryRegister is the explicit result channel and must remain silent.
                Assert.That(component.TryRegister(tryRejected), Is.False);

                LogAssert.Expect(
                    LogType.Error,
                    "[BTTickManagerComponent] Legacy Register was rejected because managed tree or " +
                    "deferred-mutation capacity was exhausted on 'Legacy Tick Manager Owner'. " +
                    "Use TryRegister to handle admission failure.");
                component.Register(firstLegacyRejected);

                component.Register(repeatedLegacyRejected);
                component.Register(null);
                component.Register(accepted);

                BTTickManagerMemoryStats stats = component.GetMemoryStats();
                Assert.That(stats.TreeCount, Is.EqualTo(1));
                Assert.That(stats.CapacityRejectedTreeCount, Is.EqualTo(3L));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PriorityTickManagerComponent_LegacyRegisterReportsOnlyFirstCapacityRejection()
        {
            var owner = new GameObject("Legacy Priority Manager Owner");
            owner.SetActive(false);
            BTPriorityTickManagerComponent component = owner.AddComponent<BTPriorityTickManagerComponent>();
            BTDistanceLODProvider lodProvider = owner.AddComponent<BTDistanceLODProvider>();
            lodProvider.MaximumTreeCount = 4;
            var boundedManager = new BTPriorityTickManager(
                budgets: null,
                initialBucketCapacity: 1,
                maximumTreeCount: 1,
                maximumPendingMutationCount: 1);
            SetPrivateField(component, "_manager", boundedManager);
            SetPrivateField(component, "_lodProvider", lodProvider);
            SetPrivateField(component, "_initialized", true);
            using RuntimeBehaviorTree accepted = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree tryRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree firstLegacyRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree repeatedLegacyRejected = CreateTree(new CallbackNode(null));
            try
            {
                Assert.That(component.TryRegister(accepted, owner.transform), Is.True);

                // The failed Try* call increments diagnostics but does not log.
                Assert.That(component.TryRegister(tryRejected, owner.transform), Is.False);

                LogAssert.Expect(
                    LogType.Error,
                    "[BTPriorityTickManagerComponent] Legacy Register was rejected because managed tree, " +
                    "LOD, or deferred-mutation capacity was exhausted on 'Legacy Priority Manager Owner'. " +
                    "Use TryRegister to handle admission failure.");
                component.Register(firstLegacyRejected, owner.transform);

                component.Register(repeatedLegacyRejected, owner.transform);
                component.Register(null, owner.transform);
                component.Register(accepted, owner.transform);

                BTPriorityTickManagerMemoryStats stats = component.GetMemoryStats();
                Assert.That(stats.RegisteredTreeCount, Is.EqualTo(1));
                Assert.That(stats.Core.CapacityRejectedTreeCount, Is.EqualTo(3L));
                Assert.That(stats.LOD.CapacityRejectedTreeCount, Is.Zero);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void DistanceLodProvider_LegacyRegisterReportsOnlyFirstCapacityRejection()
        {
            var owner = new GameObject("Legacy LOD Owner");
            BTDistanceLODProvider provider = owner.AddComponent<BTDistanceLODProvider>();
            provider.MaximumTreeCount = 1;
            using RuntimeBehaviorTree accepted = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree tryRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree firstLegacyRejected = CreateTree(new CallbackNode(null));
            using RuntimeBehaviorTree repeatedLegacyRejected = CreateTree(new CallbackNode(null));
            try
            {
                Assert.That(provider.TryRegisterTree(accepted, owner.transform), Is.True);

                Assert.That(provider.TryRegisterTree(tryRejected, owner.transform), Is.False);

                LogAssert.Expect(
                    LogType.Error,
                    "[BTDistanceLODProvider] Legacy RegisterTree was rejected because LOD tree capacity " +
                    "was exhausted on 'Legacy LOD Owner'. Use TryRegisterTree to handle admission failure.");
                provider.RegisterTree(firstLegacyRejected, owner.transform);

                provider.RegisterTree(repeatedLegacyRejected, owner.transform);
                provider.RegisterTree(null, owner.transform);
                provider.RegisterTree(accepted, owner.transform);

                BTDistanceLODProviderMemoryStats stats = provider.GetMemoryStats();
                Assert.That(stats.TreeCount, Is.EqualTo(1));
                Assert.That(stats.CapacityRejectedTreeCount, Is.EqualTo(3L));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RuntimeTree_WakeUpNotificationIsCoalescedUntilConsumed()
        {
            using RuntimeBehaviorTree tree = CreateTree(new CallbackNode(null));
            EventInfo wakeUpEvent = typeof(RuntimeBehaviorTree).GetEvent(
                "WakeUpRequested",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(wakeUpEvent, Is.Not.Null);

            int notificationCount = 0;
            Action<RuntimeBehaviorTree> handler = _ => notificationCount++;
            MethodInfo addMethod = wakeUpEvent.GetAddMethod(nonPublic: true);
            MethodInfo removeMethod = wakeUpEvent.GetRemoveMethod(nonPublic: true);
            Assert.That(addMethod, Is.Not.Null);
            Assert.That(removeMethod, Is.Not.Null);
            addMethod.Invoke(tree, new object[] { handler });
            try
            {
                tree.WakeUp();
                tree.WakeUp(3);
                Assert.That(notificationCount, Is.EqualTo(1));
                Assert.That(tree.WakeUpTickBudget, Is.EqualTo(3));

                Assert.That(tree.ConsumeWakeUp(), Is.True);
                tree.WakeUp();
                Assert.That(notificationCount, Is.EqualTo(1));
                Assert.That(tree.WakeUpTickBudget, Is.EqualTo(2));

                Assert.That(tree.ConsumeWakeUp(), Is.True);
                Assert.That(tree.ConsumeWakeUp(), Is.True);
                Assert.That(tree.ConsumeWakeUp(), Is.False);

                tree.WakeUp();
                Assert.That(notificationCount, Is.EqualTo(2));
            }
            finally
            {
                removeMethod.Invoke(tree, new object[] { handler });
            }
        }

        [Test]
        public void LODConfig_RequiresBudgetsForEveryReferencedPriority()
        {
            BTLODConfig config = ScriptableObject.CreateInstance<BTLODConfig>();
            try
            {
                config.Levels = new[]
                {
                    new BTLODConfig.LODLevel
                    {
                        MaxDistance = float.MaxValue,
                        TickInterval = 1,
                        Priority = 4
                    }
                };
                config.PriorityBudgets = new[] { 10, 10, 10, 10 };

                Assert.That(config.TryValidate(out string error), Is.False);
                Assert.That(error, Does.Contain("priority from 0 through 4"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        private static RuntimeBehaviorTree CreateTree(RuntimeNode child)
        {
            return new RuntimeBehaviorTree(
                new RuntimeRootNode { Child = child },
                new RuntimeBlackboard(),
                new RuntimeBTContext());
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private sealed class CallbackNode : RuntimeNode
        {
            private readonly Action _callback;

            public CallbackNode(Action callback)
            {
                _callback = callback;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                _callback?.Invoke();
                return RuntimeState.Success;
            }
        }
    }
}
