using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.AIPerception.Runtime.Jobs;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct HearingSensorConfig
    {
        [Range(0f, 100f)] public float Radius;
        [Range(0f, 5f)] public float UpdateInterval;
        public int TargetTypeId;
        public bool FilterByType;
        
        [Header("Sound Occlusion")]
        public bool UseOcclusion;
        public LayerMask OcclusionLayer;
        [Range(0f, 1f)] public float OcclusionAttenuation;
        
        public static HearingSensorConfig Default => new HearingSensorConfig
        {
            Radius = 15f,
            UpdateInterval = 0.2f,
            TargetTypeId = PerceptibleTypes.SoundSource,
            FilterByType = false,
            UseOcclusion = true,
            OcclusionLayer = Physics.DefaultRaycastLayers,
            OcclusionAttenuation = 0.5f
        };
    }
    
    public class HearingSensor : ISensor, IDisposable
    {
        private readonly int _sensorId;
        private readonly Transform _sensorTransform;
        private HearingSensorConfig _config;
        
        // Detection results (stable for reading)
        private NativeList<PerceptibleHandle> _detectedHandles;
        private NativeList<DetectionResult> _detectionResults;
        
        // Staging buffer for deferred mode
        private NativeList<PerceptibleHandle> _stagingHandles;
        private NativeList<DetectionResult> _stagingResults;
        
        // Job data (reused each frame)
        private NativeArray<float> _jobAudibility;
        private NativeArray<PerceptibleData> _jobTargetData;
        private JobHandle _currentJobHandle;
        private bool _jobScheduled;
        private int _jobTargetCount;
        private bool _hasPendingResults;
        
        private float _lastUpdateTime;
        private bool _disposed;
        
        public int SensorId => _sensorId;
        public SensorType Type => SensorType.Hearing;
        public bool IsEnabled { get; set; } = true;
        public float UpdateInterval => _config.UpdateInterval;
        public float LastUpdateTime => _lastUpdateTime;
        public bool HasDetection => _detectedHandles.IsCreated && _detectedHandles.Length > 0;
        public int DetectedCount => _detectedHandles.IsCreated ? _detectedHandles.Length : 0;
        
        public float Radius => _config.Radius;
        public float3 Position => _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;
        
        public HearingSensor(Transform sensorTransform, HearingSensorConfig config)
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
            if (!_jobAudibility.IsCreated || _jobAudibility.Length < targetCount)
            {
                if (_jobAudibility.IsCreated) _jobAudibility.Dispose();
                _jobAudibility = new NativeArray<float>(targetCount, Allocator.Persistent);
            }
            
            // Schedule Burst job for sphere query
            var job = new SphereQueryJob
            {
                Targets = _jobTargetData,
                Origin = Position,
                Radius = _config.Radius,
                TargetTypeId = _config.TargetTypeId,
                FilterByType = _config.FilterByType,
                Audibility = _jobAudibility
            };
            
            _currentJobHandle = job.Schedule(targetCount, 64);
            _jobScheduled = true;
            
            var manager = SensorManager.Instance;
            if (manager != null && manager.UseDeferredJobCompletion)
            {
                // Deferred mode: add to batch, process in LateUpdate
                manager.AddToBatch(_currentJobHandle);
                _hasPendingResults = true;
            }
            else
            {
                // Immediate mode: complete now and process
                _currentJobHandle.Complete();
                _jobScheduled = false;
                
                _detectedHandles.Clear();
                _detectionResults.Clear();
                ProcessJobResultsInternal(_jobTargetData, _jobTargetCount, _detectedHandles, _detectionResults);
                
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
            
            // Swap: copy staging to main
            _detectedHandles.Clear();
            _detectionResults.Clear();
            for (int i = 0; i < _stagingHandles.Length; i++)
            {
                _detectedHandles.Add(_stagingHandles[i]);
                _detectionResults.Add(_stagingResults[i]);
            }
            
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
                float audibility = _jobAudibility[i];
                if (audibility < 0.01f) continue;
                
                var target = targets[i];
                
                // Occlusion check on main thread
                if (_config.UseOcclusion)
                {
                    if (Physics.Linecast(origin, target.Position, _config.OcclusionLayer))
                    {
                        audibility *= _config.OcclusionAttenuation;
                    }
                }
                
                if (audibility < 0.01f) continue;
                
                float dist = math.distance(origin, target.Position);
                
                outHandles.Add(target.ToHandle());
                outResults.Add(new DetectionResult
                {
                    Target = target.ToHandle(),
                    Distance = dist,
                    LastKnownPosition = target.Position,
                    DetectionTime = Time.time,
                    Visibility = audibility,
                    SensorType = 1
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
            if (_jobAudibility.IsCreated) _jobAudibility.Dispose();
            if (_jobTargetData.IsCreated) _jobTargetData.Dispose();
        }
    }
}
