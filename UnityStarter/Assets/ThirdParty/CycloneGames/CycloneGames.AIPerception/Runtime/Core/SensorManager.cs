using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    [Serializable]
    public struct SensorLODLevel
    {
        [Tooltip("Maximum distance in metres for this update-frequency tier.")]
        public float Distance;

        [Tooltip("Update frequency multiplier. 0.5 means half frequency (twice the interval).")]
        [Range(0.05f, 1f)]
        public float FrequencyMultiplier;

        public static readonly SensorLODLevel[] DefaultLevels =
        {
            new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1f },
            new SensorLODLevel { Distance = 80f, FrequencyMultiplier = 0.5f },
            new SensorLODLevel { Distance = 200f, FrequencyMultiplier = 0.25f }
        };
    }

    /// <summary>
    /// Main-thread update owner for all sensors in one Unity world. Worker jobs only borrow the
    /// immutable registry snapshot and sensor-owned output buffers between Update and LateUpdate.
    /// </summary>
    public sealed class SensorManager : IDisposable
    {
        private const int InitialCapacity = 64;

        public const int DefaultMaximumSensorCount = 4_096;
        public const int HardMaximumSensorCount = 65_536;
        public const int HardMaximumLODLevelCount = 64;
        public const int HardMaximumInlineExecutionCount = 4_096;
        public const int DefaultInlineExecutionThreshold = 16;

        private static SensorManager _instance;
        private static bool _isQuitting;

        private readonly int _ownerThreadId;
        private readonly PerceptibleRegistry _registry;
        private readonly List<ISensor> _sensors;
        private readonly Dictionary<int, ISensor> _sensorLookup;
        private readonly int _maximumSensorCount;
        private NativeArray<byte> _attitudeTable;
        private NativeArray<byte> _emptyAttitudeTable;
        private readonly PerceptionStimulus[] _stimuli;
        private int _stimulusCount;
        private long _nextStimulusSequence = 1L;
        private readonly IPerceptionClock _clock;
        private JobHandle _batchedJobHandle;
        private Transform _lodReference;
        private SensorLODLevel[] _lodLevels;
        private int _nextSensorId;
        private int _peakSensorCount;
        private int _lastUpdateWorkload;
        private int _peakUpdateWorkload;
        private long _rejectedSensorRegistrationCount;
        private bool _hasScheduledJobs;
        private bool _lodEnabled;
        private bool _useDeferredJobCompletion;
        private bool _isIteratingSensors;
        private bool _isProcessingResults;
        private int _inlineExecutionThreshold = DefaultInlineExecutionThreshold;
        private readonly List<IPerceptionEventListener> _listeners = new List<IPerceptionEventListener>(4);
        private bool _isDispatchingEvents;

        public static SensorManager Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                return _instance ??= new SensorManager();
            }
        }

        public static bool HasInstance => _instance != null && !_instance.IsDisposed;
        internal static SensorManager ExistingInstance => HasInstance ? _instance : null;

        public SensorManager()
            : this(PerceptibleRegistry.Instance, DefaultMaximumSensorCount)
        {
        }

        public SensorManager(PerceptibleRegistry registry)
            : this(registry, DefaultMaximumSensorCount)
        {
        }

        public SensorManager(PerceptibleRegistry registry, int maximumSensorCount)
            : this(registry, maximumSensorCount, null)
        {
        }

        public SensorManager(PerceptibleRegistry registry, int maximumSensorCount, IPerceptionClock clock)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (maximumSensorCount <= 0 || maximumSensorCount > HardMaximumSensorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSensorCount));
            }

            _clock = clock ?? UnityPerceptionClock.Instance;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _maximumSensorCount = maximumSensorCount;
            int initialCapacity = Math.Min(InitialCapacity, maximumSensorCount);
            _sensors = new List<ISensor>(initialCapacity);
            _sensorLookup = new Dictionary<int, ISensor>(initialCapacity);
            // A scheduled job must carry created containers, so jobs that never consult attitudes get
            // this single-byte placeholder instead of an uncreated array.
            _emptyAttitudeTable = new NativeArray<byte>(1, Allocator.Persistent);
            _stimuli = new PerceptionStimulus[DefaultStimulusCapacity];
            _registry.AttachSensorManager(this);
        }

        /// <summary>
        /// Attitude table shared by every sensor of this manager. Allocated on first write so a world
        /// that never uses teams pays nothing; until then every pair resolves to
        /// <see cref="PerceptionTeamAttitude.Neutral"/>.
        /// </summary>
        public bool HasTeamAttitudeTable => _attitudeTable.IsCreated;

        /// <summary>Team attitude table for jobs. Check <see cref="HasTeamAttitudeTable"/> before indexing.</summary>
        internal NativeArray<byte> AttitudeTable =>
            _attitudeTable.IsCreated ? _attitudeTable : _emptyAttitudeTable;

        /// <summary>Sets how <paramref name="observerTeam"/> regards <paramref name="targetTeam"/>.</summary>
        public void SetTeamAttitude(int observerTeam, int targetTeam, PerceptionTeamAttitude attitude)
        {
            EnsureOwnerThread();
            if (observerTeam < 0 || observerTeam >= PerceptionTeam.TeamCount)
            {
                throw new ArgumentOutOfRangeException(nameof(observerTeam));
            }

            if (targetTeam < 0 || targetTeam >= PerceptionTeam.TeamCount)
            {
                throw new ArgumentOutOfRangeException(nameof(targetTeam));
            }

            EnsureAttitudeTable();
            _attitudeTable[PerceptionTeam.AttitudeIndex(observerTeam, targetTeam)] = (byte)attitude;
        }

        /// <summary>Sets the attitude in both directions. Use when the relation is mutual.</summary>
        public void SetTeamAttitudeSymmetric(int teamA, int teamB, PerceptionTeamAttitude attitude)
        {
            SetTeamAttitude(teamA, teamB, attitude);
            SetTeamAttitude(teamB, teamA, attitude);
        }

        /// <summary>Marks every team as friendly with itself, which is the usual baseline policy.</summary>
        public void SetSameTeamFriendly()
        {
            EnsureOwnerThread();
            EnsureAttitudeTable();
            for (int team = 0; team < PerceptionTeam.TeamCount; team++)
            {
                _attitudeTable[PerceptionTeam.AttitudeIndex(team, team)] = (byte)PerceptionTeamAttitude.Friendly;
            }
        }

        /// <summary>
        /// Replaces the whole table from a flat 256x256 buffer laid out as
        /// <c>observer * 256 + target</c>. One validation instead of 65536 per-cell calls.
        /// </summary>
        public void CopyTeamAttitudes(NativeArray<byte> source)
        {
            EnsureOwnerThread();
            if (!source.IsCreated || source.Length != PerceptionTeam.AttitudeTableSize)
            {
                throw new ArgumentException("Attitude table must be 256x256 bytes.", nameof(source));
            }

            EnsureAttitudeTable();
            NativeArray<byte>.Copy(source, _attitudeTable, source.Length);
        }

        /// <summary>Copies the current table into a managed buffer, for tooling and serialization.</summary>
        public void CopyTeamAttitudesTo(byte[] destination)
        {
            EnsureOwnerThread();
            if (destination == null || destination.Length != PerceptionTeam.AttitudeTableSize)
            {
                throw new ArgumentException("Attitude table must be 256x256 bytes.", nameof(destination));
            }

            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = _attitudeTable.IsCreated
                    ? _attitudeTable[i]
                    : (byte)PerceptionTeamAttitude.Neutral;
            }
        }

        /// <summary>Resets every pair to <see cref="PerceptionTeamAttitude.Neutral"/>.</summary>
        public void ClearTeamAttitudes()
        {
            EnsureOwnerThread();
            if (!_attitudeTable.IsCreated)
            {
                return;
            }

            for (int i = 0; i < _attitudeTable.Length; i++)
            {
                _attitudeTable[i] = (byte)PerceptionTeamAttitude.Neutral;
            }
        }

        /// <summary>Fills the whole table with one attitude, e.g. a free-for-all world.</summary>
        public void FillTeamAttitude(PerceptionTeamAttitude attitude)
        {
            EnsureOwnerThread();
            EnsureAttitudeTable();
            for (int i = 0; i < _attitudeTable.Length; i++)
            {
                _attitudeTable[i] = (byte)attitude;
            }
        }

        public PerceptionTeamAttitude GetTeamAttitude(int observerTeam, int targetTeam)
        {
            EnsureOwnerThread();
            if (!_attitudeTable.IsCreated)
            {
                return PerceptionTeamAttitude.Neutral;
            }

            if (observerTeam < 0 || observerTeam >= PerceptionTeam.TeamCount ||
                targetTeam < 0 || targetTeam >= PerceptionTeam.TeamCount)
            {
                return PerceptionTeamAttitude.Neutral;
            }

            return (PerceptionTeamAttitude)_attitudeTable[PerceptionTeam.AttitudeIndex(observerTeam, targetTeam)];
        }

        /// <summary>
        /// Materialises the attitude table. Sensors call this before scheduling a job so the job never
        /// carries an uncreated container, which the job system rejects.
        /// </summary>
        internal void EnsureTeamAttitudeTable()
        {
            EnsureAttitudeTable();
        }

        private void EnsureAttitudeTable()
        {
            if (_attitudeTable.IsCreated)
            {
                return;
            }

            _attitudeTable = new NativeArray<byte>(PerceptionTeam.AttitudeTableSize, Allocator.Persistent);
            for (int i = 0; i < _attitudeTable.Length; i++)
            {
                _attitudeTable[i] = (byte)PerceptionTeamAttitude.Neutral;
            }
        }

        /// <summary>Fixed ring size for reported stimuli. Must stay a power of two.</summary>
        private const int DefaultStimulusCapacity = 256;

        private const int StimulusIndexMask = DefaultStimulusCapacity - 1;

        public int StimulusCapacity => _stimuli.Length;

        /// <summary>Number of resident stimuli, bounded by <see cref="StimulusCapacity"/>.</summary>
        public int StimulusCount => _stimulusCount;

        /// <summary>Sequence of the newest reported stimulus, or 0 when none was reported yet.</summary>
        public long LatestStimulusSequence => _nextStimulusSequence - 1L;

        /// <summary>Sequence of the oldest resident stimulus, or <see cref="LatestStimulusSequence"/> when empty.</summary>
        public long OldestStimulusSequence => _nextStimulusSequence - _stimulusCount;

        /// <summary>
        /// Reports a one-shot stimulus. The manager stamps the timestamp and sequence so every sensor
        /// observes the same ordering regardless of which one consumes it first. When the ring is full
        /// the oldest entry is dropped; a sensor that has not consumed it yet will miss that event.
        /// </summary>
        /// <summary>
        /// Registers a listener for stimulus transitions. Listeners are called on the owner thread,
        /// once per event, in registration order.
        /// </summary>
        /// <remarks>
        /// Must not be called from inside <see cref="IPerceptionEventListener.OnPerceptionEvent"/>.
        /// </remarks>
        public void AddListener(IPerceptionEventListener listener)
        {
            EnsureOwnerThread();
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (_isDispatchingEvents)
            {
                throw new InvalidOperationException("Listeners cannot be added while events are being dispatched.");
            }

            if (!_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <inheritdoc cref="AddListener"/>
        public void RemoveListener(IPerceptionEventListener listener)
        {
            EnsureOwnerThread();
            if (_isDispatchingEvents)
            {
                throw new InvalidOperationException("Listeners cannot be removed while events are being dispatched.");
            }

            _listeners.Remove(listener);
        }

        /// <summary>
        /// Raises one transition immediately. Custom sensors call this when they do not use the
        /// built-in stimulus memory.
        /// </summary>
        public void PublishEvent(in PerceptionEvent perceptionEvent)
        {
            EnsureOwnerThread();
            Dispatch(in perceptionEvent);
        }

        private void Dispatch(in PerceptionEvent perceptionEvent)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            _isDispatchingEvents = true;
            try
            {
                for (int i = 0; i < _listeners.Count; i++)
                {
                    _listeners[i].OnPerceptionEvent(in perceptionEvent);
                }
            }
            finally
            {
                _isDispatchingEvents = false;
            }
        }

        private void SetEventTracking(ISensor sensor)
        {
            // Built-in sensors create their storage lazily, so the buffer can be absent on the first update.
            if (sensor is IPerceptionEventSource source)
            {
                SensorResultBuffer buffer = source.EventBuffer;
                if (buffer != null)
                {
                    buffer.EnableEventTracking(_listeners.Count > 0);
                }
            }
        }

        private void DrainSensorEvents(ISensor sensor)
        {
            if (!(sensor is IPerceptionEventSource source))
            {
                return;
            }

            SensorResultBuffer buffer = source.EventBuffer;
            if (buffer == null)
            {
                return;
            }

            int count = buffer.PendingEventCount;
            if (count == 0)
            {
                return;
            }

            if (_listeners.Count == 0)
            {
                buffer.ClearPendingEvents();
                return;
            }

            _isDispatchingEvents = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    PerceptionEvent perceptionEvent = buffer.GetPendingEvent(i);
                    for (int listenerIndex = 0; listenerIndex < _listeners.Count; listenerIndex++)
                    {
                        _listeners[listenerIndex].OnPerceptionEvent(in perceptionEvent);
                    }
                }
            }
            finally
            {
                _isDispatchingEvents = false;
                buffer.ClearPendingEvents();
            }
        }

        public long ReportStimulus(
            PerceptibleHandle instigator,
            float3 position,
            float strength,
            PerceptionStimulusKind kind = PerceptionStimulusKind.Damage)
        {
            EnsureOwnerThread();
            long sequence = _nextStimulusSequence++;
            int index = (int)((sequence - 1L) & StimulusIndexMask);
            _stimuli[index] = new PerceptionStimulus
            {
                Instigator = instigator,
                Position = position,
                Strength = strength,
                Kind = kind,
                Timestamp = Now,
                Sequence = sequence
            };

            if (_stimulusCount < _stimuli.Length)
            {
                _stimulusCount++;
            }

            return sequence;
        }

        /// <summary>Reads a resident stimulus by sequence. Returns false when it has already been evicted.</summary>
        public bool TryGetStimulus(long sequence, out PerceptionStimulus stimulus)
        {
            if (sequence < OldestStimulusSequence || sequence >= _nextStimulusSequence)
            {
                stimulus = default;
                return false;
            }

            stimulus = _stimuli[(int)((sequence - 1L) & StimulusIndexMask)];
            return true;
        }

        /// <summary>Time source used for interval gating and stimulus aging.</summary>
        public IPerceptionClock Clock => _clock;

        /// <summary>Current owner-thread timestamp sampled from <see cref="Clock"/>.</summary>
        public double Now => _clock.NowSeconds;

        /// <summary>
        /// Candidate count at or below which a sensor executes its query inline instead of paying
        /// job scheduling and completion overhead. Zero disables inline execution.
        /// </summary>
        /// <remarks>
        /// The default sits at the measured crossover: at one candidate an inline query costs about
        /// 7 us against 17 us for a scheduled job, while at 37 candidates the Burst-compiled job body
        /// wins because inline code pays managed math and container-check overhead. Raise this value
        /// only when Burst compilation is unavailable.
        /// </remarks>
        public int InlineExecutionThreshold
        {
            get
            {
                EnsureOwnerThread();
                return _inlineExecutionThreshold;
            }
            set
            {
                EnsureOwnerThread();
                if (value < 0 || value > HardMaximumInlineExecutionCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _inlineExecutionThreshold = value;
            }
        }

        public bool UseDeferredJobCompletion
        {
            get
            {
                EnsureOwnerThread();
                return _useDeferredJobCompletion;
            }
            set
            {
                EnsureOwnerThread();
                EnsureNotIteratingSensors();
                if (_useDeferredJobCompletion && !value)
                {
                    CompleteAndProcessPendingJobs();
                }

                _useDeferredJobCompletion = value;
            }
        }

        public int SensorCount
        {
            get
            {
                EnsureOwnerThread();
                return _sensors.Count;
            }
        }
        public bool IsDisposed { get; private set; }
        public int MaximumSensorCount => _maximumSensorCount;
        internal PerceptibleRegistry Registry => _registry;

        public bool ConfigureLOD(Transform reference, SensorLODLevel[] levels)
        {
            EnsureOwnerThread();
            EnsureNotIteratingSensors();
            if (reference == null || levels == null || levels.Length == 0)
            {
                _lodReference = null;
                _lodLevels = null;
                _lodEnabled = false;
                return true;
            }

            if (levels.Length > HardMaximumLODLevelCount)
            {
                return false;
            }

            float previousDistance = -1f;
            for (int i = 0; i < levels.Length; i++)
            {
                SensorLODLevel level = levels[i];
                if (!math.isfinite(level.Distance) || level.Distance <= previousDistance ||
                    !math.isfinite(level.FrequencyMultiplier) ||
                    level.FrequencyMultiplier <= 0f || level.FrequencyMultiplier > 1f)
                {
                    _lodReference = null;
                    _lodLevels = null;
                    _lodEnabled = false;
                    return false;
                }

                previousDistance = level.Distance;
            }

            if (_lodLevels == null || _lodLevels.Length != levels.Length)
            {
                _lodLevels = new SensorLODLevel[levels.Length];
            }

            Array.Copy(levels, _lodLevels, levels.Length);
            _lodReference = reference;
            _lodEnabled = true;
            return true;
        }

        public int GenerateSensorId()
        {
            EnsureOwnerThread();
            EnsureNotIteratingSensors();
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(SensorManager));
            }

            for (int attempt = 0; attempt <= _sensorLookup.Count; attempt++)
            {
                int id = unchecked(++_nextSensorId);
                if (id <= 0)
                {
                    _nextSensorId = 1;
                    id = 1;
                }

                if (!_sensorLookup.ContainsKey(id))
                {
                    return id;
                }
            }

            throw new InvalidOperationException("No free positive sensor ID is available.");
        }

        /// <summary>
        /// Registers a sensor without exceeding the configured owner capacity. Contract misuse
        /// throws; capacity exhaustion is reported as false.
        /// </summary>
        public bool TryRegister(ISensor sensor)
        {
            EnsureOwnerThread();
            EnsureNotIteratingSensors();
            if (sensor == null || IsDisposed)
            {
                return false;
            }

            if (_sensorLookup.TryGetValue(sensor.SensorId, out ISensor existing))
            {
                if (ReferenceEquals(sensor, existing))
                {
                    return true;
                }

                throw new InvalidOperationException($"Sensor ID {sensor.SensorId} is already registered.");
            }

            if (sensor is ISensorManagerOwned owned && !ReferenceEquals(owned.Owner, this))
            {
                throw new InvalidOperationException("A built-in sensor must be registered with its construction owner.");
            }

            if (sensor is ISensorManagerOwned ownedSensor && ownedSensor.IsDisposed)
            {
                throw new ObjectDisposedException(sensor.GetType().Name);
            }

            if (_sensors.Count >= _maximumSensorCount)
            {
                _rejectedSensorRegistrationCount = SaturatingIncrement(_rejectedSensorRegistrationCount);
                return false;
            }

            _sensors.Add(sensor);
            _sensorLookup.Add(sensor.SensorId, sensor);
            _peakSensorCount = Math.Max(_peakSensorCount, _sensors.Count);
            return true;
        }

        public void Unregister(ISensor sensor)
        {
            EnsureOwnerThread();
            EnsureNotIteratingSensors();
            if (sensor == null || !_sensorLookup.TryGetValue(sensor.SensorId, out ISensor registered) ||
                !ReferenceEquals(sensor, registered))
            {
                return;
            }

            CompleteBatchedJobs();
            _isIteratingSensors = true;
            _isProcessingResults = true;
            try
            {
                sensor.ProcessJobResults();
            }
            finally
            {
                _isProcessingResults = false;
                _isIteratingSensors = false;
            }

            _sensorLookup.Remove(sensor.SensorId);
            _sensors.Remove(sensor);
        }

        public ISensor GetSensor(int sensorId)
        {
            EnsureOwnerThread();
            _sensorLookup.TryGetValue(sensorId, out ISensor sensor);
            return sensor;
        }

        public void Update(float deltaTime)
        {
            EnsureOwnerThread();
            if (IsDisposed)
            {
                return;
            }

            CompleteAndProcessPendingJobs();
            _registry.RebuildDataForSensorManager(this);
            _lastUpdateWorkload = 0;
            int count = _sensors.Count;
            if (count == 0)
            {
                return;
            }

            double currentTime = Now;
            int updateWorkload = 0;
            _isIteratingSensors = true;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    ISensor sensor = _sensors[index];
                    if (sensor == null)
                    {
                        continue;
                    }

                    if (sensor.IsEnabled)
                    {
                        SetEventTracking(sensor);
                        float frequencyMultiplier = GetLODFrequencyMultiplier(sensor.Position);
                        double effectiveInterval = sensor.UpdateInterval <= 0f
                            ? 0d
                            : sensor.UpdateInterval / frequencyMultiplier;
                        if (currentTime - sensor.LastUpdateTime >= effectiveInterval)
                        {
                            sensor.UpdateSensor(deltaTime);
                            DrainSensorEvents(sensor);
                            if (sensor is IAIPerceptionSensorMemoryOwner memoryOwner)
                            {
                                updateWorkload = SaturatingAdd(updateWorkload, memoryOwner.LastUpdateWorkload);
                            }
                        }
                    }
                }
            }
            finally
            {
                _isIteratingSensors = false;
                _lastUpdateWorkload = updateWorkload;
                _peakUpdateWorkload = Math.Max(_peakUpdateWorkload, updateWorkload);
            }
        }

        /// <summary>Returns an allocation-free aggregate of manager- and sensor-owned storage.</summary>
        public AIPerceptionMemoryStats GetMemoryStats()
        {
            EnsureOwnerThread();
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(SensorManager));
            }

            int builtInSensorCount = 0;
            int customSensorCount = 0;
            int candidateCount = 0;
            int candidateCapacity = 0;
            int maximumCandidateCount = 0;
            int peakCandidateCount = 0;
            int resultCount = 0;
            int resultCapacity = 0;
            int maximumResultCount = 0;
            int peakResultCount = 0;
            int memoryCount = 0;
            int memoryCapacity = 0;
            int maximumMemoryCount = 0;
            int peakMemoryCount = 0;
            int nativeBufferCount = 0;
            int nativeBufferCapacity = 0;
            long sensorUpdateCount = 0L;
            long candidateCapacityRejectedCount = 0L;
            long resultCapacityRejectedCount = 0L;
            long memoryEvictionCount = 0L;

            for (int i = 0; i < _sensors.Count; i++)
            {
                if (!(_sensors[i] is IAIPerceptionSensorMemoryOwner owner))
                {
                    customSensorCount++;
                    continue;
                }

                builtInSensorCount++;
                AIPerceptionSensorMemoryStats sensor = owner.GetMemoryStats();
                candidateCount = SaturatingAdd(candidateCount, sensor.CandidateCount);
                candidateCapacity = SaturatingAdd(candidateCapacity, sensor.CandidateCapacity);
                maximumCandidateCount = SaturatingAdd(maximumCandidateCount, sensor.MaximumCandidateCount);
                peakCandidateCount = SaturatingAdd(peakCandidateCount, sensor.PeakCandidateCount);
                resultCount = SaturatingAdd(resultCount, sensor.ResultCount);
                resultCapacity = SaturatingAdd(resultCapacity, sensor.ResultCapacity);
                maximumResultCount = SaturatingAdd(maximumResultCount, sensor.MaximumResultCount);
                peakResultCount = SaturatingAdd(peakResultCount, sensor.PeakResultCount);
                memoryCount = SaturatingAdd(memoryCount, sensor.MemoryCount);
                memoryCapacity = SaturatingAdd(memoryCapacity, sensor.MemoryCapacity);
                maximumMemoryCount = SaturatingAdd(maximumMemoryCount, sensor.MaximumMemoryCount);
                peakMemoryCount = SaturatingAdd(peakMemoryCount, sensor.PeakMemoryCount);
                nativeBufferCount = SaturatingAdd(nativeBufferCount, sensor.NativeBufferCount);
                nativeBufferCapacity = SaturatingAdd(nativeBufferCapacity, sensor.NativeBufferCapacity);
                sensorUpdateCount = SaturatingAdd(sensorUpdateCount, sensor.UpdateCount);
                candidateCapacityRejectedCount = SaturatingAdd(
                    candidateCapacityRejectedCount,
                    sensor.CandidateCapacityRejectedCount);
                resultCapacityRejectedCount = SaturatingAdd(
                    resultCapacityRejectedCount,
                    sensor.ResultCapacityRejectedCount);
                memoryEvictionCount = SaturatingAdd(memoryEvictionCount, sensor.MemoryEvictionCount);
            }

            return new AIPerceptionMemoryStats(
                _registry.GetMemoryStats(),
                _sensors.Count,
                _maximumSensorCount,
                _peakSensorCount,
                _rejectedSensorRegistrationCount,
                _lodLevels?.Length ?? 0,
                builtInSensorCount,
                customSensorCount,
                candidateCount,
                candidateCapacity,
                maximumCandidateCount,
                peakCandidateCount,
                resultCount,
                resultCapacity,
                maximumResultCount,
                peakResultCount,
                memoryCount,
                memoryCapacity,
                maximumMemoryCount,
                peakMemoryCount,
                nativeBufferCount,
                nativeBufferCapacity,
                sensorUpdateCount,
                _lastUpdateWorkload,
                _peakUpdateWorkload,
                candidateCapacityRejectedCount,
                resultCapacityRejectedCount,
                memoryEvictionCount);
        }

        public void LateUpdate()
        {
            EnsureOwnerThread();
            if (!IsDisposed)
            {
                CompleteAndProcessPendingJobs();
            }
        }

        public void AddToBatch(JobHandle handle)
        {
            EnsureOwnerThread();
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(SensorManager));
            }

            if (_isProcessingResults)
            {
                throw new InvalidOperationException(
                    "Scheduling new perception jobs while result callbacks are draining is not allowed.");
            }

            _batchedJobHandle = _hasScheduledJobs
                ? JobHandle.CombineDependencies(_batchedJobHandle, handle)
                : handle;
            _hasScheduledJobs = true;
        }

        public void CompleteBatchedJobs()
        {
            EnsureOwnerThread();
            if (_isProcessingResults)
            {
                throw new InvalidOperationException(
                    "Nested perception job completion is not allowed while result callbacks are draining.");
            }

            if (!_hasScheduledJobs)
            {
                return;
            }

            _batchedJobHandle.Complete();
            _batchedJobHandle = default;
            _hasScheduledJobs = false;
        }

        internal bool IsRegistered(ISensor sensor)
        {
            EnsureOwnerThread();
            return sensor != null && _sensorLookup.TryGetValue(sensor.SensorId, out ISensor registered) &&
                   ReferenceEquals(sensor, registered);
        }

        internal void OnOwnedSensorDisposing(ISensor sensor)
        {
            EnsureOwnerThread();
            if (!IsDisposed)
            {
                Unregister(sensor);
            }
        }

        internal void DrainPendingWork()
        {
            EnsureOwnerThread();
            if (!IsDisposed)
            {
                CompleteAndProcessPendingJobs();
            }
        }

        private void CompleteAndProcessPendingJobs()
        {
            EnsureNotIteratingSensors();
            CompleteBatchedJobs();
            _isIteratingSensors = true;
            _isProcessingResults = true;
            try
            {
                for (int i = 0; i < _sensors.Count; i++)
                {
                    ISensor sensor = _sensors[i];
                    if (sensor == null)
                    {
                        continue;
                    }

                    SetEventTracking(sensor);
                    sensor.ProcessJobResults();
                    DrainSensorEvents(sensor);
                }
            }
            finally
            {
                _isProcessingResults = false;
                _isIteratingSensors = false;
            }
        }

        private static int SaturatingAdd(int left, int right)
        {
            return right > 0 && left > int.MaxValue - right ? int.MaxValue : left + right;
        }

        private static long SaturatingAdd(long left, long right)
        {
            return right > 0L && left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private static long SaturatingIncrement(long value)
        {
            return value == long.MaxValue ? long.MaxValue : value + 1L;
        }

        private float GetLODFrequencyMultiplier(float3 sensorPosition)
        {
            if (!_lodEnabled || _lodReference == null)
            {
                return 1f;
            }

            if (!math.all(math.isfinite(sensorPosition)))
            {
                return 1f;
            }

            float distance = math.distance(sensorPosition, (float3)_lodReference.position);
            if (!math.isfinite(distance))
            {
                return 1f;
            }

            for (int i = 0; i < _lodLevels.Length; i++)
            {
                if (distance <= _lodLevels[i].Distance)
                {
                    return _lodLevels[i].FrequencyMultiplier;
                }
            }

            return _lodLevels[_lodLevels.Length - 1].FrequencyMultiplier;
        }

        internal void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("SensorManager is main-owner-thread affine and is not synchronized.");
            }
        }

        private void EnsureNotIteratingSensors()
        {
            if (_isIteratingSensors)
            {
                throw new InvalidOperationException(
                    "Sensor registration, removal, disposal, and nested update are not allowed during sensor callbacks.");
            }
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            EnsureNotIteratingSensors();
            if (IsDisposed)
            {
                return;
            }

            // Disposal drains pending jobs, which would otherwise dispatch into listeners mid-teardown.
            _listeners.Clear();
            _registry.EnsureManagerCollectionMutationAllowed();
            Exception disposeFailure = null;
            try
            {
                CompleteAndProcessPendingJobs();
            }
            catch (Exception exception)
            {
                disposeFailure = exception;
            }

            IsDisposed = true;
            _isIteratingSensors = true;
            try
            {
                for (int i = _sensors.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _sensors[i]?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        disposeFailure ??= exception;
                    }
                }
            }
            finally
            {
                _isIteratingSensors = false;
                _sensors.Clear();
                _sensorLookup.Clear();
                if (_attitudeTable.IsCreated)
                {
                    _attitudeTable.Dispose();
                }

                if (_emptyAttitudeTable.IsCreated)
                {
                    _emptyAttitudeTable.Dispose();
                }

                _registry.DetachSensorManager(this);
                if (_instance == this)
                {
                    _instance = null;
                }
            }

            if (disposeFailure != null)
            {
                throw new InvalidOperationException("One or more sensors failed during SensorManager disposal.", disposeFailure);
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance?.Dispose();
            _instance = null;
            _isQuitting = false;
        }
    }
}
