using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.AIPerception.Runtime.Jobs;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct SightSensorConfig
    {
        [Range(0f, 180f)] public float HalfAngle;
        [Range(0f, 200f)] public float MaxDistance;
        [Range(0f, 5f)] public float UpdateInterval;
        public LayerMask ObstacleLayer;
        public bool UseLineOfSight;
        
        [Tooltip("Only detect targets of this type. Use PerceptibleTypes constants (0=Default, 1=Character, etc.)")]
        public int TargetTypeId;
        
        [Tooltip("If enabled, only targets matching TargetTypeId will be detected")]
        public bool FilterByType;
        
        public static SightSensorConfig Default => new SightSensorConfig
        {
            HalfAngle = 60f,
            MaxDistance = 30f,
            UpdateInterval = 0.1f,
            ObstacleLayer = Physics.DefaultRaycastLayers,
            UseLineOfSight = true,
            TargetTypeId = PerceptibleTypes.Default,
            FilterByType = false
        };
    }
    
    public class SightSensor : ISensor, IDisposable
    {
        private readonly int _sensorId;
        private readonly Transform _sensorTransform;
        private SightSensorConfig _config;
        
        // Detection results (stable for reading)
        private NativeList<PerceptibleHandle> _detectedHandles;
        private NativeList<DetectionResult> _detectionResults;
        
        // Staging buffer for deferred mode (write during job processing)
        private NativeList<PerceptibleHandle> _stagingHandles;
        private NativeList<DetectionResult> _stagingResults;
        
        // Job data (reused each frame)
        private NativeArray<int> _jobPassedFilter;
        private NativeArray<PerceptibleData> _jobTargetData;
        private JobHandle _currentJobHandle;
        private bool _jobScheduled;
        private int _jobTargetCount;
        private bool _hasPendingResults;
        
        private float _lastUpdateTime;
        private bool _disposed;
        
        public int SensorId => _sensorId;
        public SensorType Type => SensorType.Sight;
        public bool IsEnabled { get; set; } = true;
        public float UpdateInterval => _config.UpdateInterval;
        public float LastUpdateTime => _lastUpdateTime;
        public bool HasDetection => _detectedHandles.IsCreated && _detectedHandles.Length > 0;
        public int DetectedCount => _detectedHandles.IsCreated ? _detectedHandles.Length : 0;
        
        public float HalfAngle => _config.HalfAngle;
        public float MaxDistance => _config.MaxDistance;
        public float3 Position => _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;
        public float3 Forward => _sensorTransform != null ? (float3)_sensorTransform.forward : new float3(0, 0, 1);
        
        public SightSensor(Transform sensorTransform, SightSensorConfig config)
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
        }
        
        public void UpdateSensor(float deltaTime)
        {
            if (_disposed || _sensorTransform == null) return;
            
            // Complete previous job if any
            CompleteJob();
            
            var registry = PerceptibleRegistry.Instance;
            if (registry == null || registry.IsDisposed)
            {
                // Clear results if registry is gone
                _detectedHandles.Clear();
                _detectionResults.Clear();
                _lastUpdateTime = Time.time;
                return;
            }
            
            registry.RebuildData();
            int targetCount = registry.GetDataCount();
            
            if (targetCount == 0)
            {
                _detectedHandles.Clear();
                _detectionResults.Clear();
                _lastUpdateTime = Time.time;
                return;
            }
            
            // Create a copy for job processing
            _jobTargetData = registry.CreateNativeDataCopy(Allocator.TempJob);
            _jobTargetCount = targetCount;
            
            // Allocate job output
            if (!_jobPassedFilter.IsCreated || _jobPassedFilter.Length < targetCount)
            {
                if (_jobPassedFilter.IsCreated) _jobPassedFilter.Dispose();
                _jobPassedFilter = new NativeArray<int>(targetCount, Allocator.Persistent);
            }
            
            // Schedule Burst job for pre-filtering
            var job = new SightConeQueryJob
            {
                Targets = _jobTargetData,
                Origin = Position,
                Forward = Forward,
                MaxDistanceSq = _config.MaxDistance * _config.MaxDistance,
                CosHalfAngle = math.cos(math.radians(_config.HalfAngle)),
                TargetTypeId = _config.TargetTypeId,
                FilterByType = _config.FilterByType,
                PassedFilter = _jobPassedFilter
            };
            
            _currentJobHandle = job.Schedule(targetCount, 64);
            _jobScheduled = true;
            
            var manager = SensorManager.Instance;
            if (manager != null && manager.UseDeferredJobCompletion)
            {
                // Deferred mode: add to batch, process in LateUpdate
                // Keep previous results visible until new ones are ready
                manager.AddToBatch(_currentJobHandle);
                _hasPendingResults = true;
            }
            else
            {
                // Immediate mode: complete now and process
                _currentJobHandle.Complete();
                _jobScheduled = false;
                
                // Clear and write directly to main buffers
                _detectedHandles.Clear();
                _detectionResults.Clear();
                ProcessJobResultsInternal(_jobTargetData, _jobTargetCount, _detectedHandles, _detectionResults);
                
                // Dispose temp data
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
            
            // Complete job if not already done
            CompleteJob();
            
            // Write to staging buffer first
            _stagingHandles.Clear();
            _stagingResults.Clear();
            ProcessJobResultsInternal(_jobTargetData, _jobTargetCount, _stagingHandles, _stagingResults);
            
            // Swap: copy staging to main (atomic from reader's perspective)
            _detectedHandles.Clear();
            _detectionResults.Clear();
            for (int i = 0; i < _stagingHandles.Length; i++)
            {
                _detectedHandles.Add(_stagingHandles[i]);
                _detectionResults.Add(_stagingResults[i]);
            }
            
            // Dispose temp data
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
            float3 forward = Forward;
            float cosHalfAngle = math.cos(math.radians(_config.HalfAngle));
            
            for (int i = 0; i < count; i++)
            {
                if (_jobPassedFilter[i] == 0) continue;
                
                var target = targets[i];
                
                // LOS check on main thread (requires Physics)
                if (_config.UseLineOfSight)
                {
                    Vector3 rayOrigin = origin;
                    Vector3 rayDir = math.normalize(target.LOSPoint - origin);
                    float rayDist = math.distance(origin, target.LOSPoint);
                    
                    var hits = Physics.RaycastAll(rayOrigin, rayDir, rayDist, _config.ObstacleLayer);
                    bool blocked = false;
                    
                    foreach (var hit in hits)
                    {
                        float distToTarget = math.distance(hit.point, (Vector3)target.LOSPoint);
                        if (distToTarget > target.DetectionRadius + 0.1f)
                        {
                            blocked = true;
                            break;
                        }
                    }
                    
                    if (blocked) continue;
                }
                
                float3 toTarget = target.Position - origin;
                float dist = math.length(toTarget);
                float3 dir = toTarget / dist;
                float dot = math.dot(forward, dir);
                
                outHandles.Add(target.ToHandle());
                outResults.Add(new DetectionResult
                {
                    Target = target.ToHandle(),
                    Distance = dist,
                    LastKnownPosition = target.Position,
                    DetectionTime = Time.time,
                    Visibility = (dot - cosHalfAngle) / (1f - cosHalfAngle),
                    SensorType = 0
                });
            }
        }
        
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
            if (_jobPassedFilter.IsCreated) _jobPassedFilter.Dispose();
            if (_jobTargetData.IsCreated) _jobTargetData.Dispose();
        }
    }
}
