using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Weight-fade contract: trajectory, easing, replacement, completion events, release-after-fade and
    /// allocation. Time comes from <see cref="AnimationTick.Delta"/>, so the tests are independent of
    /// <c>Time.deltaTime</c> and can run in batch mode deterministically.
    /// </summary>
    public sealed class EvidenceTransitionTests : EvidenceTestBase
    {
        private static AnimationTick TickWithDelta(long step, float delta)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, delta);
        }

        [Test]
        public void F1_LinearFade_FollowsTheReferenceTrajectory()
        {
            CreateActor("F1Actor", out Animator animator);
            AnimationClip clip = CreateClip("F1Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                const float duration = 0.2f;
                const float delta = 0.05f;
                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(duration, 1f, AnimationFadeEasing.Linear));

                for (int step = 1; step <= 4; step++)
                {
                    graph.Commit(TickWithDelta(step, delta));
                    Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                    float expected = AnimationFade.Evaluate(
                        AnimationFadeEasing.Linear,
                        Mathf.Min(1f, (step * delta) / duration));
                    Assert.AreEqual(expected, buffer[0].Weight, 1e-4f, "step " + step);
                }

                Assert.AreEqual(1f, buffer[0].Weight, 1e-4f);
                NoteEvidence("F1", "linear fade over 4 x 50 ms reached weight " + buffer[0].Weight.ToString("0.000"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F2_EasingCurves_AreNormalisedAndMonotonic()
        {
            AnimationFadeEasing[] curves =
            {
                AnimationFadeEasing.Linear,
                AnimationFadeEasing.SmoothStep,
                AnimationFadeEasing.EaseIn,
                AnimationFadeEasing.EaseOut
            };

            for (int i = 0; i < curves.Length; i++)
            {
                AnimationFadeEasing curve = curves[i];
                Assert.AreEqual(0f, AnimationFade.Evaluate(curve, 0f), 1e-6f);
                Assert.AreEqual(1f, AnimationFade.Evaluate(curve, 1f), 1e-6f);

                float previous = 0f;
                for (int step = 1; step <= 10; step++)
                {
                    float value = AnimationFade.Evaluate(curve, step / 10f);
                    Assert.GreaterOrEqual(value, previous - 1e-6f, curve + " must be monotonic");
                    previous = value;
                }
            }

            NoteEvidence("F2", curves.Length + " easing curves: endpoints exact, monotonic over 10 samples");
        }

        [Test]
        public void F3_ReplacementInheritsTheCurrentWeight()
        {
            CreateActor("F3Actor", out Animator animator);
            AnimationClip clip = CreateClip("F3Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.016f));

                graph.FadePlayback(
                    new AnimationPlaybackKey(1, 0, 0, 0),
                    new AnimationFade(0.2f, 0.5f, AnimationFadeEasing.Linear));
                graph.Commit(TickWithDelta(1L, 0.1f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.75f, buffer[0].Weight, 1e-4f);

                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(0.2f, 1f, AnimationFadeEasing.Linear));
                graph.Commit(TickWithDelta(2L, 0.1f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.875f, buffer[0].Weight, 1e-4f,
                    "the new fade starts from the current 0.75, not from 0");

                NoteEvidence("F3", "replacement inherited 0.75 and advanced to " + buffer[0].Weight.ToString("0.000"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F4_CompletionEvent_FiresExactlyOnce()
        {
            CreateActor("F4Actor", out Animator animator);
            AnimationClip clip = CreateClip("F4Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            int events = 0;

            try
            {
                graph.TransitionCompleted += (handle, reason) =>
                {
                    events++;
                    Assert.AreEqual(AnimationTransitionReason.FadeCompleted, reason);
                };

                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(0.1f, 1f, AnimationFadeEasing.Linear));

                graph.Commit(TickWithDelta(1L, 0.05f));
                Assert.AreEqual(0, events);
                graph.Commit(TickWithDelta(2L, 0.05f));
                Assert.AreEqual(1, events);
                graph.Commit(TickWithDelta(3L, 0.5f));
                Assert.AreEqual(1, events, "further commits must not repeat the completion");

                NoteEvidence("F4", "completion events after 3 commits = " + events);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F5_InstantFade_AppliesImmediatelyWithoutAnEvent()
        {
            CreateActor("F5Actor", out Animator animator);
            AnimationClip clip = CreateClip("F5Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var buffer = new AnimationPlaybackSnapshot[4];
            int events = 0;

            try
            {
                graph.TransitionCompleted += (handle, reason) => events++;
                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(0f, 0.3f, AnimationFadeEasing.Linear));
                graph.Commit(TickWithDelta(0L, 0.016f));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.3f, buffer[0].Weight, 1e-4f);
                Assert.AreEqual(0, events, "a zero-duration fade is not a transition");
                NoteEvidence("F5", "instant fade weight " + buffer[0].Weight.ToString("0.000") + ", events " + events);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F6_EndClipWithFade_ReleasesOnlyAfterTheRamp()
        {
            CreateActor("F6Actor", out Animator animator);
            AnimationClip clip = CreateClip("F6Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var buffer = new AnimationPlaybackSnapshot[4];
            int events = 0;

            try
            {
                graph.TransitionCompleted += (handle, reason) => events++;
                var key = new AnimationPlaybackKey(1, 0, 0, 0);
                graph.BeginClip(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(TickWithDelta(0L, 0.016f));

                graph.EndClip(key, new AnimationFade(0.1f, 0f, AnimationFadeEasing.Linear));
                graph.Commit(TickWithDelta(1L, 0.05f));
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks);
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5f, buffer[0].Weight, 1e-4f);

                graph.Commit(TickWithDelta(2L, 0.05f));
                Assert.AreEqual(1, events);
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks, "release is deferred to the next commit");
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0f, buffer[0].Weight, 1e-4f);

                graph.Commit(TickWithDelta(3L, 0.016f));
                Assert.AreEqual(0, graph.GetStats().ActivePlaybacks);

                NoteEvidence("F6", "fade-out completed, event=" + events + ", playbacks after release="
                    + graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F7_StopAllAndDispose_DoNotRaiseCompletionEvents()
        {
            CreateActor("F7Actor", out Animator animator);
            AnimationClip clip = CreateClip("F7Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            int events = 0;

            try
            {
                graph.TransitionCompleted += (handle, reason) => events++;
                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(1f, 1f, AnimationFadeEasing.Linear));
                graph.Commit(TickWithDelta(0L, 0.1f));
                graph.StopAll();
                graph.Commit(TickWithDelta(1L, 0.1f));

                Assert.AreEqual(0, events);
                Assert.AreEqual(0, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
            }

            Assert.AreEqual(0, events, "Dispose must not raise completion events");
        }

        [Test]
        public void F8_FadePath_DoesNotAllocate()
        {
            CreateActor("F8Actor", out Animator animator);
            AnimationClip clip = CreateClip("F8Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            int events = 0;
            graph.TransitionCompleted += (handle, reason) => events++;

            try
            {
                long heapDelta = MeasureAllocationDelta(1000, 10000, () =>
                {
                    graph.BeginClip(
                        new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                        new AnimationFade(1f, 1f, AnimationFadeEasing.Linear));
                    graph.Commit(TickWithDelta(0L, 0.016f));
                });

                NoteEvidence("F8", "10000 fade starts + commits: heap delta " + FormatBytes(heapDelta)
                    + ", events=" + events);
                Assert.Less(heapDelta, 64L * 1024L, "steady-state fade path must not allocate");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F9_FadeMatrix_DurationEasingDelta_AlwaysReachesTheTarget()
        {
            CreateActor("F9Actor", out Animator animator);
            AnimationClip clip = CreateClip("F9Clip", 10f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            var buffer = new AnimationPlaybackSnapshot[4];

            float[] durations = { 0.05f, 0.1f, 0.5f, 1f };
            float[] deltas = { 0.016f, 0.05f, 0.2f };
            AnimationFadeEasing[] curves =
            {
                AnimationFadeEasing.Linear,
                AnimationFadeEasing.SmoothStep,
                AnimationFadeEasing.EaseIn,
                AnimationFadeEasing.EaseOut
            };

            int combinations = 0;
            long step = 0;

            try
            {
                for (int durationIndex = 0; durationIndex < durations.Length; durationIndex++)
                {
                    for (int deltaIndex = 0; deltaIndex < deltas.Length; deltaIndex++)
                    {
                        for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                        {
                            graph.StopAll();
                            graph.Commit(TickWithDelta(step++, 0.016f));

                            graph.BeginClip(
                                new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                                new AnimationFade(durations[durationIndex], 1f, curves[curveIndex]));

                            int steps = Mathf.CeilToInt(durations[durationIndex] / deltas[deltaIndex]) + 1;
                            for (int i = 0; i < steps; i++)
                            {
                                graph.Commit(TickWithDelta(step++, deltas[deltaIndex]));
                            }

                            Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                            Assert.AreEqual(1f, buffer[0].Weight, 1e-4f,
                                "duration=" + durations[durationIndex] + " delta=" + deltas[deltaIndex]
                                + " curve=" + curves[curveIndex]);
                            combinations++;
                        }
                    }
                }

                NoteEvidence("F9", "fade matrix: " + combinations + " combinations all reached weight 1");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void F10_UpdateClipWeight_IsIgnoredWhileAFadeIsActive()
        {
            CreateActor("F10Actor", out Animator animator);
            AnimationClip clip = CreateClip("F10Clip", 10f);
            var diagnostics = new CountingDiagnostics(true);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default }, diagnostics);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginClip(
                    new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                    new AnimationFade(1f, 1f, AnimationFadeEasing.Linear));

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.1d, 0.1f));
                graph.Commit(TickWithDelta(0L, 0.1f));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.1f, buffer[0].Weight, 1e-4f, "the fade, not UpdateClip, owns the weight");
                Assert.AreEqual(1, diagnostics.WarningCount);

                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, 0.2d, 0.2f));
                graph.Commit(TickWithDelta(1L, 0.1f));
                Assert.AreEqual(1, diagnostics.WarningCount, "the conflict warning is reported once per graph");

                NoteEvidence("F10", "weight conflict warnings=" + diagnostics.WarningCount
                    + ", weight=" + buffer[0].Weight.ToString("0.000"));
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
