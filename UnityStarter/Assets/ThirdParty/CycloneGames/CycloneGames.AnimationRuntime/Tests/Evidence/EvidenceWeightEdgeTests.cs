using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Weight-gate edges: zero/very long/NaN fades, repeated interruption, maximum layer x playback depth,
    /// negative speed, and a frozen transition clock.
    /// </summary>
    public sealed class EvidenceWeightEdgeTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public int Total;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Total++;
            }
        }

        private static AnimationTick At(double time, long step, float delta)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, delta);
        }

        private AnimationClip ClipWith(string name, float length, float markerTime)
        {
            AnimationClip clip = CreateClip(name, length);
            clip.AddEvent(new AnimationEvent { time = markerTime, functionName = "M" });
            return clip;
        }

        private static AnimationRuntimeGraph Graph(Animator animator, Sink sink, int layers = 1)
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
        public void ZeroDurationFadeOut_SilencesImmediately()
        {
            CreateActor("ZeroFadeActor", out Animator animator);
            AnimationClip clip = ClipWith("ZeroFadeClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0f, 0f)));
                graph.Commit(At(0.6d, 1L, 0.1f));

                NoteEvidence("weight-zero", "events=" + sink.Total);
                Assert.AreEqual(0, sink.Total, "zero-duration fade-out silences before the marker");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void VeryLongFadeOut_StillReportsUntilSilence()
        {
            CreateActor("LongFadeActor", out Animator animator);
            AnimationClip clip = ClipWith("LongFadeClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(10f, 0f)));

                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-long", "events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "a 10 s fade is still audible at 0.5 s");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteFadeDuration_IsSanitizedToInstant(float duration)
        {
            CreateActor("NonFiniteFadeActor", out Animator animator);
            AnimationClip clip = ClipWith("NonFiniteFadeClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(duration, 0f)));
                graph.Commit(At(0.6d, 1L, 0.1f));

                NoteEvidence("weight-nonfinite", "duration=" + duration + " events=" + sink.Total);
                Assert.AreEqual(0, sink.Total, "non-finite duration sanitizes to an instant fade");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void RepeatedFadeInterruptions_NewTargetWins()
        {
            CreateActor("InterruptActor", out Animator animator);
            AnimationClip clip = ClipWith("InterruptClip", 10f, 5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));

                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 0f)));
                graph.Commit(At(0.1d, 1L, 0.1f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 1f)));
                graph.Commit(At(0.2d, 2L, 0.1f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 0f)));
                graph.Commit(At(0.3d, 3L, 0.1f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.1f, 1f)));

                for (int i = 4; i <= 60; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-interrupts", "events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "the final target is audible when the marker is crossed");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void EightLayersFourPlaybacks_FadeOutEachReports()
        {
            const int layers = 8;
            const int perLayer = 4;
            CreateActor("MaxDepthActor", out Animator animator);
            AnimationClip clip = ClipWith("MaxDepthClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink, layers);

            try
            {
                for (int layer = 0; layer < layers; layer++)
                {
                    for (int slot = 0; slot < perLayer; slot++)
                    {
                        int instance = layer * perLayer + slot + 1;
                        graph.BeginPlayback(new AnimationClipRequest(
                            instance, 0, layer, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                        Assert.IsTrue(graph.FadePlayback(
                            new AnimationPlaybackKey(instance, 0, layer, 0), new AnimationFade(1f, 0f)));
                    }
                }

                graph.Commit(At(0d, 0L, 0.016f));
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-max-depth", layers + "x" + perLayer + " events=" + sink.Total);
                Assert.AreEqual(layers * perLayer, sink.Total, "each playback reports its marker once");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void NegativeSpeedFadeOut_ReportsOnTheReverseCrossing()
        {
            CreateActor("NegativeSpeedActor", out Animator animator);
            AnimationClip clip = ClipWith("NegativeSpeedClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 1d, 1f, false, 0, -1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 0f)));

                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-negative", "events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "the reverse crossing happens before the fade ends");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void FrozenTransitionClock_DoesNotAdvanceFade()
        {
            CreateActor("FrozenFadeActor", out Animator animator);
            AnimationClip clip = ClipWith("FrozenFadeClip", 1f, 5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.1f, 0f)));

                for (int i = 1; i <= 20; i++)
                {
                    graph.Commit(At(0d, i, 0f));
                }

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                NoteEvidence("weight-frozen", "weight=" + buffer[0].Weight + " events=" + sink.Total);
                Assert.AreEqual(1f, buffer[0].Weight, 1e-4f, "zero delta must not advance the fade");
                Assert.AreEqual(0, sink.Total);
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
