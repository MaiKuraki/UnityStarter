using System;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes;
using CycloneGames.BehaviorTree.Core.Nodes.Actions;
using CycloneGames.BehaviorTree.Core.Nodes.Compositors;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    /// <summary>
    /// Allocation gate using Unity's own counters. The managed API cannot read per-thread
    /// allocation inside the Editor, so this test asserts through ProfilerRecorder instead.
    /// Thresholds tolerate Editor background allocation and only catch per-frame regressions.
    /// </summary>
    public class BehaviorTreeAllocationGateTests
    {
        private const int Agents = 500;
        private const int Frames = 300;

        [Test]
        public void SteadyStateTick_DoesNotAllocatePerFrame([Values(16, 256)] int nodesPerTree)
        {
            using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            using var memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");

            var trees = new RuntimeBehaviorTree[Agents];
            var manager = new BTTickManager(Agents) { TickBudget = 500 };
            try
            {
                for (int i = 0; i < Agents; i++)
                {
                    var sequence = new RuntimeSequencer();
                    for (int c = 0; c < nodesPerTree - 1; c++)
                    {
                        sequence.AddChild(new RuntimeDebugLogNode());
                    }
                    sequence.AddChild(new RuntimeWaitNode { Duration = 999f });
                    sequence.Seal();
                    trees[i] = new RuntimeBehaviorTree(new RuntimeRootNode { Child = sequence }, null);
                    manager.Register(trees[i]);
                }

                for (int warmup = 0; warmup < 60; warmup++) manager.Tick();

                long heapBefore = memoryRecorder.Valid ? memoryRecorder.LastValue : 0;
                long allocated = 0;
                var random = new RuntimeDeterministicRandom((uint)nodesPerTree);
                for (int frame = 0; frame < Frames; frame++)
                {
                    for (int e = 0; e < 16; e++)
                    {
                        trees[random.NextInt(Agents)].Blackboard.SetInt(0x5EED, (int)(random.Next() & 0xFFFFu));
                    }
                    manager.Tick();
                    if (gcRecorder.Valid) allocated += gcRecorder.LastValue;
                }
                long heapAfter = memoryRecorder.Valid ? memoryRecorder.LastValue : 0;

                if (!gcRecorder.Valid)
                {
                    Assert.Ignore("GC Allocated In Frame recorder is not available on this platform.");
                }

                double bytesPerFrame = allocated / (double)Frames;
                TestContext.WriteLine(
                    $"nodes={nodesPerTree} gcBytesPerFrame={bytesPerFrame:F3} heapDelta={heapAfter - heapBefore}");

                // Editor keeps small background allocations; 16 B/frame still catches any
                // per-tick allocation regression (a single closure or boxed value per frame
                // would exceed it).
                Assert.Less(bytesPerFrame, 16.0,
                            $"steady-state allocation regressed to {bytesPerFrame:F3} B/frame at {nodesPerTree} nodes");
                Assert.Less(heapAfter - heapBefore, 16L * 1024 * 1024,
                            "retained heap grew by " + (heapAfter - heapBefore) + " bytes");
            }
            finally
            {
                manager.Clear();
                for (int i = 0; i < Agents; i++) trees[i]?.Dispose();
            }
        }
    }
}
