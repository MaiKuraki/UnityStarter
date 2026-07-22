using System;
using System.Threading;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.DOD;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class BehaviorTreeDataOrientedSafetyTests
    {
        [Test]
        public void TickJob_IsInternalSchedulerImplementationDetail()
        {
            Type tickJobType = typeof(BTTickScheduler).Assembly.GetType(
                "CycloneGames.BehaviorTree.Runtime.DOD.BTTickJob",
                throwOnError: true);

            Assert.That(typeof(BTTickScheduler).IsPublic, Is.True);
            Assert.That(tickJobType.IsVisible, Is.False);
        }

        [Test]
        public void RootNode_PropagatesChildFailure()
        {
            using var tree = CreateConditionTree(expectedValue: 1);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 0, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent();

            scheduler.ScheduleTick(0f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Failure));
        }

        [Test]
        public void RemoveAgent_RejectsDoubleRemoveAndStaleHandleAfterReuse()
        {
            using var tree = CreateConditionTree(expectedValue: 0);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 0, initialCapacity: 1);
            BTAgentHandle original = scheduler.AddAgent();

            Assert.That(scheduler.RemoveAgent(original), Is.True);
            Assert.That(scheduler.RemoveAgent(original), Is.False);

            BTAgentHandle recycled = scheduler.AddAgent();
            Assert.That(recycled.Index, Is.EqualTo(original.Index));
            Assert.That(recycled.Generation, Is.Not.EqualTo(original.Generation));
            Assert.Throws<InvalidOperationException>(() => scheduler.GetRootState(original));
            Assert.That(scheduler.ActiveAgentCount, Is.EqualTo(1));
        }

        [Test]
        public void Timeout_InvalidatesActionRequestAndRejectsLateCompletion()
        {
            using var tree = CreateTimedActionTree();
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 0, actionSlotCount: 1, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent();

            scheduler.ScheduleTick(0.1f).Complete();
            Assert.That(scheduler.TryGetActionRequest(
                agent,
                0,
                out BTActionRequestHandle firstRequest,
                out ActionRequestStatus firstStatus), Is.True);
            Assert.That(firstStatus, Is.EqualTo(ActionRequestStatus.Requested));

            scheduler.ScheduleTick(0.5f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Failure));
            Assert.That(scheduler.GetActionStatus(agent, 0), Is.EqualTo(ActionRequestStatus.Idle));
            Assert.That(scheduler.TrySetActionStatus(firstRequest, ActionRequestStatus.Success), Is.False);

            scheduler.ResetAgent(agent);
            scheduler.ScheduleTick(0.1f).Complete();
            Assert.That(scheduler.TryGetActionRequest(
                agent,
                0,
                out BTActionRequestHandle secondRequest,
                out ActionRequestStatus secondStatus), Is.True);
            Assert.That(secondStatus, Is.EqualTo(ActionRequestStatus.Requested));
            Assert.That(secondRequest.Generation, Is.Not.EqualTo(firstRequest.Generation));
        }

        [Test]
        public void PublicStateAccess_CompletesOutstandingTickBeforeReading()
        {
            using var tree = CreateConditionTree(expectedValue: 0);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 0, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent();

            scheduler.ScheduleTick(0f);

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void Scheduler_RejectsCrossThreadPublicAccess()
        {
            using var tree = CreateConditionTree(expectedValue: 0);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 0, initialCapacity: 1);
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    scheduler.AddAgent();
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.That(workerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ReactiveSequence_InvalidatesActionWhenEarlierConditionChanges()
        {
            using var tree = CreateReactiveActionTree();
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 1, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent();
            scheduler.SetBBInt(agent, 0, 1);

            scheduler.ScheduleTick(0f).Complete();
            Assert.That(scheduler.TryGetActionRequest(
                agent,
                0,
                out BTActionRequestHandle request,
                out _), Is.True);

            scheduler.SetBBInt(agent, 0, 0);
            scheduler.ScheduleTick(0f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Failure));
            Assert.That(scheduler.GetActionStatus(agent, 0), Is.EqualTo(ActionRequestStatus.Idle));
            Assert.That(scheduler.TrySetActionStatus(request, ActionRequestStatus.Success), Is.False);
        }

        [Test]
        public void EmptyParallelDefinition_ReturnsFailure()
        {
            using var flatTree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Parallel,
                        Flags = FlatNodeDef.PARALLEL_EMPTY_IS_FAILURE,
                        SuccessThreshold = 0,
                        FailureThreshold = 1
                    }
                },
                new[] { 1 });
            using var scheduler = new BTTickScheduler(
                flatTree,
                bbSlotCount: 0,
                actionSlotCount: 0,
                initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent();

            scheduler.ScheduleTick(0f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Failure));
        }

        [Test]
        public void TickInterval_AccumulatesEveryFrameForTimeout()
        {
            using var tree = CreateTimedActionTree();
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 0, actionSlotCount: 1, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 3);

            scheduler.ScheduleTick(0.1f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.1f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Failure));
        }

        [Test]
        public void TickInterval_AccumulatesEveryFrameForDelay()
        {
            using var tree = CreateDelayTree(delaySeconds: 0.5f);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 0, actionSlotCount: 0, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 3);

            scheduler.ScheduleTick(0.1f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.1f).Complete();

            Assert.That(scheduler.GetRootState(agent), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void TickInterval_AccumulatesEveryFrameForPersistentCooldown()
        {
            using var tree = CreateRepeatingCooldownActionTree(cooldownSeconds: 0.5f);
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 0, actionSlotCount: 1, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 3);

            scheduler.ScheduleTick(0.1f).Complete();
            Assert.That(scheduler.TryGetActionRequest(
                agent,
                0,
                out BTActionRequestHandle firstRequest,
                out _), Is.True);
            Assert.That(scheduler.TrySetActionStatus(firstRequest, ActionRequestStatus.Success), Is.True);

            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.1f).Complete();
            Assert.That(scheduler.GetActionStatus(agent, 0), Is.EqualTo(ActionRequestStatus.Idle));

            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.2f).Complete();
            scheduler.ScheduleTick(0.1f).Complete();

            Assert.That(scheduler.TryGetActionRequest(
                agent,
                0,
                out BTActionRequestHandle secondRequest,
                out ActionRequestStatus status), Is.True);
            Assert.That(status, Is.EqualTo(ActionRequestStatus.Requested));
            Assert.That(secondRequest.Generation, Is.Not.EqualTo(firstRequest.Generation));
        }

        [Test]
        public void SchedulerStateHash_CoversBlackboardAuxiliaryActionAndTickGatingState()
        {
            using var tree = CreateTimedActionTree();
            using var scheduler = new BTTickScheduler(tree, bbSlotCount: 1, actionSlotCount: 1, initialCapacity: 1);
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 3);

            scheduler.ScheduleTick(0.1f).Complete();
            ulong initial = scheduler.ComputeAgentStateHash(agent);
            Assert.That(initial, Is.EqualTo(6324122665040042335UL));

            scheduler.SetBBInt(agent, 0, 1);
            ulong withInt = scheduler.ComputeAgentStateHash(agent);
            scheduler.SetBBInt(agent, 0, 0);
            scheduler.SetBBFloat(agent, 0, 1.5f);
            ulong withFloat = scheduler.ComputeAgentStateHash(agent);
            scheduler.SetBBFloat(agent, 0, 0f);
            scheduler.SetBBBool(agent, 0, true);
            ulong withBool = scheduler.ComputeAgentStateHash(agent);
            scheduler.SetBBBool(agent, 0, false);

            scheduler.ScheduleTick(0.2f).Complete();
            ulong withSkippedFrameTime = scheduler.ComputeAgentStateHash(agent);

            scheduler.ResetAgent(agent);
            scheduler.ScheduleTick(0.1f).Complete();
            ulong withNewActionGeneration = scheduler.ComputeAgentStateHash(agent);

            Assert.That(withInt, Is.Not.EqualTo(initial));
            Assert.That(withFloat, Is.Not.EqualTo(initial));
            Assert.That(withBool, Is.Not.EqualTo(initial));
            Assert.That(withSkippedFrameTime, Is.Not.EqualTo(initial));
            Assert.That(withNewActionGeneration, Is.Not.EqualTo(initial));
        }

        [Test]
        public void FlatTree_HasSingleOwnerAndCannotDisposeWhileSchedulerUsesStorage()
        {
            FlatBehaviorTree tree = CreateConditionTree(expectedValue: 0);
            FlatBehaviorTree alias = tree;
            BTTickScheduler scheduler = null;
            try
            {
                FlatBehaviorTree.ReadOnlyBuffer<FlatNodeDef> nodeView = tree.Nodes;
                scheduler = new BTTickScheduler(
                    tree,
                    bbSlotCount: 1,
                    actionSlotCount: 0,
                    initialCapacity: 1);

                Assert.That(typeof(FlatBehaviorTree).IsValueType, Is.False);
                Assert.That(
                    typeof(FlatBehaviorTree.ReadOnlyBuffer<FlatNodeDef>).GetProperty("Item")?.CanWrite,
                    Is.False);
                Assert.Throws<InvalidOperationException>(() => alias.Dispose());
                Assert.That(tree.IsCreated, Is.True);

                scheduler.Dispose();
                scheduler = null;
                tree.Dispose();

                Assert.That(alias.IsCreated, Is.False);
                Assert.Throws<ObjectDisposedException>(() => _ = nodeView[0]);
                Assert.DoesNotThrow(() => alias.Dispose());
            }
            finally
            {
                scheduler?.Dispose();
                if (tree.IsCreated)
                {
                    tree.Dispose();
                }
            }
        }

        [TestCase(FlatNodeType.Inverter)]
        [TestCase(FlatNodeType.Repeater)]
        [TestCase(FlatNodeType.Succeeder)]
        [TestCase(FlatNodeType.ForceFailure)]
        [TestCase(FlatNodeType.Retry)]
        [TestCase(FlatNodeType.Timeout)]
        [TestCase(FlatNodeType.Delay)]
        [TestCase(FlatNodeType.RunOnce)]
        [TestCase(FlatNodeType.CoolDown)]
        public void SingleChildNode_RejectsMissingChild(FlatNodeType nodeType)
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = nodeType
                    }
                },
                new[] { 1 });
            AssertSchedulerConstructionFails(tree);
        }

        [Test]
        public void RootNode_RejectsMissingChild()
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root
                    }
                },
                Array.Empty<int>());

            AssertSchedulerConstructionFails(tree);
        }

        [TestCase(FlatNodeType.Repeater, 0)]
        [TestCase(FlatNodeType.Repeater, -2)]
        [TestCase(FlatNodeType.Retry, 0)]
        [TestCase(FlatNodeType.Retry, -2)]
        public void CountedDecorator_RejectsInvalidExecutionCount(FlatNodeType nodeType, int count)
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1,
                    },
                    new FlatNodeDef
                    {
                        Type = nodeType,
                        ChildStartIndex = 1,
                        ChildCount = 1,
                        ParamInt = count,
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = 1,
                    },
                },
                new[] { 1, 2 });

            AssertSchedulerConstructionFails(tree);
        }

        [TestCase(FlatNodeType.Repeater, -1)]
        [TestCase(FlatNodeType.Repeater, 1)]
        [TestCase(FlatNodeType.Retry, -1)]
        [TestCase(FlatNodeType.Retry, 1)]
        public void CountedDecorator_AcceptsInfiniteOrPositiveExecutionCount(FlatNodeType nodeType, int count)
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1,
                    },
                    new FlatNodeDef
                    {
                        Type = nodeType,
                        ChildStartIndex = 1,
                        ChildCount = 1,
                        ParamInt = count,
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = 1,
                    },
                },
                new[] { 1, 2 });

            Assert.DoesNotThrow(() =>
            {
                using var scheduler = new BTTickScheduler(
                    tree,
                    bbSlotCount: 0,
                    actionSlotCount: 0,
                    initialCapacity: 1);
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void WaitTicks_RejectsTickCountBelowOne(int tickCount)
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1,
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = tickCount,
                    },
                },
                new[] { 1 });

            AssertSchedulerConstructionFails(tree);
        }

        [Test]
        public void FlatTree_RejectsNonRootNodeAtIndexZero()
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Sequence,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = 1
                    }
                },
                new[] { 1 });

            AssertSchedulerConstructionFails(tree);
        }

        [Test]
        public void FlatTree_RejectsSecondRootNode()
        {
            using var tree = new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 1,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = 1
                    }
                },
                new[] { 1, 2 });

            AssertSchedulerConstructionFails(tree);
        }

        private static void AssertSchedulerConstructionFails(FlatBehaviorTree tree)
        {
            BTTickScheduler scheduler = null;
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    scheduler = new BTTickScheduler(
                        tree,
                        bbSlotCount: 0,
                        actionSlotCount: 0,
                        initialCapacity: 1));
            }
            finally
            {
                scheduler?.Dispose();
            }
        }

        private static FlatBehaviorTree CreateConditionTree(int expectedValue)
        {
            return new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.BlackboardCondition,
                        BBKey = 0,
                        Compare = CompareOp.Equal,
                        CompareValue = expectedValue
                    }
                },
                new[] { 1 });
        }

        private static FlatBehaviorTree CreateTimedActionTree()
        {
            return new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Timeout,
                        ChildStartIndex = 1,
                        ChildCount = 1,
                        ParamFloat = 0.5f
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.ActionSlot,
                        ParamInt = 0
                    }
                },
                new[] { 1, 2 });
        }

        private static FlatBehaviorTree CreateReactiveActionTree()
        {
            return new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.ReactiveSequence,
                        ChildStartIndex = 1,
                        ChildCount = 2
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.BlackboardCondition,
                        BBKey = 0,
                        Compare = CompareOp.Equal,
                        CompareValue = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.ActionSlot,
                        ParamInt = 0
                    }
                },
                new[] { 1, 2, 3 });
        }

        private static FlatBehaviorTree CreateDelayTree(float delaySeconds)
        {
            return new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Delay,
                        ChildStartIndex = 1,
                        ChildCount = 1,
                        ParamFloat = delaySeconds
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.WaitTicks,
                        ParamInt = 1
                    }
                },
                new[] { 1, 2 });
        }

        private static FlatBehaviorTree CreateRepeatingCooldownActionTree(float cooldownSeconds)
        {
            return new FlatBehaviorTree(
                new[]
                {
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Root,
                        ChildStartIndex = 0,
                        ChildCount = 1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.Repeater,
                        ChildStartIndex = 1,
                        ChildCount = 1,
                        ParamInt = -1
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.CoolDown,
                        ChildStartIndex = 2,
                        ChildCount = 1,
                        ParamFloat = cooldownSeconds
                    },
                    new FlatNodeDef
                    {
                        Type = FlatNodeType.ActionSlot,
                        ParamInt = 0
                    }
                },
                new[] { 1, 2, 3 });
        }
    }
}
