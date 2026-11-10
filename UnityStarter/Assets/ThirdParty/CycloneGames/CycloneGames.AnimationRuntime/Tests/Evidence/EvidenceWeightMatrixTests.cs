using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Weight-gated dispatch across fade direction, clip length, loop mode, interruption, and layer depth.
    /// A playback reports only while its current weight is above zero.
    /// </summary>
    public sealed class EvidenceWeightMatrixTests : EvidenceTestBase
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

        [TestCase(0.5f, AnimationLoopMode.Clamp)]
        [TestCase(0.5f, AnimationLoopMode.Loop)]
        [TestCase(0.5f, AnimationLoopMode.PingPong)]
        [TestCase(1f, AnimationLoopMode.Clamp)]
        [TestCase(1f, AnimationLoopMode.Loop)]
        [TestCase(1f, AnimationLoopMode.PingPong)]
        [TestCase(2f, AnimationLoopMode.Clamp)]
        [TestCase(2f, AnimationLoopMode.Loop)]
        [TestCase(2f, AnimationLoopMode.PingPong)]
        public void FadeOutMidRamp_ReportsMarkerBeforeSilence(float length, AnimationLoopMode loop)
        {
            CreateActor("WeightMidActor_" + length + "_" + loop, out Animator animator);
            AnimationClip clip = ClipWith("WeightMidClip", length, length * 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, loop));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(
                    new AnimationPlaybackKey(1, 0, 0, 0), new AnimationFade(length, 0f)));

                float step = length * 0.1f;
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * step, i, step));
                }

                NoteEvidence("weight-mid", "length=" + length + " loop=" + loop + " events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "the marker is crossed while weight is still above zero");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(0.5f, AnimationLoopMode.Clamp)]
        [TestCase(0.5f, AnimationLoopMode.Loop)]
        [TestCase(0.5f, AnimationLoopMode.PingPong)]
        [TestCase(1f, AnimationLoopMode.Clamp)]
        [TestCase(1f, AnimationLoopMode.Loop)]
        [TestCase(1f, AnimationLoopMode.PingPong)]
        [TestCase(2f, AnimationLoopMode.Clamp)]
        [TestCase(2f, AnimationLoopMode.Loop)]
        [TestCase(2f, AnimationLoopMode.PingPong)]
        public void FadeOutCompleted_ReportsNothingAfterSilence(float length, AnimationLoopMode loop)
        {
            CreateActor("WeightSilentActor_" + length + "_" + loop, out Animator animator);
            AnimationClip clip = ClipWith("WeightSilentClip", length, length * 0.8f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, loop));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(
                    new AnimationPlaybackKey(1, 0, 0, 0), new AnimationFade(length * 0.25f, 0f)));

                float step = length * 0.1f;
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * step, i, step));
                }

                NoteEvidence("weight-silent", "length=" + length + " loop=" + loop + " events=" + sink.Total);
                Assert.AreEqual(0, sink.Total, "the fade is complete before the marker is crossed");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(0.5f, AnimationLoopMode.Clamp)]
        [TestCase(1f, AnimationLoopMode.Loop)]
        [TestCase(2f, AnimationLoopMode.PingPong)]
        public void FadeIn_ReportsWhenItBecomesAudible(float length, AnimationLoopMode loop)
        {
            CreateActor("WeightInActor_" + length + "_" + loop, out Animator animator);
            AnimationClip clip = ClipWith("WeightInClip", length, length * 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 0f, false, 0, 1f, loop),
                    new AnimationFade(length, 1f));
                graph.Commit(At(0d, 0L, 0.016f));

                float step = length * 0.1f;
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * step, i, step));
                }

                NoteEvidence("weight-in", "length=" + length + " loop=" + loop + " events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "a fade-in becomes audible before the marker");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ExitTimeDuringFadeOut_FiresBeforeSilence()
        {
            CreateActor("WeightExitActor", out Animator animator);
            AnimationClip clip = ClipWith("WeightExitClip", 1f, 0.5f);
            var graph = Graph(animator, new Sink());
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            int exits = 0;

            graph.TransitionCompleted += (handle, reason) =>
            {
                if (reason == AnimationTransitionReason.ExitTime)
                {
                    exits++;
                }
            };

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.Once));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 0f)));

                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-exit", "exits=" + exits);
                Assert.AreEqual(1, exits, "the exit point is crossed before the fade reaches zero");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void InterruptedFadeOut_NewTargetWins()
        {
            CreateActor("WeightInterruptActor", out Animator animator);
            AnimationClip clip = ClipWith("WeightInterruptClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L, 0.016f));
                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(1f, 0f)));
                graph.Commit(At(0.1d, 1L, 0.1f));

                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0.1f, 1f)));
                for (int i = 2; i <= 8; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-interrupt", "events=" + sink.Total);
                Assert.AreEqual(1, sink.Total, "the interrupted fade does not suppress the marker");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(2)]
        [TestCase(8)]
        public void MultiLayerFadeOut_EachLayerReportsBeforeSilence(int layers)
        {
            CreateActor("WeightDepthActor_" + layers, out Animator animator);
            AnimationClip clip = ClipWith("WeightDepthClip", 1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink, layers);

            try
            {
                for (int i = 0; i < layers; i++)
                {
                    graph.BeginPlayback(new AnimationClipRequest(
                        i + 1, 0, i, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                    Assert.IsTrue(graph.FadePlayback(
                        new AnimationPlaybackKey(i + 1, 0, i, 0), new AnimationFade(1f, 0f)));
                }

                graph.Commit(At(0d, 0L, 0.016f));
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(At(i * 0.1d, i, 0.1f));
                }

                NoteEvidence("weight-depth", layers + " layers, events=" + sink.Total);
                Assert.AreEqual(layers, sink.Total, "each layer crosses its marker before its own fade ends");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
