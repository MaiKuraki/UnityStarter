using Unity.Collections;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Unity composition bridge for the three built-in perception senses. Authoring changes are
    /// applied transactionally through <see cref="ApplyAuthoringConfiguration"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/AI Perception")]
    public sealed class AIPerceptionComponent : MonoBehaviour
    {
        [Header("Sight")]
        [SerializeField] private bool _enableSight = true;
        [SerializeField] private SightSensorConfig _sightConfig = SightSensorConfig.Default;

        [Header("Hearing")]
        [SerializeField] private bool _enableHearing;
        [SerializeField] private HearingSensorConfig _hearingConfig = HearingSensorConfig.Default;

        [Header("Proximity")]
        [SerializeField] private bool _enableProximity;
        [SerializeField] private ProximitySensorConfig _proximityConfig = ProximitySensorConfig.Default;

        // Retained for serialized compatibility. Runtime diagnostics are rendered by the Editor
        // Inspector and do not run per-object IMGUI or reverse scene scans.
        [SerializeField, HideInInspector] private bool _showDebugOverlay;

        private SightSensor _sightSensor;
        private HearingSensor _hearingSensor;
        private ProximitySensor _proximitySensor;
        private bool _initialized;

        public SightSensor SightSensor => _sightSensor;
        public HearingSensor HearingSensor => _hearingSensor;
        public ProximitySensor ProximitySensor => _proximitySensor;
        public bool EnableSight => _enableSight;
        public bool EnableHearing => _enableHearing;
        public bool EnableProximity => _enableProximity;
        public SightSensorConfig SightConfig => _sightConfig;
        public HearingSensorConfig HearingConfig => _hearingConfig;
        public ProximitySensorConfig ProximityConfig => _proximityConfig;
        public bool IsInitialized => _initialized;
        public bool HasSightDetection => _sightSensor?.HasDetection ?? false;
        public bool HasHearingDetection => _hearingSensor?.HasDetection ?? false;
        public bool HasProximityDetection => _proximitySensor?.HasDetection ?? false;
        public bool HasAnyDetection => HasSightDetection || HasHearingDetection || HasProximityDetection;
        public int SightDetectedCount => _sightSensor?.DetectedCount ?? 0;
        public int HearingDetectedCount => _hearingSensor?.DetectedCount ?? 0;
        public int ProximityDetectedCount => _proximitySensor?.DetectedCount ?? 0;

        public bool ShowDebugOverlay
        {
            get => _showDebugOverlay;
            set => _showDebugOverlay = value;
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                DisposeSensors();
            }

            Initialize();
        }

        private void Start()
        {
            RefreshIgnoredTarget();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _ = PerceptionManagerComponent.Instance;
            PerceptibleHandle ignoredTarget = GetSelfHandle();
            SensorManager manager = SensorManager.Instance;
            if (manager == null)
            {
                return;
            }

            if (_enableSight)
            {
                _sightSensor = new SightSensor(transform, _sightConfig, manager, ignoredTarget);
                manager.Register(_sightSensor);
            }

            if (_enableHearing)
            {
                _hearingSensor = new HearingSensor(transform, _hearingConfig, manager, ignoredTarget);
                manager.Register(_hearingSensor);
            }

            if (_enableProximity)
            {
                _proximitySensor = new ProximitySensor(transform, _proximityConfig, manager, ignoredTarget);
                manager.Register(_proximitySensor);
            }

            _initialized = true;
        }

        private void OnDisable()
        {
            DisposeSensors();
        }

        /// <summary>
        /// Applies serialized settings by draining and rebuilding this component's sensors. This is
        /// a cold-path operation; callers should not invoke it from an update loop.
        /// </summary>
        public void ApplyAuthoringConfiguration()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            DisposeSensors();
            Initialize();
        }

        public IPerceptible GetClosestSightTarget() => GetClosestTarget(_sightSensor);
        public IPerceptible GetClosestHearingTarget() => GetClosestTarget(_hearingSensor);
        public IPerceptible GetClosestProximityTarget() => GetClosestTarget(_proximitySensor);

        public void GetAllSightDetections(ref NativeList<DetectionResult> results)
        {
            _sightSensor?.GetDetectionResults(ref results);
        }

        public void GetAllHearingDetections(ref NativeList<DetectionResult> results)
        {
            _hearingSensor?.GetDetectionResults(ref results);
        }

        public void GetAllProximityDetections(ref NativeList<DetectionResult> results)
        {
            _proximitySensor?.GetDetectionResults(ref results);
        }

        private static IPerceptible GetClosestTarget(ISensor sensor)
        {
            if (sensor == null || !sensor.HasDetection || !PerceptibleRegistry.HasInstance)
            {
                return null;
            }

            float closestDistance = float.MaxValue;
            IPerceptible closest = null;
            for (int i = 0; i < sensor.DetectedCount; i++)
            {
                if (!sensor.TryGetResult(i, out DetectionResult result) || result.Distance >= closestDistance)
                {
                    continue;
                }

                IPerceptible perceptible = PerceptibleRegistry.Instance.Get(result.Target);
                if (perceptible == null)
                {
                    continue;
                }

                closestDistance = result.Distance;
                closest = perceptible;
            }

            return closest;
        }

        private PerceptibleHandle GetSelfHandle()
        {
            return TryGetComponent(out PerceptibleComponent perceptible)
                ? perceptible.Handle
                : PerceptibleHandle.Invalid;
        }

        private void RefreshIgnoredTarget()
        {
            PerceptibleHandle handle = GetSelfHandle();
            _sightSensor?.SetIgnoredTarget(handle);
            _hearingSensor?.SetIgnoredTarget(handle);
            _proximitySensor?.SetIgnoredTarget(handle);
        }

        private void DisposeSensors()
        {
            SensorManager manager = SensorManager.ExistingInstance;
            DisposeSensor(manager, ref _sightSensor);
            DisposeSensor(manager, ref _hearingSensor);
            DisposeSensor(manager, ref _proximitySensor);
            _initialized = false;
        }

        private static void DisposeSensor<T>(SensorManager manager, ref T sensor)
            where T : class, ISensor
        {
            if (sensor == null)
            {
                return;
            }

            manager?.Unregister(sensor);
            sensor.Dispose();
            sensor = null;
        }
    }
}
