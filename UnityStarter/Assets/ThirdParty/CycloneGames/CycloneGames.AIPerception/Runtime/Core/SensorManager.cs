using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct SensorLODLevel
    {
        [Tooltip("Beyond this distance (m), apply this LOD level")]
        public float Distance;

        [Tooltip("Frequency multiplier at this distance (1.0 = full, 0.5 = half, 0.25 = quarter)")]
        [Range(0.05f, 1f)]
        public float FrequencyMultiplier;

        public static readonly SensorLODLevel[] DefaultLevels = new[]
        {
            new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 0.5f },
            new SensorLODLevel { Distance = 80f, FrequencyMultiplier = 0.25f },
            new SensorLODLevel { Distance = 200f, FrequencyMultiplier = 0.1f },
        };
    }

    /// <summary>
    /// Centralized sensor tick manager with LOD support and job scheduling.
    /// Supports two modes:
    /// - Immediate: Jobs complete immediately in UpdateSensor (simpler, default)
    /// - Deferred: Jobs are batched and completed in LateUpdate (more efficient for large scale)
    /// </summary>
    public sealed class SensorManager : IDisposable
    {
        private const int INITIAL_CAPACITY = 64;

        private static SensorManager _instance;
        private static bool _isQuitting;

        public static SensorManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                return _instance ??= new SensorManager();
            }
        }

        public static bool HasInstance => _instance != null;

        private readonly List<ISensor> _sensors;
        private readonly Dictionary<int, ISensor> _sensorLookup;
        private int _nextSensorId;
        
        // Job management
        private JobHandle _batchedJobHandle;
        private bool _hasScheduledJobs;
        
        // LOD configuration
        private Transform _lodReference;
        private SensorLODLevel[] _lodLevels;
        private bool _lodEnabled;
        
        /// <summary>
        /// When true, jobs are batched and completed in LateUpdate for better performance.
        /// When false (default), jobs complete immediately for simpler debugging.
        /// </summary>
        public bool UseDeferredJobCompletion { get; set; } = false;

        public int SensorCount => _sensors.Count;
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Configures LOD (Level of Detail) for sensor update frequency.
        /// Sensors farther than LOD distance thresholds update less frequently.
        /// Set reference to null to disable LOD.
        /// </summary>
        public void ConfigureLOD(Transform reference, SensorLODLevel[] levels)
        {
            _lodReference = reference;
            _lodLevels = levels;
            _lodEnabled = reference != null && levels != null && levels.Length > 0;
        }

        private float GetLODMultiplier(float3 sensorPosition)
        {
            if (!_lodEnabled || _lodReference == null) return 1f;

            float dist = math.distance(sensorPosition, (float3)_lodReference.position);

            for (int i = 0; i < _lodLevels.Length; i++)
            {
                if (dist <= _lodLevels[i].Distance)
                    return _lodLevels[i].FrequencyMultiplier;
            }

            // Beyond all LOD levels — use the last (most aggressive) multiplier
            return _lodLevels[_lodLevels.Length - 1].FrequencyMultiplier;
        }

        public SensorManager()
        {
            _sensors = new List<ISensor>(INITIAL_CAPACITY);
            _sensorLookup = new Dictionary<int, ISensor>(INITIAL_CAPACITY);
        }

        public int GenerateSensorId() => _nextSensorId++;

        public void Register(ISensor sensor)
        {
            if (sensor == null || IsDisposed) return;

            if (!_sensorLookup.ContainsKey(sensor.SensorId))
            {
                _sensors.Add(sensor);
                _sensorLookup[sensor.SensorId] = sensor;
            }
        }

        public void Unregister(ISensor sensor)
        {
            if (sensor == null) return;

            if (_sensorLookup.Remove(sensor.SensorId))
            {
                _sensors.Remove(sensor);
            }
        }

        public ISensor GetSensor(int sensorId)
        {
            _sensorLookup.TryGetValue(sensorId, out var sensor);
            return sensor;
        }

        /// <summary>
        /// Updates all sensors. Call once per frame from Update().
        /// In deferred mode, jobs are scheduled but not completed here.
        /// Sensors must not throw — exceptions indicate programming errors and will propagate.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (IsDisposed) return;

            // Rebuild perceptible data once per frame for all sensors to share
            PerceptibleRegistry.Instance?.RebuildData();

            float currentTime = Time.time;
            int count = _sensors.Count;
            
            for (int i = 0; i < count; i++)
            {
                var sensor = _sensors[i];
                if (sensor == null || !sensor.IsEnabled) continue;

                float effectiveInterval = sensor.UpdateInterval;
                if (_lodEnabled)
                    effectiveInterval = sensor.UpdateInterval * GetLODMultiplier(sensor.Position);

                if (currentTime - sensor.LastUpdateTime >= effectiveInterval)
                {
                    sensor.UpdateSensor(deltaTime);
                }
            }
        }

        /// <summary>
        /// Completes all batched jobs and processes results.
        /// Call in LateUpdate() when using deferred mode.
        /// </summary>
        public void LateUpdate()
        {
            if (IsDisposed) return;
            
            // Complete batched jobs
            CompleteBatchedJobs();
            
            // Let sensors process their job results
            int count = _sensors.Count;
            for (int i = 0; i < count; i++)
            {
                var sensor = _sensors[i];
                if (sensor == null || !sensor.IsEnabled) continue;
                
                sensor.ProcessJobResults();
            }
        }

        /// <summary>
        /// Adds a job to the batch. Used by sensors in deferred mode.
        /// </summary>
        public void AddToBatch(JobHandle handle)
        {
            _batchedJobHandle = JobHandle.CombineDependencies(_batchedJobHandle, handle);
            _hasScheduledJobs = true;
        }

        /// <summary>
        /// Completes all batched jobs.
        /// </summary>
        public void CompleteBatchedJobs()
        {
            if (_hasScheduledJobs)
            {
                _batchedJobHandle.Complete();
                _batchedJobHandle = default;
                _hasScheduledJobs = false;
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            CompleteBatchedJobs();

            for (int i = _sensors.Count - 1; i >= 0; i--)
            {
                _sensors[i]?.Dispose();
            }

            _sensors.Clear();
            _sensorLookup.Clear();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void OnApplicationQuit()
        {
            _isQuitting = true;
            _instance?.Dispose();
        }

        public static void ResetInstance()
        {
            _instance?.Dispose();
            _instance = null;
            _isQuitting = false;
        }
    }
}
