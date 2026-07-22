using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class BehaviorTreeCodeFirstTests
    {
        private const int HAS_TARGET_KEY = 101;
        private const int HIT_COUNT_KEY = 102;

        [Test]
        public void Builder_ComposesLambdaTree()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Condition(blackboard => blackboard.GetBool(HAS_TARGET_KEY))
                    .Action(blackboard =>
                    {
                        blackboard.SetInt(HIT_COUNT_KEY, blackboard.GetInt(HIT_COUNT_KEY) + 1);
                        return RuntimeState.Success;
                    })
                .End()
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
                Assert.AreEqual(0, tree.Blackboard.GetInt(HIT_COUNT_KEY));

                tree.Blackboard.SetBool(HAS_TARGET_KEY, true);
                tree.Play();
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, tree.Blackboard.GetInt(HIT_COUNT_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_AutoClosesOpenCompositeScopes()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Action(_ => RuntimeState.Success)
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_CannotBeReusedAfterBuild()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Action(_ => RuntimeState.Success);

            RuntimeBehaviorTree tree = builder.Build();

            try
            {
                Assert.Throws<InvalidOperationException>(() => builder.Build());
                Assert.Throws<InvalidOperationException>(() => builder.Action(_ => RuntimeState.Success));
                Assert.Throws<InvalidOperationException>(() => builder.WithTickInterval(2));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Builder_RejectsMultipleRootChildren()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Action(_ => RuntimeState.Success);

            Assert.Throws<InvalidOperationException>(() => builder.Action(_ => RuntimeState.Success));
        }

        [Test]
        public void Builder_RejectsDecoratorWithoutChild()
        {
            var builder = new RuntimeBehaviorTreeBuilder()
                .Inverter();

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Builder_SupportsCommandAndConditionStrategy()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Sequence()
                    .Condition(new BoolKeyConditionStrategy(HAS_TARGET_KEY))
                    .Command(new IncrementIntCommand(HIT_COUNT_KEY))
                .End()
                .Build();

            try
            {
                tree.Blackboard.SetBool(HAS_TARGET_KEY, true);

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, tree.Blackboard.GetInt(HIT_COUNT_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RandomChance_WithInvalidDenominatorFailsFast()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RuntimeBehaviorTreeBuilder().RandomChance(1f, 0f, seed: 1u));
        }

        [Test]
        public void RuntimeDeterministicRandom_ReplaysSameSequence()
        {
            var left = new RuntimeDeterministicRandom(123u);
            var right = new RuntimeDeterministicRandom(123u);

            for (int i = 0; i < 32; i++)
            {
                Assert.AreEqual(left.Next(), right.Next());
            }
        }

        [Test]
        public void SelectorRandom_UsesSelectorFallbackSemantics()
        {
            RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .SelectorRandom(seed: 7u)
                    .Action(_ => RuntimeState.Failure)
                    .Action(blackboard =>
                    {
                        blackboard.SetBool(HAS_TARGET_KEY, true);
                        return RuntimeState.Success;
                    })
                .End()
                .Build();

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.IsTrue(tree.Blackboard.GetBool(HAS_TARGET_KEY));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void BlackboardReadFrom_RejectsExcessiveCountsWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(2);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY + 1);
                writer.Write(2);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(
                reader,
                new RuntimeBlackboardSerializationLimits(maxEntriesPerType: 1, maxTotalEntries: 1)));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardReadFrom_RejectsDuplicatePrimitiveKeysWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY);
                writer.Write(1f);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(reader));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardReadFrom_RejectsStampWithoutValueWithoutClearingExistingState()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 9);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1UL);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(1);
                writer.Write(HIT_COUNT_KEY + 1);
                writer.Write(1UL);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<InvalidDataException>(() => blackboard.ReadFrom(reader));
            Assert.AreEqual(9, blackboard.GetInt(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardDelta_RejectsExcessiveCounts()
        {
            var blackboard = new RuntimeBlackboard();

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteDeltaHeader(writer, entryCount: 2, bodyLength: 9);
                writer.Write(HIT_COUNT_KEY);
                writer.Write((byte)0);
                writer.Write(1);
            }

            byte[] patch = stream.ToArray();

            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(blackboard, patch, maxPatchEntries: 1));
            Assert.IsFalse(blackboard.HasKey(HIT_COUNT_KEY));
        }

        [Test]
        public void BlackboardDelta_RejectsTrailingBytesWithoutMutation()
        {
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(HIT_COUNT_KEY, 7);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteDeltaHeader(writer, entryCount: 1, bodyLength: 9);
                writer.Write(HIT_COUNT_KEY);
                writer.Write((byte)0);
                writer.Write(99);
                writer.Write((byte)0x7F);
            }

            byte[] patch = stream.ToArray();

            Assert.Throws<InvalidDataException>(() => BTBlackboardDelta.Apply(blackboard, patch));
            Assert.AreEqual(7, blackboard.GetInt(HIT_COUNT_KEY));
        }

        private static void WriteDeltaHeader(BinaryWriter writer, int entryCount, int bodyLength)
        {
            writer.Write(0x50445442u);
            writer.Write((ushort)1);
            writer.Write((ushort)16);
            writer.Write(bodyLength);
            writer.Write(entryCount);
        }

        [Test]
        public void RuntimeNode_ReportsCompletedAbortedFaultedAndResetLifecycle()
        {
            using var blackboard = new RuntimeBlackboard();
            var completed = new ScriptedNode(RuntimeState.Success);

            Assert.AreEqual(RuntimeState.Success, completed.Run(blackboard));
            Assert.AreEqual(RuntimeNodeExitReason.Completed, completed.LastExitReason);
            Assert.IsFalse(completed.IsStarted);

            var aborted = new ScriptedNode(RuntimeState.Running);
            Assert.AreEqual(RuntimeState.Running, aborted.Run(blackboard));
            aborted.Abort(blackboard);
            Assert.AreEqual(RuntimeNodeExitReason.Aborted, aborted.LastExitReason);
            Assert.AreEqual(RuntimeState.NotEntered, aborted.State);
            Assert.IsFalse(aborted.IsStarted);

            var faulted = new FaultingNode();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => faulted.Run(blackboard));
            Assert.AreSame(exception, faulted.FaultException);
            Assert.AreEqual(RuntimeNodeExitReason.Faulted, faulted.LastExitReason);
            Assert.AreEqual(RuntimeState.Failure, faulted.State);
            Assert.IsFalse(faulted.IsStarted);

            completed.Reset(blackboard);
            Assert.AreEqual(1, completed.ResetCount);
            Assert.AreEqual(RuntimeState.NotEntered, completed.State);
        }

        [Test]
        public void RuntimeNode_RejectsNotEnteredFromOnRunWithoutPoisoningStartedState()
        {
            using var blackboard = new RuntimeBlackboard();
            var node = new ScriptedNode(RuntimeState.NotEntered);

            Assert.Throws<InvalidOperationException>(() => node.Run(blackboard));

            Assert.IsFalse(node.IsStarted);
            Assert.AreEqual(RuntimeState.Failure, node.State);
            Assert.AreEqual(RuntimeNodeExitReason.Faulted, node.LastExitReason);
        }

        [Test]
        public void RunOnce_CachesCompletionUntilExplicitReset()
        {
            using var blackboard = new RuntimeBlackboard();
            var child = new ScriptedNode(RuntimeState.Success);
            var runOnce = new RuntimeRunOnceNode { Child = child };
            runOnce.OnAwake();

            Assert.AreEqual(RuntimeState.Success, runOnce.Run(blackboard));
            Assert.AreEqual(RuntimeState.Success, runOnce.Run(blackboard));
            runOnce.Abort(blackboard);
            Assert.AreEqual(RuntimeState.Success, runOnce.Run(blackboard));
            Assert.AreEqual(1, child.RunCount);

            runOnce.Reset(blackboard);
            Assert.AreEqual(RuntimeState.Success, runOnce.Run(blackboard));
            Assert.AreEqual(2, child.RunCount);
        }

        [Test]
        public void Parallel_DefaultDoesNotRetickCompletedChildren()
        {
            var completedImmediately = new ScriptedNode(RuntimeState.Success);
            var completesLater = new ScriptedNode(RuntimeState.Running, RuntimeState.Success);
            RuntimeBehaviorTree tree = CreateTree(CreateParallel(
                RuntimeParallelMode.Default,
                completedImmediately,
                completesLater));

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, completedImmediately.RunCount);
                Assert.AreEqual(2, completesLater.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SelectorRandom_SeedSetterRebuildsGeneratorAndFreezesSetupPolicy()
        {
            var visitOrder = new List<int>(5);
            var selector = new RuntimeSelectorRandom(123u)
            {
                Seed = 456u,
                ShuffleOnStart = true,
            };

            for (int i = 0; i < 5; i++)
            {
                selector.AddChild(new RecordingFailureNode(i, visitOrder));
            }

            using RuntimeBehaviorTree tree = CreateTree(selector);

            Assert.That(tree.Tick(), Is.EqualTo(RuntimeState.Failure));
            CollectionAssert.AreEqual(new[] { 3, 4, 2, 1, 0 }, visitOrder);
            Assert.Throws<InvalidOperationException>(() => selector.Seed = 789u);
            Assert.Throws<InvalidOperationException>(() => selector.ShuffleOnStart = false);
        }

        [Test]
        public void Parallel_UntilAnyFailureReturnsFailureAndAbortsRunningSibling()
        {
            var running = new ScriptedNode(RuntimeState.Running);
            var failure = new ScriptedNode(RuntimeState.Failure);
            RuntimeBehaviorTree tree = CreateTree(CreateParallel(
                RuntimeParallelMode.UntilAnyFailure,
                running,
                failure));

            try
            {
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
                Assert.AreEqual(RuntimeNodeExitReason.Aborted, running.LastExitReason);
                Assert.IsFalse(running.IsStarted);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void ParallelAll_TracksTerminalChildrenAndUsesFailurePrecedence()
        {
            var success = new ScriptedNode(RuntimeState.Success);
            var runningThenFailure = new ScriptedNode(RuntimeState.Running, RuntimeState.Failure);
            var parallel = new RuntimeParallelAllNode
            {
                SuccessThreshold = 2,
                FailureThreshold = 1
            };
            parallel.AddChild(success);
            parallel.AddChild(runningThenFailure);
            parallel.Seal();
            RuntimeBehaviorTree tree = CreateTree(parallel);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
                Assert.AreEqual(1, success.RunCount);
                Assert.AreEqual(2, runningThenFailure.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Switch_ChangingCaseAbortsPreviousRunningBranch()
        {
            const int switchKey = 2001;
            var first = new ScriptedNode(RuntimeState.Running);
            var second = new ScriptedNode(RuntimeState.Success);
            var defaultBranch = new ScriptedNode(RuntimeState.Failure);
            var switchNode = new RuntimeSwitchNode { VariableKeyHash = switchKey };
            switchNode.AddChild(first);
            switchNode.AddChild(second);
            switchNode.AddChild(defaultBranch);
            switchNode.Seal();
            RuntimeBehaviorTree tree = CreateTree(switchNode);

            try
            {
                tree.Blackboard.SetInt(switchKey, 0);
                Assert.AreEqual(RuntimeState.Running, tree.Tick());

                tree.Blackboard.SetInt(switchKey, 1);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(RuntimeNodeExitReason.Aborted, first.LastExitReason);
                Assert.AreEqual(1, second.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void ScopedNodesUseTheSameBlackboardForRunAndAbort()
        {
            var scopedChild = new ScriptedNode(RuntimeState.Running);
            var scopedNode = new RuntimeBlackboardNode { Child = scopedChild };
            RuntimeBehaviorTree tree = CreateTree(scopedNode);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                tree.Stop();

                Assert.AreSame(scopedChild.RunBlackboard, scopedChild.ExitBlackboard);
                Assert.AreEqual(RuntimeNodeExitReason.Aborted, scopedChild.LastExitReason);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SubTree_ValidatesRemappingAndCommitsTypedOutputOnCompletion()
        {
            const int localKey = 2101;
            const int parentKey = 2102;
            var child = new BlackboardWriteNode(localKey, 42L);
            var subtree = new RuntimeSubTreeNode { Child = child };

            Assert.Throws<ArgumentException>(() =>
                subtree.SetPortRemapping(new[] { localKey }, Array.Empty<int>()));
            Assert.Throws<ArgumentException>(() =>
                subtree.SetPortRemapping(new[] { localKey }, new[] { parentKey }, Array.Empty<byte>()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                subtree.SetPortRemapping(new[] { localKey }, new[] { parentKey }, new byte[] { 9 }));
            Assert.Throws<ArgumentException>(() =>
                subtree.SetPortRemapping(
                    new[] { localKey },
                    new[] { parentKey },
                    new byte[] { 6 },
                    Array.Empty<RuntimeSubTreePortDirection>()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                subtree.SetPortRemapping(
                    new[] { localKey },
                    new[] { parentKey },
                    new byte[] { 6 },
                    new[] { (RuntimeSubTreePortDirection)0 }));

            subtree.SetPortRemapping(new[] { localKey }, new[] { parentKey }, new byte[] { 6 });
            RuntimeBehaviorTree tree = CreateTree(subtree);

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(42L, tree.Blackboard.GetLong(parentKey));
                Assert.AreSame(child.RunBlackboard, child.ExitBlackboard);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SubTree_LegacyMappingPreservesMultiTickInOutState()
        {
            const int localKey = 2201;
            const int parentKey = 2202;
            var child = new IncrementIntUntilNode(localKey, targetValue: 7);
            var subtree = new RuntimeSubTreeNode { Child = child };
            subtree.SetPortRemapping(new[] { localKey }, new[] { parentKey });
            RuntimeBehaviorTree tree = CreateTree(subtree);
            tree.Blackboard.SetInt(parentKey, 5);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(5, tree.Blackboard.GetInt(parentKey));

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(7, tree.Blackboard.GetInt(parentKey));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SubTree_InputPortRefreshesEveryStepWithoutCommittingLocalWrites()
        {
            const int localKey = 2301;
            const int parentKey = 2302;
            var child = new ObserveAndMutateInputNode(localKey);
            var subtree = new RuntimeSubTreeNode { Child = child };
            subtree.SetPortRemapping(
                new[] { localKey },
                new[] { parentKey },
                portTypes: null,
                portDirections: new[] { RuntimeSubTreePortDirection.Input });
            RuntimeBehaviorTree tree = CreateTree(subtree);
            tree.Blackboard.SetInt(parentKey, 10);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                tree.Blackboard.SetInt(parentKey, 20);

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(10, child.FirstObservedValue);
                Assert.AreEqual(20, child.SecondObservedValue);
                Assert.AreEqual(20, tree.Blackboard.GetInt(parentKey));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SubTree_OutputPortDoesNotCopyMappedParentValue()
        {
            const int localKey = 2401;
            const int parentKey = 2402;
            var child = new IncrementIntUntilNode(localKey, targetValue: 2);
            var subtree = new RuntimeSubTreeNode { Child = child };
            subtree.SetPortRemapping(
                new[] { localKey },
                new[] { parentKey },
                portTypes: null,
                portDirections: new[] { RuntimeSubTreePortDirection.Output });
            RuntimeBehaviorTree tree = CreateTree(subtree);
            tree.Blackboard.SetInt(parentKey, 99);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(99, tree.Blackboard.GetInt(parentKey));

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(2, tree.Blackboard.GetInt(parentKey));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void Service_UsesInjectedTimeProvider()
        {
            var services = new TestServices { Time = 10d, RandomFraction = 0.5f };
            var context = new RuntimeBTContext(serviceResolver: services);
            var child = new ScriptedNode(RuntimeState.Running);
            int serviceTicks = 0;
            var service = new RuntimeServiceNode
            {
                Child = child,
                Interval = 1f,
                OnServiceTick = _ => serviceTicks++
            };
            RuntimeBehaviorTree tree = CreateTree(service, context);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, serviceTicks);

                services.Time = 10.5d;
                tree.Tick();
                Assert.AreEqual(1, serviceTicks);

                services.Time = 11d;
                tree.Tick();
                Assert.AreEqual(2, serviceTicks);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void CoolDown_AllowsFirstRunAtZeroAndExactBoundary()
        {
            var services = new TestServices { Time = 0d };
            var context = new RuntimeBTContext(serviceResolver: services);
            var child = new ScriptedNode(RuntimeState.Success, RuntimeState.Success);
            var coolDown = new RuntimeCoolDownNode();
            Assert.Throws<ArgumentOutOfRangeException>(() => coolDown.CoolDown = -1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => coolDown.CoolDown = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => coolDown.CoolDown = float.PositiveInfinity);
            coolDown.Child = child;
            coolDown.CoolDown = 2f;
            RuntimeBehaviorTree tree = CreateTree(coolDown, context);

            try
            {
                Assert.Throws<InvalidOperationException>(() => coolDown.CoolDown = 3f);
                Assert.Throws<InvalidOperationException>(() => coolDown.ResetOnSuccess = true);

                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, child.RunCount);

                tree.Play();
                services.Time = 1.999d;
                Assert.AreEqual(RuntimeState.Failure, tree.Tick());
                Assert.AreEqual(1, child.RunCount);

                tree.Play();
                services.Time = 2d;
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(2, child.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RandomChance_HandlesClosedProbabilityBoundariesWithoutSampling()
        {
            var services = new TestServices { RandomFraction = 0f };
            var blackboard = new RuntimeBlackboard
            {
                Context = new RuntimeBTContext(serviceResolver: services)
            };

            try
            {
                Assert.IsFalse(new RuntimeRandomChanceNode(0f, 1f).Evaluate(blackboard));
                services.RandomFraction = 1f;
                Assert.IsTrue(new RuntimeRandomChanceNode(1f, 1f).Evaluate(blackboard));
                Assert.AreEqual(0, services.RandomCalls);
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test]
        public void ProbabilityBranch_ZeroWeightCannotWinAtZeroSample()
        {
            var services = new TestServices { RandomFraction = 0f };
            var first = new ScriptedNode(RuntimeState.Success);
            var second = new ScriptedNode(RuntimeState.Success);
            var probability = new RuntimeProbabilityBranch();
            probability.SetWeights(new[] { 0f, 1f });
            probability.AddChild(first);
            probability.AddChild(second);
            probability.Seal();
            RuntimeBehaviorTree tree = CreateTree(
                probability,
                new RuntimeBTContext(serviceResolver: services));

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(0, first.RunCount);
                Assert.AreEqual(1, second.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_SetContextUpdatesTreeAndBlackboardAtomically()
        {
            var firstContext = new RuntimeBTContext();
            var secondContext = new RuntimeBTContext();
            RuntimeBehaviorTree tree = CreateTree(new ScriptedNode(RuntimeState.Running), firstContext);

            try
            {
                tree.SetContext(secondContext);
                Assert.AreSame(secondContext, tree.Context);
                Assert.AreSame(secondContext, tree.Blackboard.Context);

                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.Throws<InvalidOperationException>(() => tree.SetContext(firstContext));
                Assert.AreSame(secondContext, tree.Context);
                Assert.AreSame(secondContext, tree.Blackboard.Context);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_LifecycleRejectsNonOwnerThread()
        {
            RuntimeBehaviorTree tree = CreateTree(new ScriptedNode(RuntimeState.Running));

            try
            {
                Exception exception = Task.Run(() =>
                {
                    try
                    {
                        tree.Tick();
                        return null;
                    }
                    catch (Exception caught)
                    {
                        return caught;
                    }
                }).Result;

                Assert.IsInstanceOf<InvalidOperationException>(exception);
                Assert.AreEqual(RuntimeState.NotEntered, tree.State);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_DisposeIsIdempotentAndClosesLifecycle()
        {
            RuntimeBehaviorTree tree = CreateTree(new ScriptedNode(RuntimeState.Running));

            tree.Dispose();
            tree.Dispose();

            Assert.IsTrue(tree.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => tree.Tick());
            Assert.Throws<ObjectDisposedException>(() => tree.Play());
        }

        [Test]
        public void RuntimeBehaviorTree_FreezesOwnedTopologyAndSetup()
        {
            var leaf = new ScriptedNode(RuntimeState.Success) { GUID = "leaf" };
            var composite = new RuntimeSelector { AbortType = RuntimeAbortType.Self };
            composite.AddChild(leaf);
            var decorator = new RuntimeRunOnceNode { Child = composite };
            var root = new RuntimeRootNode { Child = decorator, GUID = "root" };
            var tree = new RuntimeBehaviorTree(
                root,
                new RuntimeBlackboard(),
                new RuntimeBTContext());

            try
            {
                Assert.That(composite.Children.Count, Is.EqualTo(1));
                Assert.That(composite.Children[0], Is.SameAs(leaf));
                Assert.That(composite.Children, Is.Not.InstanceOf<RuntimeNode[]>());
                Assert.That(
                    composite.Children is System.Collections.Generic.IList<RuntimeNode>,
                    Is.False);
                Assert.Throws<InvalidOperationException>(() => root.Child = leaf);
                Assert.Throws<InvalidOperationException>(() => decorator.Child = leaf);
                Assert.Throws<InvalidOperationException>(() => composite.AddChild(new ScriptedNode(RuntimeState.Success)));
                Assert.Throws<InvalidOperationException>(() => composite.Seal());
                Assert.Throws<InvalidOperationException>(() => composite.AbortType = RuntimeAbortType.Both);
                Assert.Throws<InvalidOperationException>(() => leaf.GUID = "changed");
                Assert.Throws<InvalidOperationException>(() =>
                    leaf.AddPreCondition(_ => true));
                Assert.Throws<InvalidOperationException>(() =>
                    leaf.AddPostCondition(_ => true));
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeComposite_AddChildAfterSealFailsExplicitly()
        {
            var composite = new RuntimeSelector();
            composite.Seal();

            Assert.Throws<InvalidOperationException>(() =>
                composite.AddChild(new ScriptedNode(RuntimeState.Success)));
        }

        [Test]
        public void RuntimeProbabilityBranch_FreezesSetupArraysAndSeedAfterOwnership()
        {
            var probability = new RuntimeProbabilityBranch { DeterministicSeedKey = 91 };
            probability.SetWeights(new[] { 1f });
            probability.AddChild(new ScriptedNode(RuntimeState.Success));
            RuntimeBehaviorTree tree = CreateTree(probability);

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    probability.SetWeights(new[] { 2f }));
                Assert.Throws<InvalidOperationException>(() =>
                    probability.DeterministicSeedKey = 92);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsCyclicRuntimeGraphsBeforeAwake()
        {
            var root = new RuntimeRootNode();
            root.Child = root;
            using var blackboard = new RuntimeBlackboard();

            Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext()));
        }

        [Test]
        public void RuntimeBehaviorTree_ValidationFailureLeavesGraphMutableForRepairAndRetry()
        {
            var selector = new RuntimeSelector();
            var decorator = new RuntimeRunOnceNode();
            var root = new RuntimeRootNode { Child = selector };
            selector.AddChild(decorator);
            decorator.Child = selector;
            using var blackboard = new RuntimeBlackboard();

            Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext()));

            decorator.Child = new ScriptedNode(RuntimeState.Success);
            Assert.DoesNotThrow(() =>
                selector.AddChild(new ScriptedNode(RuntimeState.Failure)));

            using var tree = new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext());
            Assert.That(tree.Tick(), Is.EqualTo(RuntimeState.Success));
            Assert.Throws<InvalidOperationException>(() =>
                selector.AddChild(new ScriptedNode(RuntimeState.Success)));
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsDisposedNodesWithoutTakingBlackboardOwnership()
        {
            var root = new RuntimeRootNode
            {
                Child = new ScriptedNode(RuntimeState.Success),
            };
            var firstBlackboard = new RuntimeBlackboard();
            var firstTree = new RuntimeBehaviorTree(root, firstBlackboard, new RuntimeBTContext());
            firstTree.Dispose();

            using var secondBlackboard = new RuntimeBlackboard();
            Assert.Throws<ObjectDisposedException>(() =>
                new RuntimeBehaviorTree(root, secondBlackboard, new RuntimeBTContext()));
            Assert.IsFalse(secondBlackboard.IsDisposed);
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsDuplicateRuntimeGuidsBeforeTakingOwnership()
        {
            var selector = new RuntimeSelector { GUID = "selector" };
            var left = new ScriptedNode(RuntimeState.Success) { GUID = "duplicate" };
            var right = new ScriptedNode(RuntimeState.Failure) { GUID = "duplicate" };
            selector.AddChild(left);
            selector.AddChild(right);
            var root = new RuntimeRootNode { Child = selector, GUID = "root" };
            using var blackboard = new RuntimeBlackboard();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext()));

            Assert.That(exception.Message, Does.Contain("GUID"));
            Assert.That(blackboard.IsDisposed, Is.False);
            right.GUID = "right";
            using var repairedTree = new RuntimeBehaviorTree(root, blackboard, new RuntimeBTContext());
            Assert.That(repairedTree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void RepeatAndRetry_StartFreshChildActivationsWithoutAbortAfterCompletion()
        {
            var repeatedChild = new ScriptedNode(RuntimeState.Success);
            var repeat = new RuntimeRepeatNode
            {
                Child = repeatedChild,
                RepeatForever = false,
                RepeatCount = 3
            };
            using (RuntimeBehaviorTree repeatTree = CreateTree(repeat))
            {
                Assert.That(repeatTree.Tick(), Is.EqualTo(RuntimeState.Running));
                Assert.That(repeatTree.Tick(), Is.EqualTo(RuntimeState.Running));
                Assert.That(repeatTree.Tick(), Is.EqualTo(RuntimeState.Success));
            }

            Assert.That(repeatedChild.RunCount, Is.EqualTo(3));
            Assert.That(repeatedChild.LastExitReason, Is.EqualTo(RuntimeNodeExitReason.Completed));

            var retriedChild = new ScriptedNode(
                RuntimeState.Failure,
                RuntimeState.Failure,
                RuntimeState.Success);
            var retry = new RuntimeRetryNode
            {
                Child = retriedChild,
                MaxAttempts = 3
            };
            using (RuntimeBehaviorTree retryTree = CreateTree(retry))
            {
                Assert.That(retryTree.Tick(), Is.EqualTo(RuntimeState.Running));
                Assert.That(retryTree.Tick(), Is.EqualTo(RuntimeState.Running));
                Assert.That(retryTree.Tick(), Is.EqualTo(RuntimeState.Success));
            }

            Assert.That(retriedChild.RunCount, Is.EqualTo(3));
            Assert.That(retriedChild.LastExitReason, Is.EqualTo(RuntimeNodeExitReason.Completed));
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsGraphsBeyondConfiguredDepth()
        {
            RuntimeNode child = new ScriptedNode(RuntimeState.Success);
            for (int i = 0; i < 4; i++)
            {
                child = new RuntimeRunOnceNode { Child = child };
            }

            var root = new RuntimeRootNode { Child = child };
            using var blackboard = new RuntimeBlackboard();

            Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(
                    root,
                    blackboard,
                    new RuntimeBTContext(),
                    new RuntimeBehaviorTreeLimits(maxNodeCount: 16, maxDepth: 3)));
        }

        [Test]
        public void RuntimeBehaviorTree_RejectsGraphsBeyondConfiguredNodeCount()
        {
            var sequence = new RuntimeSequencer();
            sequence.AddChild(new ScriptedNode(RuntimeState.Success));
            sequence.AddChild(new ScriptedNode(RuntimeState.Success));
            sequence.Seal();
            var root = new RuntimeRootNode { Child = sequence };
            using var blackboard = new RuntimeBlackboard();

            Assert.Throws<InvalidOperationException>(() =>
                new RuntimeBehaviorTree(
                    root,
                    blackboard,
                    new RuntimeBTContext(),
                    new RuntimeBehaviorTreeLimits(maxNodeCount: 3, maxDepth: 8)));
        }

        private static RuntimeBehaviorTree CreateTree(RuntimeNode child, RuntimeBTContext context = null)
        {
            context ??= new RuntimeBTContext();
            var blackboard = new RuntimeBlackboard { Context = context };
            var root = new RuntimeRootNode { Child = child };
            return new RuntimeBehaviorTree(root, blackboard, context);
        }

        private static RuntimeParallelNode CreateParallel(RuntimeParallelMode mode, params RuntimeNode[] children)
        {
            var parallel = new RuntimeParallelNode { Mode = mode };
            for (int i = 0; i < children.Length; i++)
            {
                parallel.AddChild(children[i]);
            }

            parallel.Seal();
            return parallel;
        }

        private sealed class ScriptedNode : RuntimeNode
        {
            private readonly RuntimeState[] _states;

            public ScriptedNode(params RuntimeState[] states)
            {
                _states = states;
            }

            public int RunCount { get; private set; }
            public int ResetCount { get; private set; }
            public RuntimeNodeExitReason? LastExitReason { get; private set; }
            public RuntimeBlackboard RunBlackboard { get; private set; }
            public RuntimeBlackboard ExitBlackboard { get; private set; }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                RunBlackboard = blackboard;
                int index = Math.Min(RunCount, _states.Length - 1);
                RunCount++;
                return _states[index];
            }

            protected override void OnExit(
                RuntimeBlackboard blackboard,
                RuntimeNodeExitReason reason,
                Exception exception)
            {
                ExitBlackboard = blackboard;
                LastExitReason = reason;
            }

            protected override void OnReset(RuntimeBlackboard blackboard)
            {
                ResetCount++;
                LastExitReason = null;
            }
        }

        private sealed class RecordingFailureNode : RuntimeNode
        {
            private readonly int _id;
            private readonly List<int> _visitOrder;

            public RecordingFailureNode(int id, List<int> visitOrder)
            {
                _id = id;
                _visitOrder = visitOrder;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                _visitOrder.Add(_id);
                return RuntimeState.Failure;
            }
        }

        private sealed class FaultingNode : RuntimeNode
        {
            public RuntimeNodeExitReason? LastExitReason { get; private set; }
            public Exception FaultException { get; private set; }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                throw new InvalidOperationException("Expected test fault.");
            }

            protected override void OnExit(
                RuntimeBlackboard blackboard,
                RuntimeNodeExitReason reason,
                Exception exception)
            {
                LastExitReason = reason;
                FaultException = exception;
            }
        }

        private sealed class BlackboardWriteNode : RuntimeNode
        {
            private readonly int _key;
            private readonly long _value;

            public BlackboardWriteNode(int key, long value)
            {
                _key = key;
                _value = value;
            }

            public RuntimeBlackboard RunBlackboard { get; private set; }
            public RuntimeBlackboard ExitBlackboard { get; private set; }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                RunBlackboard = blackboard;
                blackboard.SetLong(_key, _value);
                return RuntimeState.Success;
            }

            protected override void OnExit(
                RuntimeBlackboard blackboard,
                RuntimeNodeExitReason reason,
                Exception exception)
            {
                ExitBlackboard = blackboard;
            }
        }

        private sealed class IncrementIntUntilNode : RuntimeNode
        {
            private readonly int _key;
            private readonly int _targetValue;

            public IncrementIntUntilNode(int key, int targetValue)
            {
                _key = key;
                _targetValue = targetValue;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                int value = blackboard.GetInt(_key) + 1;
                blackboard.SetInt(_key, value);
                return value >= _targetValue ? RuntimeState.Success : RuntimeState.Running;
            }
        }

        private sealed class ObserveAndMutateInputNode : RuntimeNode
        {
            private readonly int _key;
            private int _runCount;

            public ObserveAndMutateInputNode(int key)
            {
                _key = key;
            }

            public int FirstObservedValue { get; private set; }
            public int SecondObservedValue { get; private set; }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                int value = blackboard.GetInt(_key);
                if (_runCount == 0)
                {
                    FirstObservedValue = value;
                }
                else
                {
                    SecondObservedValue = value;
                }

                _runCount++;
                blackboard.SetInt(_key, value + 100);
                return _runCount >= 2 ? RuntimeState.Success : RuntimeState.Running;
            }
        }

        private sealed class TestServices :
            IRuntimeBTServiceResolver,
            IRuntimeBTTimeProvider,
            IRuntimeBTRandomProvider
        {
            public double Time { get; set; }
            public float RandomFraction { get; set; }
            public int RandomCalls { get; private set; }

            public double TimeAsDouble => Time;
            public double UnscaledTimeAsDouble => Time;

            public T Resolve<T>() where T : class
            {
                return this as T;
            }

            public float Range(float minInclusive, float maxInclusive)
            {
                RandomCalls++;
                return minInclusive + ((maxInclusive - minInclusive) * RandomFraction);
            }
        }

        private sealed class IncrementIntCommand : IRuntimeBTCommand
        {
            private readonly int _key;

            public IncrementIntCommand(int key)
            {
                _key = key;
            }

            public RuntimeState Execute(RuntimeBlackboard blackboard)
            {
                blackboard.SetInt(_key, blackboard.GetInt(_key) + 1);
                return RuntimeState.Success;
            }
        }

        private sealed class BoolKeyConditionStrategy : IRuntimeBTConditionStrategy
        {
            private readonly int _key;

            public BoolKeyConditionStrategy(int key)
            {
                _key = key;
            }

            public bool Evaluate(RuntimeBlackboard blackboard)
            {
                return blackboard.GetBool(_key);
            }
        }
    }
}
