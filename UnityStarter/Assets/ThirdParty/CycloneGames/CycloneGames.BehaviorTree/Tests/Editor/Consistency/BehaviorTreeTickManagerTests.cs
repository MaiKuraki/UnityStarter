using System;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using NUnit.Framework;
using UnityEngine;

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
