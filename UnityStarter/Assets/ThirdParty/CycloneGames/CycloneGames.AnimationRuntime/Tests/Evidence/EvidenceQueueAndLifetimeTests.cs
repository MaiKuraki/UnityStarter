using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Event queue capacity and clip-event cache lifetime. Both are production limits, not micro-optimisations:
    /// a dropped marker is a missed gameplay event, and a retained clip is an asset that cannot unload.
    /// </summary>
    public sealed class EvidenceQueueAndLifetimeTests : EvidenceTestBase
    {
        private sealed class Sink : IAnimationClipEventSink
        {
            public int Total;
            public readonly Dictionary<AnimationPlaybackHandle, int> PerHandle =
                new Dictionary<AnimationPlaybackHandle, int>();

            public void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent)
            {
                Total++;
                PerHandle.TryGetValue(handle, out int count);
                PerHandle[handle] = count + 1;
            }
        }

        private static AnimationTick Tick(long step)
        {
            return new AnimationTick(step * 0.016d, step, AnimationTimeSource.Update, false, 0.016f);
        }

        private AnimationClip ClipWithMarkers(int markerCount)
        {
            AnimationClip clip = CreateClip("QueueClip_" + markerCount, 1f);
            clip.wrapMode = WrapMode.ClampForever;
            for (int i = 0; i < markerCount; i++)
            {
                clip.AddEvent(new AnimationEvent
                {
                    time = (i + 1f) / (markerCount + 1f),
                    functionName = "Q" + i
                });
            }

            return clip;
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(8)]
        public void EventQueue_MaxDepth_AllMarkersAreDelivered(int markerCount)
        {
            CreateActor("Queue_" + markerCount, out Animator animator);
            AnimationClip clip = ClipWithMarkers(markerCount);
            var descriptors = new AnimationLayerDescriptor[8];
            for (int i = 0; i < 8; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 8);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors);
            var sink = new Sink();
            graph.SetClipEventSink(sink);
            var keys = new List<AnimationPlaybackKey>(64);

            try
            {
                for (int layer = 0; layer < 8; layer++)
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        int instance = layer * 8 + slot + 1;
                        graph.BeginPlayback(new AnimationClipRequest(instance, 0, layer, clip, 0d, 1f));
                        keys.Add(new AnimationPlaybackKey(instance, 0, layer, 0));
                    }
                }

                graph.Commit(Tick(0L));
                for (int i = 0; i < keys.Count; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(
                        keys[i].InstanceId, keys[i].PlaybackChannel, keys[i].Layer, clip, 1d, 1f));
                }

                graph.Commit(Tick(1L));

                NoteEvidence("queue", "8x8 playbacks x " + markerCount + " markers: total=" + sink.Total
                    + " handles=" + sink.PerHandle.Count);
                Assert.AreEqual(64, sink.PerHandle.Count);
                Assert.AreEqual(64 * markerCount, sink.Total, "no marker may be dropped at max depth");
                foreach (KeyValuePair<AnimationPlaybackHandle, int> pair in sink.PerHandle)
                {
                    Assert.AreEqual(markerCount, pair.Value);
                }
            }
            finally
            {
                graph.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CacheClipAndDispose(AnimationRuntimeGraph graph)
        {
            var clip = new AnimationClip { name = "CacheClip", wrapMode = WrapMode.ClampForever };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            clip.AddEvent(new AnimationEvent { time = 0.5f, functionName = "CacheMarker" });

            graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
            graph.Commit(Tick(0L));
            graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f));
            graph.Commit(Tick(1L));
            graph.Dispose();

            var weak = new WeakReference(clip);
            UnityEngine.Object.DestroyImmediate(clip);
            return weak;
        }

        [Test]
        public void EventQueue_PeakThenSteady_DoesNotAllocate()
        {
            CreateActor("QueueAlloc", out Animator animator);
            AnimationClip clip = ClipWithMarkers(8);
            var descriptors = new AnimationLayerDescriptor[8];
            for (int i = 0; i < 8; i++)
            {
                descriptors[i] = new AnimationLayerDescriptor(null, false, 1f, 8);
            }

            var graph = new AnimationRuntimeGraph(animator, descriptors);
            var sink = new Sink();
            graph.SetClipEventSink(sink);
            var keys = new List<AnimationPlaybackKey>(64);

            try
            {
                for (int layer = 0; layer < 8; layer++)
                {
                    for (int slot = 0; slot < 8; slot++)
                    {
                        int instance = layer * 8 + slot + 1;
                        graph.BeginPlayback(new AnimationClipRequest(instance, 0, layer, clip, 0d, 1f));
                        keys.Add(new AnimationPlaybackKey(instance, 0, layer, 0));
                    }
                }

                graph.Commit(Tick(0L));
                for (int i = 0; i < keys.Count; i++)
                {
                    graph.UpdatePlayback(new AnimationClipRequest(
                        keys[i].InstanceId, keys[i].PlaybackChannel, keys[i].Layer, clip, 1d, 1f));
                }

                graph.Commit(Tick(1L)); // grows the array to 512 once

                long step = 2;
                long heapDelta = MeasureAllocationDelta(100, 2000, () =>
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        graph.UpdatePlayback(new AnimationClipRequest(
                            keys[i].InstanceId, keys[i].PlaybackChannel, keys[i].Layer, clip, 0d, 1f));
                    }

                    graph.Commit(new AnimationTick(step * 0.016d, step++, AnimationTimeSource.Update, false, 0.016f));
                    for (int i = 0; i < keys.Count; i++)
                    {
                        graph.UpdatePlayback(new AnimationClipRequest(
                            keys[i].InstanceId, keys[i].PlaybackChannel, keys[i].Layer, clip, 1d, 1f));
                    }

                    graph.Commit(new AnimationTick(step * 0.016d, step++, AnimationTimeSource.Update, false, 0.016f));
                });

                NoteEvidence("queue-alloc", "peak 512 then 2000 cycles: heap delta " + FormatBytes(heapDelta));
                Assert.Less(heapDelta, 128L * 1024L, "the grown queue must be reused without steady allocation");
            }
            finally
            {
                graph.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ClipEventCache_DoesNotKeepDestroyedClipAlive()
        {
            CreateActor("CacheActor", out Animator animator);
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });

            WeakReference weak = CacheClipAndDispose(graph);

            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            NoteEvidence("cache", "destroyed clip alive after GC = " + weak.IsAlive);
            Assert.IsFalse(weak.IsAlive, "the clip-event cache must not keep a destroyed clip alive");
        }

        [Test]
        public void ClipDestroyedMidPlay_TerminatesWithoutEvents()
        {
            CreateActor("DestroyedClipActor", out Animator animator);
            var clip = new AnimationClip { name = "DestroyedClip", wrapMode = WrapMode.ClampForever };
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            clip.AddEvent(new AnimationEvent { time = 0.5f, functionName = "Gone" });

            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f));
                graph.Commit(Tick(0L));
                int before = sink.Total;

                UnityEngine.Object.DestroyImmediate(clip);
                clip = null;

                // The development watchdog runs every 30 commits; commit through one full interval.
                for (int i = 0; i < 30; i++)
                {
                    graph.Commit(Tick(1L + i));
                }

                NoteEvidence("destroyed", "events before=" + before + " after=" + sink.Total
                    + " active=" + graph.GetStats().ActivePlaybacks);
                Assert.AreEqual(before, sink.Total, "a destroyed clip must not dispatch markers");
                Assert.AreEqual(0, graph.GetStats().ActivePlaybacks);
            }
            finally
            {
                graph.Dispose();
                if (clip != null)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }
            }
        }

        [Test]
        public void SameInstanceTwoClipChannels_EventsAreIsolated()
        {
            CreateActor("ChannelActor", out Animator animator);
            AnimationClip clip = CreateClip("ChannelClip", 1f);
            clip.AddEvent(new AnimationEvent { time = 0.5f, functionName = "ChannelMarker" });
            var sink = new Sink();
            var graph = new AnimationRuntimeGraph(animator, new[] { AnimationLayerDescriptor.Default });
            graph.SetClipEventSink(sink);

            try
            {
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 0));
                graph.BeginPlayback(new AnimationClipRequest(1, 0, 0, clip, 0d, 1f, false, 1));
                graph.Commit(Tick(0L));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f, false, 0));
                graph.UpdatePlayback(new AnimationClipRequest(1, 0, 0, clip, 0.6d, 1f, false, 1));
                graph.Commit(Tick(1L));

                NoteEvidence("channels", "events=" + sink.Total + " handles=" + sink.PerHandle.Count);
                Assert.AreEqual(2, sink.Total);
                Assert.AreEqual(2, sink.PerHandle.Count);
                foreach (KeyValuePair<AnimationPlaybackHandle, int> pair in sink.PerHandle)
                {
                    Assert.AreEqual(1, pair.Value);
                }
            }
            finally
            {
                graph.Dispose();
            }
        }
    }
}
