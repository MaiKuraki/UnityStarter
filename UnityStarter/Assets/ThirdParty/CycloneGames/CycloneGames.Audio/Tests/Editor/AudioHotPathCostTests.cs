// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Measures per-operation cost and allocation of the native reads and dispatches used by audio
    /// hot paths. Numbers are reported; only the identity comparison and zero-allocation invariants
    /// are asserted.
    /// </summary>
    public sealed class AudioHotPathCostTests
    {
        private const int Iterations = 200000;
        private const int Rounds = 5;

        private static int intSink;
        private static float floatSink;
        private static int listSink;

        [Test]
        public void Report_PrimitiveCosts()
        {
            var created = new List<Object>();
            AudioBank bank = ScriptableObject.CreateInstance<AudioBank>();
            created.Add(bank);

            GameObject probe = new GameObject("AudioHotPathProbe");
            AudioSource source = probe.AddComponent<AudioSource>();
            AudioClip clip = AudioClip.Create("AudioHotPathClip", 4410, 1, 44100, false);
            created.Add(clip);
            source.clip = clip;
            Transform probeTransform = probe.transform;

            _ = bank.RuntimeId;

            try
            {
                double instanceIdNs = MeasureBest(() => MeasureGetInstanceId(bank));
                double runtimeIdNs = MeasureBest(() => MeasureRuntimeId(bank));
                double clipNs = MeasureBest(() => MeasureAudioSourceClip(source));
                double timeNs = MeasureBest(() => MeasureAudioSourceTime(source));
                double pitchNs = MeasureBest(() => MeasureAudioSourcePitch(source));
                double isPlayingNs = MeasureBest(() => MeasureAudioSourceIsPlaying(source));
                double transformPositionNs = MeasureBest(() => MeasureTransformPosition(probeTransform));
                double listenerPositionNs = MeasureBest(MeasureReferenceListenerPosition);
                double linecastNs = MeasureBest(MeasureLinecast);
                double invocationListNs = MeasureBest(MeasureGetInvocationList);
                double callbackListNs = MeasureBest(MeasureCallbackListIteration);

                TestContext.WriteLine("audio-hotpath-cost (ns/op, best of " + Rounds + ", iterations=" + Iterations + ")");
                TestContext.WriteLine("  Object.GetInstanceID           : " + instanceIdNs.ToString("F2"));
                TestContext.WriteLine("  IAudioIdentified.RuntimeId     : " + runtimeIdNs.ToString("F2"));
                TestContext.WriteLine("  AudioSource.clip               : " + clipNs.ToString("F2"));
                TestContext.WriteLine("  AudioSource.time               : " + timeNs.ToString("F2"));
                TestContext.WriteLine("  AudioSource.pitch              : " + pitchNs.ToString("F2"));
                TestContext.WriteLine("  AudioSource.isPlaying          : " + isPlayingNs.ToString("F2"));
                TestContext.WriteLine("  Transform.position             : " + transformPositionNs.ToString("F2"));
                TestContext.WriteLine("  GetReferenceListenerPosition   : " + listenerPositionNs.ToString("F2"));
                TestContext.WriteLine("  Physics.Linecast               : " + linecastNs.ToString("F2"));
                TestContext.WriteLine("  Delegate.GetInvocationList     : " + invocationListNs.ToString("F2"));
                TestContext.WriteLine("  List<EventCompleted> iterate   : " + callbackListNs.ToString("F2"));

                Assert.Greater(instanceIdNs, 0d);
                Assert.Greater(runtimeIdNs, 0d);
                Assert.Less(
                    runtimeIdNs,
                    instanceIdNs,
                    "The managed identity must be cheaper than the native instance ID call it replaced.");
            }
            finally
            {
                Object.DestroyImmediate(probe);
                for (int i = 0; i < created.Count; i++)
                {
                    if (created[i] != null) Object.DestroyImmediate(created[i]);
                }
            }
        }

        [Test]
        public void Identity_SteadyStateAllocatesNothing()
        {
            AudioBank bank = ScriptableObject.CreateInstance<AudioBank>();
            GameObject probe = new GameObject("AudioHotPathAllocationProbe");

            try
            {
                _ = bank.RuntimeId;
                _ = AudioObjectId.Of(probe);

                const int iterations = 100000;
                long identityBytes = AudioObjectIdentityTests.MeasureAllocatedBytes(
                    () => intSink += bank.RuntimeId, iterations);
                long foreignBytes = AudioObjectIdentityTests.MeasureAllocatedBytes(
                    () => intSink += AudioObjectId.Of(probe), iterations);

                TestContext.WriteLine(
                    "audio-hotpath-allocation (bytes over " + iterations + " iterations): identity=" + identityBytes +
                    " foreign=" + foreignBytes);

                Assert.AreEqual(0, identityBytes);
                Assert.AreEqual(0, foreignBytes);
            }
            finally
            {
                Object.DestroyImmediate(probe);
                Object.DestroyImmediate(bank);
            }
        }

        [Test]
        public void ActiveEvent_Construction_DoesNotEagerlyCreateParameterSlots()
        {
            var activeEvent = new ActiveEvent();

            var field = typeof(ActiveEvent).GetField(
                "activeParameters",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "ActiveEvent.activeParameters layout changed; update this guard.");

            var slots = field.GetValue(activeEvent) as ActiveParameter[];
            Assert.IsNotNull(slots);
            Assert.AreEqual(8, slots.Length);

            for (int i = 0; i < slots.Length; i++)
            {
                Assert.IsNull(
                    slots[i],
                    "ActiveEvent must create parameter slots on first use, not during construction.");
            }
        }

        private static double MeasureBest(Func<long> measurement)
        {
            double best = double.MaxValue;
            for (int round = 0; round < Rounds; round++)
            {
                long ticks = measurement();
                double ns = ticks * (1000000000d / Stopwatch.Frequency) / Iterations;
                if (ns < best) best = ns;
            }

            return best;
        }

        private static long MeasureGetInstanceId(AudioBank bank)
        {
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += bank.GetInstanceID();
            }

            stopwatch.Stop();
            intSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureRuntimeId(AudioBank bank)
        {
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += bank.RuntimeId;
            }

            stopwatch.Stop();
            intSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureAudioSourceClip(AudioSource source)
        {
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += ReferenceEquals(source.clip, null) ? 0 : 1;
            }

            stopwatch.Stop();
            intSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureAudioSourceTime(AudioSource source)
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += source.time;
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureAudioSourcePitch(AudioSource source)
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += source.pitch;
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureAudioSourceIsPlaying(AudioSource source)
        {
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += source.isPlaying ? 1 : 0;
            }

            stopwatch.Stop();
            intSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureTransformPosition(Transform transform)
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += transform.position.x;
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureReferenceListenerPosition()
        {
            var stopwatch = Stopwatch.StartNew();
            float sum = 0f;
            for (int i = 0; i < Iterations; i++)
            {
                sum += AudioManager.GetReferenceListenerPosition().x;
            }

            stopwatch.Stop();
            floatSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureLinecast()
        {
            var origin = new Vector3(0f, 0f, 0f);
            var target = new Vector3(0f, 0f, 10f);
            int layerMask = ~0;
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += Physics.Linecast(origin, target, layerMask) ? 1 : 0;
            }

            stopwatch.Stop();
            intSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static long MeasureGetInvocationList()
        {
            Action callback = SinkA;
            callback += SinkB;

            // One array allocation per iteration, so fewer iterations than the allocation-free probes.
            int iterations = Iterations / 10;
            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < iterations; i++)
            {
                sum += callback.GetInvocationList().Length;
            }

            stopwatch.Stop();
            intSink = sum;
            return (long)(stopwatch.ElapsedTicks * (double)Iterations / iterations);
        }

        private static long MeasureCallbackListIteration()
        {
            var callbacks = new List<Action>(2) { SinkA, SinkB };

            var stopwatch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                sum += callbacks.Count;
            }

            stopwatch.Stop();
            listSink = sum;
            return stopwatch.ElapsedTicks;
        }

        private static void SinkA()
        {
        }

        private static void SinkB()
        {
        }
    }
}
