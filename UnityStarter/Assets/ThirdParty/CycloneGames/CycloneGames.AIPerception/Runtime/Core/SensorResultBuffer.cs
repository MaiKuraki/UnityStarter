using System;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Single-owner persistent result and stimulus-memory storage shared by all built-in senses.
    /// All methods must be called from the perception owner thread after the sensor job completes.
    /// </summary>
    /// <remarks>
    /// Results and memory entries are main-thread owned and are consumed by gameplay code, never by a
    /// job. They live in preallocated managed arrays so that adding, reading and sorting them costs no
    /// container safety check. Only the handle index is native because the sight job reads it to apply
    /// look-alike hysteresis.
    /// </remarks>
    internal sealed class SensorResultBuffer : IDisposable
    {
        private DetectionResult[] _results;
        private StimulusMemoryEntry[] _memoryEntries;
        private NativeParallelHashMap<PerceptibleHandle, int> _memoryLookup;
        private readonly int _maximumResults;
        private readonly int _maximumMemoryEntries;
        private readonly SensorMemoryCounters _memoryCounters;
        private int _resultCount;
        private int _memoryCount;
        private int _initialResultCapacity;
        private int _initialMemoryCapacity;
        private uint _refreshVersion;
        private bool _disposed;

        public SensorResultBuffer(
            in PerceptionSensorCapacity capacity,
            SensorMemoryCounters memoryCounters)
        {
            PerceptionSensorCapacity normalized = capacity.Normalize();
            _maximumResults = normalized.MaximumResults;
            _maximumMemoryEntries = normalized.MaximumMemoryEntries;
            _initialResultCapacity = normalized.InitialResultCapacity;
            _initialMemoryCapacity = normalized.InitialMemoryCapacity;
            _memoryCounters = memoryCounters ?? throw new ArgumentNullException(nameof(memoryCounters));
            _results = new DetectionResult[_initialResultCapacity];
            _memoryEntries = new StimulusMemoryEntry[_initialMemoryCapacity];
            _memoryLookup = new NativeParallelHashMap<PerceptibleHandle, int>(
                _initialMemoryCapacity,
                Allocator.Persistent);
        }

        public int ResultCount => _resultCount;
        public int ResultCapacity => _results.Length;
        public int MaximumResultCount => _maximumResults;
        public int MemoryCount => _memoryCount;
        public int MemoryCapacity => _memoryEntries.Length;
        public int MaximumMemoryCount => _maximumMemoryEntries;

        /// <summary>Only the handle index is a native container; results and memory are managed.</summary>
        public int NativeBufferCount => _memoryLookup.IsCreated ? 1 : 0;

        public int NativeBufferCapacity => _memoryLookup.IsCreated ? _memoryLookup.Capacity : 0;

        public bool HasResults => _resultCount > 0;

        /// <summary>
        /// Read-only handle index of remembered stimuli. Sensors may borrow it for a job to apply
        /// sight hysteresis; capacity changes only happen in <see cref="Commit"/>, which runs after
        /// the borrowing job has completed.
        /// </summary>
        internal NativeParallelHashMap<PerceptibleHandle, int> MemoryLookup => _memoryLookup;

        public void BeginUpdate()
        {
            ThrowIfDisposed();
            _resultCount = 0;
        }

        public bool TryAddLive(in DetectionResult result)
        {
            ThrowIfDisposed();
            if (_resultCount >= _maximumResults)
            {
                _memoryCounters.RecordResultCapacityRejected();
                return false;
            }

            EnsureResultCapacity(_resultCount + 1);
            _results[_resultCount++] = result;
            _memoryCounters.RecordResultCount(_resultCount);
            return true;
        }

        public SensorUpdateStatus Commit(
            double timestamp,
            float memoryDuration,
            SensorType sensorType,
            SensorUpdateStatus status)
        {
            ThrowIfDisposed();
            int liveCount = _resultCount;
            uint refreshVersion = NextRefreshVersion();

            if (memoryDuration <= 0f || !math.isfinite(memoryDuration))
            {
                ClearMemory();
                SortResults();
                return status;
            }

            for (int i = 0; i < liveCount; i++)
            {
                DetectionResult detection = _results[i];
                if (_memoryLookup.TryGetValue(detection.Target, out int memoryIndex))
                {
                    StimulusMemoryEntry entry = _memoryEntries[memoryIndex];
                    entry.LastKnownPosition = detection.LastKnownPosition;
                    entry.LastDetectedTime = timestamp;
                    entry.VisibilityAtLastDetection = detection.Visibility;
                    entry.DistanceAtDetection = detection.Distance;
                    entry.SensorType = sensorType;
                    entry.RefreshVersion = refreshVersion;
                    _memoryEntries[memoryIndex] = entry;
                    continue;
                }

                if (_memoryCount >= _maximumMemoryEntries)
                {
                    EvictOldestMemory();
                }

                EnsureMemoryCapacity(_memoryCount + 1);
                EnsureMemoryLookupCapacity(_memoryCount + 1);
                int newIndex = _memoryCount;
                _memoryEntries[newIndex] = new StimulusMemoryEntry
                {
                    Target = detection.Target,
                    LastKnownPosition = detection.LastKnownPosition,
                    LastDetectedTime = timestamp,
                    VisibilityAtLastDetection = detection.Visibility,
                    SensorType = sensorType,
                    DistanceAtDetection = detection.Distance,
                    RefreshVersion = refreshVersion
                };
                _memoryCount++;
                _memoryCounters.RecordMemoryCount(_memoryCount);
                if (!_memoryLookup.TryAdd(detection.Target, newIndex))
                {
                    _memoryCount--;
                    throw new InvalidOperationException("Stimulus-memory lookup rejected a unique perceptible handle.");
                }
            }

            bool resultCapacityExceeded = false;
            for (int i = _memoryCount - 1; i >= 0; i--)
            {
                StimulusMemoryEntry entry = _memoryEntries[i];
                if (entry.RefreshVersion == refreshVersion)
                {
                    continue;
                }

                double age = timestamp - entry.LastDetectedTime;
                if (!double.IsFinite(age) || age < 0d || age >= memoryDuration)
                {
                    RemoveMemoryAt(i);
                    continue;
                }

                float visibility = entry.VisibilityAtLastDetection * (1f - (float)(age / memoryDuration));
                if (!math.isfinite(visibility) || visibility <= 0.01f)
                {
                    RemoveMemoryAt(i);
                    continue;
                }

                if (_resultCount >= _maximumResults)
                {
                    resultCapacityExceeded = true;
                    _memoryCounters.RecordResultCapacityRejected();
                    continue;
                }

                EnsureResultCapacity(_resultCount + 1);
                _results[_resultCount++] = new DetectionResult
                {
                    Target = entry.Target,
                    Distance = entry.DistanceAtDetection,
                    LastKnownPosition = entry.LastKnownPosition,
                    DetectionTime = entry.LastDetectedTime,
                    Visibility = visibility,
                    SensorType = entry.SensorType,
                    IsFromMemory = true
                };
                _memoryCounters.RecordResultCount(_resultCount);
            }

            SortResults();
            return resultCapacityExceeded &&
                   (status == SensorUpdateStatus.Ready || status == SensorUpdateStatus.NoTargets)
                ? SensorUpdateStatus.ResultCapacityExceeded
                : status;
        }

        public bool TryGetResult(int index, out DetectionResult result)
        {
            if (_disposed || index < 0 || index >= _resultCount)
            {
                result = default;
                return false;
            }

            result = _results[index];
            return true;
        }

        public void CopyResultsTo(ref NativeList<DetectionResult> destination)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _resultCount; i++)
            {
                destination.Add(_results[i]);
            }
        }

        public void CopyHandlesTo(ref NativeList<PerceptibleHandle> destination)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _resultCount; i++)
            {
                destination.Add(_results[i].Target);
            }
        }

        public void ClearAll()
        {
            if (_disposed)
            {
                return;
            }

            _resultCount = 0;
            ClearMemory();
        }

        private void EnsureResultCapacity(int required)
        {
            if (required <= _results.Length)
            {
                return;
            }

            int capacity = math.max(_results.Length, 1);
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _results, math.min(capacity, _maximumResults));
        }

        private void EnsureMemoryCapacity(int required)
        {
            if (required <= _memoryEntries.Length)
            {
                return;
            }

            int capacity = math.max(_memoryEntries.Length, 1);
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _memoryEntries, math.min(capacity, _maximumMemoryEntries));
        }

        private uint NextRefreshVersion()
        {
            _refreshVersion = unchecked(_refreshVersion + 1u);
            if (_refreshVersion == 0u)
            {
                _refreshVersion = 1u;
                for (int i = 0; i < _memoryCount; i++)
                {
                    StimulusMemoryEntry entry = _memoryEntries[i];
                    entry.RefreshVersion = 0u;
                    _memoryEntries[i] = entry;
                }
            }

            return _refreshVersion;
        }

        private void EvictOldestMemory()
        {
            if (_memoryCount == 0)
            {
                return;
            }

            int oldestIndex = 0;
            StimulusMemoryEntry oldest = _memoryEntries[0];
            for (int i = 1; i < _memoryCount; i++)
            {
                StimulusMemoryEntry candidate = _memoryEntries[i];
                if (candidate.LastDetectedTime < oldest.LastDetectedTime ||
                    (candidate.LastDetectedTime.Equals(oldest.LastDetectedTime) &&
                     CompareHandle(candidate.Target, oldest.Target) < 0))
                {
                    oldest = candidate;
                    oldestIndex = i;
                }
            }

            RemoveMemoryAt(oldestIndex);
            _memoryCounters.RecordMemoryEviction();
        }

        private void RemoveMemoryAt(int index)
        {
            int lastIndex = _memoryCount - 1;
            PerceptibleHandle removed = _memoryEntries[index].Target;
            _memoryLookup.Remove(removed);

            if (index != lastIndex)
            {
                StimulusMemoryEntry moved = _memoryEntries[lastIndex];
                _memoryEntries[index] = moved;
                _memoryLookup[moved.Target] = index;
            }

            _memoryEntries[lastIndex] = default;
            _memoryCount = lastIndex;
        }

        private void EnsureMemoryLookupCapacity(int required)
        {
            if (required <= _memoryLookup.Capacity)
            {
                return;
            }

            int doubled = _memoryLookup.Capacity <= _maximumMemoryEntries / 2
                ? _memoryLookup.Capacity * 2
                : _maximumMemoryEntries;
            _memoryLookup.Capacity = math.min(math.max(required, doubled), _maximumMemoryEntries);
        }

        private void ClearMemory()
        {
            _memoryCount = 0;
            _memoryLookup.Clear();
        }

        /// <summary>
        /// In-place heap sort. Every managed <see cref="Array.Sort{T}(T[])"/> overload allocates in the
        /// editor runtime, and this path runs once per sensor per update, so the buffer sorts itself.
        /// The ordering is a total order on (distance, handle), which makes stability irrelevant.
        /// </summary>
        private void SortResults()
        {
            int count = _resultCount;
            if (count <= 1)
            {
                return;
            }

            for (int root = (count >> 1) - 1; root >= 0; root--)
            {
                SiftDown(root, count);
            }

            for (int end = count - 1; end > 0; end--)
            {
                DetectionResult swap = _results[0];
                _results[0] = _results[end];
                _results[end] = swap;
                SiftDown(0, end);
            }
        }

        private void SiftDown(int root, int count)
        {
            int current = root;
            while (true)
            {
                int child = (current << 1) + 1;
                if (child >= count)
                {
                    return;
                }

                if (child + 1 < count && _results[child + 1].CompareTo(_results[child]) > 0)
                {
                    child++;
                }

                if (_results[current].CompareTo(_results[child]) >= 0)
                {
                    return;
                }

                DetectionResult swap = _results[current];
                _results[current] = _results[child];
                _results[child] = swap;
                current = child;
            }
        }

        private static int CompareHandle(PerceptibleHandle left, PerceptibleHandle right)
        {
            int registry = left.RegistryId.CompareTo(right.RegistryId);
            if (registry != 0)
            {
                return registry;
            }

            int id = left.Id.CompareTo(right.Id);
            return id != 0 ? id : left.Generation.CompareTo(right.Generation);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SensorResultBuffer));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _results = Array.Empty<DetectionResult>();
            _memoryEntries = Array.Empty<StimulusMemoryEntry>();
            if (_memoryLookup.IsCreated)
            {
                _memoryLookup.Dispose();
            }
        }
    }
}
