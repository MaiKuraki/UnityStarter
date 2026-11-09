using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Clip-event sweep matrix. The zero marker is the canary: a clamp clip that starts at 0 fires it once, and no
    /// later forward hitch may replay it. Loop wraps must still report the tail and head bands.
    /// </summary>
    public sealed class EvidenceClipEventSweepTests : EvidenceTestBase
    {
        private sealed class CountingSink : IAnimationClipEventSink
        {
            public readonly Dictionary<string, int> PerName = new Dictionary<string, int>();
            public readonly Dictionary<AnimationPlaybackHandle, int> PerHandle =
                new Dictionary<AnimationPlaybackHandle, int>();
            public int Total;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Total++;
                PerName.TryGetValue(clipEvent.FunctionName, out int byName);
                PerName[clipEvent.FunctionName] = byName + 1;
                PerHandle.TryGetValue(handle, out int byHandle);
                PerHandle[handle] = byHandle + 1;
            }

            public int Count(string name)
            {
                return PerName.TryGetValue(name, out int value) ? value : 0;
            }
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private AnimationClip ClipWith(string name, float length, params float[] times)
        {
            AnimationClip clip = CreateClip(name, length);
            clip.wrapMode = WrapMode.ClampForever;
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent { time = times[i], functionName = "M" + i });
            }

            return clip;
        }

        private static void Begin(AnimationRuntimeGraph graph, AnimationClip clip, float length, AnimationTimePolicy policy)
        {
            graph.BeginPlayback(new AnimationClipRequest(
                1, 0, 0, clip, 0d, 1f, false, 0, 1f,
                policy == AnimationTimePolicy.TickDriven ? AnimationLoopMode.Clamp : AnimationLoopMode.Clamp));
        }

        [TestCase(0.25f, 1.5f, AnimationTimePolicy.External)]
        [TestCase(0.25f, 3f, AnimationTimePolicy.External)]
        [TestCase(0.5f, 1.5f, AnimationTimePolicy.External)]
        [TestCase(0.5f, 3f, AnimationTimePolicy.External)]
        [TestCase(1f, 1.5f, AnimationTimePolicy.External)]
        [TestCase(1f, 3f, AnimationTimePolicy.External)]
        [TestCase(2f, 1.5f, AnimationTimePolicy.External)]
        [TestCase(2f, 3f, AnimationTimePolicy.External)]
        [TestCase(0.25f, 1.5f, AnimationTimePolicy.TickDriven)]
        [TestCase(0.25f, 3f, AnimationTimePolicy.TickDriven)]
        [TestCase(0.5f, 1.5f, AnimationTimePolicy.TickDriven)]
        [TestCase(0.5f, 3f, AnimationTimePolicy.TickDriven)]
        [TestCase(1f, 1.5f, AnimationTimePolicy.TickDriven)]
        [TestCase(1f, 3f, AnimationTimePolicy.TickDriven)]
        [TestCase(2f, 1.5f, AnimationTimePolicy.TickDriven)]
        [TestCase(2f, 3f, AnimationTimePolicy.TickDriven)]
        public void ClampHitch_DoesNotReplayTheMarkerAtZero(
            float length,
            float hitchMultiplier,
            AnimationTimePolicy policy)
        {
            CreateActor("Sweep_" + length + "_" + hitchMultiplier + "_" + policy, out Animator animator);
            AnimationClip clip = ClipWith("SweepClip", length, 0f, length * 0.5f, length * 0.9f);
            var sink = new CountingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                policy);
            graph.SetClipEventSink(sink);

            try
            {
                Begin(graph, clip, length, policy);
                graph.Commit(policy == AnimationTimePolicy.TickDriven
                    ? new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0.016f)
                    : Tick(0L));

                float hitch = length * hitchMultiplier;
                if (policy == AnimationTimePolicy.TickDriven)
                {
                    graph.Commit(new AnimationTick(hitch, 1L, AnimationTimeSource.Update, false, hitch));
                }
                else
                {
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, hitch, 1f));
                    graph.Commit(Tick(1L));
                }

                NoteEvidence(
                    "sweep",
                    "length=" + length + " hitch=" + hitchMultiplier + "x policy=" + policy
                    + " zero=" + sink.Count("M0") + " total=" + sink.Total);

                Assert.AreEqual(1, sink.Count("M0"),
                    "a clamp hitch must not replay the marker already fired at time 0");
                Assert.AreEqual(3, sink.Total, "each marker fires once");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ClampSoak_RepeatedHitches_KeepZeroMarkerOnce()
        {
            CreateActor("SoakActor", out Animator animator);
            AnimationClip clip = ClipWith("SoakClip", 1f, 0f, 0.5f);
            var sink = new CountingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                Begin(graph, clip, 1f, AnimationTimePolicy.External);
                graph.Commit(Tick(0L));

                long step = 1;
                for (int i = 0; i < 10000; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                    graph.Commit(Tick(step++));
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 1d, 1f));
                    graph.Commit(Tick(step++));
                }

                NoteEvidence("soak", "10000 hitch cycles: zero=" + sink.Count("M0")
                    + " half=" + sink.Count("M1") + " total=" + sink.Total);

                Assert.AreEqual(1, sink.Count("M0"), "the marker at 0 fires once for the lifetime of the playback");
                Assert.AreEqual(10001, sink.Total, "the 0.5 marker fires once per hitch cycle");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ClampFuzz_RandomLengthsAndMarkers_FireEachMarkerOnce()
        {
            CreateActor("FuzzActor", out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var random = new System.Random(7919);

            try
            {
                for (int iteration = 0; iteration < 150; iteration++)
                {
                    float length = 0.25f + (float)random.NextDouble() * 2.75f;
                    var times = new float[4];
                    times[0] = 0f;
                    for (int i = 1; i < times.Length; i++)
                    {
                        times[i] = (float)random.NextDouble() * length;
                    }

                    AnimationClip clip = ClipWith("FuzzClip_" + iteration, length, times);
                    graph.StopAll();
                    graph.Commit(Tick(0L));

                    var sink = new CountingSink();
                    graph.SetClipEventSink(sink);
                    Begin(graph, clip, length, AnimationTimePolicy.External);
                    graph.Commit(Tick(1L));

                    float hitch = length * (1f + (random.Next(1, 4) * 0.5f));
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, hitch, 1f));
                    graph.Commit(Tick(2L));

                    Assert.AreEqual(1, sink.Count("M0"),
                        "iteration " + iteration + " replayed the marker at 0");
                    Assert.AreEqual(4, sink.Total,
                        "iteration " + iteration + " did not fire each marker exactly once");
                }

                NoteEvidence("fuzz", "150 random clamp clips: each marker fired once, zero marker never replayed");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        public void MultiLayerHitch_EachPlaybackReportsItsMarkersOnce(int layerCount)
        {
            CreateActor("Depth_" + layerCount, out Animator animator);
            AnimationClip clip = ClipWith("DepthClip", 1f, 0f, 0.5f);
            var descriptors = new AnimationLayerDescriptor[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 4);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors);
            var sink = new CountingSink();
            graph.SetClipEventSink(sink);
            var keys = new List<AnimationPlaybackKey>(layerCount * 4);

            try
            {
                for (int layer = 0; layer < layerCount; layer++)
                {
                    for (int slot = 0; slot < 4; slot++)
                    {
                        int instance = layer * 4 + slot + 1;
                        graph.BeginPlayback(new AnimationClipRequest(instance, 0, layer, clip, 0d, 1f));
                        keys.Add(new AnimationPlaybackKey(instance, 0, layer, 0));
                    }
                }

                graph.Commit(Tick(0L));
                for (int i = 0; i < keys.Count; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(
                        keys[i].InstanceId, keys[i].PlaybackChannel, keys[i].Layer, clip, 1d, 1f));
                }

                graph.Commit(Tick(1L));

                NoteEvidence("depth", layerCount + " layers x 4 playbacks: handles=" + sink.PerHandle.Count
                    + " total=" + sink.Total);

                Assert.AreEqual(keys.Count, sink.PerHandle.Count);
                foreach (KeyValuePair<AnimationPlaybackHandle, int> pair in sink.PerHandle)
                {
                    Assert.AreEqual(2, pair.Value, "each playback receives its 0 and 0.5 markers once");
                }
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void LoopWrap_StillReportsTailAndHeadMarkers()
        {
            CreateActor("LoopActor", out Animator animator);
            AnimationClip clip = ClipWith("LoopClip", 1f, 0.1f, 0.5f, 0.9f);
            var sink = new CountingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(0.6d, 1L, AnimationTimeSource.Update, false, 0.6f));
                graph.Commit(new AnimationTick(1.1d, 2L, AnimationTimeSource.Update, false, 0.5f));

                NoteEvidence("loop", "0.1=" + sink.Count("M0") + " 0.5=" + sink.Count("M1")
                    + " 0.9=" + sink.Count("M2"));
                Assert.AreEqual(2, sink.Count("M0"), "0.1 is crossed on the first pass and after the wrap");
                Assert.AreEqual(1, sink.Count("M1"));
                Assert.AreEqual(1, sink.Count("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
