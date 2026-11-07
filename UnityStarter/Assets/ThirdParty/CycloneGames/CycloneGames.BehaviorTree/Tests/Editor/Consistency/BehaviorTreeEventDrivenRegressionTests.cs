using System.Threading.Tasks;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Tests.Editor.Framework;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BehaviorTreeEventDrivenRegressionTests
    {
        private sealed class ConditionalFailingNode : RuntimeNode
        {
            public override bool CanEvaluate => true;
            public override bool Evaluate(RuntimeBlackboard blackboard) => true;
            protected override RuntimeState OnRun(RuntimeBlackboard blackboard) => RuntimeState.Failure;
        }

        [Test]
        public void SelectorTakeover_FallingHighPriorityChildContinuesScan()
        {
            var high = new ConditionalFailingNode();
            var low = new FixedStateNode(RuntimeState.Running);
            var selector = BehaviorTreeTestFactory.CreateSelector(high, low);
            selector.AbortType = RuntimeAbortType.LowerPriority;
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(RuntimeState.Running, tree.Tick(), "tick " + i);
                }

                Assert.IsFalse(tree.IsStopped);
                Assert.AreEqual(4, low.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SelectorTakeover_BothModeFallingHighPriorityChildContinuesScan()
        {
            var high = new ConditionalFailingNode();
            var low = new FixedStateNode(RuntimeState.Running);
            var selector = BehaviorTreeTestFactory.CreateSelector(high, low);
            selector.AbortType = RuntimeAbortType.Both;
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.IsFalse(tree.IsStopped);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void UnseededSelectorRandom_IsReproducibleAcrossConstructions()
        {
            ulong expected = 0;
            for (int repeat = 0; repeat < 5; repeat++)
            {
                var selector = new RuntimeSelectorRandom(0u);
                for (int i = 0; i < 8; i++)
                {
                    selector.AddChild(new FixedStateNode(
                        i == 7 ? RuntimeState.Success : RuntimeState.Failure));
                }
                selector.Seal();
                var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);
                ulong hash = 14695981039346656037UL;
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        hash = (hash ^ (uint)(int)tree.Tick()) * 1099511628211UL;
                        hash = (hash ^ (uint)selector.CurrentIndex) * 1099511628211UL;
                    }
                }
                finally
                {
                    tree.Dispose();
                }

                if (repeat == 0) expected = hash;
                else Assert.AreEqual(expected, hash, "repeat " + repeat);
            }
        }

        [Test]
        public void UnseededProbabilityBranch_IsReproducibleAcrossConstructions()
        {
            ulong expected = 0;
            for (int repeat = 0; repeat < 5; repeat++)
            {
                var branch = new RuntimeProbabilityBranch();
                branch.SetWeights(new float[] { 1f, 2f, 3f });
                for (int i = 0; i < 3; i++) branch.AddChild(new FixedStateNode(RuntimeState.Success));
                branch.Seal();
                var tree = BehaviorTreeTestFactory.CreateRuntimeTree(branch);
                ulong hash = 14695981039346656037UL;
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        hash = (hash ^ (uint)(int)tree.Tick()) * 1099511628211UL;
                        hash = (hash ^ (uint)branch.SelectedBranch) * 1099511628211UL;
                    }
                }
                finally
                {
                    tree.Dispose();
                }

                if (repeat == 0) expected = hash;
                else Assert.AreEqual(expected, hash, "repeat " + repeat);
            }
        }

        [Test]
        public void EvaluationController_CoalescesRequestsAndHonorsModes()
        {
            var controller = new RuntimeBTEvaluationController();
            int lastSeen = 0;
            int pollCounter = 0;

            Assert.IsTrue(controller.ShouldReevaluate(ref lastSeen, ref pollCounter,
                RuntimeReevaluationMode.PollEveryTick, 8));
            Assert.IsFalse(controller.ShouldReevaluate(ref lastSeen, ref pollCounter,
                RuntimeReevaluationMode.OnChange, 8));

            controller.RequestReevaluation();
            controller.RequestReevaluation();
            controller.RequestReevaluation();
            Assert.AreEqual(3, controller.Epoch);
            Assert.IsTrue(controller.ShouldReevaluate(ref lastSeen, ref pollCounter,
                RuntimeReevaluationMode.OnChange, 8));
            Assert.IsFalse(controller.ShouldReevaluate(ref lastSeen, ref pollCounter,
                RuntimeReevaluationMode.OnChange, 8));

            int hybridSeen = controller.Epoch;
            int hybridPoll = 0;
            for (int i = 0; i < 7; i++)
            {
                Assert.IsFalse(controller.ShouldReevaluate(ref hybridSeen, ref hybridPoll,
                    RuntimeReevaluationMode.Hybrid, 8), "hybrid tick " + i);
            }
            Assert.IsTrue(controller.ShouldReevaluate(ref hybridSeen, ref hybridPoll,
                RuntimeReevaluationMode.Hybrid, 8));
        }

        [Test]
        public void EvaluationController_ConcurrentRequestsAreLossless()
        {
            var controller = new RuntimeBTEvaluationController();
            Parallel.For(0, 8, _ =>
            {
                for (int i = 0; i < 10000; i++) controller.RequestReevaluation();
            });
            Assert.AreEqual(80000, controller.Epoch);
        }

        [Test]
        public void BlackboardObserverDecorator_TriggersReevaluationAndTakeover()
        {
            const int key = 9001;
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(key, 0);
            var trackedChild = new FixedStateNode(RuntimeState.Running);
            var decorator = new RuntimeBBComparisonNode
            {
                KeyHash = key,
                Operator = BBComparisonOp.Equal,
                ValueType = BehaviorTreeComparisonValueType.Int,
                RefInt = 1,
                AbortMode = RuntimeAbortType.Both,
                Child = trackedChild,
            };
            var low = new FixedStateNode(RuntimeState.Running);
            var selector = new RuntimeSelector
            {
                AbortType = RuntimeAbortType.LowerPriority,
                ReevaluationMode = RuntimeReevaluationMode.OnChange,
            };
            selector.AddChild(decorator);
            selector.AddChild(low);
            selector.Seal();
            var root = new RuntimeRootNode { Child = selector };
            var tree = new RuntimeBehaviorTree(root, blackboard);

            try
            {
                int epochBefore = tree.Evaluation.Epoch;
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(0, trackedChild.RunCount);
                Assert.GreaterOrEqual(low.RunCount, 1);

                blackboard.SetInt(key, 1);
                Assert.Greater(tree.Evaluation.Epoch, epochBefore);
                Assert.IsTrue(tree.HasWakeUpRequest);

                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, trackedChild.RunCount);
                Assert.IsFalse(tree.IsStopped);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void BlackboardObserverDecorator_NoneModeDoesNotSubscribe()
        {
            const int key = 9002;
            var blackboard = new RuntimeBlackboard();
            blackboard.SetInt(key, 0);
            var decorator = new RuntimeBBComparisonNode
            {
                KeyHash = key,
                Operator = BBComparisonOp.Equal,
                ValueType = BehaviorTreeComparisonValueType.Int,
                RefInt = 1,
                AbortMode = RuntimeAbortType.None,
                Child = new FixedStateNode(RuntimeState.Success),
            };
            var root = new RuntimeRootNode { Child = decorator };
            var tree = new RuntimeBehaviorTree(root, blackboard);

            try
            {
                int epochBefore = tree.Evaluation.Epoch;
                blackboard.SetInt(key, 1);
                Assert.AreEqual(epochBefore, tree.Evaluation.Epoch);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void ReactiveSequence_OnChangeAdvancesOnlyActiveBranch()
        {
            var children = new FixedStateNode[16];
            var sequence = new RuntimeReactiveSequence
            {
                ReevaluationMode = RuntimeReevaluationMode.OnChange,
            };
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = new FixedStateNode(
                    i == 8 ? RuntimeState.Running : RuntimeState.Success);
                sequence.AddChild(children[i]);
            }
            sequence.Seal();
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(sequence);

            try
            {
                tree.Tick();
                for (int i = 0; i < 8; i++)
                {
                    Assert.AreEqual(1, children[i].RunCount, "completed child " + i);
                }

                for (int tick = 0; tick < 5; tick++) tree.Tick();
                Assert.AreEqual(6, children[8].RunCount);
                for (int i = 0; i < 8; i++)
                {
                    Assert.AreEqual(1, children[i].RunCount, "idle child " + i);
                }
                for (int i = 9; i < children.Length; i++)
                {
                    Assert.AreEqual(0, children[i].RunCount, "later child " + i);
                }

                tree.RequestReevaluation();
                tree.Tick();
                Assert.AreEqual(2, children[0].RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }
    }
}
