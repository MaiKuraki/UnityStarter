using System;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    /// <summary>
    /// Short-running gates for the properties that only show up over time: steady-state
    /// allocation, pulse recovery, and graph depth handling. Budget: under five seconds.
    /// </summary>
    public class BehaviorTreeShortSoakGateTests
    {
        private const int Agents = 1000;
        private const int NodesPerTree = 32;
        private const int Frames = 600;

        [Test]
        public void ShortSoak_ThousandAgentsWithPulsesStaysStable()
        {
            var trees = new RuntimeBehaviorTree[Agents];
            var manager = new BTTickManager(Agents) { TickBudget = 1000 };
            var frames = new double[Frames];
            long heapBefore = 0;
            int gen1Before = 0;
            int errors = 0;
            try
            {
                for (int i = 0; i < Agents; i++)
                {
                    var sequence = new RuntimeSequencer();
                    for (int c = 0; c < NodesPerTree - 1; c++)
                    {
                        sequence.AddChild(new RuntimeDebugLogNode());
                    }
                    sequence.AddChild(new RuntimeWaitNode { Duration = 999f });
                    sequence.Seal();
                    trees[i] = new RuntimeBehaviorTree(new RuntimeRootNode { Child = sequence }, null);
                    manager.Register(trees[i]);
                }

                for (int warmup = 0; warmup < 60; warmup++) manager.Tick();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                heapBefore = GC.GetTotalMemory(true);
                gen1Before = GC.CollectionCount(1);

                var random = new RuntimeDeterministicRandom(20261108u);
                for (int frame = 0; frame < Frames; frame++)
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        for (int e = 0; e < 64; e++)
                        {
                            trees[random.NextInt(Agents)].Blackboard.SetInt(0x5EED, (int)(random.Next() & 0xFFFFu));
                        }
                        if (frame % 100 == 0)
                        {
                            for (int e = 0; e < 2000; e++)
                            {
                                trees[random.NextInt(Agents)].Blackboard.SetInt(0x5EED, (int)(random.Next() & 0xFFFFu));
                            }
                            for (int a = 0; a < Agents; a++)
                            {
                                trees[a].WakeUp(2);
                            }
                        }
                        manager.Tick();
                    }
                    catch (Exception)
                    {
                        errors++;
                    }
                    frames[frame] = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                                    * 1_000_000_000d / System.Diagnostics.Stopwatch.Frequency;
                }

                long heapAfter = GC.GetTotalMemory(false);
                int gen1After = GC.CollectionCount(1);
                Array.Sort(frames);
                double p99 = frames[(int)(Frames * 0.99)];
                double max = frames[Frames - 1];

                Assert.AreEqual(0, errors, "short soak raised exceptions");
                // Thresholds are intentionally loose: this gate catches order-of-magnitude
                // regressions, not host noise. Editor measurements of this workload are ~0.6 ms p99.
                Assert.Less(p99, 5_000_000d, "p99 frame exceeded 5 ms: " + p99);
                Assert.Less(max, 100_000_000d, "max frame exceeded 100 ms: " + max);
                Assert.Less(heapAfter - heapBefore, 16L * 1024 * 1024,
                            "retained heap grew by " + (heapAfter - heapBefore) + " bytes");
                Assert.LessOrEqual(gen1After - gen1Before, 2,
                                   "unexpected gen1 collections: " + (gen1After - gen1Before));
            }
            finally
            {
                manager.Clear();
                for (int i = 0; i < Agents; i++) trees[i]?.Dispose();
            }
        }

        [Test]
        public void Depth_AtLimitRunsAndBeyondLimitIsRejected()
        {
            RuntimeBehaviorTree atLimit = null;
            try
            {
                RuntimeNode chain = new RuntimeDebugLogNode();
                for (int i = 0; i < BTCapacityLimits.MaxTreeDepth - 2; i++)
                {
                    chain = new ChainNode { Child = chain };
                }
                atLimit = new RuntimeBehaviorTree(new RuntimeRootNode { Child = chain }, null);
                for (int i = 0; i < 50; i++) atLimit.Tick();
                Assert.AreNotEqual(RuntimeState.NotEntered, atLimit.State);
            }
            finally
            {
                atLimit?.Dispose();
            }

            RuntimeNode tooDeep = new RuntimeDebugLogNode();
            for (int i = 0; i < BTCapacityLimits.MaxTreeDepth + 4; i++)
            {
                tooDeep = new ChainNode { Child = tooDeep };
            }
            Assert.Throws<InvalidOperationException>(
                () => new RuntimeBehaviorTree(new RuntimeRootNode { Child = tooDeep }, null).Dispose());
        }

        private sealed class ChainNode : RuntimeDecoratorNode
        {
            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                return Child == null ? RuntimeState.Failure : Child.Run(blackboard);
            }
        }
    }
}
