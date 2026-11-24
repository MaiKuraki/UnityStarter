using System;
using System.Reflection;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Allocation and scaling evidence. Allocation is measured as a heap trend around a warm-up loop because
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c> always returns 0 inside the editor's Mono host. Timing is
    /// published as a note and only guarded loosely, so the suite cannot become flaky on a loaded machine.
    /// </summary>
    public sealed class EvidencePerformanceTests : EvidenceTestBase
    {
        private const int WarmupIterations = 2000;
        private const int MeasuredIterations = 200000;

        // ---------------------------------------------------------------- H05

        [Test]
        public void H05_RejectedBeginPlayback_DoesNotAllocate_WhenDiagnosticsAreDisabled()
        {
            CreateActor("H05Actor", out Animator animator);
            AnimationClip clip = CreateClip("H05Clip", 1f);

            var nullGraph = new AnimationRuntimeGraph(
                animator,
                new[] { new AnimationLayerDescriptor(null, false, 1f, 1) },
                NullAnimationDiagnostics.Instance);

            try
            {
                nullGraph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                nullGraph.Commit(Tick(0L, 0d));

                int rejected = 0;
                long delta = MeasureAllocationDelta(WarmupIterations, MeasuredIterations, () =>
                {
                    if (!nullGraph.BeginPlayback(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f)).IsValid)
                    {
                        rejected++;
                    }
                });

                NoteEvidence(
                    "H05",
                    MeasuredIterations + " rejected BeginPlayback calls with NullAnimationDiagnostics: heap delta "
                    + FormatBytes(delta) + ", dropped counter=" + nullGraph.GetStats().DroppedPlaybacks);

                Assert.AreEqual(WarmupIterations + MeasuredIterations, rejected,
                    "every call must take the capacity-exhausted path");
                Assert.AreEqual((long)(WarmupIterations + MeasuredIterations), nullGraph.GetStats().DroppedPlaybacks);
                Assert.Less(delta, 64L * 1024,
                    "the drop path formats no string: Warn() is passed a constant literal and the concat lives "
                    + "inside the IsEnabled check");
            }
            finally
            {
                nullGraph.Dispose();
            }
        }

        [Test]
        public void H05b_RejectedBeginPlayback_ReportsOnlyWhenTheChannelIsEnabled()
        {
            CreateActor("H05bActor", out Animator animator);
            AnimationClip clip = CreateClip("H05bClip", 1f);

            var disabled = new CountingDiagnostics(false);
            var enabled = new CountingDiagnostics(true);

            var quietGraph = new AnimationRuntimeGraph(
                animator,
                new[] { new AnimationLayerDescriptor(null, false, 1f, 1) },
                disabled);

            try
            {
                quietGraph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                quietGraph.Commit(Tick(0L, 0d));

                const int drops = 10000;
                for (int i = 0; i < drops; i++)
                {
                    quietGraph.BeginPlayback(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f));
                }

                NoteEvidence(
                    "H05b",
                    drops + " drops with IsEnabled=false: Report called " + disabled.WarningCount + " times");

                Assert.AreEqual(0, disabled.WarningCount,
                    "callers gate on IsEnabled, so a disabled channel never receives a message");
            }
            finally
            {
                quietGraph.Dispose();
            }

            CreateActor("H05bActor2", out Animator animator2);
            var loudGraph = new AnimationRuntimeGraph(
                animator2,
                new[] { new AnimationLayerDescriptor(null, false, 1f, 1) },
                enabled);

            try
            {
                loudGraph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                loudGraph.Commit(Tick(0L, 0d));

                long delta = MeasureAllocationDelta(0, 10000, () =>
                {
                    loudGraph.BeginPlayback(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f));
                });

                NoteEvidence(
                    "H05b",
                    "10000 drops with IsEnabled=true: " + enabled.WarningCount
                    + " reports, heap delta " + FormatBytes(delta)
                    + " (this is the cost of formatting a message that is actually emitted)");

                Assert.AreEqual(10000, enabled.WarningCount,
                    "a drop is reported once per call when the channel accepts warnings");
            }
            finally
            {
                loudGraph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H11

        [Test]
        public void H11_PausedActor_SkipsEvaluationWhileDirtyCommitsPayForIt()
        {
            CreateActor("H11Static", out Animator staticAnimator);
            AnimationClip clip = CreateClip("H11Clip", 1f);
            var graph = new AnimationRuntimeGraph(staticAnimator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.25d, 1f));
                graph.Commit(Tick(0L, 0d));

                const int commits = 100000;
                long step = 1;

                long evaluationsBefore = graph.GetStats().Evaluations;
                long writesBefore = graph.GetStats().NativeWrites;
                long startPaused = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < commits; i++)
                {
                    graph.Commit(Tick(step++, 0d));
                }

                double pausedMs = Milliseconds(startPaused);
                long pausedEvaluations = graph.GetStats().Evaluations - evaluationsBefore;
                long pausedWrites = graph.GetStats().NativeWrites - writesBefore;

                // Same playback, but every commit carries a new time.
                long startDirty = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < commits; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, (i % 60) * 0.016d, 1f));
                    graph.Commit(Tick(step++, 0d));
                }

                double dirtyMs = Milliseconds(startDirty);
                long dirtyEvaluations = graph.GetStats().Evaluations - evaluationsBefore - pausedEvaluations;
                long dirtyWrites = graph.GetStats().NativeWrites - writesBefore - pausedWrites;

                NoteEvidence(
                    "H11",
                    commits + " paused commits: " + pausedMs.ToString("0.0") + " ms ("
                    + (pausedMs * 1000.0 / commits).ToString("0.000") + " us/commit, evaluations=" + pausedEvaluations
                    + ", writes=" + pausedWrites + "); " + commits + " dirty commits: "
                    + dirtyMs.ToString("0.0") + " ms (" + (dirtyMs * 1000.0 / commits).ToString("0.000")
                    + " us/commit, evaluations=" + dirtyEvaluations + ", writes=" + dirtyWrites + ")");

                Assert.AreEqual(0L, pausedEvaluations, "nothing changed, so the graph is never evaluated");
                Assert.AreEqual(0L, pausedWrites, "nothing changed, so no native write is issued");
                Assert.AreEqual((long)commits, dirtyEvaluations, "a real change evaluates exactly once per commit");
                Assert.AreEqual((long)commits, dirtyWrites, "a changed time writes exactly once per commit");
                Assert.Less(pausedMs, dirtyMs, "the dirty gate must be cheaper than a full evaluation");

                // Loose guard only: this must never dominate a frame even on a loaded build machine.
                Assert.Less(dirtyMs / commits, 5.0, "one evaluated commit must stay far below one millisecond");
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H13

        [Test]
        public void H13_FindPlaybackIsALinearScan_MeasuredAtMaximumLayerAndSlotCount()
        {
            double fullFrameUs = MeasurePlaybackFrame(8, 8, 20000, "H13Full", out AnimationRuntimeStats fullStats);
            double singlePlaybackUs = MeasurePlaybackFrame(1, 1, 20000, "H13Single", out AnimationRuntimeStats singleStats);

            NoteEvidence(
                "H13",
                "8 layers x 8 slots: " + fullFrameUs.ToString("0.000") + " us/frame (playbacks "
                + fullStats.ActivePlaybacks + ", writes " + fullStats.NativeWrites + "); "
                + "1 layer x 1 slot: " + singlePlaybackUs.ToString("0.000") + " us/frame (playbacks "
                + singleStats.ActivePlaybacks + ", writes " + singleStats.NativeWrites + ")");

            Assert.AreEqual(64, fullStats.ActivePlaybacks, "the maximum configuration really holds 64 playbacks");
            Assert.AreEqual(1, singleStats.ActivePlaybacks);
            Assert.Less(fullFrameUs, 5000.0, "a full update plus commit must stay below 5 ms per frame");
        }

        private double MeasurePlaybackFrame(int layerCount, int capacity, int frames, string name, out AnimationRuntimeStats stats)
        {
            CreateActor(name, out Animator animator);

            var descriptors = new AnimationLayerDescriptor[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, capacity);
            }

            var clips = new AnimationClip[capacity];
            for (int i = 0; i < capacity; i++)
            {
                clips[i] = CreateClip(name + "Clip" + i, 1f);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors);

            try
            {
                for (int layer = 0; layer < layerCount; layer++)
                {
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        graph.BeginPlayback(new AnimationClipRequest(slot + 1, 0, layer, clips[slot], 0d, 1f / capacity));
                    }
                }

                graph.Commit(Tick(0L, 0d));

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int frame = 1; frame <= frames; frame++)
                {
                    double time = (frame % 60) * 0.016d;
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        for (int slot = 0; slot < capacity; slot++)
                        {
                            graph.UpdatePlayback(new AnimationClipRequest(
                                slot + 1, 0, layer, clips[slot], time, 1f / capacity));
                        }
                    }

                    graph.Commit(Tick(frame, time));
                }

                double ms = Milliseconds(start);
                stats = graph.GetStats();
                return ms * 1000.0 / frames;
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- H17

        [Test]
        public void H17a_CommitEarlyReturnPath_DoesNotAllocate()
        {
            // Nothing is dirty, so Commit returns before ApplyLayer/Evaluate.
            CreateActor("H17Actor", out Animator animator);
            AnimationClip clip = CreateClip("H17Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.5d, 1f));
                graph.Commit(Tick(0L, 0d));

                long step = 1;
                long delta = MeasureAllocationDeltaExact(WarmupIterations, MeasuredIterations, () =>
                {
                    graph.Commit(Tick(step++, 0.5d));
                }, out bool exact);

                NoteEvidence(
                    "H17a",
                    MeasuredIterations + " early-return commits: heap delta " + FormatBytes(delta)
                    + " (" + (delta / (double)MeasuredIterations).ToString("0.000") + " B/commit, exact="
                    + exact + ")");

                Assert.Less(delta, 64L * 1024,
                    "Commit must not allocate on the early-return path: no LINQ, no boxing, no closures");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void H17b_CommitFullApplyPath_AllocationBudget()
        {
            // Every commit moves clip time through UpdatePlayback, so ApplyLayer and Evaluate really run.
            // This is the path H17a never reaches; it must stay within the same budget.
            CreateActor("H17bActor", out Animator animator);
            AnimationClip clip = CreateClip("H17bClip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0.25d, 1f));
                graph.Commit(Tick(0L, 0d));

                long step = 1;
                long delta = MeasureAllocationDeltaExact(WarmupIterations, MeasuredIterations, () =>
                {
                    double time = (step % 60) * 0.016d;
                    graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, time, 1f));
                    graph.Commit(Tick(step, time));
                    step++;
                }, out bool exact);

                AnimationRuntimeStats stats = graph.GetStats();
                NoteEvidence(
                    "H17b",
                    MeasuredIterations + " full-apply commits: heap delta " + FormatBytes(delta)
                    + " (" + (delta / (double)MeasuredIterations).ToString("0.000") + " B/commit, exact="
                    + exact + ", evaluations=" + stats.Evaluations + ", writes=" + stats.NativeWrites + ")");

                Assert.Greater(stats.Evaluations, 0L, "the measured path must really evaluate the graph");
                Assert.Less(delta, 64L * 1024,
                    "a dirty commit must not allocate per frame; this covers ApplyLayer and Evaluate");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void H18_EnabledDiagnostics_AllocatePerReport()
        {
            // Documents the budgeted path: an enabled diagnostics channel formats a string per report.
            // The README performance contract must not claim this path is allocation-free.
            CreateActor("H18Actor", out Animator animator);
            AnimationClip clip = CreateClip("H18Clip", 1f);
            var diagnostics = new FormattingDiagnostics();
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { new AnimationLayerDescriptor(null, false, 1f, 1) },
                diagnostics);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L, 0d));

                long delta = MeasureAllocationDeltaExact(2000, 20000, () =>
                {
                    graph.BeginPlayback(new AnimationClipRequest(2, 0, 0, clip, 0d, 1f));
                }, out bool exact);

                NoteEvidence(
                    "H18",
                    "20000 capacity drops with a formatting diagnostics channel: " + FormatBytes(delta)
                    + " (exact=" + exact + ", reports=" + diagnostics.Reports + ")");

                Assert.Greater(diagnostics.Reports, 0, "the channel must receive the drop reports");
                Assert.Greater(delta, 0L, "enabled diagnostics format a message; this path is budgeted, not zero");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void H19_ClipEventQueue_StaysEmptyUntilTheFirstMarker()
        {
            // The README says the dispatch queue is empty until the first marker. Verify the array itself,
            // not just the dispatch count, because the array is the allocation that matters.
            CreateActor("H19Actor", out Animator animator);
            AnimationClip clip = CreateClip("H19Clip", 1f);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));

                FieldInfo queueField = typeof(AnimationRuntimeGraph).GetField(
                    "_pendingClipEvents",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(queueField, "the dispatch queue field moved; update H19");

                var before = (Array)queueField.GetValue(graph);
                Assert.AreEqual(0, before.Length, "the queue must not allocate before the first marker");

                for (int i = 0; i < 100000; i++)
                {
                    graph.Commit(Tick(i, (i % 60) * 0.016d));
                }

                var after = (Array)queueField.GetValue(graph);
                NoteEvidence("H19", "100000 marker-less commits: queue length " + after.Length);
                Assert.AreEqual(0, after.Length, "no clip markers means no dispatch queue allocation");
            }
            finally
            {
                graph.Dispose();
            }
        }

        private sealed class FormattingDiagnostics : IAnimationDiagnostics
        {
            public int Reports;

            public bool IsEnabled(AnimationDiagnosticLevel level)
            {
                return level >= AnimationDiagnosticLevel.Warning;
            }

            public void Report(AnimationDiagnosticLevel level, string message)
            {
                Reports++;
            }
        }
    }
}
