using System;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Parameterised verification matrix. The fixed cases in the other fixtures prove specific contract points;
    /// this fixture sweeps the parameter space and repeats it under different seeds, layer counts, capacities,
    /// clip lengths, speeds and loop modes.
    ///
    /// The tick-driven cases compare the runtime against an independently written reference implementation, so a
    /// shared bug in the production formula cannot make both sides agree by construction.
    /// </summary>
    public sealed class EvidenceMatrixTests : EvidenceTestBase
    {
        private static readonly float[] ClipLengths = { 0.5f, 1f, 3f };
        private static readonly float[] Speeds = { -2f, -1f, 0f, 0.5f, 1f, 2f };
        private static readonly AnimationLoopMode[] Loops =
        {
            AnimationLoopMode.Clamp,
            AnimationLoopMode.Loop,
            AnimationLoopMode.PingPong
        };
        private static readonly double[] StartTicks = { 0d, 10d, 1000d };
        private static readonly double[] Deltas = { 0d, 0.016d, 0.25d, 1d, 10d };

        // ---------------------------------------------------------------- M1: tick-driven math matrix

        [Test]
        public void M1_TickDriven_MathMatrix_MatchesAnIndependentReference()
        {
            CreateActor("M1Actor", out Animator animator);
            var clips = new AnimationClip[ClipLengths.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = CreateClip("M1Clip" + i, ClipLengths[i]);
            }

            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { new AnimationLayerDescriptor(null, false, 1f, 1) },
                null,
                AnimationTimePolicy.TickDriven);

            var buffer = new AnimationPlaybackSnapshot[1];
            int combinations = 0;
            double worstError = 0d;

            try
            {
                long step = 0;
                for (int lengthIndex = 0; lengthIndex < ClipLengths.Length; lengthIndex++)
                {
                    for (int speedIndex = 0; speedIndex < Speeds.Length; speedIndex++)
                    {
                        for (int loopIndex = 0; loopIndex < Loops.Length; loopIndex++)
                        {
                            for (int startIndex = 0; startIndex < StartTicks.Length; startIndex++)
                            {
                                for (int deltaIndex = 0; deltaIndex < Deltas.Length; deltaIndex++)
                                {
                                    float length = ClipLengths[lengthIndex];
                                    float speed = Speeds[speedIndex];
                                    AnimationLoopMode loop = Loops[loopIndex];
                                    double startTick = StartTicks[startIndex];
                                    double delta = Deltas[deltaIndex];

                                    graph.BeginClip(new AnimationClipRequest(
                                        1, 0, 0, clips[lengthIndex], 0d, 1f, false, 0, speed, loop));

                                    graph.Commit(Tick(step++, startTick));          // anchor
                                    graph.Commit(Tick(step++, startTick + delta));  // advance

                                    Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                                    double expected = ReferenceTime(length, speed, loop, delta);
                                    double error = Math.Abs(expected - buffer[0].Time);
                                    if (error > worstError)
                                    {
                                        worstError = error;
                                    }

                                    Assert.AreEqual(expected, buffer[0].Time, 1e-6,
                                        "length=" + length + " speed=" + speed + " loop=" + loop
                                        + " start=" + startTick + " delta=" + delta);
                                    combinations++;
                                }
                            }
                        }
                    }
                }

                NoteEvidence(
                    "M1",
                    "tick-driven matrix: " + combinations + " combinations, worst absolute error "
                    + worstError.ToString("0.000000000") + " s");
            }
            finally
            {
                graph.Dispose();
            }
        }

        /// <summary>
        /// Independent formulation: clamp via Min/Max, loop via a positive modulo, ping-pong via a triangle wave.
        /// Independent formulation of the reference curve.
        /// </summary>
        private static double ReferenceTime(float length, float speed, AnimationLoopMode loop, double delta)
        {
            double l = length;
            double t = delta * speed;

            switch (loop)
            {
                case AnimationLoopMode.Loop:
                {
                    double wrapped = t % l;
                    return wrapped < 0d ? wrapped + l : wrapped;
                }

                case AnimationLoopMode.PingPong:
                {
                    double period = 2d * l;
                    double phase = t % period;
                    if (phase < 0d)
                    {
                        phase += period;
                    }

                    return l - Math.Abs(phase - l);
                }

                default:
                    return Math.Min(Math.Max(t, 0d), l);
            }
        }

        // ---------------------------------------------------------------- M2: layer x capacity matrix

        [Test]
        public void M2_LayerCapacityMatrix_EnforcesEveryBudget()
        {
            int[] layerCounts = { 1, 2, 4, 8 };
            int[] capacities = { 1, 2, 4, 8 };
            int configurations = 0;

            for (int layerIndex = 0; layerIndex < layerCounts.Length; layerIndex++)
            {
                for (int capacityIndex = 0; capacityIndex < capacities.Length; capacityIndex++)
                {
                    int layers = layerCounts[layerIndex];
                    int capacity = capacities[capacityIndex];
                    CreateActor("M2Actor_" + layers + "_" + capacity, out Animator animator);
                    AnimationClip clip = CreateClip("M2Clip_" + layers + "_" + capacity, 1f);

                    var descriptors = new AnimationLayerDescriptor[layers];
                    for (int i = 0; i < layers; i++)
                    {
                        descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, capacity);
                    }

                    var graph = new AnimationRuntimeGraph(animator, descriptors);
                    var buffer = new AnimationPlaybackSnapshot[8];

                    try
                    {
                        for (int layer = 0; layer < layers; layer++)
                        {
                            for (int slot = 0; slot <= capacity; slot++)
                            {
                                graph.BeginClip(new AnimationClipRequest(
                                    layer * 100 + slot, 0, layer, clip, 0d, 1f));
                            }
                        }

                        graph.Commit(Tick(0L, 0d));

                        Assert.AreEqual(layers * capacity, graph.GetStats().ActivePlaybacks,
                            "accepted playbacks = layers x capacity (" + layers + "x" + capacity + ")");
                        Assert.AreEqual(layers, graph.GetStats().DroppedPlaybacks,
                            "exactly one over-capacity request is dropped per layer");
                        Assert.AreEqual(layerCounts[layerIndex], graph.LayerCount);

                        for (int layer = 0; layer < layers; layer++)
                        {
                            Assert.AreEqual(capacity, graph.GetLayerCapacity(layer));
                            Assert.AreEqual(capacity, graph.GetLayerActivePlaybacks(layer));
                            Assert.AreEqual(capacity, graph.CopyPlaybackSnapshots(layer, buffer));
                        }

                        for (int layer = 0; layer < layers; layer++)
                        {
                            graph.EndClip(new AnimationPlaybackKey(layer * 100, 0, layer));
                        }

                        graph.Commit(Tick(1L, 0.016d));
                        Assert.AreEqual(layers * (capacity - 1), graph.GetStats().ActivePlaybacks);
                        configurations++;
                    }
                    finally
                    {
                        graph.Dispose();
                    }
                }
            }

            NoteEvidence("M2", "layer x capacity matrix: " + configurations + " configurations verified");
        }

        // ---------------------------------------------------------------- M3: playback identity matrix

        [Test]
        public void M3_PlaybackIdentityMatrix_ClipChannelSeparatesOtherwiseEqualKeys()
        {
            CreateActor("M3Actor", out Animator animator);
            AnimationClip clip = CreateClip("M3Clip", 1f);
            int[] clipChannels = { 0, 1, 2 };
            int combinations = 0;

            for (int sameInstance = 0; sameInstance < 2; sameInstance++)
            {
                for (int samePlayback = 0; samePlayback < 2; samePlayback++)
                {
                    for (int channelIndex = 0; channelIndex < clipChannels.Length; channelIndex++)
                    {
                        var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
                        try
                        {
                            int playbackA = 0;
                            int playbackB = samePlayback == 1 ? 0 : 1;
                            int instanceA = 1;
                            int instanceB = sameInstance == 1 ? 1 : 2;
                            int clipChannel = clipChannels[channelIndex];

                            graph.BeginClip(new AnimationClipRequest(
                                instanceA, playbackA, 0, clip, 0d, 0.5f, false, 0));
                            graph.BeginClip(new AnimationClipRequest(
                                instanceB, playbackB, 0, clip, 0d, 0.5f, false, clipChannel));
                            graph.Commit(Tick(0L, 0d));

                            int expected = sameInstance == 1 && samePlayback == 1 && clipChannel == 0 ? 1 : 2;
                            Assert.AreEqual(expected, graph.GetStats().ActivePlaybacks,
                                "sameInstance=" + sameInstance + " samePlayback=" + samePlayback
                                + " clipChannel=" + clipChannel);

                            graph.StopAll();
                            graph.Commit(Tick(1L, 0.016d));
                            Assert.AreEqual(0, graph.GetStats().ActivePlaybacks);
                            combinations++;
                        }
                        finally
                        {
                            graph.Dispose();
                        }
                    }
                }
            }

            NoteEvidence("M3", "playback identity matrix: " + combinations + " combinations verified");
        }

        // ---------------------------------------------------------------- M4: multi-seed fuzz

        [Test]
        public void M4_ExternalPolicy_MultiSeedFuzz_KeepsInvariants()
        {
            int[,] configs = { { 1, 1 }, { 2, 4 }, { 4, 2 }, { 8, 8 } };
            int seeds = 4;
            int operationsPerSeed = 3000;
            int totalOperations = 0;

            for (int configIndex = 0; configIndex < configs.GetLength(0); configIndex++)
            {
                int layers = configs[configIndex, 0];
                int capacity = configs[configIndex, 1];

                for (int seed = 1; seed <= seeds; seed++)
                {
                    CreateActor("M4Actor_" + layers + "_" + capacity + "_" + seed, out Animator animator);
                    AnimationClip clip = CreateClip("M4Clip_" + layers + "_" + capacity + "_" + seed, 1f);

                    var descriptors = new AnimationLayerDescriptor[layers];
                    for (int i = 0; i < layers; i++)
                    {
                        descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, capacity);
                    }

                    var graph = new AnimationRuntimeGraph(animator, descriptors);
                    var random = new System.Random(seed * 7919 + configIndex);
                    var buffer = new AnimationPlaybackSnapshot[8];
                    long step = 0;

                    try
                    {
                        for (int operation = 0; operation < operationsPerSeed; operation++)
                        {
                            int kind = random.Next(0, 8);
                            int instance = random.Next(1, 5);
                            int playback = random.Next(0, 3);
                            int clipChannel = random.Next(0, 3);
                            int layer = random.Next(-1, layers + 1);
                            float weight = (float)random.NextDouble();
                            double time = random.NextDouble() * 2d;

                            switch (kind)
                            {
                                case 0:
                                    graph.BeginClip(new AnimationClipRequest(
                                        instance, playback, layer, clip, time, weight, false, clipChannel));
                                    break;
                                case 1:
                                    graph.UpdateClip(new AnimationClipRequest(
                                        instance, playback, layer, clip, time, weight, false, clipChannel));
                                    break;
                                case 2:
                                    graph.EndClip(new AnimationPlaybackKey(
                                        instance, playback, layer, clipChannel));
                                    break;
                                case 3:
                                    graph.SeekClip(new AnimationPlaybackKey(
                                        instance, playback, layer, clipChannel), time);
                                    break;
                                case 4:
                                    graph.StopAll();
                                    break;
                                case 5:
                                    graph.SetLayerWeight(layer, weight);
                                    break;
                                default:
                                    graph.Commit(Tick(step++, step * 0.016d));
                                    Validate(graph, layers, capacity, buffer);
                                    break;
                            }

                            totalOperations++;
                        }
                    }
                    finally
                    {
                        graph.Dispose();
                    }
                }
            }

            NoteEvidence(
                "M4",
                "external fuzz: " + configs.GetLength(0) + " configs x " + seeds + " seeds x "
                + operationsPerSeed + " ops = " + totalOperations + " operations");
        }

        private static void Validate(
            AnimationRuntimeGraph graph,
            int layers,
            int capacity,
            AnimationPlaybackSnapshot[] buffer)
        {
            Assert.LessOrEqual(graph.GetStats().ActivePlaybacks, layers * capacity);

            for (int layer = 0; layer < layers; layer++)
            {
                int count = graph.CopyPlaybackSnapshots(layer, buffer);
                Assert.AreEqual(graph.GetLayerActivePlaybacks(layer), count);

                for (int i = 0; i < count; i++)
                {
                    AnimationPlaybackSnapshot playback = buffer[i];
                    Assert.GreaterOrEqual(playback.Time, 0d);
                    Assert.LessOrEqual(playback.Time, playback.Length + 1e-3,
                        "time is clamped to the clip");
                    Assert.GreaterOrEqual(playback.Weight, 0f);
                    Assert.LessOrEqual(playback.Weight, 1f);

                    for (int j = i + 1; j < count; j++)
                    {
                        Assert.IsFalse(
                            playback.InstanceId == buffer[j].InstanceId
                            && playback.PlaybackChannel == buffer[j].PlaybackChannel
                            && playback.ClipChannel == buffer[j].ClipChannel,
                            "one layer cannot hold the same playback key twice");
                    }
                }
            }
        }

        // ---------------------------------------------------------------- M5: soak across durations

        [Test]
        public void M5_SoakAcrossDurationsAndPlaybackCounts_DoesNotGrowTheHeap()
        {
            int[] playbackCounts = { 1, 4, 64 };
            int[] commitCounts = { 5000, 20000, 50000 };
            int runs = 0;

            for (int playbackIndex = 0; playbackIndex < playbackCounts.Length; playbackIndex++)
            {
                for (int commitIndex = 0; commitIndex < commitCounts.Length; commitIndex++)
                {
                    int playbacks = playbackCounts[playbackIndex];
                    int commits = commitCounts[commitIndex];
                    int layers = (playbacks + 7) / 8;
                    int capacity = Math.Min(8, playbacks);

                    CreateActor("M5Actor_" + playbacks + "_" + commits, out Animator animator);
                    AnimationClip clip = CreateClip("M5Clip_" + playbacks + "_" + commits, 1f);

                    var descriptors = new AnimationLayerDescriptor[layers];
                    for (int i = 0; i < layers; i++)
                    {
                        descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, capacity);
                    }

                    var graph = new AnimationRuntimeGraph(animator, descriptors);

                    try
                    {
                        int started = 0;
                        for (int layer = 0; layer < layers && started < playbacks; layer++)
                        {
                            for (int slot = 0; slot < capacity && started < playbacks; slot++)
                            {
                                graph.BeginClip(new AnimationClipRequest(
                                    started + 1, 0, layer, clip, 0d, 1f));
                                started++;
                            }
                        }

                        graph.Commit(Tick(0L, 0d));
                        long heapBefore = GC.GetTotalMemory(true);

                        for (int i = 1; i <= commits; i++)
                        {
                            graph.UpdateClip(new AnimationClipRequest(
                                1, 0, 0, clip, (i % 60) * 0.016d, 1f));
                            graph.Commit(Tick(i, i * 0.016d));
                        }

                        long heapAfter = GC.GetTotalMemory(true);
                        long delta = heapAfter - heapBefore;

                        Assert.AreEqual(playbacks, graph.GetStats().ActivePlaybacks);
                        Assert.Less(delta, 2L * 1024 * 1024,
                            "heap must not grow across " + commits + " commits with " + playbacks + " playbacks");

                        NoteEvidence(
                            "M5",
                            playbacks + " playbacks x " + commits + " commits: heap delta " + delta + " B");
                        runs++;
                    }
                    finally
                    {
                        graph.Dispose();
                    }
                }
            }

            NoteEvidence("M5", "soak matrix: " + runs + " runs");
        }

        // ---------------------------------------------------------------- M6: long run and rewind

        [Test]
        public void M6_TenMinuteTickDrivenRun_DoesNotDrift_AndRewindReanchors()
        {
            CreateActor("M6Actor", out Animator animator);
            AnimationClip clip = CreateClip("M6Clip", 3f);
            var graph = new AnimationRuntimeGraph(
                animator,
                new[] { AnimationLayerDescriptor.Default },
                null,
                AnimationTimePolicy.TickDriven);

            var buffer = new AnimationPlaybackSnapshot[4];
            const double delta = 1d / 60d;
            const int commits = 36000; // ten minutes at 60 Hz

            try
            {
                graph.BeginClip(new AnimationClipRequest(
                    1, 0, 0, clip, 0d, 1f, false, 0, 1f, AnimationLoopMode.Loop));

                graph.Commit(Tick(0L, 0d));
                for (int i = 1; i <= commits; i++)
                {
                    graph.Commit(Tick(i, i * delta));
                }

                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                double expected = ReferenceTime(clip.length, 1f, AnimationLoopMode.Loop, commits * delta);
                NoteEvidence(
                    "M6",
                    commits + " commits (" + (commits * delta).ToString("0.0") + " s simulated): time="
                    + buffer[0].Time.ToString("0.000000") + " expected=" + expected.ToString("0.000000"));

                Assert.AreEqual(expected, buffer[0].Time, 1e-5,
                    "the absolute-time formula must not accumulate drift over ten simulated minutes");

                // Rewind: the next commit re-anchors at the current pose instead of jumping.
                double beforeRewind = buffer[0].Time;
                graph.Commit(new AnimationTick(5d, commits + 1L, AnimationTimeSource.Replay, true, 0.016f));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                Assert.AreEqual(beforeRewind, buffer[0].Time, 1e-6,
                    "a backward tick re-anchors without moving the pose");

                graph.Commit(Tick(commits + 2L, 5.1d));
                Assert.AreEqual(1, graph.CopyPlaybackSnapshots(0, buffer));
                double expectedAfterRewind = beforeRewind + 0.1d;
                if (expectedAfterRewind >= clip.length)
                {
                    expectedAfterRewind -= clip.length;
                }

                Assert.AreEqual(expectedAfterRewind, buffer[0].Time, 1e-6,
                    "after the rewind the clip advances from the re-anchored pose");
            }
            finally
            {
                graph.Dispose();
            }
        }

        // ---------------------------------------------------------------- M7: topology change cost

        [Test]
        public void M7_DynamicTopologyChange_HasAMeasurablePerChangeCost()
        {
            CreateActor("M7Actor", out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            try
            {
                AnimationMixerPlayable mixer = AnimationMixerPlayable.Create(graph.PlayableGraph, 1);
                const int changes = 2000;

                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < changes; i++)
                {
                    mixer.SetInputCount(8);
                    mixer.SetInputCount(1);
                }

                watch.Stop();

                double totalMs = watch.Elapsed.TotalMilliseconds;
                double microsecondsPerChange = totalMs * 1000.0 / (changes * 2);

                NoteEvidence(
                    "M7",
                    (changes * 2) + " SetInputCount transitions in " + totalMs.ToString("0.0")
                    + " ms = " + microsecondsPerChange.ToString("0.000") + " us/change; "
                    + "a fixed-topology graph performs zero of these per frame");

                Assert.Less(microsecondsPerChange, 500.0,
                    "a topology change must stay well below one frame");
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
