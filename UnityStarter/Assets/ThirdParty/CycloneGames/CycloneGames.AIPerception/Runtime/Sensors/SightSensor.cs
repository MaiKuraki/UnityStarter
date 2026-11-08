using System;
using CycloneGames.AIPerception.Runtime.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
        [Min(0)] public int MaximumLineOfSightChecksPerUpdate;

        [Tooltip("Targets closer than this radius are sensed regardless of view angle. Zero disables point-blank awareness.")]
        [Min(0f)] public float NearClippingRadius;

        [Tooltip("Range used for targets that are already remembered. Zero or below MaxDistance keeps MaxDistance.")]
        [Min(0f)] public float LoseSightRadius;

        [Tooltip("Only detect targets of this stable PerceptibleTypes ID.")]
        public int TargetTypeId;
        public bool FilterByType;

        /// <summary>Team of the observer. Used to resolve the attitude towards each target.</summary>
        [Range(0, PerceptionTeam.NoTeam)] public byte ObserverTeamId;

        /// <summary>Which attitudes this sense reacts to. <see cref="PerceptionAffiliation.All"/> skips the lookup.</summary>
        public PerceptionAffiliation Affiliation;

        [Range(0f, 60f)] public float MemoryDuration;

        public PerceptionSensorCapacity Capacity;

        public static SightSensorConfig Default => new SightSensorConfig
        {
            HalfAngle = 60f,
            MaxDistance = 30f,
            UpdateInterval = 0.1f,
            ObstacleLayer = Physics.DefaultRaycastLayers,
            UseLineOfSight = true,
            MaximumLineOfSightChecksPerUpdate = 64,
            NearClippingRadius = 0f,
            LoseSightRadius = 0f,
            TargetTypeId = PerceptibleTypes.Default,
            FilterByType = false,
            ObserverTeamId = PerceptionTeam.NoTeam,
            Affiliation = PerceptionAffiliation.All,
            MemoryDuration = 3f,
            Capacity = PerceptionSensorCapacity.Default
        };
    }

    /// <summary>
    /// Main-thread-owned sight sensor. Burst performs the cone query; Unity Physics refinement and
    /// result commit run on the owner thread using the pose and timestamp captured at schedule time.
    /// </summary>
    public sealed class SightSensor : ISensor, ISensorManagerOwned, IAIPerceptionSensorMemoryOwner, IPerceptionEventSource
    {
        private readonly int _sensorId;
        private readonly SensorManager _owner;
        private readonly Transform _sensorTransform;
        private readonly SensorMemoryCounters _memoryCounters = new SensorMemoryCounters();
        private PerceptibleHandle _ignoredTarget;
        private SightSensorConfig _config;
        private PerceptionSensorCapacity _capacity;
        private NativeList<int> _candidateIndices;
        private NativeArray<int> _jobPassedFilter;
        private NativeArray<PerceptibleData> _queryTargets;
        private SensorResultBuffer _resultBuffer;
        private JobHandle _currentJobHandle;
        private float3 _queryOrigin;
        private float3 _queryForward;
        private double _queryTimestamp;
        private int _queryTargetCount;
        private int _lineOfSightCursor;
        private bool _jobScheduled;
        private bool _hasPendingResults;
        private bool _initialized;
        private bool _disposed;
        private bool _isEnabled = true;
        private double _lastUpdateTime;

        public SightSensor(
            Transform sensorTransform,
            SightSensorConfig config,
            PerceptibleHandle ignoredTarget = default)
            : this(sensorTransform, config, SensorManager.Instance, ignoredTarget)
        {
        }

        public SightSensor(
            Transform sensorTransform,
            SightSensorConfig config,
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
        public SensorType Type => SensorType.Sight;
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
        public float3 Position
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;
            }
        }
        public float3 Forward
        {
            get
            {
                _owner.EnsureOwnerThread();
                return _sensorTransform != null ? (float3)_sensorTransform.forward : new float3(0f, 0f, 1f);
            }
        }
        public float HalfAngle => _config.HalfAngle;
        public float MaxDistance => _config.MaxDistance;
        public SightSensorConfig Config => _config;
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
                throw new ObjectDisposedException(nameof(SightSensor));
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

        public void ApplyConfig(in SightSensorConfig config)
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
            _memoryCounters.BeginUpdate();
            double timestamp = _owner.Now;
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
            _queryForward = math.normalizesafe(Forward, new float3(0f, 0f, 1f));
            _queryTimestamp = timestamp;
            // The broadphase must cover the widest range any per-target rule can accept, otherwise
            // near-clipping and lose-sight hysteresis would be filtered out before the job runs.
            float effectiveRange = math.max(_config.MaxDistance, _config.NearClippingRadius);
            effectiveRange = math.max(effectiveRange, _config.LoseSightRadius);
            float queryRange = effectiveRange + registry.MaximumDetectionRadius;
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
                _memoryCounters.RecordCandidateCapacityRejected(_capacity.MaximumCandidates);
                CommitEmpty(timestamp, SensorUpdateStatus.CandidateCapacityExceeded);
                return;
            }

            _queryTargetCount = _candidateIndices.Length;
            _memoryCounters.RecordCandidates(_queryTargetCount);
            if (_queryTargetCount == 0)
            {
                CommitEmpty(timestamp, SensorUpdateStatus.NoTargets);
                return;
            }

            EnsureJobCapacity(_queryTargetCount);
            _queryTargets = registry.NativeData;
            bool useLoseSightRadius = _config.LoseSightRadius > _config.MaxDistance;
            if (_config.Affiliation != PerceptionAffiliation.All)
            {
                // The job system rejects an uncreated container, so materialise the table first.
                _owner.EnsureTeamAttitudeTable();
            }

            var job = new SightConeQueryJob
            {
                Targets = _queryTargets,
                CandidateIndices = _candidateIndices.AsArray(),
                Origin = _queryOrigin,
                Forward = _queryForward,
                MaxDistance = _config.MaxDistance,
                CosHalfAngle = math.cos(math.radians(_config.HalfAngle)),
                NearClippingRadius = _config.NearClippingRadius,
                LoseSightRadius = _config.LoseSightRadius,
                UseLoseSightRadius = useLoseSightRadius,
                MemoryLookup = _resultBuffer.MemoryLookup,
                TargetTypeId = _config.TargetTypeId,
                FilterByType = _config.FilterByType,
                ObserverTeamId = _config.ObserverTeamId,
                Affiliation = _config.Affiliation,
                AttitudeTable = _owner.AttitudeTable,
                IgnoredTarget = _ignoredTarget,
                PassedFilter = _jobPassedFilter
            };

            bool deferred = _owner.UseDeferredJobCompletion && _owner.IsRegistered(this);
            _hasPendingResults = true;
            int inlineThreshold = _owner.InlineExecutionThreshold;
            if (inlineThreshold > 0 && _queryTargetCount <= inlineThreshold)
            {
                // Same evaluation, no job: the snapshot cannot change between Update and LateUpdate,
                // so running the body eagerly is equivalent to scheduling it.
                for (int i = 0; i < _queryTargetCount; i++)
                {
                    job.Execute(i);
                }

                _jobScheduled = false;
            }
            else
            {
                _currentJobHandle = job.Schedule(_queryTargetCount, 64);
                _jobScheduled = true;
            }

            if (deferred)
            {
                if (_jobScheduled)
                {
                    _owner.AddToBatch(_currentJobHandle);
                }
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
            float cosine = math.cos(math.radians(_config.HalfAngle));
            float visibilityDenominator = 1f - cosine;
            int maximumChecks = _config.MaximumLineOfSightChecksPerUpdate;
            int lineOfSightChecks = 0;
            int nextCursor = _lineOfSightCursor;

            for (int offset = 0; offset < _queryTargetCount; offset++)
            {
                int candidateIndex = (_lineOfSightCursor + offset) % _queryTargetCount;
                int filterResult = _jobPassedFilter[candidateIndex];
                if (filterResult < 0)
                {
                    if (status == SensorUpdateStatus.Ready)
                    {
                        status = SensorUpdateStatus.CoordinateRangeExceeded;
                    }

                    continue;
                }

                if (filterResult == 0)
                {
                    continue;
                }

                PerceptibleData target = _queryTargets[_candidateIndices[candidateIndex]];
                if (_config.UseLineOfSight)
                {
                    if (maximumChecks > 0 && lineOfSightChecks >= maximumChecks)
                    {
                        status = SensorUpdateStatus.LineOfSightBudgetExceeded;
                        nextCursor = candidateIndex;
                        break;
                    }

                    lineOfSightChecks++;
                    if (!PerceptionNumerics.TryGetFiniteDirectionAndDistance(
                            in _queryOrigin,
                            in target.LOSPoint,
                            out float3 rayDirection,
                            out float rayDistance))
                    {
                        status = SensorUpdateStatus.CoordinateRangeExceeded;
                        continue;
                    }

                    if (rayDistance > 0.0001f && Physics.Raycast(
                            (Vector3)_queryOrigin,
                            (Vector3)rayDirection,
                            rayDistance,
                            _config.ObstacleLayer,
                            QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                }

                if (!PerceptionNumerics.TryGetFiniteDirectionAndDistance(
                        in _queryOrigin,
                        in target.Position,
                        out float3 directionToTarget,
                        out float distance))
                {
                    status = SensorUpdateStatus.CoordinateRangeExceeded;
                    continue;
                }

                float visibility;
                if (distance <= 0.0001f || visibilityDenominator <= 0.000001f)
                {
                    visibility = 1f;
                }
                else
                {
                    float dot = math.dot(_queryForward, directionToTarget);
                    visibility = math.saturate((dot - cosine) / visibilityDenominator);
                }

                if (!_resultBuffer.TryAddLive(new DetectionResult
                    {
                        Target = target.ToHandle(),
                        Distance = distance,
                        LastKnownPosition = target.Position,
                        DetectionTime = _queryTimestamp,
                        Visibility = visibility,
                        SensorType = SensorType.Sight,
                        IsFromMemory = false
                    }))
                {
                    status = SensorUpdateStatus.ResultCapacityExceeded;
                    nextCursor = candidateIndex;
                    break;
                }
            }

            if (_queryTargetCount > 0)
            {
                _lineOfSightCursor = nextCursor % _queryTargetCount;
            }

            LastUpdateStatus = _resultBuffer.Commit(
                _queryTimestamp,
                _config.MemoryDuration,
                SensorType.Sight,
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
                SensorType.Sight,
                status);
            _lastUpdateTime = timestamp;
        }

        private bool IsConfigurationValid()
        {
            return math.isfinite(_config.HalfAngle) && _config.HalfAngle >= 0f && _config.HalfAngle <= 180f &&
                   math.isfinite(_config.MaxDistance) && _config.MaxDistance >= 0f &&
                   math.isfinite(_config.UpdateInterval) && _config.UpdateInterval >= 0f &&
                   math.isfinite(_config.MemoryDuration) && _config.MemoryDuration >= 0f &&
                   math.isfinite(_config.NearClippingRadius) && _config.NearClippingRadius >= 0f &&
                   math.isfinite(_config.LoseSightRadius) && _config.LoseSightRadius >= 0f &&
                   _config.MaximumLineOfSightChecksPerUpdate >= 0;
        }

        private void EnsureJobCapacity(int required)
        {
            if (_jobPassedFilter.IsCreated && _jobPassedFilter.Length >= required)
            {
                return;
            }

            int current = _jobPassedFilter.IsCreated ? _jobPassedFilter.Length : 0;
            int doubled = current <= _capacity.MaximumCandidates / 2
                ? current * 2
                : _capacity.MaximumCandidates;
            int capacity = math.min(_capacity.MaximumCandidates, math.max(required, math.max(1, doubled)));
            var replacement = new NativeArray<int>(capacity, Allocator.Persistent);
            if (_jobPassedFilter.IsCreated)
            {
                _jobPassedFilter.Dispose();
            }

            _jobPassedFilter = replacement;
        }

        private void RecreateStorage(in PerceptionSensorCapacity capacity)
        {
            NativeList<int> candidates = default;
            NativeArray<int> output = default;
            SensorResultBuffer resultBuffer = null;
            try
            {
                candidates = new NativeList<int>(capacity.InitialCandidateCapacity, Allocator.Persistent);
                output = new NativeArray<int>(capacity.InitialCandidateCapacity, Allocator.Persistent);
                resultBuffer = new SensorResultBuffer(in capacity, _memoryCounters);
                resultBuffer.BindSensorId(SensorId);
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

            if (_jobPassedFilter.IsCreated)
            {
                _jobPassedFilter.Dispose();
            }

            _resultBuffer?.Dispose();
            _capacity = capacity;
            _candidateIndices = candidates;
            _jobPassedFilter = output;
            _resultBuffer = resultBuffer;
            _lineOfSightCursor = 0;
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

        int IAIPerceptionSensorMemoryOwner.LastUpdateWorkload => _memoryCounters.LastUpdateWorkload;

        SensorResultBuffer IPerceptionEventSource.EventBuffer => _resultBuffer;

        AIPerceptionSensorMemoryStats IAIPerceptionSensorMemoryOwner.GetMemoryStats()
        {
            _owner.EnsureOwnerThread();
            SensorResultBuffer results = _resultBuffer;
            int candidateCapacity = _candidateIndices.IsCreated ? _candidateIndices.Capacity : 0;
            int outputCapacity = _jobPassedFilter.IsCreated ? _jobPassedFilter.Length : 0;
            return new AIPerceptionSensorMemoryStats(
                SensorType.Sight,
                _candidateIndices.IsCreated ? _candidateIndices.Length : 0,
                candidateCapacity,
                _capacity.MaximumCandidates,
                _memoryCounters.PeakCandidateCount,
                results?.ResultCount ?? 0,
                results?.ResultCapacity ?? 0,
                results?.MaximumResultCount ?? _capacity.MaximumResults,
                _memoryCounters.PeakResultCount,
                results?.MemoryCount ?? 0,
                results?.MemoryCapacity ?? 0,
                results?.MaximumMemoryCount ?? _capacity.MaximumMemoryEntries,
                _memoryCounters.PeakMemoryCount,
                (_candidateIndices.IsCreated ? 1 : 0) + (_jobPassedFilter.IsCreated ? 1 : 0) +
                (results?.NativeBufferCount ?? 0),
                candidateCapacity + outputCapacity + (results?.NativeBufferCapacity ?? 0),
                _memoryCounters.UpdateCount,
                _memoryCounters.LastUpdateWorkload,
                _memoryCounters.PeakUpdateWorkload,
                _memoryCounters.CandidateCapacityRejectedCount,
                _memoryCounters.ResultCapacityRejectedCount,
                _memoryCounters.MemoryEvictionCount);
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
                throw new ObjectDisposedException(nameof(SightSensor));
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

            if (_jobPassedFilter.IsCreated)
            {
                _jobPassedFilter.Dispose();
            }

            _resultBuffer?.Dispose();
            _resultBuffer = null;
        }
    }
}
