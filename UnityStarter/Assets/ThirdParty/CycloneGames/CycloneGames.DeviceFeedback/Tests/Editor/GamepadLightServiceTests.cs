using CycloneGames.DeviceFeedback.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Tests.Editor
{
    public sealed class GamepadLightServiceTests
    {
        [Test]
        public void DefaultBackend_IsExplicitNoop()
        {
            using var service = new GamepadLightService();

            Assert.That(service.IsAvailable, Is.False);
            Assert.DoesNotThrow(() => service.Initialize());
            Assert.DoesNotThrow(() => service.SetColor(Color.red));
            Assert.DoesNotThrow(() => service.Flash(Color.red, Color.black, 0.1f, 0.1f));
            Assert.DoesNotThrow(() => service.PlayGradient(new Gradient(), 0.1f));
            Assert.DoesNotThrow(() => service.PlayIntensityCurve(Color.white, AnimationCurve.Linear(0f, 0f, 1f, 1f), 0.1f));
            Assert.DoesNotThrow(() => service.CancelAnimation());
            Assert.DoesNotThrow(() => service.Reset());
        }

        [Test]
        public void InjectedBackend_ReceivesClampedColor()
        {
            var backend = new FakeDeviceLightBackend { IsAvailableValue = true };
            using var service = new GamepadLightService(backend, ownsBackend: false);

            service.Initialize();
            service.SetColor(new Color(2f, -1f, 0.5f, 2f));

            Assert.That(backend.InitializeCount, Is.EqualTo(1));
            Assert.That(backend.LastColor, Is.EqualTo(new Color(1f, 0f, 0.5f, 1f)));
            Assert.That(backend.SetColorCount, Is.EqualTo(1));
        }

        [Test]
        public void TimedOperations_ForwardSanitizedIntervals()
        {
            var backend = new FakeDeviceLightBackend { IsAvailableValue = true };
            using var service = new GamepadLightService(backend, ownsBackend: false);

            service.Flash(new Color(2f, 0f, 0f, 2f), new Color(0f, -1f, 0f, 1f), 0.1f, 0.2f);
            service.PlayGradient(new Gradient(), 1f, 0);
            service.PlayIntensityCurve(new Color(0f, 0f, 2f, 2f), AnimationCurve.Linear(0f, 0f, 1f, 1f), 1f, 5);

            Assert.That(backend.FlashCount, Is.EqualTo(1));
            Assert.That(backend.PlayGradientCount, Is.EqualTo(1));
            Assert.That(backend.PlayIntensityCurveCount, Is.EqualTo(1));
            Assert.That(backend.LastGradientSampleIntervalMs, Is.EqualTo(10));
            Assert.That(backend.LastIntensitySampleIntervalMs, Is.EqualTo(10));
        }

        [Test]
        public void IsActiveFalse_CancelsAndResetsBackend()
        {
            var backend = new FakeDeviceLightBackend { IsAvailableValue = true };
            using var service = new GamepadLightService(backend, ownsBackend: false);

            service.IsActive = false;

            Assert.That(backend.CancelAnimationCount, Is.EqualTo(1));
            Assert.That(backend.ResetCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_ResetsAndDisposesOwnedBackend()
        {
            var backend = new FakeDeviceLightBackend { IsAvailableValue = true };
            var service = new GamepadLightService(backend);

            service.Dispose();

            Assert.That(service.IsAvailable, Is.False);
            Assert.That(backend.CancelAnimationCount, Is.EqualTo(1));
            Assert.That(backend.ResetCount, Is.EqualTo(1));
            Assert.That(backend.IsDisposed, Is.True);
        }

        private sealed class FakeDeviceLightBackend : IDeviceLightBackend
        {
            public bool IsAvailableValue;
            public bool IsDisposed;
            public int InitializeCount;
            public int SetColorCount;
            public int FlashCount;
            public int PlayGradientCount;
            public int PlayIntensityCurveCount;
            public int CancelAnimationCount;
            public int ResetCount;
            public int LastGradientSampleIntervalMs;
            public int LastIntensitySampleIntervalMs;
            public Color LastColor;

            public bool IsAvailable => IsAvailableValue;

            public void Initialize()
            {
                InitializeCount++;
            }

            public void SetColor(Color color)
            {
                SetColorCount++;
                LastColor = color;
            }

            public void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds)
            {
                FlashCount++;
            }

            public void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs)
            {
                PlayGradientCount++;
                LastGradientSampleIntervalMs = sampleIntervalMs;
            }

            public void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs)
            {
                PlayIntensityCurveCount++;
                LastIntensitySampleIntervalMs = sampleIntervalMs;
            }

            public void CancelAnimation()
            {
                CancelAnimationCount++;
            }

            public void Reset()
            {
                ResetCount++;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
