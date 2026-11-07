using System;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    /// <summary>
    /// Gate tests for the two structures most likely to regress silently: the open-addressed
    /// blackboard store and the bounded round-robin scheduler.
    /// </summary>
    public class BehaviorTreeStoreAndSchedulerGateTests
    {
        private sealed class CountingNode : RuntimeNode
        {
            public int Ticks;
            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                Ticks++;
                return RuntimeState.Running;
            }
        }

        private struct ReferenceValue
        {
            public byte Type;
            public int IntValue;
            public float FloatValue;
            public bool BoolValue;
        }

        [Test]
        public void BlackboardStore_RandomizedMutationsMatchReferenceModel()
        {
            const int KeySpace = 64;
            const int Operations = 5000;

            for (int seed = 1; seed <= 4; seed++)
            {
                var random = new RuntimeDeterministicRandom((uint)(seed * 7919));
                var blackboard = new RuntimeBlackboard(initialCapacity: 16);
                var reference = new ReferenceValue[KeySpace];
                int mismatches = 0;

                try
                {
                    for (int i = 0; i < Operations; i++)
                    {
                        int key = random.NextInt(KeySpace);
                        int action = random.NextInt(100);
                        if (action < 20)
                        {
                            blackboard.Remove(key);
                            reference[key] = default;
                        }
                        else if (action < 55)
                        {
                            int value = (int)(random.Next() & 0x7FFFFFFFu);
                            blackboard.SetInt(key, value);
                            reference[key] = new ReferenceValue { Type = 1, IntValue = value };
                        }
                        else if (action < 80)
                        {
                            float value = (float)(random.Next() & 0xFFFFu) / 100f;
                            blackboard.SetFloat(key, value);
                            reference[key] = new ReferenceValue { Type = 2, FloatValue = value };
                        }
                        else
                        {
                            bool value = (random.Next() & 1u) != 0u;
                            blackboard.SetBool(key, value);
                            reference[key] = new ReferenceValue { Type = 3, BoolValue = value };
                        }

                        if ((i & 63) == 0)
                        {
                            mismatches += Check(blackboard, reference, random.NextInt(KeySpace));
                        }
                    }

                    for (int key = 0; key < KeySpace; key++)
                    {
                        mismatches += Check(blackboard, reference, key);
                    }
                }
                finally
                {
                    blackboard.Dispose();
                }

                Assert.AreEqual(0, mismatches, "seed " + seed + " produced " + mismatches + " mismatches");
            }
        }

        private static int Check(RuntimeBlackboard blackboard, ReferenceValue[] reference, int key)
        {
            ReferenceValue expected = reference[key];
            bool present = blackboard.HasKey(key);
            if (present != (expected.Type != 0)) return 1;

            switch (expected.Type)
            {
                case 0:
                    return 0;
                case 1:
                    return blackboard.TryGetInt(key, out int i) && i == expected.IntValue ? 0 : 1;
                case 2:
                    return blackboard.TryGetFloat(key, out float f) && f == expected.FloatValue ? 0 : 1;
                default:
                    return blackboard.TryGetBool(key, out bool b) && b == expected.BoolValue ? 0 : 1;
            }
        }

        [Test]
        public void Scheduler_VisitsEveryTreeWithBoundedBudget()
        {
            const int Agents = 1000;
            const int Frames = 300;

            foreach (int budget in new[] { 10, 100 })
            {
                var nodes = new CountingNode[Agents];
                var trees = new RuntimeBehaviorTree[Agents];
                var manager = new BTTickManager(Agents) { TickBudget = budget };
                try
                {
                    for (int i = 0; i < Agents; i++)
                    {
                        nodes[i] = new CountingNode();
                        trees[i] = new RuntimeBehaviorTree(new RuntimeRootNode { Child = nodes[i] }, null);
                        manager.Register(trees[i]);
                    }

                    var random = new RuntimeDeterministicRandom((uint)(budget * 31));
                    for (int frame = 0; frame < Frames; frame++)
                    {
                        if (frame % 50 == 0)
                        {
                            for (int k = 0; k < 10; k++)
                            {
                                int index = random.NextInt(Agents);
                                trees[index].Stop();
                                trees[index].Play();
                            }
                        }
                        manager.Tick();
                    }

                    int min = int.MaxValue;
                    int max = 0;
                    int zero = 0;
                    for (int i = 0; i < Agents; i++)
                    {
                        int ticks = nodes[i].Ticks;
                        if (ticks < min) min = ticks;
                        if (ticks > max) max = ticks;
                        if (ticks == 0) zero++;
                    }

                    Assert.AreEqual(0, zero, "budget " + budget + " left trees unvisited");
                    Assert.AreEqual(min, max, "budget " + budget + " produced uneven tick counts");
                    Assert.AreEqual(Frames * budget / Agents, min, "budget " + budget + " tick count mismatch");
                }
                finally
                {
                    manager.Clear();
                    for (int i = 0; i < Agents; i++) trees[i]?.Dispose();
                }
            }
        }
    }
}
