using System;
using System.Collections.Generic;
using CycloneGames.AIPerception.Runtime;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Tests.Editor
{
    /// <summary>
    /// Contract tests for stimulus transitions: acquisition, loss through memory expiry, loss without
    /// memory, listener registration and the custom-sensor publish path.
    /// </summary>
    public sealed class PerceptionEventTests
    {
        private PerceptibleRegistry _registry;
        private SensorManager _manager;
        private ManualPerceptionClock _clock;
        private ProximitySensor _sensor;
        private GameObject _sensorObject;
        private EventPerceptible _target;
        private PerceptibleHandle _targetHandle;

        [SetUp]
        public void SetUp()
        {
            PerceptibleRegistry.ResetInstance();
            _clock = new ManualPerceptionClock(0d);
            _registry = new PerceptibleRegistry(initialCapacity: 8, maximumCapacity: 64, cellSize: 8f);
            _manager = new SensorManager(_registry, maximumSensorCount: 8, clock: _clock);
            _sensorObject = new GameObject("perception-event-sensor");

            _target = new EventPerceptible
            {
                PerceptibleId = 1,
                Position = float3.zero,
                LineOfSightPoint = float3.zero,
                IsDetectable = true
            };
            _targetHandle = _registry.Register(_target);
        }

        [TearDown]
        public void TearDown()
        {
            _sensor?.Dispose();
            _manager?.Dispose();
            _registry?.Dispose();
            if (_sensorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_sensorObject);
            }

            PerceptibleRegistry.ResetInstance();
        }

        [Test]
        public void FirstDetection_RaisesAcquiredWithSensorIdentity()
        {
            CreateProximity(memoryDuration: 0f);
            var listener = new RecordingListener();

            _manager.AddListener(listener);
            _manager.Update(0.016f);

            Assert.AreEqual(1, listener.Events.Count);
            PerceptionEvent acquired = listener.Events[0];
            Assert.AreEqual(PerceptionEventKind.Acquired, acquired.Kind);
            Assert.AreEqual(SensorType.Proximity, acquired.SensorType);
            Assert.AreEqual(_sensor.SensorId, acquired.SensorId);
            Assert.AreEqual(_targetHandle, acquired.Handle);
        }

        [Test]
        public void SteadyDetection_DoesNotRepeatAcquired()
        {
            CreateProximity(memoryDuration: 0f);
            var listener = new RecordingListener();
            _manager.AddListener(listener);

            for (int i = 0; i < 5; i++)
            {
                _clock.Advance(0.016d);
                _manager.Update(0.016f);
            }

            Assert.AreEqual(1, listener.Events.Count, "a target that stays detected must raise one acquisition");
        }

        [Test]
        public void TargetLeaves_WithoutMemory_RaisesLostImmediately()
        {
            CreateProximity(memoryDuration: 0f);
            var listener = new RecordingListener();
            _manager.AddListener(listener);

            _manager.Update(0.016f);
            _target.Position = new float3(100f, 0f, 0f);
            _clock.Advance(0.016d);
            _manager.Update(0.016f);

            Assert.AreEqual(2, listener.Events.Count);
            Assert.AreEqual(PerceptionEventKind.Lost, listener.Events[1].Kind);
            Assert.AreEqual(_targetHandle, listener.Events[1].Handle);
        }

        [Test]
        public void TargetLeaves_WithMemory_RaisesLostOnlyAfterDuration()
        {
            CreateProximity(memoryDuration: 1f);
            var listener = new RecordingListener();
            _manager.AddListener(listener);

            _manager.Update(0.016f);
            Assert.AreEqual(1, listener.Events.Count);

            _target.Position = new float3(100f, 0f, 0f);
            _clock.Advance(0.4d);
            _manager.Update(0.016f);
            Assert.AreEqual(1, listener.Events.Count, "a remembered target must not raise loss yet");

            _clock.Advance(0.8d);
            _manager.Update(0.016f);
            Assert.AreEqual(2, listener.Events.Count);
            Assert.AreEqual(PerceptionEventKind.Lost, listener.Events[1].Kind);
        }

        [Test]
        public void TargetReturns_AfterLoss_RaisesAcquiredAgain()
        {
            CreateProximity(memoryDuration: 0f);
            var listener = new RecordingListener();
            _manager.AddListener(listener);

            _manager.Update(0.016f);
            _target.Position = new float3(100f, 0f, 0f);
            _manager.Update(0.016f);
            _target.Position = float3.zero;
            _manager.Update(0.016f);

            Assert.AreEqual(3, listener.Events.Count);
            Assert.AreEqual(PerceptionEventKind.Acquired, listener.Events[2].Kind);
        }

        [Test]
        public void MultipleListeners_AllReceiveInRegistrationOrder()
        {
            CreateProximity(memoryDuration: 0f);
            var first = new RecordingListener();
            var second = new RecordingListener();
            _manager.AddListener(first);
            _manager.AddListener(second);

            _manager.Update(0.016f);

            Assert.AreEqual(1, first.Events.Count);
            Assert.AreEqual(1, second.Events.Count);

            _manager.RemoveListener(first);
            _target.Position = new float3(100f, 0f, 0f);
            _manager.Update(0.016f);

            Assert.AreEqual(1, first.Events.Count, "a removed listener must stop receiving");
            Assert.AreEqual(2, second.Events.Count);
        }

        [Test]
        public void MutatingListenersDuringDispatch_Throws()
        {
            CreateProximity(memoryDuration: 0f);
            var listener = new RecordingListener();
            var other = new RecordingListener();
            listener.OnEvent = () => _manager.AddListener(other);
            _manager.AddListener(listener);

            Assert.Throws<InvalidOperationException>(() => _manager.Update(0.016f));
        }

        [Test]
        public void NoListeners_UpdateDoesNotThrow()
        {
            CreateProximity(memoryDuration: 0f);
            Assert.DoesNotThrow(() => _manager.Update(0.016f));
            Assert.DoesNotThrow(() => _manager.Update(0.016f));
        }

        [Test]
        public void PublishEvent_ReachesRegisteredListener()
        {
            var listener = new RecordingListener();
            _manager.AddListener(listener);

            var custom = new PerceptionEvent(
                42, SensorType.Custom, PerceptionEventKind.Acquired,
                _targetHandle, float3.zero, 1f, 2f, _clock.NowSeconds);
            _manager.PublishEvent(in custom);

            Assert.AreEqual(1, listener.Events.Count);
            Assert.AreEqual(42, listener.Events[0].SensorId);
            Assert.AreEqual(SensorType.Custom, listener.Events[0].SensorType);
        }

        private void CreateProximity(float memoryDuration)
        {
            ProximitySensorConfig config = ProximitySensorConfig.Default;
            config.Radius = 5f;
            config.UpdateInterval = 0f;
            config.MemoryDuration = memoryDuration;

            _sensor = new ProximitySensor(_sensorObject.transform, config, _manager);
            Assert.IsTrue(_manager.TryRegister(_sensor));
        }

        private sealed class RecordingListener : IPerceptionEventListener
        {
            public readonly List<PerceptionEvent> Events = new List<PerceptionEvent>(8);
            public Action OnEvent;

            public void OnPerceptionEvent(in PerceptionEvent perceptionEvent)
            {
                Events.Add(perceptionEvent);
                OnEvent?.Invoke();
            }
        }

        private sealed class EventPerceptible : IPerceptible
        {
            public int PerceptibleId { get; set; }
            public int PerceptibleTypeId { get; set; }
            public int TeamId { get; set; }
            public bool IsDetectable { get; set; }
            public float3 Position { get; set; }
            public float DetectionRadius { get; set; }
            public float Loudness { get; set; }
            public bool IsSoundSource { get; set; }
            public string Tag => string.Empty;
            public float3 LineOfSightPoint { get; set; }
            public float3 GetLOSPoint() => LineOfSightPoint;
        }
    }
}
