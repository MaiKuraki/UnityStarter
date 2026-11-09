using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Clip-local events and exit time. Events come from <see cref="AnimationClip.events"/> and are dispatched by
    /// a half-open interval sweep; exit time uses the same normalized crossing and reports through
    /// <c>TransitionCompleted</c>.
    /// </summary>
    public sealed class EvidenceClipEventTests : EvidenceTestBase
    {
        private sealed class RecordingSink : IAnimationClipEventSink
        {
            public int Count;
            public string LastName;
            public string LastParameter;
            public int LastInt;
            public readonly List<string> Order = new List<string>(8);

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Count++;
                LastName = clipEvent.FunctionName;
                LastParameter = clipEvent.StringParameter;
                LastInt = clipEvent.IntParameter;
                Order.Add("clip:" + clipEvent.FunctionName);
            }
        }

        private sealed class CountingSink : IAnimationClipEventSink
        {
            public int Count;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Count++;
            }
        }

        private static AnimationTick TickWithDelta(long step, float delta)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, delta);
        }

        private AnimationClip CreateEventClip(string name, float length, float eventTime)
        {
            AnimationClip clip = CreateClip(name, length);
            clip.wrapMode = WrapMode.ClampForever;
            clip.AddEvent(new AnimationEvent
            {
                time = eventTime,
                functionName = "Footstep",
                stringParameter = "Stone",
                intParameter = 7,
                floatParameter = 0.5f
            });
            return clip;
        }

        /// <summary>Clip with one marker per supplied time, in the order given.</summary>
        private AnimationClip CreateEventClipAt(string name, float length, params float[] times)
        {
            AnimationClip clip = CreateClip(name, length);
            clip.wrapMode = WrapMode.ClampForever;
            for (int i = 0; i < times.Length; i++)
            {
                clip.AddEvent(new AnimationEvent
                {
                    time = times[i],
                    functionName = "Marker" + i,
                    stringParameter = "Stone",
                    intParameter = i,
                    floatParameter = 0.5f
                });
            }

            return clip;
        }

        [Test]
        public void E1_ClipEvent_FiresWhenTimeCrossesIt()
        {
            CreateActor("E1Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E1Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            var request = new AnimationClipRequest(1, 0, 0, clip, 0d, 1f);
            try
            {
                graph.BeginClip(request);
                graph.Commit(TickWithDelta(0L, 0.1f));
                Assert.AreEqual(0, sink.Count, "the marker is ahead of the first sample");

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(TickWithDelta(1L, 0.1f));

                NoteEvidence("E1", "events=" + sink.Count + " name=" + sink.LastName
                    + " param=" + sink.LastParameter + " int=" + sink.LastInt);
                Assert.AreEqual(1, sink.Count);
                Assert.AreEqual("Footstep", sink.LastName);
                Assert.AreEqual("Stone", sink.LastParameter);
                Assert.AreEqual(7, sink.LastInt);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E2_ClipEvent_FiresOncePerLoopCrossing()
        {
            CreateActor("E2Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E2Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(0L, 0f));            // anchor at t=0
                graph.Commit(new AnimationTick(1.6d, 1L, AnimationTimeSource.Update, false, 0.016f));
                Assert.AreEqual(1, sink.Count, "crossing 0.5 on the first loop");
                graph.Commit(new AnimationTick(2.4d, 2L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(2.6d, 3L, AnimationTimeSource.Update, false, 0.016f));
                Assert.AreEqual(2, sink.Count, "the marker fires again after the wrap");

                NoteEvidence("E2", "events across wraps = " + sink.Count);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E3_BackwardSeek_SuppressesEvents()
        {
            CreateActor("E3Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E3Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.1f));
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.9d, 1f));
                graph.Commit(TickWithDelta(1L, 0.1f));
                Assert.AreEqual(1, sink.Count);

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.1d, 1f));
                graph.Commit(TickWithDelta(2L, 0.1f));
                Assert.AreEqual(1, sink.Count, "a backward seek does not replay history");

                NoteEvidence("E3", "events after backward seek = " + sink.Count);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E4_ClipEventAtZero_FiresOnTheFirstCommit()
        {
            CreateActor("E4Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E4Clip", 1f, 0f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.1f));
                Assert.AreEqual(1, sink.Count, "an event authored at time 0 fires on the first sample");
                NoteEvidence("E4", "event at t=0 count = " + sink.Count);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E5_ExitTimeOnce_FiresExactlyOnce()
        {
            CreateActor("E5Actor", out Animator animator);
            AnimationClip clip = CreateClip("E5Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            int exitCount = 0;
            graph.TransitionCompleted += (handle, reason) =>
            {
                if (reason == AnimationTransitionReason.ExitTime)
                {
                    exitCount++;
                }
            };

            try
            {
                var key = new AnimationPlaybackKey(1, 0, 0, 0);
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.Once));
                graph.Commit(TickWithDelta(0L, 0.1f));

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(TickWithDelta(1L, 0.1f));
                Assert.AreEqual(1, exitCount);

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.9d, 1f));
                graph.Commit(TickWithDelta(2L, 0.1f));
                Assert.AreEqual(1, exitCount, "Once fires at the first crossing only");

                NoteEvidence("E5", "exit events = " + exitCount);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E6_ExitTimeEveryLoop_FiresOncePerLoop()
        {
            CreateActor("E6Actor", out Animator animator);
            AnimationClip clip = CreateClip("E6Clip", 1f);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            int exitCount = 0;
            graph.TransitionCompleted += (handle, reason) =>
            {
                if (reason == AnimationTransitionReason.ExitTime)
                {
                    exitCount++;
                }
            };

            try
            {
                var key = new AnimationPlaybackKey(1, 0, 0, 0);
                graph.BeginClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.EveryLoop));
                graph.Commit(new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0.016f));

                graph.Commit(new AnimationTick(1.6d, 1L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(2.4d, 2L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(2.6d, 3L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(3.4d, 4L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(3.6d, 5L, AnimationTimeSource.Update, false, 0.016f));

                Assert.AreEqual(3, exitCount, "EveryLoop fires on each crossing");
                NoteEvidence("E6", "every-loop exit events = " + exitCount);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E7_ClipEventAndExitTime_AreOrderedClipThenExit()
        {
            CreateActor("E7Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E7Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);
            graph.TransitionCompleted += (handle, reason) => sink.Order.Add("exit:" + reason);

            try
            {
                var key = new AnimationPlaybackKey(1, 0, 0, 0);
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.Once));
                graph.Commit(TickWithDelta(0L, 0.1f));
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(TickWithDelta(1L, 0.1f));

                NoteEvidence("E7", "order=" + string.Join(",", sink.Order));
                Assert.AreEqual(2, sink.Order.Count);
                StringAssert.StartsWith("clip:Footstep", sink.Order[0]);
                StringAssert.StartsWith("exit:ExitTime", sink.Order[1]);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E8_TwoPlaybacksOnTheSameClip_EachReceiveTheirEvent()
        {
            CreateActor("E8Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E8Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0));
                graph.BeginClip(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f, false, 1));
                graph.Commit(TickWithDelta(0L, 0.1f));
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f, false, 0));
                graph.UpdateClip(new AnimationClipRequest(2, 0, 0, clip, 0.6d, 1f, false, 1));
                graph.Commit(TickWithDelta(1L, 0.1f));

                Assert.AreEqual(2, sink.Count, "one event per playback, even when the clip is shared");
                NoteEvidence("E8", "two playbacks on one clip: events=" + sink.Count);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E9_DispatchSwitch_DisablesClipEvents()
        {
            CreateActor("E9Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E9Clip", 1f, 0.5f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.External,
                false);
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.1f));
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(TickWithDelta(1L, 0.1f));

                Assert.AreEqual(0, sink.Count, "a timeline-owned graph can suppress clip events");
                NoteEvidence("E9", "dispatch disabled: events=" + sink.Count);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E11_NativeFireEventsWithDispatchEnabled_IsReported()
        {
            CreateActor("E11Actor", out Animator animator);
            animator.fireEvents = true;
            var diagnostics = new CountingDiagnostics(true);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                diagnostics);

            try
            {
                NoteEvidence("E11", "warnings with native fireEvents enabled = " + diagnostics.WarningCount);
                Assert.AreEqual(1, diagnostics.WarningCount,
                    "the double-dispatch hazard is reported once at graph construction");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E10_EventDispatch_DoesNotAllocate()
        {
            CreateActor("E10Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E10Clip", 1f, 0.5f);
            var sink = new CountingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.1f));

                long heapDelta = MeasureAllocationDelta(1000, 10000, () =>
                {
                    graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.4d, 1f));
                    graph.Commit(TickWithDelta(1L, 0.1f));
                    graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                    graph.Commit(TickWithDelta(2L, 0.1f));
                });

                NoteEvidence("E10", "10000 event crossings: heap delta " + FormatBytes(heapDelta)
                    + ", events=" + sink.Count);
                Assert.Less(heapDelta, 64L * 1024L, "event dispatch must not allocate per crossing");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E12_ZeroDurationFadeMidPlayback_DoesNotReplayMarkers()
        {
            CreateActor("E12Actor", out Animator animator);
            AnimationClip clip = CreateEventClipAt("E12Clip", 1f, 0.2f, 0.5f, 0.8f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.016f));
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.4d, 1f));
                graph.Commit(TickWithDelta(1L, 0.016f));
                Assert.AreEqual(1, sink.Count, "only the 0.2 marker has been crossed");

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.9d, 1f));
                graph.Commit(TickWithDelta(2L, 0.016f));
                int before = sink.Count;
                Assert.AreEqual(3, before, "all three markers have been crossed");

                Assert.IsTrue(graph.FadePlayback(key, new AnimationFade(0f, 0.5f)));
                graph.Commit(TickWithDelta(3L, 0.016f));

                NoteEvidence("E12", "markers before the zero-duration fade = " + before
                    + ", after = " + sink.Count);

                Assert.AreEqual(before, sink.Count, "a zero-duration fade must not replay passed markers");
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5f, buffer[0].Weight, 1e-4f, "the fade still applies its target weight");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E13_ZeroDurationFadeOnSpawn_StillFiresTheMarkerAtZero()
        {
            CreateActor("E13Actor", out Animator animator);
            AnimationClip clip = CreateEventClip("E13Clip", 1f, 0f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(0f, 1f));
                graph.Commit(TickWithDelta(0L, 0.016f));

                NoteEvidence("E13", "marker at t=0 with a zero-duration fade = " + sink.Count);
                Assert.AreEqual(1, sink.Count, "spawning with a fade must not change which markers fire");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E14_LoopedBackwardSeek_SuppressesEvents()
        {
            CreateActor("E14Actor", out Animator animator);
            AnimationClip clip = CreateEventClipAt("E14Clip", 1f, 0.05f, 0.5f, 0.95f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                // Loop is ignored under the external policy but still stored in the clock; the markers sit in the
                // tail and head bands a wrap would report.
                graph.BeginClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(0L, 0.016f));
                graph.UpdateClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0.9d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(1L, 0.016f));
                int before = sink.Count;
                Assert.AreEqual(2, before, "the 0.05 and 0.5 markers have been crossed");

                graph.UpdateClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0.1d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(2L, 0.016f));

                NoteEvidence("E14", "markers before the backward seek = " + before
                    + ", after = " + sink.Count);
                Assert.AreEqual(before, sink.Count, "a backward host write is a discontinuity, not a wrap");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E14b_LoopedSeekBack_DoesNotReplayTheExitPoint()
        {
            CreateActor("E14bActor", out Animator animator);
            AnimationClip clip = CreateClip("E14bClip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            int exitCount = 0;

            graph.TransitionCompleted += (handle, reason) =>
            {
                if (reason == AnimationTransitionReason.ExitTime)
                {
                    exitCount++;
                }
            };

            try
            {
                graph.BeginClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.EveryLoop));
                graph.Commit(TickWithDelta(0L, 0.016f));

                graph.UpdateClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0.9d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(1L, 0.016f));
                Assert.AreEqual(1, exitCount);

                graph.UpdateClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0.6d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(TickWithDelta(2L, 0.016f));

                NoteEvidence("E14b", "exit reports after seeking back to 0.6 = " + exitCount);
                Assert.AreEqual(1, exitCount, "seeking back past the exit point is not a loop crossing");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void E15_HitchLargerThanTheClip_DoesNotReplayPassedMarkers()
        {
            CreateActor("E15Actor", out Animator animator);
            AnimationClip clip = CreateEventClipAt("E15Clip", 1f, 0.2f, 0.5f, 0.8f);
            var sink = new RecordingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0f));
                graph.Commit(new AnimationTick(0.3d, 1L, AnimationTimeSource.Update, false, 0.3f));
                Assert.AreEqual(1, sink.Count, "only the 0.2 marker has been crossed");

                // 2.5 s of elapsed time on a 1 s clip: the clip clamps at its end, a 0.7 L displacement.
                graph.Commit(new AnimationTick(2.5d, 2L, AnimationTimeSource.Update, false, 2.5f));

                NoteEvidence("E15", "markers after a 2.5 s hitch on a 1 s clip = " + sink.Count
                    + " (expected 3: 0.2, then 0.5 and 0.8, never 0.2 again)");
                Assert.AreEqual(3, sink.Count, "a hitch reports the markers the clip actually crossed");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
