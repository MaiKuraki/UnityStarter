using CycloneGames.Logging;
using Unity.Collections;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Unity composition bridge for the three built-in perception senses. Authoring changes are
    /// applied transactionally through <see cref="ApplyAuthoringConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// The component never creates a hidden driver object. A <see cref="PerceptionManagerComponent"/>
    /// must exist in the scene, or an explicit <see cref="SensorManager"/> must be injected with
    /// <see cref="SetSensorManager"/> before the component enables.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/AI Perception")]
    public sealed class AIPerceptionComponent : MonoBehaviour
    {
        private static readonly LogChannel Log = AIPerceptionRuntimeLog.Channel;

        [Header("Sight")]
        [SerializeField] private bool _enableSight = true;
        [SerializeField] private SightSensorConfig _sightConfig = SightSensorConfig.Default;

        [Header("Hearing")]
        [SerializeField] private bool _enableHearing;
        [SerializeField] private HearingSensorConfig _hearingConfig = HearingSensorConfig.Default;

        [Header("Proximity")]
        [SerializeField] private bool _enableProximity;
        [SerializeField] private ProximitySensorConfig _proximityConfig = ProximitySensorConfig.Default;

        [Header("Damage")]
        [SerializeField] private bool _enableDamage;
        [SerializeField] private DamageSensorConfig _damageConfig = DamageSensorConfig.Default;

        [Header("Identity")]
        [Tooltip("Team of this observer in 0..254. 255 means no team, which resolves to neutral.")]
        [Range(0, PerceptionTeam.NoTeam)]
        [SerializeField] private int _teamId = PerceptionTeam.NoTeam;

        // Scene-view pin state. Read by the gizmo drawer; no per-object IMGUI runs at runtime.
        [SerializeField, HideInInspector] private bool _showDebugOverlay;

        private SightSensor _sightSensor;
        private HearingSensor _hearingSensor;
        private ProximitySensor _proximitySensor;
        private DamageSensor _damageSensor;
        private SensorManager _sensorManager;
        private bool _initialized;
        private bool _driverWarningLogged;

        public SightSensor SightSensor => _sightSensor;
        public HearingSensor HearingSensor => _hearingSensor;
        public ProximitySensor ProximitySensor => _proximitySensor;
        public DamageSensor DamageSensor => _damageSensor;
        public int TeamId => _teamId;
        public bool EnableSight => _enableSight;
        public bool EnableHearing => _enableHearing;
        public bool EnableProximity => _enableProximity;
        public bool EnableDamage => _enableDamage;
        public SightSensorConfig SightConfig => _sightConfig;
        public HearingSensorConfig HearingConfig => _hearingConfig;
        public ProximitySensorConfig ProximityConfig => _proximityConfig;
        public bool IsInitialized => _initialized;

        /// <summary>Sensor manager this component's sensors are registered with, or null before initialization.</summary>
        public SensorManager SensorManager => _sensorManager;

        /// <summary>
        /// Binds an explicit sensor manager. Use this from a composition root or DI container instead
        /// of relying on the process-wide default world. Rebinds live sensors when already enabled.
        /// </summary>
        public void SetSensorManager(SensorManager manager, bool rebuild = true)
        {
            if (_sensorManager == manager)
            {
                return;
            }

            if (rebuild && isActiveAndEnabled)
            {
                DisposeSensors();
            }

            _sensorManager = manager;
            if (rebuild && isActiveAndEnabled)
            {
                Initialize();
            }
        }

        public bool HasSightDetection => _sightSensor?.HasDetection ?? false;
        public bool HasHearingDetection => _hearingSensor?.HasDetection ?? false;
        public bool HasProximityDetection => _proximitySensor?.HasDetection ?? false;
        public bool HasDamageDetection => _damageSensor?.HasDetection ?? false;
        public bool HasAnyDetection => HasSightDetection || HasHearingDetection || HasProximityDetection || HasDamageDetection;
        public int SightDetectedCount => _sightSensor?.DetectedCount ?? 0;
        public int HearingDetectedCount => _hearingSensor?.DetectedCount ?? 0;
        public int ProximityDetectedCount => _proximitySensor?.DetectedCount ?? 0;
        public int DamageDetectedCount => _damageSensor?.DetectedCount ?? 0;

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

            SensorManager manager = _sensorManager ?? SensorManager.ExistingInstance;
            if (manager == null)
            {
                if (!_driverWarningLogged)
                {
                    _driverWarningLogged = true;
                    Log.Error(
                        name,
                        static (context, builder) => builder
                            .Append("No perception driver is available for '").Append(context)
                            .Append("'. Add a PerceptionManagerComponent to the scene or inject a ")
                            .Append("SensorManager with SetSensorManager. Perception never spawns a hidden driver."));
                }

                return;
            }

            _sensorManager = manager;
            _driverWarningLogged = false;
            PerceptibleHandle ignoredTarget = GetSelfHandle();
            if (_enableSight)
            {
                SightSensorConfig sightConfig = _sightConfig;
                sightConfig.ObserverTeamId = (byte)_teamId;
                _sightSensor = new SightSensor(transform, sightConfig, manager, ignoredTarget);
                if (!manager.TryRegister(_sightSensor))
                {
                    _sightSensor.Dispose();
                    _sightSensor = null;
                }
            }

            if (_enableHearing)
            {
                HearingSensorConfig hearingConfig = _hearingConfig;
                hearingConfig.ObserverTeamId = (byte)_teamId;
                _hearingSensor = new HearingSensor(transform, hearingConfig, manager, ignoredTarget);
                if (!manager.TryRegister(_hearingSensor))
                {
                    _hearingSensor.Dispose();
                    _hearingSensor = null;
                }
            }

            if (_enableProximity)
            {
                ProximitySensorConfig proximityConfig = _proximityConfig;
                proximityConfig.ObserverTeamId = (byte)_teamId;
                _proximitySensor = new ProximitySensor(transform, proximityConfig, manager, ignoredTarget);
                if (!manager.TryRegister(_proximitySensor))
                {
                    _proximitySensor.Dispose();
                    _proximitySensor = null;
                }
            }

            if (_enableDamage)
            {
                DamageSensorConfig damageConfig = _damageConfig;
                damageConfig.ObserverTeamId = (byte)_teamId;
                _damageSensor = new DamageSensor(transform, damageConfig, manager, ignoredTarget);
                if (!manager.TryRegister(_damageSensor))
                {
                    _damageSensor.Dispose();
                    _damageSensor = null;
                }
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

        public IPerceptible GetClosestDamageTarget() => GetClosestTarget(_damageSensor);

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
            SensorManager manager = _sensorManager ?? SensorManager.ExistingInstance;
            DisposeSensor(manager, ref _sightSensor);
            DisposeSensor(manager, ref _hearingSensor);
            DisposeSensor(manager, ref _proximitySensor);
            DisposeSensor(manager, ref _damageSensor);
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
