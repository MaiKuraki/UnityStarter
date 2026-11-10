using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Loop and ping-pong crossings: a step of a whole clip, a step that turns at the end of the clip, the exit point
    /// on the far side of a turn, and the clock re-anchored by a rewind or a seek.
    /// </summary>
    public sealed class EvidenceCycleTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly List<string> Order = new List<string>(32);
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(8);

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

        private AnimationClip ClipWith(float length, params float[] times)
        {
            AnimationClip clip = CreateClip("CycleClip_" + length + "_" + times.Length, length);
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent { time = times[i], functionName = "M" + i });
            }

            return clip;
        }

        private static AnimationTick At(double time, long step)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private static AnimationTick Rewind(double time, long step)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, true, 0.016f);
        }

        private AnimationRuntimeGraph ExternalGraph(Animator animator, Sink sink)
        {
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.External);
            graph.SetClipEventSink(sink);
            return graph;
        }

        private AnimationRuntimeGraph Graph(Animator animator, Sink sink)
        {
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);
            return graph;
        }

        [Test]
        public void LoopWholeCycle_FiresEveryMarkerOnce()
        {
            CreateActor("CycleLoopActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(2.4d, 1L));

                NoteEvidence("cycle-loop", "order=" + string.Join(",", sink.Order)
                    + " 0.25=" + sink.Get("M0") + " 0.5=" + sink.Get("M1") + " 0.75=" + sink.Get("M2"));

                Assert.AreEqual(1, sink.Get("M0"), "the clock passed the first marker");
                Assert.AreEqual(1, sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void LoopWholeCycleTwice_FiresEveryMarkerOncePerCycle()
        {
            CreateActor("CycleLoopTwiceActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(1.3d, 1L));
                graph.Commit(At(2.6d, 2L));

                NoteEvidence("cycle-loop-twice", "0.25=" + sink.Get("M0") + " 0.5=" + sink.Get("M1")
                    + " 0.75=" + sink.Get("M2"));
                Assert.AreEqual(2, sink.Get("M0"), "two whole-clip steps, one report each");
                Assert.AreEqual(2, sink.Get("M1"));
                Assert.AreEqual(2, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongTurn_FiresTheMarkersAboveTheFoldedInterval()
        {
            CreateActor("CycleTurnActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.9f, 0.95f, 0.98f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                graph.Commit(At(1.1d, 2L));

                NoteEvidence("cycle-turn", "0.9=" + sink.Get("M0") + " 0.95=" + sink.Get("M1")
                    + " 0.98=" + sink.Get("M2"));
                Assert.AreEqual(2, sink.Get("M0"), "0.9 is crossed on the way up and again on the way back");
                Assert.AreEqual(2, sink.Get("M1"), "0.95 is only reachable through the turn");
                Assert.AreEqual(2, sink.Get("M2"), "0.98 is only reachable through the turn");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongTurn_ReportsBothLegsOfTheTurn()
        {
            CreateActor("CycleTurnBothLegsActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.5f, 0.9f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.5d, 1L));
                graph.Commit(At(1.2d, 2L));

                NoteEvidence("cycle-turn-both", "order=" + string.Join(",", sink.Order)
                    + " 0.5=" + sink.Get("M0") + " 0.9=" + sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M0"), "0.5 is crossed on the way up only");
                Assert.AreEqual(2, sink.Get("M1"), "0.9 is crossed on the way up and on the way back");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongTurn_ExitTimeFires()
        {
            CreateActor("CycleTurnExitActor", out Animator animator);
            AnimationClip clip = ClipWith(1f);
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
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.SetExitTime(key, new AnimationExitTime(0.95f, AnimationExitMode.EveryCrossing));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                graph.Commit(At(1.1d, 2L));

                NoteEvidence("cycle-turn-exit", "exit reports = " + exits);
                Assert.GreaterOrEqual(exits, 1, "the exit point sits above the folded interval");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongTenRoundTrips_EachMarkerPassedTwentyTimes()
        {
            CreateActor("CycleConservationActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.2f, 0.5f, 0.8f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));

                // 165 commits over exactly 10 round trips; no tick lands on a turning point.
                const int commits = 165;
                for (int i = 0; i <= commits; i++)
                {
                    graph.Commit(At(i * 20d / commits, i));
                }

                NoteEvidence("cycle-conservation", "0.2=" + sink.Get("M0") + " 0.5=" + sink.Get("M1")
                    + " 0.8=" + sink.Get("M2"));
                Assert.AreEqual(20, sink.Get("M0"), "ten round trips cross the first marker twenty times");
                Assert.AreEqual(20, sink.Get("M1"));
                Assert.AreEqual(20, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void Rewind_AfterAWholeCycleStep_ReportsNothingAndKeepsThePose()
        {
            CreateActor("CycleRewindActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.5f, 0.9f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(2.6d, 1L));
                Assert.AreEqual(1, sink.Get("M0"), "the whole-cycle step reports both markers once");
                Assert.AreEqual(1, sink.Get("M1"));

                // A rewind re-anchors the clock on the current pose; no cycle or turn is inferred.
                graph.Commit(Rewind(2d, 2L));
                Assert.AreEqual(1, sink.Get("M0"), "a rewind reports nothing");
                Assert.AreEqual(1, sink.Get("M1"));

                graph.Commit(At(2.3d, 3L));
                NoteEvidence("cycle-rewind", "0.5=" + sink.Get("M0") + " 0.9=" + sink.Get("M1")
                    + " (expected 1 and 2)");
                Assert.AreEqual(1, sink.Get("M0"), "the clock continued from the pose, so 0.5 is not crossed");
                Assert.AreEqual(2, sink.Get("M1"), "the descent from 1.0 crosses 0.9 again");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void Seek_BackwardsAcrossATurn_ReportsNothing()
        {
            CreateActor("CycleSeekActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.5f, 0.9f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                Assert.AreEqual(1, sink.Get("M0"));
                Assert.AreEqual(1, sink.Get("M1"));

                Assert.IsTrue(graph.SeekPlayback(key, 0.5d));
                graph.Commit(At(1d, 2L));
                graph.Commit(At(1.2d, 3L));

                NoteEvidence("cycle-seek", "0.5=" + sink.Get("M0") + " 0.9=" + sink.Get("M1")
                    + " (expected 1 and 1)");
                Assert.AreEqual(1, sink.Get("M0"), "a seek is a discontinuity, not a crossing");
                Assert.AreEqual(1, sink.Get("M1"), "the turn band must not fire on a backward seek");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongHalfCycle_ReportsTheMarkerAtTheEnd()
        {
            CreateActor("CycleHalfActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.5d, 1L));
                graph.Commit(At(1.5d, 2L));

                NoteEvidence("cycle-half-marker", "0.5=" + sink.Get("M0") + " (expected 2)");
                Assert.AreEqual(2, sink.Get("M0"), "a half round trip returns to 0.5, so it is crossed twice");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongHalfCycle_ExitTimeFires()
        {
            CreateActor("CycleHalfExitActor", out Animator animator);
            AnimationClip clip = ClipWith(1f);
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
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.SetExitTime(key, new AnimationExitTime(0.83f, AnimationExitMode.EveryCrossing));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(1.2d, 1L));

                NoteEvidence("cycle-half-exit", "exit reports = " + exits + " (expected 1)");
                Assert.AreEqual(1, exits, "0.83 lies inside the up leg of a half round trip");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void NegativeSpeed_Clamp_ReportsMarkersInReverse()
        {
            CreateActor("CycleNegativeClampActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 1d, 1f, false, 0, -1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(1d, 1L));

                NoteEvidence("cycle-negative-clamp", "order=" + string.Join(",", sink.Order)
                    + " 0.25=" + sink.Get("M0") + " 0.5=" + sink.Get("M1") + " 0.75=" + sink.Get("M2"));
                Assert.AreEqual(1, sink.Get("M0"), "the reverse pass crosses every marker once");
                Assert.AreEqual(1, sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void NegativeSpeed_Loop_ReportsTheWholeDescent()
        {
            CreateActor("CycleNegativeLoopActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0.9d, 1f, false, 0, -1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.8d, 1L));

                NoteEvidence("cycle-negative-loop", "0.25=" + sink.Get("M0") + " 0.75=" + sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M0"), "the descent covers 0.1 to 0.9, so both markers fire");
                Assert.AreEqual(1, sink.Get("M1"));
            }
            finally
            {
                graph.Dispose();
            }
        }
        [Test]
        public void ClampHugeJump_ReportsEveryMarkerOnce()
        {
            CreateActor("CycleHugeJumpActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(5000d, 1L));

                NoteEvidence("cycle-huge-jump", "0.25=" + sink.Get("M0") + " 0.5=" + sink.Get("M1")
                    + " 0.75=" + sink.Get("M2"));
                Assert.AreEqual(1, sink.Get("M0"), "a clamped jump past the end still reports each marker once");
                Assert.AreEqual(1, sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }
        [Test]
        public void ReverseStart_ReportsEachMarkerOnce()
        {
            CreateActor("CycleReverseStartActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.2f, 0.5f, 0.8f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0.9d, 1f, false, 0, -1f, AnimationLoopMode.Clamp));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));

                NoteEvidence("cycle-reverse-start", "0.2=" + sink.Get("M0") + " 0.5=" + sink.Get("M1")
                    + " 0.8=" + sink.Get("M2"));
                Assert.AreEqual(1, sink.Get("M0"), "a reverse run from 0.9 to 0 crosses every marker once");
                Assert.AreEqual(1, sink.Get("M1"));
                Assert.AreEqual(1, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }
        [Test]
        public void ExternalForwardWrite_SweepsTheInterval()
        {
            CreateActor("CycleExternalActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = ExternalGraph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(At(0d, 0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(At(0.1d, 1L));

                NoteEvidence("external-forward", "0.25=" + sink.Get("M0") + " 0.5=" + sink.Get("M1")
                    + " 0.75=" + sink.Get("M2"));
                Assert.AreEqual(1, sink.Get("M0"));
                Assert.AreEqual(1, sink.Get("M1"));
                Assert.AreEqual(0, sink.Get("M2"), "0.75 is ahead of the write");

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.9d, 1f));
                graph.Commit(At(0.2d, 2L));
                Assert.AreEqual(1, sink.Get("M2"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ExternalBackwardWrite_Suppresses()
        {
            CreateActor("CycleExternalBackActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.25f, 0.5f, 0.75f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = ExternalGraph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(At(0d, 0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.9d, 1f));
                graph.Commit(At(0.1d, 1L));
                int before = sink.Get("M0") + sink.Get("M1") + sink.Get("M2");

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.1d, 1f));
                graph.Commit(At(0.2d, 2L));

                NoteEvidence("external-backward", "before=" + before + " after="
                    + (sink.Get("M0") + sink.Get("M1") + sink.Get("M2")));
                Assert.AreEqual(before, sink.Get("M0") + sink.Get("M1") + sink.Get("M2"),
                    "a backward host write is a discontinuity");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ExternalRepeatedWrite_ReportsNothing()
        {
            CreateActor("CycleExternalRepeatActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = ExternalGraph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(At(0d, 0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(At(0.1d, 1L));
                Assert.AreEqual(1, sink.Get("M0"));

                for (int i = 0; i < 5; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                    graph.Commit(At(0.2d + (i * 0.01d), 2L + i));
                }

                NoteEvidence("external-repeat", "0.5=" + sink.Get("M0"));
                Assert.AreEqual(1, sink.Get("M0"), "a write that does not move reports nothing");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void ReplaceClip_StopsReportingTheOldClip()
        {
            CreateActor("CycleReplaceActor", out Animator animator);
            AnimationClip first = ClipWith(1f, 0.1f, 0.9f);
            AnimationClip second = ClipWith(1f, 0.5f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, first, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.5d, 1L));
                Assert.AreEqual(1, sink.Get("M0"), "0.1 was crossed on the old clip");

                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, second, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(At(1.4d, 2L));
                graph.Commit(At(2d, 3L));

                NoteEvidence("cycle-replace", "0.1=" + sink.Get("M0") + " 0.9=" + sink.Get("M1")
                    + " 0.5=" + sink.Get("M2"));
                Assert.AreEqual(1, sink.Get("M0"), "the old clip reports nothing after the replacement");
                Assert.AreEqual(0, sink.Get("M1"), "0.9 of the old clip was never reached");
                Assert.AreEqual(1, sink.Get("M2"), "the new clip reports its own marker");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void EndPlaybackBeforeTheTurningCommit_EmitsNothing()
        {
            CreateActor("CycleEndTurnActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.95f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                Assert.AreEqual(0, sink.Get("M0"), "0.95 is only reachable through the turn");

                graph.EndPlayback(key);
                graph.Commit(At(1.1d, 2L));

                NoteEvidence("cycle-end-turn", "0.95=" + sink.Get("M0"));
                Assert.AreEqual(0, sink.Get("M0"), "an ended playback does not report the turn it never committed");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void CommittedTurnThenEnd_KeepsTheMarkers()
        {
            CreateActor("CycleCommitThenEndActor", out Animator animator);
            AnimationClip clip = ClipWith(1f, 0.95f);
            var sink = new Sink();
            AnimationRuntimeGraph graph = Graph(animator, sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.PingPong));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.9d, 1L));
                graph.Commit(At(1.1d, 2L));
                int afterTurn = sink.Get("M0");

                graph.EndPlayback(key);
                graph.Commit(At(1.2d, 3L));

                NoteEvidence("cycle-commit-end", "after turn=" + afterTurn + " final=" + sink.Get("M0"));
                Assert.GreaterOrEqual(afterTurn, 1, "the committed turn reports 0.95");
                Assert.AreEqual(afterTurn, sink.Get("M0"), "ending after the commit does not add or drop markers");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}