using System;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// Stress, soak, fuzz and scale tests over the real PlayableGraph, Unity GC and object lifetime.
    /// Editor timings include editor overhead; use the scale test for linearity, not absolute milliseconds.
    /// </summary>
    public sealed class AnimationRuntimeStressTests : LoggedTestBase
    {
        private readonly List<GameObject> _created = new List<GameObject>(64);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        private Animator CreateActor(string name, out AnimationRuntimeGraph graph, params AnimationLayerDescriptor[] layers)
        {
            var go = new GameObject(name);
            _created.Add(go);

            var animator = go.AddComponent<Animator>();
            graph = new AnimationRuntimeGraph(
                animator,
                layers != null && layers.Length > 0 ? layers : new[] { AnimationLayerDescriptor.Default });

            return animator;
        }

        private static AnimationClip CreateClip(string name)
        {
            return new AnimationClip { name = name };
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        // ------------------------------------------------------------------ fuzz

        [Test]
        [Timeout(120000)]
        public void Fuzz_RandomOperationSequence_KeepsInvariants()
        {
            CreateActor("FuzzActor", out AnimationRuntimeGraph graph);
            var clips = new AnimationClip[4];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = CreateClip("FuzzClip" + i);
            }

            var rng = new System.Random(20261109);
            var keys = new List<AnimationPlaybackKey>(16);
            long step = 0;

            try
            {
                for (int i = 0; i < 20000; i++)
                {
                    int operation = rng.Next(0, 7);
                    int instance = rng.Next(1, 6);
                    int channel = rng.Next(0, 3);
                    int layer = rng.Next(0, 2);

                    switch (operation)
                    {
                        case 0:
                        {
                            var request = new AnimationClipRequest(
                                instance, channel, layer, clips[rng.Next(0, clips.Length)],
                                rng.NextDouble() * 2d, (float)rng.NextDouble());
                            AnimationPlaybackHandle handle = graph.BeginClip(request);
                            if (handle.IsValid)
                            {
                                keys.Add(request.Key);
                            }

                            break;
                        }
                        case 1:
                        {
                            graph.UpdateClip(new AnimationClipRequest(
                                instance, channel, layer, clips[0], rng.NextDouble() * 2d, (float)rng.NextDouble()));
                            break;
                        }
                        case 2:
                        {
                            if (keys.Count > 0)
                            {
                                int index = rng.Next(0, keys.Count);
                                graph.EndClip(keys[index]);
                                keys.RemoveAt(index);
                            }

                            break;
                        }
                        case 3:
                            graph.SetLayerWeight(layer, (float)rng.NextDouble());
                            break;
                        case 4:
                            graph.StopAll();
                            keys.Clear();
                            break;
                        case 5:
                            graph.Commit(Tick(step++));
                            break;
                        default:
                            graph.Commit(Tick(step++));
                            break;
                    }

                    AnimationRuntimeStats stats = graph.GetStats();
                    Assert.LessOrEqual(stats.ActivePlaybacks, 8, "active playbacks never exceed total capacity");
                    Assert.GreaterOrEqual(stats.ActivePlaybacks, 0);
                    Assert.GreaterOrEqual(stats.PlayableCount, 0);
                }
            }
            finally
            {
                graph.Dispose();
                for (int i = 0; i < clips.Length; i++)
                {
                    Object.DestroyImmediate(clips[i]);
                }
            }
        }

        // ------------------------------------------------------------------ soak

        [Test]
        [Timeout(300000)]
        public void Soak_TwoHundredThousandCommits_IsStable()
        {
            CreateActor("SoakActor", out AnimationRuntimeGraph graph, new[]
            {
                new AnimationLayerDescriptor(null, false, 1f, 8)
            });

            var clips = new AnimationClip[4];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = CreateClip("SoakClip" + i);
                graph.BeginClip(new AnimationClipRequest(i + 1, 0, 0, clips[i], 0d, 0.25f));
            }

            try
            {
                graph.Commit(Tick(0));

                long memoryBefore = GC.GetTotalMemory(true);
                long start = Stopwatch.GetTimestamp();

                for (int step = 1; step <= 200000; step++)
                {
                    double time = (step % 60) * 0.016d;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        graph.UpdateClip(new AnimationClipRequest(i + 1, 0, 0, clips[i], time, 0.25f));
                    }

                    graph.Commit(Tick(step));
                }

                long elapsed = Stopwatch.GetTimestamp() - start;
                long memoryAfter = GC.GetTotalMemory(true);
                AnimationRuntimeStats stats = graph.GetStats();

                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                TestRunLog.Note("soak", "200000 commits in " + ms.ToString("0") + " ms ("
                    + (ms / 200000.0).ToString("0.0000") + " ms/commit), heap delta "
                    + (memoryAfter - memoryBefore) + " B, playbacks=" + stats.ActivePlaybacks
                    + ", recreated=" + stats.RecreatedPlayables);

                Assert.AreEqual(4, stats.ActivePlaybacks, "playbacks stay alive across 200k commits");
                Assert.Less(memoryAfter - memoryBefore, 4L * 1024 * 1024, "managed heap does not grow during the soak");
                Assert.Less(stats.RecreatedPlayables, 8L, "no per-commit playable churn");
            }
            finally
            {
                graph.Dispose();
                for (int i = 0; i < clips.Length; i++)
                {
                    Object.DestroyImmediate(clips[i]);
                }
            }
        }

        // ------------------------------------------------------------------ clip churn

        /// <summary>
        /// Measures clip switching, which destroys and recreates the playable on every change.
        /// Asserts one recreation per switch; update this test if a playable cache is introduced.
        /// </summary>
        [Test]
        [Timeout(180000)]
        public void ClipChurn_CyclingClips_MeasuresRecreationCost()
        {
            CreateActor("ChurnBenchActor", out AnimationRuntimeGraph graph, new[]
            {
                new AnimationLayerDescriptor(null, false, 1f, 4)
            });

            var clips = new AnimationClip[8];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = CreateClip("ChurnBench" + i);
            }

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clips[0], 0d, 1f));
                graph.Commit(Tick(0));

                long recreatedBefore = graph.GetStats().RecreatedPlayables;
                const int switches = 2000;

                long start = Stopwatch.GetTimestamp();
                for (int i = 1; i <= switches; i++)
                {
                    graph.BeginClip(new AnimationClipRequest(1, 0, 0, clips[i % clips.Length], 0d, 1f));
                    graph.Commit(Tick(i));
                }

                long elapsed = Stopwatch.GetTimestamp() - start;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                long recreated = graph.GetStats().RecreatedPlayables - recreatedBefore;

                TestRunLog.Note(
                    "churn",
                    switches + " clip switches in " + ms.ToString("0.0") + " ms ("
                    + (ms / switches).ToString("0.0000") + " ms/switch), recreated=" + recreated
                    + ", dropped=" + graph.GetStats().DroppedPlaybacks);

                Assert.AreEqual((int)switches, (int)recreated,
                    "every switch to a different clip recreates exactly one playable");
                Assert.AreEqual(0L, graph.GetStats().DroppedPlaybacks);
            }
            finally
            {
                graph.Dispose();
                for (int i = 0; i < clips.Length; i++)
                {
                    Object.DestroyImmediate(clips[i]);
                }
            }
        }

        // ------------------------------------------------------------------ scale

        [Test]
        [Timeout(300000)]
        public void Scale_Actors_1_10_50()
        {
            int[] actorCounts = { 1, 10, 50 };
            var results = new List<string>(3);

            foreach (int count in actorCounts)
            {
                var graphs = new List<AnimationRuntimeGraph>(count);
                var clips = new List<AnimationClip>(count);

                for (int i = 0; i < count; i++)
                {
                    CreateActor("ScaleActor" + i, out AnimationRuntimeGraph graph);
                    AnimationClip clip = CreateClip("ScaleClip" + i);
                    graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                    graph.Commit(Tick(0));
                    graphs.Add(graph);
                    clips.Add(clip);
                }

                const int commits = 2000;
                long start = Stopwatch.GetTimestamp();
                for (int step = 1; step <= commits; step++)
                {
                    for (int i = 0; i < graphs.Count; i++)
                    {
                        graphs[i].Commit(Tick(step));
                    }
                }

                long elapsed = Stopwatch.GetTimestamp() - start;
                double totalMs = elapsed * 1000.0 / Stopwatch.Frequency;
                double perActorMs = totalMs / count / commits;

                results.Add(count + " actors: " + perActorMs.ToString("0.00000") + " ms/commit/actor");

                for (int i = 0; i < graphs.Count; i++)
                {
                    graphs[i].Dispose();
                    Object.DestroyImmediate(clips[i]);
                }
            }

            for (int i = 0; i < results.Count; i++)
            {
                TestRunLog.Note("scale", results[i]);
            }
        }

        // ------------------------------------------------------------------ lifetime

        [Test]
        [Timeout(120000)]
        public void Lifecycle_DestroyedClip_IsTerminatedByTheWatchdog()
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // The liveness watchdog is compiled out of release players, so this expectation cannot hold there.
            // The watchdog is not compiled into release players.
            Assert.Ignore("The clip-liveness watchdog exists only in the editor and development builds.");
#endif
            CreateActor("WatchdogActor", out AnimationRuntimeGraph graph);
            AnimationClip clip = CreateClip("DoomedClip");

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0));
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks);

                Object.DestroyImmediate(clip);

                long step = 1;
                for (int i = 0; i < 40; i++)
                {
                    graph.Commit(Tick(step++));
                }

                Assert.AreEqual(0, graph.GetStats().ActivePlaybacks,
                    "the dev watchdog terminates a playback whose clip was destroyed");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        [Timeout(60000)]
        public void TransformActive_ToggleDoesNotBreakCommit()
        {
            CreateActor("ActiveActor", out AnimationRuntimeGraph graph);
            GameObject root = graph.TargetAnimator.gameObject;
            AnimationClip clip = CreateClip("ActiveClip");

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0));
                int before = graph.GetStats().ActivePlaybacks;

                root.SetActive(false);
                graph.Commit(Tick(1));
                Assert.AreEqual(before, graph.GetStats().ActivePlaybacks);

                root.SetActive(true);
                graph.Commit(Tick(2));
                Assert.AreEqual(before, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        [Timeout(60000)]
        public void AnimatorDisabled_CommitDoesNotThrow()
        {
            CreateActor("DisabledActor", out AnimationRuntimeGraph graph);
            Animator animator = graph.TargetAnimator;
            AnimationClip clip = CreateClip("DisabledClip");

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                animator.enabled = false;
                graph.Commit(Tick(0));
                animator.enabled = true;
                graph.Commit(Tick(1));
            }
            finally
            {
                graph.Dispose();
                Object.DestroyImmediate(clip);
            }
        }
    }
}
