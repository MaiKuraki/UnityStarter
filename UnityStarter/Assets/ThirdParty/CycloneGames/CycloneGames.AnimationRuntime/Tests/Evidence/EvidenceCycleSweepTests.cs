using System;
using System.Collections.Generic;
using System.Globalization;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Drives the real graph over a parameter grid and compares every marker count with the analytic crossing count.
    /// A marker at p is crossed once each time the unfolded clock reaches p + k L (loop) or ±p + k 2L (ping-pong).
    /// Steps are chosen so no tick lands on a turning point.
    /// </summary>
    public sealed class EvidenceCycleSweepTests : EvidenceTestBase
    {
        private static readonly float[] Lengths = { 0.5f, 1f, 2f };
        private static readonly int[] Cycles = { 1, 3 };
        private static readonly float[] Markers = { 0.13f, 0.29f, 0.47f, 0.61f, 0.79f, 0.91f };

        private sealed class Sink : IAnimationClipEventSink
        {
            public readonly int[] Counts = new int[Markers.Length];

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                for (int i = 0; i < Markers.Length; i++)
                {
                    if (clipEvent.FunctionName == "M" + i)
                    {
                        Counts[i]++;
                        return;
                    }
                }
            }
        }

        private static long CountBetween(double start, double end, double anchor, double period)
        {
            long first = (long)Math.Floor((start - anchor) / period) + 1L;
            long last = (long)Math.Floor((end - anchor) / period);
            long count = last - first + 1L;
            return count <= 0L ? 0L : count;
        }

        private static int Expected(double advance, double marker, double length, AnimationLoopMode loop)
        {
            double absolute = marker * length;

            if (loop == AnimationLoopMode.Loop)
            {
                return (int)CountBetween(0d, advance, absolute, length);
            }

            double cycle = length * 2d;
            return (int)(CountBetween(0d, advance, absolute, cycle) + CountBetween(0d, advance, -absolute, cycle));
        }

        private AnimationClip ClipWith(float requestedLength)
        {
            AnimationClip clip = CreateClip("SweepClip_" + requestedLength.ToString("0.##", CultureInfo.InvariantCulture), requestedLength);

            // Marker times derive from the length the clip reports, not from the requested length.
            float length = clip.length;
            for (int i = 0; i < Markers.Length; i++)
            {
                clip.AddEvent(new AnimationEvent
                {
                    time = Markers[i] * length,
                    functionName = "M" + i
                });
            }

            return clip;
        }

        [Test]
        public void CycleGrid_MatchesTheAnalyticCrossingCount()
        {
            long checks = 0;
            int failures = 0;

            foreach (float requestedLength in Lengths)
            {
                foreach (AnimationLoopMode loop in new[] { AnimationLoopMode.Loop, AnimationLoopMode.PingPong })
                {
                    foreach (int cycles in Cycles)
                    {
                        CreateActor("SweepActor_" + requestedLength + "_" + loop + "_" + cycles, out Animator animator);
                        AnimationClip clip = ClipWith(requestedLength);
                        double length = clip.length;
                        double cycleLength = loop == AnimationLoopMode.Loop ? length : length * 2d;
                        double total = cycles * cycleLength;
                        int commits = (8 * cycles) + 3;
                        double step = total / commits;

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
                                1, 0, 0, clip, 0d, 1f, false, 0, 1f, loop));

                            for (int i = 0; i <= commits; i++)
                            {
                                graph.Commit(new AnimationTick(i * step, i, AnimationTimeSource.Update, false, 0.016f));
                            }

                            var actual = new List<int>(Markers.Length);
                            var expected = new List<int>(Markers.Length);
                            for (int i = 0; i < Markers.Length; i++)
                            {
                                int want = Expected(total, Markers[i], length, loop);
                                actual.Add(sink.Counts[i]);
                                expected.Add(want);
                                checks++;

                                if (sink.Counts[i] != want)
                                {
                                    failures++;
                                }
                            }

                            NoteEvidence(
                                "sweep",
                                "length=" + length.ToString("0.####", CultureInfo.InvariantCulture)
                                + " loop=" + loop + " cycles=" + cycles + " commits=" + commits
                                + " expected=[" + string.Join(",", expected) + "]"
                                + " actual=[" + string.Join(",", actual) + "]");
                        }
                        finally
                        {
                            graph.Dispose();
                        }
                    }
                }
            }

            NoteEvidence("sweep-total", "marker checks = " + checks + ", mismatches = " + failures);
            Assert.AreEqual(0, failures, "every marker must match its analytic crossing count");
        }
    }
}
