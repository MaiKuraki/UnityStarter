using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// PingPong reverse legs and seek sanitization. The reverse leg must report markers in descending order;
    /// a backward segment is not a loop wrap.
    /// </summary>
    public sealed class EvidencePingPongTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly List<string> Order = new List<string>();
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Order.Add(clipEvent.FunctionName);
                Counts.TryGetValue(clipEvent.FunctionName, out int count);
                Counts[clipEvent.FunctionName] = count + 1;
            }

            public int Get(string name)
            {
                return Counts.TryGetValue(name, out int count) ? count : 0;
            }
        }

        private static AnimationTick At(double time, long step)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private AnimationClip ClipWith(float length, params float[] times)
        {
            AnimationClip clip = CreateClip("PingPongClip_" + length + "_" + times.Length, length);
            clip.wrapMode = WrapMode.PingPong;
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent { time = times[i], functionName = "M" + i });
            }

            return clip;
        }

        private static void BeginPingPong(AnimationRuntimeGraph graph, AnimationClip clip, int instance = 1, int layer = 0)
        {
            graph.BeginPlayback(new AnimationClipRequest(
                instance, 0, layer, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
        }

        [Test]
        public void LoopWrap_ReportsTailThenHead()
        {
            CreateActor("LoopOrderActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.05f, 0.65f);
            var sink = new Sink();
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
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.5d, 1L));
                graph.Commit(At(1.1d, 2L));

                NoteEvidence("loop-order", "order=" + string.Join(",", sink.Order));
                Assert.AreEqual("M1", sink.Order[1], "the tail marker comes first");
                Assert.AreEqual("M0", sink.Order[2], "the head marker comes second");
                Assert.AreEqual(2, sink.Get("M0"));
                Assert.AreEqual(1, sink.Get("M1"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPong_Endpoints_DoNotReplayAtTheTurnaround()
        {
            CreateActor("PingPongEndpointActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0f, 1f);
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                BeginPingPong(graph, clip);
                graph.Commit(At(0d, 0L));
                graph.Commit(At(1d, 1L));
                graph.Commit(At(1.5d, 2L));
                graph.Commit(At(2d, 3L));
                graph.Commit(At(2.5d, 4L));

                NoteEvidence("pingpong-endpoint", "order=" + string.Join(",", sink.Order));
                Assert.AreEqual(2, sink.Get("M0"), "0 fires at the start and when the reverse leg reaches 0");
                Assert.AreEqual(1, sink.Get("M1"), "1 fires once at the forward turn");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(2f)]
        public void PingPong_ReverseLeg_ReportsMarkersInDescendingOrder(float length)
        {
            CreateActor("PingPongActor_" + length, out Animator animator);
            AnimationClip clip = ClipWith(length, length * 0.2f, length * 0.5f, length * 0.8f);
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                BeginPingPong(graph, clip);
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d * length, 1L));
                Assert.AreEqual(3, sink.Order.Count, "forward leg crosses all three");

                graph.Commit(At(1.9d * length, 2L)); // ping-pong back to 0.1 * length

                NoteEvidence("pingpong", "order=" + string.Join(",", sink.Order)
                    + " 0.2=" + sink.Get("M0") + " 0.5=" + sink.Get("M1") + " 0.8=" + sink.Get("M2"));

                Assert.AreEqual(2, sink.Get("M0"), "0.2 is crossed on both legs");
                Assert.AreEqual(2, sink.Get("M1"), "0.5 is crossed on both legs");
                Assert.AreEqual(2, sink.Get("M2"), "0.8 is crossed on both legs");
                Assert.AreEqual("M2", sink.Order[3], "reverse leg starts at the high marker");
                Assert.AreEqual("M1", sink.Order[4]);
                Assert.AreEqual("M0", sink.Order[5], "reverse leg ends at the low marker");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPong_EveryCrossingExit_FiresOnBothLegs()
        {
            CreateActor("PingPongExitActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.2f, 0.5f, 0.8f);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
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
                BeginPingPong(graph, clip);
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.EveryCrossing));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.6d, 1L));
                Assert.AreEqual(1, exits, "forward crossing");

                graph.Commit(At(1.9d, 2L)); // back to 0.1, crosses 0.5 again
                Assert.AreEqual(2, exits, "reverse crossing");

                graph.Commit(At(2.9d, 3L)); // forward to 0.9, crosses 0.5 again
                Assert.AreEqual(3, exits, "forward crossing after the bounce");

                NoteEvidence("pingpong-exit", "exit reports = " + exits);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        public void PingPong_MultiLayer_EachPlaybackReportsBothLegs(int layerCount)
        {
            CreateActor("PingPongDepth_" + layerCount, out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.2f, 0.5f, 0.8f);
            var descriptors = new AnimationLayerDescriptor[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 4);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors, null, AnimationTimePolicy.TickDriven);
            var sink = new Sink();
            graph.SetClipEventSink(sink);

            try
            {
                for (int layer = 0; layer < layerCount; layer++)
                {
                    for (int slot = 0; slot < 4; slot++)
                    {
                        BeginPingPong(graph, clip, layer * 4 + slot + 1, layer);
                    }
                }

                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                graph.Commit(At(1.9d, 2L));

                NoteEvidence("pingpong-depth", layerCount + " layers x 4: total=" + sink.Order.Count);
                Assert.AreEqual(layerCount * 4 * 6, sink.Order.Count,
                    "each playback reports three markers on each leg");
                Assert.AreEqual(layerCount * 4 * 2, sink.Get("M1"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPong_Soak_RepeatedCyclesKeepTheSameCounts()
        {
            CreateActor("PingPongSoak", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.2f, 0.5f, 0.8f);
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                BeginPingPong(graph, clip);
                graph.Commit(At(0d, 0L));

                long step = 1;
                for (int cycle = 0; cycle < 1000; cycle++)
                {
                    graph.Commit(At(0.9d + (cycle * 2d), step++));
                    graph.Commit(At(1.9d + (cycle * 2d), step++));
                }

                NoteEvidence("pingpong-soak", "1000 cycles: 0.2=" + sink.Get("M0")
                    + " 0.5=" + sink.Get("M1") + " 0.8=" + sink.Get("M2"));
                Assert.AreEqual(2000, sink.Get("M0"));
                Assert.AreEqual(2000, sink.Get("M1"));
                Assert.AreEqual(2000, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [TestCase(0.25f, double.NaN)]
        [TestCase(0.25f, double.PositiveInfinity)]
        [TestCase(0.25f, double.NegativeInfinity)]
        [TestCase(1f, double.NaN)]
        [TestCase(1f, double.PositiveInfinity)]
        [TestCase(1f, double.NegativeInfinity)]
        [TestCase(2.5f, double.NaN)]
        [TestCase(2.5f, double.PositiveInfinity)]
        [TestCase(2.5f, double.NegativeInfinity)]
        public void SeekPlayback_ExtremeTime_IsSanitized(float length, double time)
        {
            CreateActor("SeekSanitize_" + length, out Animator animator);
            AnimationClip clip = CreateClip("SeekSanitizeClip_" + length, length);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(At(0d, 0L));
                Assert.IsTrue(graph.SeekPlayback(key, time));
                graph.Commit(At(1d, 1L));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                NoteEvidence("seek-sanitize", "seek(" + time + ") -> time " + buffer[0].Time);
                Assert.IsFalse(double.IsNaN(buffer[0].Time), "NaN must not reach the playable");
                Assert.IsTrue(buffer[0].Time >= 0d && buffer[0].Time <= length);
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
