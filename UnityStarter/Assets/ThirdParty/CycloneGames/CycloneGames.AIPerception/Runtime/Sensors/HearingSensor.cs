using System;
using CycloneGames.AIPerception.Runtime.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
        [Min(0)] public int MaximumOcclusionChecksPerUpdate;

        [Header("Memory")]
        [Range(0f, 60f)] public float MemoryDuration;

        [Header("Capacity")]
        public PerceptionSensorCapacity Capacity;

        public static HearingSensorConfig Default => new HearingSensorConfig
        {
            Radius = 15f,
            UpdateInterval = 0.2f,
            TargetTypeId = PerceptibleTypes.SoundSource,
            FilterByType = false,
            UseOcclusion = true,
            OcclusionLayer = Physics.DefaultRaycastLayers,
            OcclusionAttenuation = 0.5f,
            MaximumOcclusionChecksPerUpdate = 64,
            MemoryDuration = 5f,
            Capacity = PerceptionSensorCapacity.Default
        };
    }

    /// <summary>
    /// Main-thread-owned continuous-emission hearing sensor. Only perceptibles that explicitly
    /// expose IsSoundSource participate; event-like sounds should use a dedicated product adapter.
    /// </summary>
    public sealed class HearingSensor : ISensor, ISensorManagerOwned
    {
        private readonly int _sensorId;
        private readonly SensorManager _owner;
        private readonly Transform _sensorTransform;
        private PerceptibleHandle _ignoredTarget;
        private HearingSensorConfig _config;
        private PerceptionSensorCapacity _capacity;
        private NativeList<int> _candidateIndices;
        private NativeArray<float> _jobAudibility;
        private NativeArray<PerceptibleData> _queryTargets;
        private SensorResultBuffer _resultBuffer;
        private JobHandle _currentJobHandle;
        private float3 _queryOrigin;
        private double _queryTimestamp;
        private int _queryTargetCount;
        private int _occlusionCursor;
        private bool _jobScheduled;
        private bool _hasPendingResults;
        private bool _initialized;
        private bool _disposed;
        private bool _isEnabled = true;
        private double _lastUpdateTime;

        public HearingSensor(
            Transform sensorTransform,
            HearingSensorConfig config,
            PerceptibleHandle ignoredTarget = default)
            : this(sensorTransform, config, SensorManager.Instance, ignoredTarget)
        {
        }

        public HearingSensor(
            Transform sensorTransform,
            HearingSensorConfig config,
            SensorManager owner,
            PerceptibleHandle ignoredTarget = default)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _sensorId = _owner.GenerateSensorId();
            _sensorTransform = sensorTransform;
            _ignoredTarget = ignoredTarget;
            _config = config;
            Initialize();
        }

        public int SensorId => _sensorId;
        public SensorType Type => SensorType.Hearing;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _owner.EnsureOwnerThread();
                ThrowIfDisposed();
                if (_isEnabled == value)
                {
                    return;
                }

                CompleteAndCommitPending();
                _isEnabled = value;
                if (!value)
                {
                    _resultBuffer.ClearAll();
                    LastUpdateStatus = SensorUpdateStatus.Ready;
                }
            }
        }
        public float UpdateInterval => _config.UpdateInterval;
        public double LastUpdateTime => _lastUpdateTime;
        public float Radius => _config.Radius;
        public float3 Position
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;
            }
        }
        public HearingSensorConfig Config => _config;
        public SensorUpdateStatus LastUpdateStatus { get; private set; }
        public bool HasDetection
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _resultBuffer != null && _resultBuffer.HasResults;
            }
        }

        public int DetectedCount
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _resultBuffer?.ResultCount ?? 0;
            }
        }

        public int MemoryCount
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _resultBuffer?.MemoryCount ?? 0;
            }
        }
        SensorManager ISensorManagerOwned.Owner => _owner;
        bool ISensorManagerOwned.IsDisposed => _disposed;

        public void Initialize()
        {
            _owner.EnsureOwnerThread();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HearingSensor));
            }

            if (_initialized)
            {
                return;
            }

            _capacity = _config.Capacity.Normalize();
            RecreateStorage(in _capacity);
            _initialized = true;
            LastUpdateStatus = SensorUpdateStatus.Ready;
        }

        public void ApplyConfig(in HearingSensorConfig config)
        {
            _owner.EnsureOwnerThread();
            ThrowIfDisposed();
            CompleteAndCommitPending();
            PerceptionSensorCapacity normalized = config.Capacity.Normalize();
            if (!_capacity.HasSameLimits(in normalized))
            {
                RecreateStorage(in normalized);
            }

            _config = config;
        }

        public void SetIgnoredTarget(PerceptibleHandle target)
        {
            _owner.EnsureOwnerThread();
            ThrowIfDisposed();
            CompleteAndCommitPending();
            _ignoredTarget = target;
        }

        public void UpdateSensor(float deltaTime)
        {
            _owner.EnsureOwnerThread();
            ThrowIfDisposed();
            CompleteAndCommitPending();
            double timestamp = Time.timeAsDouble;
            if (_sensorTransform == null || !IsConfigurationValid())
            {
                CommitEmpty(timestamp, SensorUpdateStatus.InvalidConfiguration);
                return;
            }

            PerceptibleRegistry registry = _owner.Registry;
            if (registry.GetDataCount() == 0)
            {
                CommitEmpty(timestamp, SensorUpdateStatus.NoTargets);
                return;
            }

            _queryOrigin = Position;
            _queryTimestamp = timestamp;
            float queryRange = (_config.Radius * registry.MaximumLoudness) + registry.MaximumDetectionRadius;
            if (!math.isfinite(queryRange))
            {
                CommitEmpty(timestamp, SensorUpdateStatus.InvalidConfiguration);
                return;
            }

            if (!registry.CollectCandidateIndices(
                    _queryOrigin,
                    queryRange,
                    ref _candidateIndices,
                    _capacity.MaximumCandidates))
            {
                CommitEmpty(timestamp, SensorUpdateStatus.CandidateCapacityExceeded);
                return;
            }

            _queryTargetCount = _candidateIndices.Length;
            if (_queryTargetCount == 0)
            {
                CommitEmpty(timestamp, SensorUpdateStatus.NoTargets);
                return;
            }

            EnsureJobCapacity(_queryTargetCount);
            _queryTargets = registry.NativeData;
            var job = new SphereQueryJob
            {
                Targets = _queryTargets,
                CandidateIndices = _candidateIndices.AsArray(),
                Origin = _queryOrigin,
                Radius = _config.Radius,
                TargetTypeId = _config.TargetTypeId,
                FilterByType = _config.FilterByType,
                IgnoredTarget = _ignoredTarget,
                Audibility = _jobAudibility
            };

            _currentJobHandle = job.Schedule(_queryTargetCount, 64);
            _jobScheduled = true;
            _hasPendingResults = true;
            if (_owner.UseDeferredJobCompletion && _owner.IsRegistered(this))
            {
                _owner.AddToBatch(_currentJobHandle);
            }
            else
            {
                CompleteAndCommitPending();
            }
        }

        public void ProcessJobResults()
        {
            _owner.EnsureOwnerThread();
            if (!_disposed)
            {
                CompleteAndCommitPending();
            }
        }

        private void CompleteAndCommitPending()
        {
            if (!_hasPendingResults)
            {
                return;
            }

            CompleteJob();
            _resultBuffer.BeginUpdate();
            SensorUpdateStatus status = SensorUpdateStatus.Ready;
            int maximumChecks = _config.MaximumOcclusionChecksPerUpdate;
            int occlusionChecks = 0;
            int nextCursor = _occlusionCursor;
            for (int offset = 0; offset < _queryTargetCount; offset++)
            {
                int i = (_occlusionCursor + offset) % _queryTargetCount;
                float audibility = _jobAudibility[i];
                if (audibility < 0f)
                {
                    if (status == SensorUpdateStatus.Ready)
                    {
                        status = SensorUpdateStatus.CoordinateRangeExceeded;
                    }

                    continue;
                }

                if (audibility < 0.01f)
                {
                    continue;
                }

                PerceptibleData target = _queryTargets[_candidateIndices[i]];
                if (_config.UseOcclusion)
                {
                    if (maximumChecks > 0 && occlusionChecks >= maximumChecks)
                    {
                        status = SensorUpdateStatus.OcclusionBudgetExceeded;
                        nextCursor = i;
                        break;
                    }

                    occlusionChecks++;
                    if (Physics.Linecast(
                            (Vector3)_queryOrigin,
                            (Vector3)target.Position,
                            _config.OcclusionLayer,
                            QueryTriggerInteraction.Ignore))
                    {
                        audibility *= _config.OcclusionAttenuation;
                    }
                }

                if (audibility < 0.01f)
                {
                    continue;
                }

                if (!PerceptionNumerics.TryGetFiniteDistance(
                        in _queryOrigin,
                        in target.Position,
                        out _,
                        out float distance))
                {
                    status = SensorUpdateStatus.CoordinateRangeExceeded;
                    continue;
                }

                if (!_resultBuffer.TryAddLive(new DetectionResult
                    {
                        Target = target.ToHandle(),
                        Distance = distance,
                        LastKnownPosition = target.Position,
                        DetectionTime = _queryTimestamp,
                        Visibility = audibility,
                        SensorType = SensorType.Hearing,
                        IsFromMemory = false
                    }))
                {
                    status = SensorUpdateStatus.ResultCapacityExceeded;
                    nextCursor = i;
                    break;
                }
            }

            if (_queryTargetCount > 0)
            {
                _occlusionCursor = nextCursor % _queryTargetCount;
            }

            LastUpdateStatus = _resultBuffer.Commit(
                _queryTimestamp,
                _config.MemoryDuration,
                SensorType.Hearing,
                status);
            _lastUpdateTime = _queryTimestamp;
            _queryTargetCount = 0;
            _queryTargets = default;
            _hasPendingResults = false;
        }

        private void CommitEmpty(double timestamp, SensorUpdateStatus status)
        {
            _resultBuffer.BeginUpdate();
            LastUpdateStatus = _resultBuffer.Commit(
                timestamp,
                _config.MemoryDuration,
                SensorType.Hearing,
                status);
            _lastUpdateTime = timestamp;
        }

        private bool IsConfigurationValid()
        {
            return math.isfinite(_config.Radius) && _config.Radius >= 0f &&
                   math.isfinite(_config.UpdateInterval) && _config.UpdateInterval >= 0f &&
                   math.isfinite(_config.OcclusionAttenuation) &&
                   _config.OcclusionAttenuation >= 0f && _config.OcclusionAttenuation <= 1f &&
                   _config.MaximumOcclusionChecksPerUpdate >= 0 &&
                   math.isfinite(_config.MemoryDuration) && _config.MemoryDuration >= 0f;
        }

        private void EnsureJobCapacity(int required)
        {
            if (_jobAudibility.IsCreated && _jobAudibility.Length >= required)
            {
                return;
            }

            int current = _jobAudibility.IsCreated ? _jobAudibility.Length : 0;
            int doubled = current <= _capacity.MaximumCandidates / 2
                ? current * 2
                : _capacity.MaximumCandidates;
            int capacity = math.min(_capacity.MaximumCandidates, math.max(required, math.max(1, doubled)));
            var replacement = new NativeArray<float>(capacity, Allocator.Persistent);
            if (_jobAudibility.IsCreated)
            {
                _jobAudibility.Dispose();
            }

            _jobAudibility = replacement;
        }

        private void RecreateStorage(in PerceptionSensorCapacity capacity)
        {
            NativeList<int> candidates = default;
            NativeArray<float> output = default;
            SensorResultBuffer resultBuffer = null;
            try
            {
                candidates = new NativeList<int>(capacity.InitialCandidateCapacity, Allocator.Persistent);
                output = new NativeArray<float>(capacity.InitialCandidateCapacity, Allocator.Persistent);
                resultBuffer = new SensorResultBuffer(in capacity);
            }
            catch
            {
                if (candidates.IsCreated)
                {
                    candidates.Dispose();
                }

                if (output.IsCreated)
                {
                    output.Dispose();
                }

                resultBuffer?.Dispose();
                throw;
            }

            if (_candidateIndices.IsCreated)
            {
                _candidateIndices.Dispose();
            }

            if (_jobAudibility.IsCreated)
            {
                _jobAudibility.Dispose();
            }

            _resultBuffer?.Dispose();
            _capacity = capacity;
            _candidateIndices = candidates;
            _jobAudibility = output;
            _resultBuffer = resultBuffer;
            _occlusionCursor = 0;
            LastUpdateStatus = SensorUpdateStatus.Ready;
        }

        private void CompleteJob()
        {
            if (!_jobScheduled)
            {
                return;
            }

            _currentJobHandle.Complete();
            _currentJobHandle = default;
            _jobScheduled = false;
        }

        public bool TryGetResult(int index, out DetectionResult result)
        {
            _owner.EnsureOwnerThread();
            if (_resultBuffer != null)
            {
                return _resultBuffer.TryGetResult(index, out result);
            }

            result = default;
            return false;
        }

        public DetectionResult GetResult(int index) =>
            TryGetResult(index, out DetectionResult result) ? result : default;

        public void GetDetectionResults(ref NativeList<DetectionResult> results)
        {
            _owner.EnsureOwnerThread();
            _resultBuffer.CopyResultsTo(ref results);
        }

        public void GetDetectedHandles(ref NativeList<PerceptibleHandle> results)
        {
            _owner.EnsureOwnerThread();
            _resultBuffer.CopyHandlesTo(ref results);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HearingSensor));
            }
        }

        public void Dispose()
        {
            _owner.EnsureOwnerThread();
            if (_disposed)
            {
                return;
            }

            _owner.OnOwnedSensorDisposing(this);
            CompleteAndCommitPending();
            _disposed = true;
            LastUpdateStatus = SensorUpdateStatus.Disposed;
            if (_candidateIndices.IsCreated)
            {
                _candidateIndices.Dispose();
            }

            if (_jobAudibility.IsCreated)
            {
                _jobAudibility.Dispose();
            }

            _resultBuffer?.Dispose();
            _resultBuffer = null;
        }
    }
}
