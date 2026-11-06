using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct DamageSensorConfig
    {
        [Range(0f, 200f)] public float MaxDistance;
        [Range(0f, 5f)] public float UpdateInterval;

        /// <summary>Stimuli below this strength are ignored. Zero accepts every reported damage.</summary>
        [Min(0f)] public float MinimumStrength;

        [Range(0f, 60f)] public float MemoryDuration;

        /// <summary>Team of the observer. Used to resolve the attitude towards the instigator.</summary>
        [Range(0, PerceptionTeam.NoTeam)] public byte ObserverTeamId;

        /// <summary>Which attitudes this sense reacts to.</summary>
        public PerceptionAffiliation Affiliation;

        public PerceptionSensorCapacity Capacity;

        public static DamageSensorConfig Default => new DamageSensorConfig
        {
            MaxDistance = 25f,
            UpdateInterval = 0f,
            MinimumStrength = 0f,
            MemoryDuration = 3f,
            ObserverTeamId = PerceptionTeam.NoTeam,
            Affiliation = PerceptionAffiliation.All,
            Capacity = PerceptionSensorCapacity.Default
        };
    }

    /// <summary>
    /// Consumes one-shot damage stimuli reported through <see cref="SensorManager.ReportStimulus"/>.
    /// The sensor holds no candidates and schedules no job: a frame usually carries a handful of
    /// stimuli, so the whole path stays on the owner thread.
    /// </summary>
    public sealed class DamageSensor : ISensor, ISensorManagerOwned, IAIPerceptionSensorMemoryOwner
    {
        private readonly int _sensorId;
        private readonly SensorManager _owner;
        private readonly Transform _sensorTransform;
        private readonly PerceptibleHandle _ignoredTarget;
        private readonly SensorMemoryCounters _memoryCounters = new SensorMemoryCounters();

        private DamageSensorConfig _config;
        private PerceptionSensorCapacity _capacity;
        private SensorResultBuffer _resultBuffer;
        private float3 _origin;
        private double _lastUpdateTime;
        private double _queryTimestamp;
        private long _consumedSequence;
        private int _lastWorkload;
        private bool _initialized;
        private bool _disposed;

        public DamageSensor(
            Transform sensorTransform,
            DamageSensorConfig config,
            SensorManager owner,
            PerceptibleHandle ignoredTarget = default)
        {
            _sensorTransform = sensorTransform;
            _config = config;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _sensorId = _owner.GenerateSensorId();
            _ignoredTarget = ignoredTarget;
            IsEnabled = true;
            Initialize();
        }

        public int SensorId => _sensorId;
        public SensorType Type => SensorType.Damage;
        public bool IsEnabled { get; set; }
        public float UpdateInterval => _config.UpdateInterval;
        public double LastUpdateTime => _lastUpdateTime;
        public float3 Position => _sensorTransform != null ? (float3)_sensorTransform.position : float3.zero;
        public SensorUpdateStatus LastUpdateStatus { get; private set; }
        public bool HasDetection => _resultBuffer != null && _resultBuffer.HasResults;
        public int DetectedCount => _resultBuffer?.ResultCount ?? 0;
        public int LastUpdateWorkload => _lastWorkload;
        public SensorManager Owner => _owner;
        public bool IsDisposed => _disposed;

        public DamageSensorConfig Config => _config;

        public void ApplyConfig(in DamageSensorConfig config)
        {
            ThrowIfDisposed();
            _config = config;
            if (_initialized)
            {
                _capacity = config.Capacity.Normalize();
            }
        }

        public void Initialize()
        {
            _owner.EnsureOwnerThread();
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            _capacity = _config.Capacity.Normalize();
            _resultBuffer = new SensorResultBuffer(in _capacity, _memoryCounters);
            // Only stimuli reported after initialization are relevant; replaying history would make
            // the sensor fire on events it was never meant to observe.
            _consumedSequence = _owner.LatestStimulusSequence;
            _initialized = true;
            LastUpdateStatus = SensorUpdateStatus.Ready;
        }

        public void UpdateSensor(float deltaTime)
        {
            _owner.EnsureOwnerThread();
            ThrowIfDisposed();
            _memoryCounters.BeginUpdate();
            double timestamp = _owner.Now;

            if (_sensorTransform == null || !IsConfigurationValid())
            {
                CommitEmpty(timestamp, SensorUpdateStatus.InvalidConfiguration);
                return;
            }

            long newest = _owner.LatestStimulusSequence;
            if (newest <= _consumedSequence)
            {
                CommitEmpty(timestamp, SensorUpdateStatus.NoTargets);
                return;
            }

            _origin = Position;
            _queryTimestamp = timestamp;
            float maxDistance = _config.MaxDistance;
            float maxDistanceSq = maxDistance * maxDistance;
            int workload = 0;

            _resultBuffer.BeginUpdate();
            long oldest = _owner.OldestStimulusSequence;
            long start = _consumedSequence + 1L;
            if (start < oldest)
            {
                // The ring overran between updates; those events are gone and cannot be replayed.
                start = oldest;
            }

            SensorUpdateStatus status = SensorUpdateStatus.Ready;
            for (long sequence = start; sequence <= newest; sequence++)
            {
                if (!_owner.TryGetStimulus(sequence, out PerceptionStimulus stimulus))
                {
                    continue;
                }

                if (stimulus.Kind != PerceptionStimulusKind.Damage)
                {
                    continue;
                }

                workload++;
                if (stimulus.Strength < _config.MinimumStrength)
                {
                    continue;
                }

                if (stimulus.Instigator == _ignoredTarget)
                {
                    continue;
                }

                if (_config.Affiliation != PerceptionAffiliation.All)
                {
                    IPerceptible instigator = _owner.Registry.Get(stimulus.Instigator);
                    int teamId = instigator is IPerceptionTeamProvider provider
                        ? provider.TeamId
                        : PerceptionTeam.NoTeam;
                    PerceptionTeamAttitude attitude = _owner.GetTeamAttitude(_config.ObserverTeamId, teamId);
                    if (((int)_config.Affiliation & (1 << (int)attitude)) == 0)
                    {
                        continue;
                    }
                }

                if (!PerceptionNumerics.TryGetFiniteDistance(
                        in _origin,
                        in stimulus.Position,
                        out _,
                        out float distance))
                {
                    status = SensorUpdateStatus.CoordinateRangeExceeded;
                    continue;
                }

                if (distance > maxDistance)
                {
                    continue;
                }

                float falloff = maxDistanceSq <= 0f ? 1f : 1f - (distance / maxDistance);
                float visibility = math.saturate(falloff);
                if (visibility <= 0.01f)
                {
                    continue;
                }

                if (!_resultBuffer.TryAddLive(new DetectionResult
                    {
                        Target = stimulus.Instigator,
                        Distance = distance,
                        LastKnownPosition = stimulus.Position,
                        DetectionTime = stimulus.Timestamp,
                        Visibility = visibility,
                        SensorType = SensorType.Damage,
                        IsFromMemory = false
                    }))
                {
                    status = SensorUpdateStatus.ResultCapacityExceeded;
                    break;
                }
            }

            _consumedSequence = newest;
            _lastWorkload = workload;
            LastUpdateStatus = _resultBuffer.Commit(
                timestamp,
                _config.MemoryDuration,
                SensorType.Damage,
                status);
            _lastUpdateTime = timestamp;
        }

        public void ProcessJobResults()
        {
            // No job is scheduled; results are committed inside UpdateSensor.
        }

        public bool TryGetResult(int index, out DetectionResult result)
        {
            if (_resultBuffer == null)
            {
                result = default;
                return false;
            }

            return _resultBuffer.TryGetResult(index, out result);
        }

        public void GetDetectionResults(ref NativeList<DetectionResult> results)
        {
            ThrowIfDisposed();
            _resultBuffer?.CopyResultsTo(ref results);
        }

        public void GetDetectedHandles(ref NativeList<PerceptibleHandle> results)
        {
            ThrowIfDisposed();
            _resultBuffer?.CopyHandlesTo(ref results);
        }

        AIPerceptionSensorMemoryStats IAIPerceptionSensorMemoryOwner.GetMemoryStats()
        {
            _owner.EnsureOwnerThread();
            SensorResultBuffer results = _resultBuffer;
            return new AIPerceptionSensorMemoryStats(
                SensorType.Damage,
                0,
                0,
                0,
                0,
                results?.ResultCount ?? 0,
                results?.ResultCapacity ?? 0,
                results?.MaximumResultCount ?? _capacity.MaximumResults,
                _memoryCounters.PeakResultCount,
                results?.MemoryCount ?? 0,
                results?.MemoryCapacity ?? 0,
                results?.MaximumMemoryCount ?? _capacity.MaximumMemoryEntries,
                _memoryCounters.PeakMemoryCount,
                results?.NativeBufferCount ?? 0,
                results?.NativeBufferCapacity ?? 0,
                _memoryCounters.UpdateCount,
                _memoryCounters.LastUpdateWorkload,
                _memoryCounters.PeakUpdateWorkload,
                _memoryCounters.CandidateCapacityRejectedCount,
                _memoryCounters.ResultCapacityRejectedCount,
                _memoryCounters.MemoryEvictionCount);
        }

        private void CommitEmpty(double timestamp, SensorUpdateStatus status)
        {
            _resultBuffer.BeginUpdate();
            LastUpdateStatus = _resultBuffer.Commit(
                timestamp,
                _config.MemoryDuration,
                SensorType.Damage,
                status);
            _lastUpdateTime = timestamp;
            _lastWorkload = 0;
        }

        private bool IsConfigurationValid()
        {
            return math.isfinite(_config.MaxDistance) &&
                   _config.MaxDistance >= 0f &&
                   math.isfinite(_config.UpdateInterval) &&
                   _config.UpdateInterval >= 0f &&
                   math.isfinite(_config.MinimumStrength) &&
                   _config.MinimumStrength >= 0f &&
                   math.isfinite(_config.MemoryDuration) &&
                   _config.MemoryDuration >= 0f;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DamageSensor));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _resultBuffer?.Dispose();
            _resultBuffer = null;
        }
    }
}
