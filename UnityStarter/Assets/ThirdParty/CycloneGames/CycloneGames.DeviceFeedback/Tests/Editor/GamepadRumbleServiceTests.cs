using CycloneGames.DeviceFeedback.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Tests.Editor
{
    public sealed class GamepadRumbleServiceTests
    {
        [Test]
        public void DefaultBackend_IsExplicitNoop()
        {
            using var service = new GamepadRumbleService();

            Assert.That(service.IsAvailable, Is.False);
            Assert.DoesNotThrow(() => service.Initialize());
            Assert.DoesNotThrow(() => service.PlayPreset(HapticPreset.Success));
            Assert.DoesNotThrow(() => service.Play(0.8f, 0.1f, 0.7f));
            Assert.DoesNotThrow(() => service.PlayCurve(AnimationCurve.Linear(0f, 0f, 1f, 1f), 0.1f));
            Assert.DoesNotThrow(() => service.SetMotorSpeeds(1f, 1f));
            Assert.DoesNotThrow(() => service.Rumble(1f, 1f, 0.1f));
            Assert.DoesNotThrow(() => service.Cancel());
        }

        [Test]
        public void InjectedBackend_ReceivesClampedMotorSpeeds()
        {
            var backend = new FakeGamepadRumbleBackend { IsAvailableValue = true };
            using var service = new GamepadRumbleService(backend, ownsBackend: false);

            service.Initialize();
            service.SetMotorSpeeds(2f, -1f);

            Assert.That(backend.InitializeCount, Is.EqualTo(1));
            Assert.That(backend.LastLowFrequency, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(backend.LastHighFrequency, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(backend.SetMotorSpeedsCount, Is.EqualTo(1));
        }

        [Test]
        public void Play_MapsIntensityAndSharpnessToBackendRumble()
        {
            var backend = new FakeGamepadRumbleBackend { IsAvailableValue = true };
            using var service = new GamepadRumbleService(backend, ownsBackend: false);

            service.Play(0.8f, 0.25f, 1f);

            Assert.That(backend.RumbleCount, Is.EqualTo(1));
            Assert.That(backend.LastLowFrequency, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(backend.LastHighFrequency, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(backend.LastDurationSeconds, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void IsActiveFalse_StopsBackend()
        {
            var backend = new FakeGamepadRumbleBackend { IsAvailableValue = true };
            using var service = new GamepadRumbleService(backend, ownsBackend: false);

            service.IsActive = false;

            Assert.That(backend.StopCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_StopsAndDisposesOwnedBackend()
        {
            var backend = new FakeGamepadRumbleBackend { IsAvailableValue = true };
            var service = new GamepadRumbleService(backend);

            service.Dispose();

            Assert.That(service.IsAvailable, Is.False);
            Assert.That(backend.StopCount, Is.EqualTo(1));
            Assert.That(backend.IsDisposed, Is.True);
        }

        private sealed class FakeGamepadRumbleBackend : IGamepadRumbleBackend
        {
            public bool IsAvailableValue;
            public bool IsDisposed;
            public int InitializeCount;
            public int SetMotorSpeedsCount;
            public int RumbleCount;
            public int StopCount;
            public float LastLowFrequency;
            public float LastHighFrequency;
            public float LastDurationSeconds;

            public bool IsAvailable => IsAvailableValue;

            public void Initialize()
            {
                InitializeCount++;
            }

            public void SetMotorSpeeds(float lowFrequency, float highFrequency)
            {
                SetMotorSpeedsCount++;
                LastLowFrequency = lowFrequency;
                LastHighFrequency = highFrequency;
            }

            public void Rumble(float lowFrequency, float highFrequency, float durationSeconds)
            {
                RumbleCount++;
                LastLowFrequency = lowFrequency;
                LastHighFrequency = highFrequency;
                LastDurationSeconds = durationSeconds;
            }

            public void Stop()
            {
                StopCount++;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
