using System.Threading;
using CycloneGames.DeviceFeedback.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Tests.Editor
{
    /// <summary>
    /// Contract tests for admission, diagnostics, results, buffers and thread affinity.
    /// </summary>
    public sealed class DeviceFeedbackCoreTests
    {
        private sealed class TestRumbleBackend : IGamepadRumbleBackend
        {
            public bool IsAvailable => true;
            public long RumbleCount;
            public void Initialize() { }
            public void SetMotorSpeeds(float lowFrequency, float highFrequency) { }
            public void Rumble(float lowFrequency, float highFrequency, float durationSeconds) => RumbleCount++;
            public void Stop() { }
            public void Dispose() { }
        }

        private sealed class TestLightBackend : IDeviceLightBackend
        {
            public bool IsAvailable => true;
            public int LastGradientSampleIntervalMs;
            public void Initialize() { }
            public void SetColor(Color color) { }
            public void Flash(Color a, Color b, float on, float off) { }
            public void PlayGradient(Gradient gradient, float duration, int interval)
                => LastGradientSampleIntervalMs = interval;
            public void PlayIntensityCurve(Color c, AnimationCurve curve, float duration, int interval) { }
            public void CancelAnimation() { }
            public void Reset() { }
            public void Dispose() { }
        }

        private static GamepadRumbleService CreateService(DeviceFeedbackLimits limits)
        {
            return new GamepadRumbleService(new TestRumbleBackend(), limits, false);
        }

        [Test]
        public void CapacityBoundaries_AreExactAtLimitMinusOneLimitAndLimitPlusOne()
        {
            var limits = new DeviceFeedbackLimits(1000, 64, 32, 64);
            using var service = CreateService(limits);

            service.Play(0.5f, 0.999f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.Accepted));

            service.Play(0.5f, 1f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.Accepted));

            service.Play(0.5f, 1.001f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedCapacity));

            DeviceFeedbackMemoryStats stats = service.GetMemoryStats();
            Assert.That(stats.AcceptedOperationCount, Is.EqualTo(2));
            Assert.That(stats.CapacityRejectedOperationCount, Is.EqualTo(1));
        }

        [Test]
        public void WaveformCapacityBoundary_RejectsBeforeDispatch()
        {
            // Duration budget is 10 s so the 1.3 s calls isolate the waveform
            // sample budget instead of being refused by the duration budget.
            var limits = new DeviceFeedbackLimits(10_000, 64, 32, 64);
            using var service = CreateService(limits);
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            long before = ((TestRumbleBackend)GetBackend(service)).RumbleCount;

            service.PlayCurve(curve, 1.26f, null, 20);   // 63 raw samples -> accepted
            service.PlayCurve(curve, 1.28f, null, 20);   // 64 raw samples -> accepted
            service.PlayCurve(curve, 1.30f, null, 20);   // 65 raw samples -> capacity

            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedCapacity));
            Assert.That(((TestRumbleBackend)GetBackend(service)).RumbleCount, Is.EqualTo(before + 2));
        }

        [Test]
        public void NonFiniteInputs_AreRejectedWithoutReachingBackend()
        {
            using var service = CreateService(DeviceFeedbackLimits.Default);
            var backend = (TestRumbleBackend)GetBackend(service);
            long before = backend.RumbleCount;

            service.Play(float.NaN, 0.1f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedInvalid));
            service.Play(0.5f, float.PositiveInfinity);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedInvalid));
            service.Rumble(float.NaN, 0.5f, 0.1f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedInvalid));
            Assert.That(backend.RumbleCount, Is.EqualTo(before));
        }

        [Test]
        public void NonFiniteHapticEvents_AreRejectedByAdmission()
        {
            using var service = CreateService(DeviceFeedbackLimits.Default);
            var clip = ScriptableObject.CreateInstance<HapticClip>();
            clip.events = new[]
            {
                new HapticEvent
                {
                    type = HapticEventType.Transient,
                    time = 0f,
                    duration = 0f,
                    intensity = float.NaN,
                    sharpness = 0.5f
                }
            };
            service.PlayClip(clip);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedInvalid));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void InvalidEnums_DoNotThrow()
        {
            using var service = new MobileVibrationService();
            Assert.DoesNotThrow(() => service.VibrateIOS((IOSImpactStyle)99));
            Assert.DoesNotThrow(() => service.VibrateIOS((IOSNotificationStyle)99));
            Assert.DoesNotThrow(() => service.PlayPreset((HapticPreset)99));
        }

        [Test]
        public void Diagnostics_ArePerInstance()
        {
            using var first = CreateService(DeviceFeedbackLimits.Default);
            using var second = CreateService(DeviceFeedbackLimits.Default);
            first.Play(0.5f, 0.1f);
            first.Play(0.5f, 0.1f);
            second.Play(0.5f, 0.1f);

            Assert.That(first.GetMemoryStats().AcceptedOperationCount, Is.EqualTo(2));
            Assert.That(second.GetMemoryStats().AcceptedOperationCount, Is.EqualTo(1));
        }

        [Test]
        public void Reinitialize_RevivesADisposedService()
        {
            using var service = new MobileVibrationService();
            service.Dispose();
            service.Play(0.5f, 0.1f);
            Assert.That(service.LastResult.Status, Is.EqualTo(FeedbackStatus.RejectedDisposed));

            service.Reinitialize();
            service.Play(0.5f, 0.1f);
            Assert.That(service.LastResult.Status, Is.Not.EqualTo(FeedbackStatus.RejectedDisposed));
        }

        [Test]
        public void ForeignThread_IsRejectedWithoutMutatingInstanceState()
        {
            using var service = CreateService(DeviceFeedbackLimits.Default);
            service.Play(0.5f, 0.1f);
            long before = service.GetMemoryStats().AcceptedOperationCount;

            var thread = new Thread(() => service.Play(0.5f, 0.1f));
            thread.Start();
            thread.Join();

            Assert.That(service.GetMemoryStats().AcceptedOperationCount, Is.EqualTo(before));
        }

        [Test]
        public void BufferGrowth_IsAmortizedAcrossIncreasingDepths()
        {
            var limits = new DeviceFeedbackLimits(300000, 4096, 2048, 4096);
            using var service = new MobileVibrationService(limits);
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            for (int count = 1; count <= 256; count++)
            {
                service.PlayCurve(curve, count * 5f / 1000f, null, 5);
            }
            Assert.That(service.GetMemoryStats().BufferGrowthCount, Is.LessThanOrEqualTo(12));
        }

        [Test]
        public void PlayCurve_ReportsQuantizedSampleCountWithinLimits()
        {
            var limits = new DeviceFeedbackLimits(1000, 64, 32, 64);
            using var service = CreateService(limits);
            service.PlayCurve(AnimationCurve.Linear(0f, 0f, 1f, 1f), 0.5f, null, 20);
            Assert.That(service.LastResult.WaveformSampleCount, Is.GreaterThan(0));
            Assert.That(service.LastResult.WaveformSampleCount,
                        Is.LessThanOrEqualTo(limits.MaximumWaveformSampleCount));
        }

        [Test]
        public void Profile_MinimumSampleIntervalIsInjectable()
        {
            var profile = new DeviceFeedbackProfile(minimumSampleIntervalMilliseconds: 20);
            var backend = new TestLightBackend();
            using var service = new GamepadLightService(backend, DeviceFeedbackLimits.Default, profile, false);
            service.PlayGradient(new Gradient(), 0.5f, 0);
            Assert.That(backend.LastGradientSampleIntervalMs, Is.EqualTo(20));
        }

        [Test]
        public void Profile_RejectsValuesAboveAbsoluteCeilings()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new DeviceFeedbackProfile(exactArrayCacheMaxEntries: DeviceFeedbackProfile.AbsoluteMaximumCacheEntries + 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new DeviceFeedbackProfile(exactArrayCacheBudgetBytes: DeviceFeedbackProfile.AbsoluteMaximumCacheBudgetBytes + 1));
        }

        [Test]
        public void PlayClip_DoesNotMutateAuthoringEventOrder()
        {
            using var service = CreateService(DeviceFeedbackLimits.Default);
            var clip = ScriptableObject.CreateInstance<HapticClip>();
            clip.events = new[]
            {
                new HapticEvent { type = HapticEventType.Transient, time = 0.2f, intensity = 0.5f, sharpness = 0.5f },
                new HapticEvent { type = HapticEventType.Transient, time = 0.0f, intensity = 0.9f, sharpness = 0.5f }
            };
            service.PlayClip(clip);
            Assert.That(clip.events[0].time, Is.EqualTo(0.2f));
            Assert.That(clip.events[1].time, Is.EqualTo(0f));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void BakedSnapshot_HasExactLengthArraysAndDoesNotMutateAsset()
        {
            var clip = ScriptableObject.CreateInstance<HapticClip>();
            clip.events = new[]
            {
                new HapticEvent { type = HapticEventType.Transient, time = 0.2f, intensity = 0.5f, sharpness = 0.5f },
                new HapticEvent { type = HapticEventType.Continuous, time = 0.0f, duration = 0.1f, intensity = 0.9f, sharpness = 0.4f }
            };
            bool ok = HapticClipBaker.TryBake(clip, DeviceFeedbackLimits.Default,
                DeviceFeedbackProfile.Default, out HapticClipSnapshot snapshot, out FeedbackResult result);
            Assert.That(ok, Is.True);
            Assert.That(result.IsAccepted, Is.True);
            Assert.That(snapshot.IsEventMode, Is.True);
            Assert.That(snapshot.EventCount, Is.EqualTo(2));
            Assert.That(snapshot.SegmentCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(snapshot.IsValid, Is.True);
            Assert.That(snapshot.TotalVibrationMilliseconds, Is.GreaterThan(0));
            Assert.That(clip.events[0].time, Is.EqualTo(0.2f), "authoring asset must stay untouched");
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void PlayClipSnapshot_IsRepeatable()
        {
            var clip = ScriptableObject.CreateInstance<HapticClip>();
            clip.events = new[]
            {
                new HapticEvent { type = HapticEventType.Transient, time = 0f, intensity = 0.6f, sharpness = 0.5f }
            };
            Assert.That(HapticClipBaker.TryBake(clip, DeviceFeedbackLimits.Default,
                DeviceFeedbackProfile.Default, out HapticClipSnapshot snapshot, out _), Is.True);
            using var service = new MobileVibrationService();
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 1000; i++) service.PlayClipSnapshot(snapshot);
            });
            Assert.That(service.LastResult.Status, Is.Not.EqualTo(FeedbackStatus.RejectedInvalid));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void CommandQueue_IsBoundedAndThreadSafe()
        {
            using var service = new MobileVibrationService();
            service.Play(0.1f, 0.01f); // establish owner thread
            long exceptions = 0;
            var threads = new System.Threading.Thread[4];
            for (int t = 0; t < threads.Length; t++)
            {
                threads[t] = new System.Threading.Thread(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        var command = new FeedbackCommand
                        {
                            Kind = FeedbackCommandKind.Cancel,
                            Priority = FeedbackPriority.Background
                        };
                        try { service.TrySubmit(in command); }
                        catch { System.Threading.Interlocked.Increment(ref exceptions); }
                    }
                });
                threads[t].Start();
            }
            for (int t = 0; t < threads.Length; t++) threads[t].Join();
            Assert.That(exceptions, Is.Zero);
            DeviceFeedbackCommandStats stats = service.GetCommandStats();
            Assert.That(stats.PendingCount, Is.LessThanOrEqualTo(stats.Capacity));
            Assert.That(stats.EnqueuedCount + stats.RejectedCount, Is.EqualTo(4000));
            for (int i = 0; i < 20; i++) service.Drain(256);
            Assert.That(service.GetCommandStats().PendingCount, Is.Zero);
        }

        [Test]
        public void TrySubmit_RejectsNonFinitePayload()
        {
            using var service = new MobileVibrationService();
            var command = new FeedbackCommand
            {
                Kind = FeedbackCommandKind.Play,
                A = float.NaN,
                DurationMilliseconds = 10L
            };
            Assert.That(service.TrySubmit(in command), Is.False);
            Assert.That(service.GetCommandStats().EnqueuedCount, Is.Zero);
        }

        private static IGamepadRumbleBackend GetBackend(GamepadRumbleService service)
        {
            var field = typeof(GamepadRumbleService).GetField(
                "_backend", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (IGamepadRumbleBackend)field.GetValue(service);
        }
    }
}
