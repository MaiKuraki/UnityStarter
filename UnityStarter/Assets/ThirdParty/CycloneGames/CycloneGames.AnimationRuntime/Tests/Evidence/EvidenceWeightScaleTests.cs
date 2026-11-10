using System;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Weight-gate scale tests: maximum topology with fade-out, long fade cycling, and seeded random scenarios.
    /// </summary>
    public sealed class EvidenceWeightScaleTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly Dictionary<AnimationPlaybackHandle, int> PerHandle =
                new Dictionary<AnimationPlaybackHandle, int>();
            public int Total;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Total++;
                PerHandle.TryGetValue(handle, out int count);
                PerHandle[handle] = count + 1;
            }
        }

        private static AnimationTick At(double time, long step, float delta)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, delta);
        }

        private AnimationClip ClipWithMarkers(string name, int markerCount)
        {
            AnimationClip clip = CreateClip(name, 1f);
            for (int i = 0; i < markerCount; i++)
            {
                clip.AddEvent(new AnimationEvent
                {
                    time = (i + 1f) / (markerCount + 1f),
                    functionName = "M" + i
                });
            }

            return clip;
        }

        [TestCase(3)]
        [TestCase(8)]
        public void MaxDepthFadeOut_AllMarkersReportBeforeSilence(int markerCount)
        {
            CreateActor("ScaleFade_" + markerCount, out Animator animator);
            AnimationClip clip = ClipWithMarkers("ScaleFadeClip", markerCount);
            var descriptors = new AnimationLayerDescriptor[8];
            for (int i = 0; i < 8; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 8);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors, null, AnimationTimePolicy.TickDriven);
            var sink = new Sink();
            graph.SetClipEventSink(sink);
            var keys = new List<AnimationPlaybackKey>(64);

            try
            {
                for (int layer = 0; layer < 8; layer++)
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        int instance = layer * 8 + slot + 1;
                        graph.BeginPlayback(new AnimationClipRequest(
                            instance, 0, layer, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                        Assert.IsTrue(graph.FadePlayback(
                            new AnimationPlaybackKey(instance, 0, layer, 0), new AnimationFade(1f, 0f)));
                        keys.Add(new AnimationPlaybackKey(instance, 0, layer, 0));
                    }
                }

                graph.Commit(At(0d, 0L, 0.016f));
                // Drive to the clip end; every marker must be crossed while the fade is still audible.
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-scale", "8x8 x " + markerCount + " markers: total=" + sink.Total
                    + " handles=" + sink.PerHandle.Count);
                Assert.AreEqual(64, sink.PerHandle.Count);
                Assert.AreEqual(64 * markerCount, sink.Total);
                foreach (KeyValuePair<AnimationPlaybackHandle, int> pair in sink.PerHandle)
                {
                    Assert.AreEqual(markerCount, pair.Value);
                }
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void TenThousandFadeCycles_StayStable()
        {
            CreateActor("FadeSoakActor", out Animator animator);
            AnimationClip clip = CreateClip("FadeSoakClip", 1f);
            clip.AddEvent(new AnimationEvent { time = 0.45f, functionName = "M" });
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default }, null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.4d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.01f));

                long step = 1;
                for (int i = 0; i < 10000; i++)
                {
                    Assert.IsTrue(graph.SeekPlayback(key, 0.4d));
                    Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.01f, 0f)));
                    graph.Commit(At(step * 0.001d, step++, 0.01f));
                    Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.01f, 1f)));
                    graph.Commit(At(step * 0.001d, step++, 0.01f));
                }

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                NoteEvidence("weight-soak", "10000 cycles: weight=" + buffer[0].Weight
                    + " active=" + graph.GetStats().ActivePlaybacks + " events=" + sink.Total);
                Assert.IsTrue(buffer[0].Weight >= 0f && buffer[0].Weight <= 1f);
                Assert.IsFalse(float.IsNaN(buffer[0].Weight));
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(1)]
        [TestCase(7919)]
        [TestCase(104729)]
        public void SeededRandomFadeScenarios_StayFinite(int seed)
        {
            CreateActor("FadeFuzzActor_" + seed, out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default }, null,
                AnimationTimePolicy.TickDriven);
            var random = new System.Random(seed);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                for (int iteration = 0; iteration < 50; iteration++)
                {
                    float length = 0.25f + (float)random.NextDouble() * 1.75f;
                    AnimationLoopMode loop = (AnimationLoopMode)random.Next(0, 3);
                    float target = random.Next(0, 3) * 0.5f;
                    float duration = random.Next(0, 5) * 0.25f;
                    AnimationClip clip = CreateClip("FuzzFadeClip_" + seed + "_" + iteration, length);
                    clip.AddEvent(new AnimationEvent { time = length * 0.5f, functionName = "M" });

                    graph.BeginPlayback(new AnimationClipRequest(
                        1, 0, 0, clip, 0d, 1f, false, 0, 1f, loop));
                    graph.Commit(At(0d, 0L, 0.016f));
                    graph.FadePlayback(new AnimationPlaybackKey(1, 0, 0, 0), new AnimationFade(duration, target));

                    for (int tick = 0; tick < 10; tick++)
                    {
                        float delta = 0.001f + (float)random.NextDouble() * 0.2f;
                        graph.Commit(At((tick + 1) * 0.01d, tick + 1, delta));
                    }

                    Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                    Assert.IsFalse(float.IsNaN(buffer[0].Weight) || float.IsInfinity(buffer[0].Weight));
                    Assert.IsTrue(buffer[0].Weight >= 0f && buffer[0].Weight <= 1f);

                    graph.StopAll();
                    graph.Commit(At(20d, 100L + iteration, 0.01f));
                }

                NoteEvidence("weight-fuzz", "seed=" + seed + " scenarios=50, all finite");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
