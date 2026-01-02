using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
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
        
        /// <summary>
        /// When true, jobs are batched and completed in LateUpdate for better performance.
        /// When false (default), jobs complete immediately for simpler debugging.
        /// </summary>
        public bool UseDeferredJobCompletion { get; set; } = false;

        public int SensorCount => _sensors.Count;
        public bool IsDisposed { get; private set; }

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
        /// </summary>
        public void Update(float deltaTime)
        {
            if (IsDisposed) return;

            float currentTime = Time.time;
            int count = _sensors.Count;
            
            for (int i = 0; i < count; i++)
            {
                var sensor = _sensors[i];
                if (sensor == null || !sensor.IsEnabled) continue;

                if (currentTime - sensor.LastUpdateTime >= sensor.UpdateInterval)
                {
                    try
                    {
                        sensor.UpdateSensor(deltaTime);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[AIPerception] Sensor {sensor.SensorId} update failed: {e.Message}");
                    }
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
