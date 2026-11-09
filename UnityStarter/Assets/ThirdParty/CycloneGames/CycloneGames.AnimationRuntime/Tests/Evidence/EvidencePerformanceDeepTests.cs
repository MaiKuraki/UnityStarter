using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Measurement-only suite for the performance/data-layout audit. It asserts only loose guards; the numbers
    /// are published to the evidence file so an optimisation decision can be based on them.
    /// </summary>
    public sealed class EvidencePerformanceDeepTests : EvidenceTestBase
    {
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

        private static double MeasureMicroseconds(int iterations, Action body, int rounds = 3)
        {
            for (int i = 0; i < 100; i++)
            {
                body();
            }

            double best = double.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++)
                {
                    body();
                }

                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                if (ms < best)
                {
                    best = ms;
                }
            }

            return best * 1000.0 / iterations;
        }

        private AnimationRuntimeGraph CreateGraph(
            int layers,
            int capacity,
            int playbacks,
            AnimationClip clip,
            out AnimationPlaybackKey[] keys,
            IAnimationClipEventSink sink = null,
            AnimationTimePolicy policy = AnimationTimePolicy.External)
        {
            CreateActor("Perf_" + layers + "x" + capacity + "_" + playbacks, out Animator animator);
            var descriptors = new AnimationLayerDescriptor[layers];
            for (int i = 0; i < layers; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, capacity);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors, null, policy);
            if (sink != null)
            {
                graph.SetClipEventSink(sink);
            }

            keys = new AnimationPlaybackKey[playbacks];
            int started = 0;
            for (int layer = 0; layer < layers && started < playbacks; layer++)
            {
                for (int slot = 0; slot < capacity && started < playbacks; slot++)
                {
                    int instance = started + 1;
                    graph.BeginClip(new AnimationClipRequest(
                        instance, 0, layer, clip, 0d, 1f, false, 0));
                    keys[started] = new AnimationPlaybackKey(instance, 0, layer, 0);
                    started++;
                }
            }

            graph.Commit(TickWithDelta(0L, 0.016f));
            return graph;
        }

        private AnimationClip CreateEventClip(int eventCount)
        {
            AnimationClip clip = CreateClip("PerfClip_" + eventCount, 1f);
            for (int i = 0; i < eventCount; i++)
            {
                float time = (i + 1f) / (eventCount + 1f);
                clip.AddEvent(new AnimationEvent
                {
                    time = time,
                    functionName = "E" + i,
                    intParameter = i
                });
            }

            return clip;
        }

        [Test]
        public void D1_StructFootprint()
        {
            bool unsafeUsed;
            int handle = UnsafeSizeOf(typeof(AnimationPlaybackHandle), out unsafeUsed);
            int tick = UnsafeSizeOf(typeof(AnimationTick), out _);
            int request = UnsafeSizeOf(typeof(AnimationClipRequest), out _);
            int fade = UnsafeSizeOf(typeof(AnimationFade), out _);
            int exit = UnsafeSizeOf(typeof(AnimationExitTime), out _);
            int clipEvent = UnsafeSizeOf(typeof(AnimationClipEvent), out _);
            int transitionEvent = UnsafeSizeOf(typeof(AnimationRuntimeGraph)
                .GetNestedType("TransitionEvent", BindingFlags.NonPublic), out _);
            int clock = UnsafeSizeOf(typeof(AnimationRuntimeGraph)
                .GetNestedType("PlaybackClock", BindingFlags.NonPublic), out _);
            int transition = UnsafeSizeOf(typeof(AnimationRuntimeGraph)
                .GetNestedType("PlaybackTransition", BindingFlags.NonPublic), out _);
            int layer = UnsafeSizeOf(typeof(AnimationRuntimeGraph)
                .GetNestedType("Layer", BindingFlags.NonPublic), out _);

            int playback = AnimationRuntimeGraph.PlaybackStructSize;
            int perSlot = playback + clock + transition + 4 + 16 + 8; // playback, clock, transition, generation, cached playable, cached clip
            NoteEvidence(
                "D1",
                "sizes: handle=" + handle + " tick=" + tick + " request=" + request + " fade=" + fade
                + " exit=" + exit + " clipEvent=" + clipEvent + " transitionEvent=" + transitionEvent
                + " playback=" + playback + " clock=" + clock + " transition=" + transition
                + " layer=" + layer + " perSlotBytes=" + perSlot
                + " measurement=" + (unsafeUsed ? "Unsafe.SizeOf" : "layout calculator"));

            Assert.AreEqual(12, handle);
            Assert.AreEqual(64, playback, "Playback must remain one cache line");
            Assert.LessOrEqual(perSlot, 176, "per-slot cold+hot footprint guard (event cache adds one reference)");
        }

        [Test]
        public void D2_CommitCost_ExternalScaling()
        {
            AnimationClip clip = CreateClip("D2Clip", 10f);
            double one = MeasureCommitCost(1, 1, 1, clip, AnimationTimePolicy.External);
            double four = MeasureCommitCost(2, 2, 4, clip, AnimationTimePolicy.External);
            double full = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.External);

            NoteEvidence("D2", "external commit us/frame: 1 playback=" + one.ToString("0.000")
                + ", 4 playbacks=" + four.ToString("0.000") + ", 64 playbacks=" + full.ToString("0.000"));
            Assert.Less(full, 500.0);
        }

        [Test]
        public void D3_CommitCost_TickDriven()
        {
            AnimationClip clip = CreateClip("D3Clip", 10f);
            double external = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.External);
            double tickDriven = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.TickDriven);

            NoteEvidence("D3", "64 playbacks us/frame: external=" + external.ToString("0.000")
                + ", tickDriven=" + tickDriven.ToString("0.000"));
            Assert.Less(tickDriven, 600.0);
        }

        [Test]
        public void D4_EventDispatchOverhead()
        {
            AnimationClip none = CreateClip("D4None", 10f);
            AnimationClip one = CreateEventClip(1);
            AnimationClip eight = CreateEventClip(8);

            double noneUs = MeasureCommitCost(8, 8, 64, none, AnimationTimePolicy.TickDriven);
            double oneUs = MeasureCommitCost(8, 8, 64, one, AnimationTimePolicy.TickDriven, new CountingSink());
            double eightUs = MeasureCommitCost(8, 8, 64, eight, AnimationTimePolicy.TickDriven, new CountingSink());

            NoteEvidence("D4", "64 playbacks tick-driven us/frame: no events=" + noneUs.ToString("0.000")
                + ", 1 event/loop=" + oneUs.ToString("0.000")
                + ", 8 events/loop=" + eightUs.ToString("0.000"));
            Assert.Less(eightUs, 800.0);
        }

        [Test]
        public void D5_FadeAndExitOverhead()
        {
            AnimationClip clip = CreateClip("D5Clip", 10f);
            double baseline = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.External);
            double fading = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.External, null, graph =>
            {
                for (int i = 0; i < 64; i++)
                {
                    int layer = i / 8;
                    int instance = i + 1;
                    graph.FadePlayback(
                        new AnimationPlaybackKey(instance, 0, layer, 0),
                        new AnimationFade(1e6f, 1f, AnimationFadeEasing.Linear));
                }
            });
            double exitArmed = MeasureCommitCost(8, 8, 64, clip, AnimationTimePolicy.External, null, graph =>
            {
                for (int i = 0; i < 64; i++)
                {
                    int layer = i / 8;
                    int instance = i + 1;
                    graph.SetExitTime(
                        new AnimationPlaybackKey(instance, 0, layer, 0),
                        new AnimationExitTime(0.5f, AnimationExitMode.EveryLoop));
                }
            });

            NoteEvidence("D5", "64 playbacks us/frame: baseline=" + baseline.ToString("0.000")
                + ", all fading=" + fading.ToString("0.000") + ", exit armed=" + exitArmed.ToString("0.000"));
            Assert.Less(fading, 1500.0);
        }

        [Test]
        public void D6_UpdateClipCost()
        {
            AnimationClip clip = CreateClip("D6Clip", 10f);
            CreateActor("D6Actor", out Animator animator);
            var descriptors = new AnimationLayerDescriptor[8];
            for (int i = 0; i < 8; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 8);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors);
            var keys = new AnimationPlaybackKey[64];
            for (int i = 0; i < 64; i++)
            {
                int instance = i + 1;
                int layer = i / 8;
                graph.BeginClip(new AnimationClipRequest(instance, 0, layer, clip, 0d, 1f));
                keys[i] = new AnimationPlaybackKey(instance, 0, layer, 0);
            }

            graph.Commit(TickWithDelta(0L, 0.016f));

            long step = 1;
            double us = MeasureMicroseconds(2000, () =>
            {
                for (int i = 0; i < 64; i++)
                {
                    graph.UpdateClip(new AnimationClipRequest(
                        keys[i].InstanceId, 0, keys[i].Layer, clip, (step % 60) * 0.016d, 1f));
                }

                graph.Commit(TickWithDelta(step++, 0.016f));
            });

            try
            {
                NoteEvidence("D6", "64 UpdateClip + 1 Commit: " + us.ToString("0.000") + " us/frame");
                Assert.Less(us, 500.0);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void D7_CombinedAllocation()
        {
            AnimationClip clip = CreateEventClip(4);
            var sink = new CountingSink();
            CreateActor("D7Actor", out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            var key = new AnimationPlaybackKey(1, 0, 0, 0);
            graph.BeginClip(
                new AnimationClipRequest(1, 0, 0, clip, 0d, 1f),
                new AnimationFade(1e6f, 1f, AnimationFadeEasing.Linear));
            graph.SetExitTime(key, new AnimationExitTime(0.5f, AnimationExitMode.EveryLoop));
            graph.Commit(TickWithDelta(0L, 0.016f));

            long step = 1;
            long heapDelta = MeasureAllocationDelta(1000, 50000, () =>
            {
                graph.UpdateClip(new AnimationClipRequest(1, 0, 0, clip, (step % 60) * 0.016d, 1f));
                graph.Commit(TickWithDelta(step++, 0.016f));
            });

            try
            {
                NoteEvidence("D7", "50000 commits with fade+exit+events: heap delta " + FormatBytes(heapDelta));
                Assert.Less(heapDelta, 128L * 1024L);
            }
            finally
            {
                graph.Dispose();
            }
        }

        [Test]
        public void D8_LayerSourceSwitchCost()
        {
            AnimationClip a = CreateClip("D8A", 1f);
            AnimationClip b = CreateClip("D8B", 1f);
            CreateActor("D8Actor", out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            LinearBlendNode node = LinearBlendNode.Create(graph.PlayableGraph, new[] { a, b }, new[] { 0f, 1f });

            try
            {
                double us = MeasureMicroseconds(2000, () =>
                {
                    graph.SetLayerSource(0, node.Playable);
                    graph.ClearLayerSource(0);
                });

                NoteEvidence("D8", "SetLayerSource+Clear: " + us.ToString("0.000") + " us/cycle");
                Assert.Less(us, 100.0);
            }
            finally
            {
                node.Dispose();
                graph.Dispose();
            }
        }

        private double MeasureCommitCost(
            int layers,
            int capacity,
            int playbacks,
            AnimationClip clip,
            AnimationTimePolicy policy,
            IAnimationClipEventSink sink = null,
            Action<AnimationRuntimeGraph> setup = null)
        {
            AnimationRuntimeGraph graph = CreateGraph(
                layers, capacity, playbacks, clip, out AnimationPlaybackKey[] keys, sink, policy);
            setup?.Invoke(graph);

            long step = 1;
            try
            {
                return MeasureMicroseconds(2000, () =>
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        graph.UpdateClip(new AnimationClipRequest(
                            keys[i].InstanceId, 0, keys[i].Layer, clip, (step % 60) * 0.016d, 1f));
                    }

                    graph.Commit(TickWithDelta(step++, 0.016f));
                });
            }
            finally
            {
                graph.Dispose();
            }
        }

        private static int UnsafeSizeOf(Type type, out bool usedUnsafe)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type unsafeType = assembly.GetType("System.Runtime.CompilerServices.Unsafe");
                if (unsafeType == null)
                {
                    continue;
                }

                MethodInfo method = unsafeType.GetMethod("SizeOf", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    continue;
                }

                try
                {
                    usedUnsafe = true;
                    return (int)method.MakeGenericMethod(type).Invoke(null, null);
                }
                catch
                {
                    break;
                }
            }

            usedUnsafe = false;
            return LayoutSize(type, out _);
        }

        private static int LayoutSize(Type type, out string layout)
        {
            if (type == null)
            {
                layout = "<null>";
                return 0;
            }

            int offset = 0;
            int maxAlignment = 1;
            var builder = new System.Text.StringBuilder();

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                int size = ManagedSize(field.FieldType);
                int alignment = Math.Min(size, 8);
                offset = (offset + alignment - 1) / alignment * alignment;
                builder.Append(field.Name).Append('@').Append(offset).Append(' ');
                offset += size;
                if (alignment > maxAlignment)
                {
                    maxAlignment = alignment;
                }
            }

            offset = (offset + maxAlignment - 1) / maxAlignment * maxAlignment;
            layout = builder.ToString();
            return offset;
        }

        private static int ManagedSize(Type type)
        {
            if (type.IsPointer || type.IsClass || type == typeof(string) || type.IsArray)
            {
                return IntPtr.Size;
            }

            if (type.IsEnum)
            {
                return 4;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Boolean:
                    return 1;
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Char:
                    return 2;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Single:
                    return 4;
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Double:
                    return 8;
            }

            return LayoutSize(type, out _);
        }
    }
}
