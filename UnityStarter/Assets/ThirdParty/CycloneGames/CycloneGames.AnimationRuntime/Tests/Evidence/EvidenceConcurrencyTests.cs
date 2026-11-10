using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Several playbacks sharing one graph, and the weight gate that silences a playback reporting nothing.
    /// </summary>
    public sealed class EvidenceConcurrencyTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly List<string> Order = new List<string>(64);
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(16);

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                string key = handle.Layer + ":" + clipEvent.FunctionName;
                Order.Add(key);
                Counts.TryGetValue(key, out int count);
                Counts[key] = count + 1;
            }

            public int Get(int layer, string name)
            {
                return Counts.TryGetValue(layer + ":" + name, out int count) ? count : 0;
            }

            public int Total()
            {
                int total = 0;
                foreach (int value in Counts.Values)
                {
                    total += value;
                }

                return total;
            }
        }

        private AnimationClip ClipWith(string name, float length, params float[] times)
        {
            AnimationClip clip = CreateClip(name, length);
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent { time = times[i], functionName = "M" + i });
            }

            return clip;
        }

        private AnimationClip ClipWithNamed(string name, float length, float[] times, string prefix)
        {
            AnimationClip clip = CreateClip(name, length);
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent { time = times[i], functionName = prefix + i });
            }

            return clip;
        }

        private static AnimationTick At(double time, long step, float delta = 0.016f)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, delta);
        }

        private AnimationRuntimeGraph Graph(Animator animator, Sink sink, int layers = 1)
        {
            var descriptors = new AnimationLayerDescriptor[layers];
            for (int i = 0; i < layers; i++)
            {
                descriptors[i] = AnimationLayerDescriptor.Default;
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors, null, AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);
            return graph;
        }

        [Test]
        public void WeightZeroPlayback_ReportsNothing()
        {
            CreateActor("ConcurrencySilentActor", out Animator animator);
            AnimationClip clip = ClipWith("SilentClip", 1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 0f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(2.5d, 1L));

                NoteEvidence("concurrency-silent", "events = " + sink.Total() + " (expected 0)");
                Assert.AreEqual(0, sink.Total(), "a zero-weight playback owns no audible pose");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void FadeOutToZero_StopsReporting()
        {
            CreateActor("ConcurrencyFadeActor", out Animator animator);
            AnimationClip clip = ClipWith("FadeClip", 1f, 0.1f, 0.25f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.2d, 1L));

                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.1f, 0f)));
                graph.Commit(At(0.3d, 2L, 0.05f));  // weight 0.5, crosses 0.25
                graph.Commit(At(0.4d, 3L, 0.05f));  // weight 0, fade complete
                int afterFade = sink.Total();

                graph.Commit(At(0.9d, 4L, 0.01f));

                NoteEvidence("concurrency-fade", "after fade = " + afterFade + ", later = " + sink.Total());
                Assert.AreEqual(2, afterFade, "0.1 and 0.25 are reported while the playback is still audible");
                Assert.AreEqual(afterFade, sink.Total(), "a completed fade-out silences the playback");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void TwoClipChannels_EachReportTheirOwnMarkers()
        {
            CreateActor("ConcurrencyChannelsActor", out Animator animator);
            AnimationClip first = ClipWithNamed("ChannelClipA", 1f, new[] { 0.2f }, "A");
            AnimationClip second = ClipWithNamed("ChannelClipB", 1f, new[] { 0.8f }, "B");
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, first, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, second, 0.4d, 1f, false, 1, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(1d, 1L));

                NoteEvidence("concurrency-channels", "A=" + sink.Get(0, "A0") + " B=" + sink.Get(0, "B0")
                    + " active=" + graph.GetLayerActivePlaybacks(0));
                Assert.AreEqual(1, sink.Get(0, "A0"), "the first channel reports its own marker");
                Assert.AreEqual(1, sink.Get(0, "B0"), "the second channel reports its own marker");
                Assert.AreEqual(2, graph.GetLayerActivePlaybacks(0), "both channels stay live");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void TwoLayers_IndependentPhases()
        {
            CreateActor("ConcurrencyLayersActor", out Animator animator);
            AnimationClip clip = ClipWith("LayerClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink, 2);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.BeginPlayback(new AnimationClipRequest(
                    2, 0, 1, clip, 0.4d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.2d, 1L));

                NoteEvidence("concurrency-layers", "layer0=" + sink.Get(0, "M0") + " layer1=" + sink.Get(1, "M0"));
                Assert.AreEqual(0, sink.Get(0, "M0"), "the first layer has not reached 0.5 yet");
                Assert.AreEqual(1, sink.Get(1, "M0"), "the second layer starts past 0.4 and crosses 0.5");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void FourPlaybacks_MixedDirections_KeepIndependentCounts()
        {
            CreateActor("ConcurrencyFourActor", out Animator animator);
            AnimationClip clip = ClipWith("FourClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink, 2);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.BeginPlayback(new AnimationClipRequest(
                    2, 0, 0, clip, 0d, 1f, false, 1, 1f, AnimationLoopMode.Loop));
                graph.BeginPlayback(new AnimationClipRequest(
                    3, 0, 1, clip, 1d, 1f, false, 0, -1f, AnimationLoopMode.Clamp));
                graph.BeginPlayback(new AnimationClipRequest(
                    4, 0, 1, clip, 1d, 1f, false, 1, -1f, AnimationLoopMode.Clamp));

                for (int i = 0; i <= 40; i++)
                {
                    graph.Commit(At(i * 0.016d, i));
                }

                NoteEvidence("concurrency-four", "layer0 forward=" + sink.Get(0, "M0")
                    + " layer1 reverse=" + sink.Get(1, "M0")
                    + " total=" + sink.Total());
                Assert.AreEqual(2, sink.Get(0, "M0"), "two forward channels each cross 0.5 once");
                Assert.AreEqual(2, sink.Get(1, "M0"), "two reverse channels each cross 0.5 once");
            }
            finally
            {
                graph.Dispose();
            }
        }
        [Test]
        public void HalfWeightPlayback_StillReports()
        {
            CreateActor("ConcurrencyHalfActor", out Animator animator);
            AnimationClip clip = ClipWith("HalfClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 0.5f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.8d, 1L));

                NoteEvidence("concurrency-half", "0.5=" + sink.Get(0, "M0"));
                Assert.AreEqual(1, sink.Get(0, "M0"), "any weight above zero still reports");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void EightLayers_EachReportsItsOwnCrossings()
        {
            const int layers = 8;
            const int commits = 500;

            CreateActor("ConcurrencyTopologyActor", out Animator animator);
            AnimationClip clip = ClipWith("TopologyClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink, layers);

            try
            {
                for (int i = 0; i < layers; i++)
                {
                    graph.BeginPlayback(new AnimationClipRequest(
                        i + 1, 0, i, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                }

                // 2.5 cycles in small steps: each layer crosses 0.5 at 0.5, 1.5 and 2.5.
                for (int i = 0; i <= commits; i++)
                {
                    graph.Commit(At(i * 0.005d, i));
                }

                int total = 0;
                for (int i = 0; i < layers; i++)
                {
                    total += sink.Get(i, "M0");
                }

                NoteEvidence("concurrency-topology", "layers = " + layers + ", total crossings = " + total
                    + " (expected " + (layers * 3) + ")");
                for (int i = 0; i < layers; i++)
                {
                    Assert.AreEqual(3, sink.Get(i, "M0"), "layer " + i + " crosses 0.5 three times");
                }
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}