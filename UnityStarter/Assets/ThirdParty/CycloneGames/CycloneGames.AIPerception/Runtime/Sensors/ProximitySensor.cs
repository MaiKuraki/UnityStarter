using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.AIPerception.Runtime.Jobs;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct ProximitySensorConfig
    {
        [Range(0f, 50f)] public float Radius;
        [Range(0f, 5f)] public float UpdateInterval;
        public int TargetTypeId;
        public bool FilterByType;

        [Header("Memory")]
        [Tooltip("Seconds to remember a target after it leaves sensor range. 0 disables memory.")]
        [Range(0f, 60f)]
        public float MemoryDuration;

        public static ProximitySensorConfig Default => new ProximitySensorConfig
        {
            Radius = 5f,
            UpdateInterval = 0.15f,
            TargetTypeId = PerceptibleTypes.Default,
            FilterByType = false,
            MemoryDuration = 2f
        };
    }

    public class ProximitySensor : ISensor, IDisposable
    {
        private readonly int _sensorId;
        private readonly Transform _sensorTransform;
        private ProximitySensorConfig _config;

        // Detection results (stable for reading)
        private NativeList<PerceptibleHandle> _detectedHandles;
        private NativeList<DetectionResult> _detectionResults;

        // Staging buffer for deferred mode
        private NativeList<PerceptibleHandle> _stagingHandles;
        private NativeList<DetectionResult> _stagingResults;
        
        // Stimulus memory — persists after target leaves sensor range
        private NativeList<StimulusMemoryEntry> _memoryEntries;

        // Job data (reused each frame)
        private NativeArray<float> _jobProximity;
        private NativeArray<PerceptibleData> _jobTargetData;
        private JobHandle _currentJobHandle;
        private bool _jobScheduled;
        private int _jobTargetCount;
        private bool _hasPendingResults;

        private float _lastUpdateTime;
        private bool _disposed;

        public int SensorId => _sensorId;
        public SensorType Type => SensorType.Proximity;
        public bool IsEnabled { get; set; } = true;
        public float UpdateInterval => _config.UpdateInterval;
        public float LastUpdateTime => _lastUpdateTime;
        public bool HasDetection => (_detectedHandles.IsCreated && _detectedHandles.Length > 0) || (_memoryEntries.IsCreated && _memoryEntries.Length > 0);
        public int DetectedCount => (_detectedHandles.IsCreated ? _detectedHandles.Length : 0) + (_memoryEntries.IsCreated ? _memoryEntries.Length : 0);

        public float Radius => _config.Radius;
        public float3 Position => _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;

        public ProximitySensor(Transform sensorTransform, ProximitySensorConfig config)
        {
            _sensorId = SensorManager.Instance?.GenerateSensorId() ?? 0;
            _sensorTransform = sensorTransform;
            _config = config;

            Initialize();
        }

        public void Initialize()
        {
            _detectedHandles = new NativeList<PerceptibleHandle>(32, Allocator.Persistent);
            _detectionResults = new NativeList<DetectionResult>(32, Allocator.Persistent);
            _stagingHandles = new NativeList<PerceptibleHandle>(32, Allocator.Persistent);
            _stagingResults = new NativeList<DetectionResult>(32, Allocator.Persistent);
            _memoryEntries = new NativeList<StimulusMemoryEntry>(32, Allocator.Persistent);
        }

        public void UpdateSensor(float deltaTime)
        {
            if (_disposed || _sensorTransform == null) return;

            // Complete previous job if any
            CompleteJob();

            var registry = PerceptibleRegistry.Instance;
            if (registry == null || registry.IsDisposed)
            {
                _detectedHandles.Clear();
                _detectionResults.Clear();
                _lastUpdateTime = Time.time;
                return;
            }

            // RebuildData is called once per frame by SensorManager.Update
            int targetCount = registry.GetDataCount();

            if (targetCount == 0)
            {
                _detectedHandles.Clear();
                _detectionResults.Clear();
                _lastUpdateTime = Time.time;
                return;
            }

            // Create a spatially filtered copy for job processing
            _jobTargetData = registry.CreateNativeDataCopyInRange(Position, _config.Radius, Allocator.TempJob);
            _jobTargetCount = _jobTargetData.Length;

            // Allocate job output
            if (!_jobProximity.IsCreated || _jobProximity.Length < _jobTargetCount)
            {
                if (_jobProximity.IsCreated) _jobProximity.Dispose();
                _jobProximity = new NativeArray<float>(_jobTargetCount, Allocator.Persistent);
            }

            // Schedule Burst job for sphere query
            var job = new ProximityQueryJob
            {
                Targets = _jobTargetData,
                Origin = Position,
                Radius = _config.Radius,
                TargetTypeId = _config.TargetTypeId,
                FilterByType = _config.FilterByType,
                Proximity = _jobProximity
            };

            _currentJobHandle = job.Schedule(_jobTargetCount, 64);
            _jobScheduled = true;

            var manager = SensorManager.Instance;
            if (manager != null && manager.UseDeferredJobCompletion)
            {
                manager.AddToBatch(_currentJobHandle);
                _hasPendingResults = true;
            }
            else
            {
                _currentJobHandle.Complete();
                _jobScheduled = false;

                _detectedHandles.Clear();
                _detectionResults.Clear();
                ProcessJobResultsInternal(_jobTargetData, _jobTargetCount, _detectedHandles, _detectionResults);
                MergeMemory();

                if (_jobTargetData.IsCreated)
                {
                    _jobTargetData.Dispose();
                    _jobTargetData = default;
                }
                _jobTargetCount = 0;
            }

            _lastUpdateTime = Time.time;
        }

        public void ProcessJobResults()
        {
            if (!_hasPendingResults) return;
            if (!_jobTargetData.IsCreated || _jobTargetCount == 0)
            {
                _hasPendingResults = false;
                return;
            }

            CompleteJob();

            _stagingHandles.Clear();
            _stagingResults.Clear();
            ProcessJobResultsInternal(_jobTargetData, _jobTargetCount, _stagingHandles, _stagingResults);

            _detectedHandles.Clear();
            _detectionResults.Clear();
            for (int i = 0; i < _stagingHandles.Length; i++)
            {
                _detectedHandles.Add(_stagingHandles[i]);
                _detectionResults.Add(_stagingResults[i]);
            }
            MergeMemory();

            if (_jobTargetData.IsCreated)
            {
                _jobTargetData.Dispose();
                _jobTargetData = default;
            }
            _jobTargetCount = 0;
            _hasPendingResults = false;
        }

        private void ProcessJobResultsInternal(
            NativeArray<PerceptibleData> targets,
            int count,
            NativeList<PerceptibleHandle> outHandles,
            NativeList<DetectionResult> outResults)
        {
            float3 origin = Position;

            for (int i = 0; i < count; i++)
            {
                float proximity = _jobProximity[i];
                if (proximity < 0.01f) continue;

                var target = targets[i];
                float dist = math.distance(origin, target.Position);

                outHandles.Add(target.ToHandle());
                outResults.Add(new DetectionResult
                {
                    Target = target.ToHandle(),
                    Distance = dist,
                    LastKnownPosition = target.Position,
                    DetectionTime = Time.time,
                    Visibility = proximity,
                    SensorType = (int)SensorType.Proximity
                });
            }
        }

        private void MergeMemory()
        {
            if (_config.MemoryDuration <= 0f || !_memoryEntries.IsCreated) return;

            float now = Time.time;
            var refreshed = new NativeArray<bool>(_memoryEntries.Length, Allocator.Temp);

            for (int i = 0; i < _detectionResults.Length; i++)
            {
                var dr = _detectionResults[i];
                int memIdx = FindMemoryIndex(dr.Target);

                if (memIdx >= 0)
                {
                    var entry = _memoryEntries[memIdx];
                    entry.LastDetectedTime = now;
                    entry.LastKnownPosition = dr.LastKnownPosition;
                    entry.PeakVisibility = math.max(entry.PeakVisibility, dr.Visibility);
                    entry.DistanceAtDetection = dr.Distance;
                    _memoryEntries[memIdx] = entry;
                    refreshed[memIdx] = true;
                }
                else
                {
                    _memoryEntries.Add(new StimulusMemoryEntry
                    {
                        Target = dr.Target,
                        LastKnownPosition = dr.LastKnownPosition,
                        LastDetectedTime = now,
                        PeakVisibility = dr.Visibility,
                        SensorType = (int)SensorType.Proximity,
                        DistanceAtDetection = dr.Distance
                    });
                }
            }

            for (int i = _memoryEntries.Length - 1; i >= 0; i--)
            {
                if (i < refreshed.Length && refreshed[i]) continue;

                var entry = _memoryEntries[i];
                float age = now - entry.LastDetectedTime;

                if (age >= _config.MemoryDuration)
                {
                    _memoryEntries.RemoveAtSwapBack(i);
                    continue;
                }

                float visibility = entry.PeakVisibility * (1f - age / _config.MemoryDuration);
                if (visibility <= 0.01f)
                {
                    _memoryEntries.RemoveAtSwapBack(i);
                    continue;
                }

                var memResult = new DetectionResult
                {
                    Target = entry.Target,
                    Distance = entry.DistanceAtDetection,
                    LastKnownPosition = entry.LastKnownPosition,
                    DetectionTime = entry.LastDetectedTime,
                    Visibility = visibility,
                    SensorType = (int)SensorType.Proximity,
                    IsFromMemory = true
                };

                _detectionResults.Add(memResult);
                _detectedHandles.Add(entry.Target);
            }

            if (refreshed.IsCreated) refreshed.Dispose();
        }

        private int FindMemoryIndex(PerceptibleHandle target)
        {
            for (int i = 0; i < _memoryEntries.Length; i++)
            {
                if (_memoryEntries[i].Target == target) return i;
            }
            return -1;
        }

        public int MemoryCount => _memoryEntries.IsCreated ? _memoryEntries.Length : 0;

        private void CompleteJob()
        {
            if (_jobScheduled)
            {
                _currentJobHandle.Complete();
                _jobScheduled = false;
            }
        }

        public void GetDetectedHandles(ref NativeList<PerceptibleHandle> results)
        {
            for (int i = 0; i < _detectedHandles.Length; i++)
            {
                results.Add(_detectedHandles[i]);
            }
        }

        public DetectionResult GetResult(int index)
        {
            return index >= 0 && index < _detectionResults.Length
                ? _detectionResults[index]
                : default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CompleteJob();

            if (_detectedHandles.IsCreated) _detectedHandles.Dispose();
            if (_detectionResults.IsCreated) _detectionResults.Dispose();
            if (_stagingHandles.IsCreated) _stagingHandles.Dispose();
            if (_stagingResults.IsCreated) _stagingResults.Dispose();
            if (_jobProximity.IsCreated) _jobProximity.Dispose();
            if (_jobTargetData.IsCreated) _jobTargetData.Dispose();
            if (_memoryEntries.IsCreated) _memoryEntries.Dispose();
        }
    }
}
