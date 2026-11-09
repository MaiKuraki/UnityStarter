using System;
using System.Reflection;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Evidence that needs a real <see cref="UnityEngine.Playables.PlayableGraph"/>. Every assertion is structural
    /// (counters, snapshots, capacities, exception types) so the suite is stable in batch mode; timings are only
    /// published as notes.
    ///
    /// These cases encode the post-fix contract. The matching pre-fix numbers are archived in
    /// <c>deliverables/review/animationruntime-2026-11-09/raw/AnimationRuntimeEvidence.md</c> and the differential
    /// table lives in <c>before-after.md</c>.
    /// </summary>
    public sealed class EvidenceGraphTests : EvidenceTestBase
    {
        // ---------------------------------------------------------------- H02 external policy

        [Test]
        public void H02_ExternalPolicy_TickTimeIsDiagnosticOnly()
        {
            CreateActor("H02Actor", out Animator animator);
            AnimationClip clip = CreateClip("H02Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            var buffer = new AnimationPlaybackSnapshot[8];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L, 0d));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0d, buffer[0].Time, 1e-9, "the playback starts at the requested local time");

                long writesBefore = graph.GetStats().NativeWrites;
                long evaluationsBefore = graph.GetStats().Evaluations;

                // Ten commits whose tick time advances by 100 ms each. Nothing calls UpdatePlayback.
                for (int i = 1; i <= 10; i++)
                {
                    graph.Commit(Tick(i, i * 0.1d));
                }

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                NoteEvidence(
                    "H02",
                    "External policy: after 10 commits with tick time up to 1.0 s the playback time is still "
                    + buffer[0].Time.ToString("0.000") + " s; native writes delta = "
                    + (graph.GetStats().NativeWrites - writesBefore)
                    + ", evaluations delta = " + (graph.GetStats().Evaluations - evaluationsBefore));

                Assert.AreEqual(0d, buffer[0].Time, 1e-9,
                    "external policy never advances a clip from the tick");
                Assert.AreEqual(0L, graph.GetStats().NativeWrites - writesBefore,
                    "an unchanged pose is not rewritten: the dirty gate skips evaluation entirely");
                Assert.AreEqual(0L, graph.GetStats().Evaluations - evaluationsBefore);

                // The only path that moves the pose is the request payload.
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.5d, 1f));
                graph.Commit(Tick(11L, 1.1d));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5d, buffer[0].Time, 1e-9);
                Assert.AreEqual(1L, graph.GetStats().Evaluations - evaluationsBefore,
                    "a real change evaluates exactly once");
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H02b tick-driven policy

        [Test]
        public void H02b_TickDrivenPolicy_AdvancesFromTickTime()
        {
            CreateActor("H02bActor", out Animator animator);
            AnimationClip clip = CreateClip("H02bClip", 1f);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);

            var buffer = new AnimationPlaybackSnapshot[8];

            try
            {
                graph.Commit(Tick(0L, 10d)); // establish a tick before the playback exists

                var request = new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Clamp);
                graph.BeginPlayback(request);

                // The first commit that sees the playback anchors the clock at its requested local time.
                graph.Commit(Tick(1L, 10.5d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0d, buffer[0].Time, 1e-6, "a new playback starts at LocalTime on its first commit");

                graph.Commit(Tick(2L, 11.0d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5d, buffer[0].Time, 1e-6, "0.5 s of tick time advances the clip 0.5 s");

                graph.Commit(Tick(3L, 11.5d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(1.0d, buffer[0].Time, 1e-6, "clamp stops at the clip length");

                NoteEvidence(
                    "H02b",
                    "TickDriven: tick 10.5 -> 11.0 -> 11.5 produced playback time 0.000 -> 0.500 -> 1.000 s (clamp)");

                // Loop wraps instead of clamping.
                graph.BeginPlayback(new AnimationClipRequest(
                    2, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));
                graph.Commit(Tick(4L, 11.75d)); // anchor instance 2
                graph.Commit(Tick(5L, 12.0d));
                AnimationPlaybackSnapshot looped = FindSnapshot(buffer, 2, graph);
                Assert.AreEqual(0.25d, looped.Time, 1e-6, "0.25 s into a 1 s loop");

                // Speed scales elapsed time.
                graph.BeginPlayback(new AnimationClipRequest(
                    3, 0, 0, clip, 0d, 1f, false, 0, 2f, AnimationLoopMode.Loop));
                graph.Commit(Tick(6L, 12.1d)); // anchor instance 3
                graph.Commit(Tick(7L, 12.2d));
                AnimationPlaybackSnapshot fastest = FindSnapshot(buffer, 3, graph);
                Assert.AreEqual(0.2d, fastest.Time, 1e-6, "0.1 s at speed 2 wraps to 0.2 s");

                // SeekPlayback re-anchors the clock explicitly; the next commit starts from the seek target.
                Assert.IsTrue(graph.SeekPlayback(new AnimationPlaybackKey(1, 0, 0, 0), 0.25d));
                graph.Commit(Tick(8L, 12.3d));
                AnimationPlaybackSnapshot seeked = FindSnapshot(buffer, 1, graph);
                Assert.AreEqual(0.25d, seeked.Time, 1e-6, "seek re-anchors at 0.25 s");
                graph.Commit(Tick(9L, 12.4d));
                seeked = FindSnapshot(buffer, 1, graph);
                Assert.AreEqual(0.35d, seeked.Time, 1e-6, "and then advances normally");
            }
            finally
            {
                graph.Dispose();
            }
        }

        private static AnimationPlaybackSnapshot FindSnapshot(AnimationPlaybackSnapshot[] buffer, int instanceId, AnimationRuntimeGraph graph)
        {
            int count = graph.CopyPlaybackSnapshots(0, buffer);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].InstanceId == instanceId)
                {
                    return buffer[i];
                }
            }

            Assert.Fail("playback " + instanceId + " not found");
            return default;
        }

        // ---------------------------------------------------------------- H07

        [Test]
        public void H07_UpdatePlayback_WithADifferentClip_IsRejectedAndWarned()
        {
            CreateActor("H07Actor", out Animator animator);
            AnimationClip shortClip = CreateClip("H07Short", 1f);
            AnimationClip longClip = CreateClip("H07Long", 3f);
            var diagnostics = new CountingDiagnostics(true);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default }, diagnostics);
            var buffer = new AnimationPlaybackSnapshot[8];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, shortClip, 0.5d, 1f));
                graph.Commit(Tick(0L, 0d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(1f, buffer[0].Length, 1e-3f);

                long recreatedBefore = graph.GetStats().RecreatedPlayables;

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, longClip, 2d, 1f));
                graph.Commit(Tick(1L, 0.016d));

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                NoteEvidence(
                    "H07",
                    "UpdatePlayback with a different clip: length=" + buffer[0].Length.ToString("0.00")
                    + " s, time=" + buffer[0].Time.ToString("0.00")
                    + " s, recreated=" + (graph.GetStats().RecreatedPlayables - recreatedBefore)
                    + ", warnings=" + diagnostics.WarningCount);

                Assert.AreEqual(1f, buffer[0].Length, 1e-3f, "the playback still samples the first clip");
                Assert.AreEqual(1d, buffer[0].Time, 1e-3d, "time is clamped by the live clip");
                Assert.AreEqual(1, diagnostics.WarningCount,
                    "the ignored clip change is reported once instead of silently swallowed");

                // Re-beginning with the same key is the supported way to change the clip.
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, longClip, 2d, 1f));
                graph.Commit(Tick(2L, 0.032d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(3f, buffer[0].Length, 1e-3f);
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H08

        [Test]
        public void H08_PerLayerRootMotionFields_AreGone()
        {
            FieldInfo[] fields = typeof(AnimationLayerDescriptor).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.AreNotEqual("RootMotion", fields[i].Name);
                Assert.AreNotEqual("RootMotionScale", fields[i].Name);
            }

            NoteEvidence(
                "H08",
                "AnimationLayerDescriptor public fields: " + fields.Length
                + ", none named RootMotion/RootMotionScale; root motion is a binding-level concern");
        }

        // ---------------------------------------------------------------- H12

        [Test]
        public void H12_UnchangedCommits_DoNotRewriteTime()
        {
            CreateActor("H12Actor", out Animator animator);
            AnimationClip clip = CreateClip("H12Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.5d, 1f));
                graph.Commit(Tick(0L, 0d));

                long writesBefore = graph.GetStats().NativeWrites;
                const int commits = 100;
                for (int i = 1; i <= commits; i++)
                {
                    graph.Commit(Tick(i, i * 0.016d));
                }

                long delta = graph.GetStats().NativeWrites - writesBefore;
                NoteEvidence("H12", commits + " unchanged commits produced " + delta + " native writes");

                Assert.AreEqual(0L, delta, "the dirty gate skips both the write and the evaluation");

                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.75d, 1f));
                graph.Commit(Tick(commits + 1, 2d));
                Assert.AreEqual(1L, graph.GetStats().NativeWrites - writesBefore,
                    "one changed time produces one SetTime write");
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H15

        [Test]
        public void H15_AlternatingClips_ReuseTheCachedPlayable()
        {
            CreateActor("H15Actor", out Animator animator);
            AnimationClip a = CreateClip("H15A", 1f);
            AnimationClip b = CreateClip("H15B", 2f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, a, 0d, 1f));
                graph.Commit(Tick(0L, 0d));
                long recreatedBefore = graph.GetStats().RecreatedPlayables;

                const int switches = 500;
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 1; i <= switches; i++)
                {
                    graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, (i & 1) == 0 ? a : b, 0d, 1f));
                    graph.Commit(Tick(i, i * 0.016d));
                }

                double ms = Milliseconds(start);
                long recreated = graph.GetStats().RecreatedPlayables - recreatedBefore;
                long reused = graph.GetStats().ReusedPlayables;

                NoteEvidence(
                    "H15",
                    switches + " alternating switches in " + ms.ToString("0.0") + " ms, recreated=" + recreated
                    + ", reused=" + reused + ", playables=" + graph.GetStats().PlayableCount);

                Assert.AreEqual(1L, recreated, "only the first switch to the second clip creates a node");
                Assert.AreEqual((long)switches - 1L, reused, "every later switch hits the slot cache");
                Assert.Less(ms / switches, 5.0, "a single switch must stay far below one frame");
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H16

        [Test]
        public void H16_StopAll_KeepsPlayablesAlive_SoThePoolSurvives()
        {
            CreateActor("H16Actor", out Animator animator);
            AnimationClip clip = CreateClip("H16Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L, 0d));

                int before = graph.PlayableCount;
                graph.StopAll();
                int after = graph.PlayableCount;

                NoteEvidence("H16", "PlayableCount before StopAll = " + before + ", after = " + after);

                Assert.AreEqual(0, graph.GetStats().ActivePlaybacks);
                Assert.AreEqual(before, after,
                    "StopAll releases with destroy:false, so the clip playable stays in the graph for reuse");

                AnimationPlaybackHandle again = graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(1L, 0.016d));

                Assert.IsTrue(again.IsValid, "the pooled slot is reusable");
                Assert.AreEqual(after, graph.PlayableCount, "reuse does not allocate a second playable");
            }
            finally
            {
                graph.Dispose();
                Assert.AreEqual(0, graph.PlayableCount, "Dispose tears the graph down");
            }
        }

        // ---------------------------------------------------------------- H18

        [Test]
        public void H18_RebindToAnOwnedAnimator_IsRejectedWithoutChangingState()
        {
            CreateActor("H18First", out Animator first);
            CreateActor("H18Second", out Animator second);

            var moving = new AnimationRuntimeGraph(first, new[] { AnimationLayerDescriptor.Default });
            var owner = new AnimationRuntimeGraph(second, new[] { AnimationLayerDescriptor.Default });

            try
            {
                Assert.AreSame(first, moving.TargetAnimator);

                bool moved = moving.TryRebind(second);
                NoteEvidence(
                    "H18",
                    "TryRebind to an owned Animator returned " + moved
                    + ", IsInitialized=" + moving.IsInitialized
                    + ", TargetAnimator=" + moving.TargetAnimator.name
                    + ", first owned=" + AnimationRuntimeRegistry.IsAnimatorOwned(first));

                Assert.IsFalse(moved, "a conflict is reported instead of throwing after teardown");
                Assert.IsTrue(moving.IsInitialized, "the live graph is untouched");
                Assert.AreSame(first, moving.TargetAnimator, "both the graph and the component keep the old Animator");
                Assert.IsTrue(AnimationRuntimeRegistry.IsAnimatorOwned(first));
                Assert.IsTrue(AnimationRuntimeRegistry.IsAnimatorOwned(second));

                Assert.IsTrue(moving.TryRebind(first), "rebinding to the current Animator is a no-op success");
            }
            finally
            {
                moving.Dispose();
                owner.Dispose();
            }
        }

        // ---------------------------------------------------------------- H19

        [Test]
        public void H19_ThrowingDependencyDuringConstruction_RollsBackTheClaim()
        {
            CreateActor("H19Actor", out Animator animator);
            int graphsBefore = AnimationRuntimeRegistry.LiveGraphCount;

            Assert.Throws<InvalidOperationException>(() =>
                new AnimationRuntimeGraph(
                    animator,
                    new[] { AnimationLayerDescriptor.Default },
                    new ThrowingDiagnostics()));

            NoteEvidence(
                "H19",
                "construction threw; IsAnimatorOwned=" + AnimationRuntimeRegistry.IsAnimatorOwned(animator)
                + ", LiveGraphCount delta=" + (AnimationRuntimeRegistry.LiveGraphCount - graphsBefore));

            Assert.IsFalse(AnimationRuntimeRegistry.IsAnimatorOwned(animator),
                "a half-built graph releases the Animator claim");
            Assert.AreEqual(graphsBefore, AnimationRuntimeRegistry.LiveGraphCount);

            // The Animator is usable again, which is the practical proof of the rollback.
            var recovered = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            try
            {
                Assert.IsTrue(recovered.IsInitialized);
            }
            finally
            {
                recovered.Dispose();
            }
        }

        [Test]
        public void H19b_ConstructingAgainstADestroyedAnimator_ThrowsArgumentNull_NotAnOwnershipError()
        {
            GameObject go = CreateActor("H19bActor", out Animator animator);
            UnityEngine.Object.DestroyImmediate(go);

            var exception = Assert.Throws<ArgumentNullException>(() =>
                new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default }));

            NoteEvidence("H19b", "message: " + exception.Message.Replace('\n', ' '));

            Assert.AreEqual("animator", exception.ParamName);
        }

        // ---------------------------------------------------------------- H20

        [Test]
        public void H20b_ForgottenDispose_LeavesTheLiveGraphCounterIncremented()
        {
            int graphsBefore = AnimationRuntimeRegistry.LiveGraphCount;
            CreateActor("H20Actor", out Animator animator);
            AnimationClip clip = CreateClip("H20Clip", 1f);

            var abandoned = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            abandoned.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
            abandoned.Commit(Tick(0L, 0d));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            NoteEvidence(
                "H20",
                "an undisposed graph keeps LiveGraphCount at " + AnimationRuntimeRegistry.LiveGraphCount
                + " (baseline " + graphsBefore + ") with no exception, warning or assertion");

            Assert.AreEqual(graphsBefore + 1, AnimationRuntimeRegistry.LiveGraphCount);

            abandoned.Dispose();
            Assert.AreEqual(graphsBefore, AnimationRuntimeRegistry.LiveGraphCount);
        }

        // ---------------------------------------------------------------- H22

        [Test]
        public void H22_OutOfRangeLayerIndex_IsClampedAndWarnedOnce()
        {
            CreateActor("H22Actor", out Animator animator);
            AnimationClip clip = CreateClip("H22Clip", 1f);
            var diagnostics = new CountingDiagnostics(true);
            var graph = new AnimationRuntimeGraph(animator, new[]
            {
                new AnimationLayerDescriptor(null, false, 1f, 2),
                new AnimationLayerDescriptor(null, false, 1f, 2),
                new AnimationLayerDescriptor(null, false, 1f, 2)
            }, diagnostics);

            try
            {
                AnimationPlaybackHandle tooHigh = graph.BeginPlayback(new AnimationClipRequest(1, 0, 5, clip, 0d, 1f));
                AnimationPlaybackHandle tooLow = graph.BeginPlayback(new AnimationClipRequest(2, 0, -7, clip, 0d, 1f));

                NoteEvidence(
                    "H22",
                    "3 layers: requested 5 -> " + tooHigh.Layer + ", requested -7 -> " + tooLow.Layer
                    + ", warnings=" + diagnostics.WarningCount);

                Assert.AreEqual(2, tooHigh.Layer);
                Assert.AreEqual(0, tooLow.Layer);
                Assert.AreEqual(1, diagnostics.WarningCount, "the clamp is reported once per graph");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void H27_TickDrivenPause_RebasePreventsAJump()
        {
            CreateActor("H27Actor", out Animator animator);
            AnimationClip clip = CreateClip("H27Clip", 1f);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);
            var buffer = new AnimationPlaybackSnapshot[4];

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));

                graph.Commit(new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, 0.016f));
                graph.Commit(new AnimationTick(0.5d, 1L, AnimationTimeSource.Update, false, 0.016f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5d, buffer[0].Time, 1e-6);

                graph.RebaseTickClocks();
                graph.Commit(new AnimationTick(10d, 2L, AnimationTimeSource.Update, false, 0.016f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.5d, buffer[0].Time, 1e-6, "the pause gap must not advance the clip");

                graph.Commit(new AnimationTick(10.1d, 3L, AnimationTimeSource.Update, false, 0.016f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(0.6d, buffer[0].Time, 1e-6, "normal advancement resumes after the anchor");

                NoteEvidence("H27", "pause 9.5 s: 0.5 -> rebase -> 0.5 -> " + buffer[0].Time.ToString("0.000"));
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- ClipChannel

        [Test]
        public void H23_TwoClipChannels_CoexistOnTheSameInstanceAndLayer()
        {
            CreateActor("H23Actor", out Animator animator);
            AnimationClip a = CreateClip("H23A", 1f);
            AnimationClip b = CreateClip("H23B", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                AnimationPlaybackHandle first = graph.BeginPlayback(
                    new AnimationClipRequest(1, 0, 0, a, 0d, 0.5f, false, 0));
                AnimationPlaybackHandle second = graph.BeginPlayback(
                    new AnimationClipRequest(1, 0, 0, b, 0d, 0.5f, false, 1));
                graph.Commit(Tick(0L, 0d));

                NoteEvidence(
                    "H23",
                    "same instance/channel/layer with clip channels 0 and 1 -> active playbacks "
                    + graph.GetStats().ActivePlaybacks + ", handles valid=" + first.IsValid + "/" + second.IsValid);

                Assert.IsTrue(first.IsValid);
                Assert.IsTrue(second.IsValid);
                Assert.AreNotEqual(first, second);
                Assert.AreEqual(2, graph.GetStats().ActivePlaybacks,
                    "clip channel is part of playback identity, which is what a cross-fade needs");

                graph.EndPlayback(new AnimationPlaybackKey(1, 0, 0, 1));
                graph.Commit(Tick(1L, 0.016d));
                Assert.AreEqual(1, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- external source

        [Test]
        public void H24_ExternalSource_ForcesEvaluationEveryCommit()
        {
            CreateActor("H24Actor", out Animator animator);
            AnimationClip a = CreateClip("H24A", 1f);
            AnimationClip b = CreateClip("H24B", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            LinearBlendNode node = LinearBlendNode.Create(
                graph.PlayableGraph,
                new[] { a, b },
                new[] { 0f, 1f });

            try
            {
                Assert.IsTrue(graph.SetLayerSource(0, node.Playable));
                graph.Commit(Tick(0L, 0d));

                long before = graph.GetStats().Evaluations;
                for (int i = 1; i <= 50; i++)
                {
                    node.SetTime(i * 0.01d);
                    graph.Commit(Tick(i, i * 0.016d));
                }

                long forced = graph.GetStats().Evaluations - before;
                NoteEvidence(
                    "H24",
                    "50 commits with a host-owned blend node produced " + forced + " evaluations");

                Assert.AreEqual(50L, forced,
                    "the runtime cannot observe writes inside a host node, so it must evaluate every commit");

                graph.ClearLayerSource(0);
                graph.Commit(Tick(51L, 1d));
                long afterClear = graph.GetStats().Evaluations;
                graph.Commit(Tick(52L, 1.016d));
                Assert.AreEqual(afterClear, graph.GetStats().Evaluations, "without a source the dirty gate applies again");
            }
            finally
            {
                node.Dispose();
                graph.Dispose();
            }
        }
    }
}
