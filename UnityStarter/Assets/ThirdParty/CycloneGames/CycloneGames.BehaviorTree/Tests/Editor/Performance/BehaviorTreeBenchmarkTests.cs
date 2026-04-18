using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Tests.Editor.Framework;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.BehaviorTree.Tests.Editor.Performance
{
    public class BehaviorTreeBenchmarkTests
    {
        private const int AgentCount = 1000;
        private const int BlackboardOpCount = 4096;
        private const int DeltaTrackedKeyCount = 64;

        [Test, Performance]
        public void ManagedRuntimeTick_1000Agents_StaysBenchmarked()
        {
            var trees = new List<RuntimeBehaviorTree>(AgentCount);

            for (int i = 0; i < AgentCount; i++)
            {
                int key = 1000 + i;
                var gate = new ConditionalRunningNode(key, true);
                var fallback = new RecordingStatefulActionNode(RuntimeState.Running, RuntimeState.Running);
                var selector = BehaviorTreeTestFactory.CreateSelector(gate, fallback);
                selector.AbortType = RuntimeAbortType.LowerPriority;

                var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);
                tree.Blackboard.SetBool(key, true);
                trees.Add(tree);
            }

            try
            {
                Measure.Method(() =>
                    {
                        for (int i = 0; i < trees.Count; i++)
                        {
                            trees[i].Tick();
                        }
                    })
                    .WarmupCount(5)
                    .MeasurementCount(20)
                    .IterationsPerMeasurement(10)
                    .Run();
            }
            finally
            {
                for (int i = 0; i < trees.Count; i++)
                {
                    trees[i].Dispose();
                }
            }
        }

        [Test, Performance]
        public void RuntimeBlackboard_MixedReadWrite_IsBenchmarked()
        {
            var blackboard = new RuntimeBlackboard(initialCapacity: BlackboardOpCount);

            try
            {
                Measure.Method(() =>
                    {
                        for (int i = 0; i < BlackboardOpCount; i++)
                        {
                            blackboard.SetInt(i, i);
                            blackboard.SetFloat(i + 10000, i * 0.25f);
                            blackboard.SetBool(i + 20000, (i & 1) == 0);

                            _ = blackboard.GetInt(i);
                            _ = blackboard.GetFloat(i + 10000);
                            _ = blackboard.GetBool(i + 20000);
                        }
                    })
                    .WarmupCount(5)
                    .MeasurementCount(20)
                    .IterationsPerMeasurement(5)
                    .Run();
            }
            finally
            {
                blackboard.Dispose();
            }
        }

        [Test, Performance]
        public void BlackboardDelta_Flush64TrackedKeys_IsBenchmarked()
        {
            var blackboard = new RuntimeBlackboard(initialCapacity: DeltaTrackedKeyCount);
            var delta = new BTBlackboardDelta(DeltaTrackedKeyCount);

            try
            {
                for (int i = 0; i < DeltaTrackedKeyCount; i++)
                {
                    delta.TrackKey(i);
                    blackboard.SetInt(i, i);
                }

                delta.TryFlush(blackboard, out _);

                Measure.Method(() =>
                    {
                        for (int i = 0; i < DeltaTrackedKeyCount; i++)
                        {
                            blackboard.SetInt(i, blackboard.GetInt(i) + 1);
                        }

                        delta.TryFlush(blackboard, out _);
                    })
                    .WarmupCount(5)
                    .MeasurementCount(20)
                    .IterationsPerMeasurement(20)
                    .Run();
            }
            finally
            {
                blackboard.Dispose();
            }
        }
    }
}
