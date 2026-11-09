using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Whole-cycle boundary behaviour: a marker at the previous position and an exit point must still fire when
    /// the step advances exactly one clip length.
    /// </summary>
    public sealed class EvidenceCycleBoundaryTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Counts.TryGetValue(clipEvent.FunctionName, out int count);
                Counts[clipEvent.FunctionName] = count + 1;
            }

            public int Get(string name)
            {
                return Counts.TryGetValue(name, out int value) ? value : 0;
            }
        }

        private static AnimationTick At(double time, long step)
        {
            return new AnimationTick(time, step, AnimationTimeSource.Update, false, 0.016f);
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

        [Test]
        public void LoopWholeCycle_FromNonZeroStart_RepeatsTheMarkerAtTheStart()
        {
            CreateActor("ProbeA", out Animator animator);
            AnimationClip clip = ClipWith("ProbeClipA", 1f, 0.5f);
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
                graph.Commit(At(1.5d, 2L));

                NoteEvidence("probe-a", "0.5 marker count = " + sink.Get("M0"));
                Assert.AreEqual(2, sink.Get("M0"), "0.5 is crossed at the first pass and again at the loop boundary");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void LoopWholeCycle_ExitTimeEveryCrossing_FiresAgain()
        {
            CreateActor("ProbeB", out Animator animator);
            AnimationClip clip = ClipWith("ProbeClipB", 1f);
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
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.EveryCrossing));
                graph.Commit(At(0d, 0L));
                graph.Commit(At(0.5d, 1L));
                graph.Commit(At(1.5d, 2L));

                NoteEvidence("probe-b", "exit count = " + exits);
                Assert.AreEqual(2, exits, "EveryCrossing crosses 0.5 at 0.5 and again after one full loop");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void PingPongTurn_ExitEveryCrossing_FiresExactlyOnce()
        {
            CreateActor("ProbeC", out Animator animator);
            AnimationClip clip = ClipWith("ProbeClipC", 1f);
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

                NoteEvidence("probe-c", "exit count = " + exits);
                Assert.AreEqual(1, exits, "the turn reaches 0.95 once");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
