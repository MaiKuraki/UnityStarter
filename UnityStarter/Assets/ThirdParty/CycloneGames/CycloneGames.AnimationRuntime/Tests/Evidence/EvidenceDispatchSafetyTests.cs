using System;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Event handlers sit on the commit boundary. A throwing handler must not abort the remaining events, and a
    /// handler that calls Commit must not re-enter the graph.
    /// </summary>
    public sealed class EvidenceDispatchSafetyTests : EvidenceTestBase
    {
        private sealed class ThrowingSink : IAnimationClipEventSink
        {
            public int Calls;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Calls++;
                if (Calls == 1)
                {
                    throw new InvalidOperationException("injected sink failure");
                }
            }
        }

        private sealed class ReentrantSink : IAnimationClipEventSink
        {
            public AnimationRuntimeGraph Graph;
            public AnimationClip Clip;
            public int Calls;

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Calls++;
                if (Calls != 1)
                {
                    return;
                }

                Graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, Clip, 0.6d, 0.5f));
                Graph.Commit(new AnimationTick(1d, 99L, AnimationTimeSource.Update, false, 0.016f));
            }
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private AnimationClip TwoMarkerClip(string name)
        {
            AnimationClip clip = CreateClip(name, 1f);
            clip.AddEvent(new AnimationEvent { time = 0.2f, functionName = "A" });
            clip.AddEvent(new AnimationEvent { time = 0.5f, functionName = "B" });
            return clip;
        }

        [Test]
        public void SinkThrows_RemainingEventsAreStillDelivered()
        {
            CreateActor("SafetyThrow", out Animator animator);
            AnimationClip clip = TwoMarkerClip("SafetyThrowClip");
            var diagnostics = new CountingDiagnostics(true);
            var sink = new ThrowingSink();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                diagnostics);
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));

                Assert.DoesNotThrow(() => graph.Commit(Tick(1L)));
                NoteEvidence("safety", "throwing sink calls=" + sink.Calls + " errors=" + diagnostics.ErrorCount);
                Assert.AreEqual(2, sink.Calls, "the second handler invocation must still run");
                Assert.AreEqual(1, diagnostics.ErrorCount, "the exception is reported once");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void TransitionHandlerThrows_IsContained()
        {
            CreateActor("SafetyTransition", out Animator animator);
            AnimationClip clip = TwoMarkerClip("SafetyTransitionClip");
            var diagnostics = new CountingDiagnostics(true);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                diagnostics);
            var key = new AnimationPlaybackKey(1, 0, 0, 0);

            graph.TransitionCompleted += (handle, reason) =>
                throw new InvalidOperationException("injected transition failure");

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.Once));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));

                Assert.DoesNotThrow(() => graph.Commit(Tick(1L)));
                NoteEvidence("safety-transition", "errors=" + diagnostics.ErrorCount);
                Assert.AreEqual(1, diagnostics.ErrorCount);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void TransitionHandlerReentersCommit_IsIgnored()
        {
            CreateActor("SafetyTransitionReentrant", out Animator animator);
            AnimationClip clip = TwoMarkerClip("SafetyTransitionReentrantClip");
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            int calls = 0;

            graph.TransitionCompleted += (handle, reason) =>
            {
                calls++;
                if (calls != 1)
                {
                    return;
                }

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 0.5f));
                graph.Commit(new AnimationTick(1d, 99L, AnimationTimeSource.Update, false, 0.016f));
            };

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.Once));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(Tick(1L));

                NoteEvidence("safety-transition-reentrant", "calls=" + calls
                    + " evaluations=" + graph.GetStats().Evaluations);
                Assert.AreEqual(1, calls);
                Assert.AreEqual(2, graph.GetStats().Evaluations);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void SinkReentersCommit_IsIgnored()
        {
            CreateActor("SafetyReentrant", out Animator animator);
            AnimationClip clip = TwoMarkerClip("SafetyReentrantClip");
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var sink = new ReentrantSink { Graph = graph, Clip = clip };
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
                graph.Commit(Tick(1L));

                NoteEvidence("safety-reentrant", "sink calls=" + sink.Calls
                    + " evaluations=" + graph.GetStats().Evaluations);
                Assert.AreEqual(2, sink.Calls, "both markers are delivered to the sink");
                Assert.AreEqual(2, graph.GetStats().Evaluations,
                    "the outer commit and the first commit only; the nested commit is rejected");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
