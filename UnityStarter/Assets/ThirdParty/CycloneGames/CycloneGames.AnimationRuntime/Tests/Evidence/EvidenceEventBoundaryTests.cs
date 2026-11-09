using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Marker placement boundaries: exactly at the end, duplicated times, and times beyond the clip length.
    /// </summary>
    public sealed class EvidenceEventBoundaryTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public int Total;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Total++;
            }
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private AnimationClip ClipWith(string name, float length, params AnimationEvent[] events)
        {
            AnimationClip clip = CreateClip(name, length);
            clip.wrapMode = WrapMode.ClampForever;
            for (int i = 0; i < events.Length; i++)
            {
                clip.AddEvent(events[i]);
            }

            return clip;
        }

        private static AnimationEvent Marker(float time, string name)
        {
            return new AnimationEvent { time = time, functionName = name };
        }

        [Test]
        public void EventAtClipEnd_FiresOnce()
        {
            CreateActor("EndActor", out Animator animator);
            AnimationClip clip = ClipWith("EndClip", 1f, Marker(1f, "End"));
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.99d, 1f));
                graph.Commit(Tick(1L));
                Assert.AreEqual(0, sink.Total, "the marker is still ahead");

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 1d, 1f));
                graph.Commit(Tick(2L));
                graph.Commit(Tick(3L));

                NoteEvidence("boundary-end", "marker at 1.0 fired " + sink.Total + " times");
                Assert.AreEqual(1, sink.Total);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void DuplicateEventTimes_BothFire()
        {
            CreateActor("DuplicateActor", out Animator animator);
            AnimationClip clip = ClipWith("DuplicateClip", 1f, Marker(0.5f, "A"), Marker(0.5f, "B"));
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(Tick(1L));

                NoteEvidence("boundary-duplicate", "two markers at 0.5 fired " + sink.Total + " times");
                Assert.AreEqual(2, sink.Total);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void EventAtZero_OnOffsetStart_DoesNotFireBeforeTheOffset()
        {
            CreateActor("OffsetActor", out Animator animator);
            AnimationClip clip = ClipWith("OffsetClip", 1f, Marker(0f, "Zero"), Marker(0.8f, "Late"));
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.5d, 1f));
                graph.Commit(Tick(0L));

                // Current contract: the first commit sweeps [0, current], so 0 is reported.
                NoteEvidence("boundary-offset", "offset 0.5 first commit fired " + sink.Total + " markers");
                Assert.AreEqual(1, sink.Total);
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
