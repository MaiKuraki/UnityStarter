using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Single-owner persistent result and stimulus-memory storage shared by all built-in senses.
    /// All methods must be called from the perception owner thread after the sensor job completes.
    /// </summary>
    internal sealed class SensorResultBuffer : IDisposable
    {
        private struct DetectionResultComparer : IComparer<DetectionResult>
        {
            public int Compare(DetectionResult left, DetectionResult right)
            {
                int distance = left.Distance.CompareTo(right.Distance);
                if (distance != 0)
                {
                    return distance;
                }

                int registry = left.Target.RegistryId.CompareTo(right.Target.RegistryId);
                if (registry != 0)
                {
                    return registry;
                }

                int id = left.Target.Id.CompareTo(right.Target.Id);
                if (id != 0)
                {
                    return id;
                }

                int generation = left.Target.Generation.CompareTo(right.Target.Generation);
                if (generation != 0)
                {
                    return generation;
                }

                return left.IsFromMemory.CompareTo(right.IsFromMemory);
            }
        }

        private NativeList<DetectionResult> _results;
        private NativeList<StimulusMemoryEntry> _memoryEntries;
        private NativeParallelHashMap<PerceptibleHandle, int> _memoryLookup;
        private readonly int _maximumResults;
        private readonly int _maximumMemoryEntries;
        private uint _refreshVersion;
        private bool _disposed;

        public SensorResultBuffer(in PerceptionSensorCapacity capacity)
        {
            PerceptionSensorCapacity normalized = capacity.Normalize();
            _maximumResults = normalized.MaximumResults;
            _maximumMemoryEntries = normalized.MaximumMemoryEntries;
            try
            {
                _results = new NativeList<DetectionResult>(normalized.InitialResultCapacity, Allocator.Persistent);
                _memoryEntries = new NativeList<StimulusMemoryEntry>(
                    normalized.InitialMemoryCapacity,
                    Allocator.Persistent);
                _memoryLookup = new NativeParallelHashMap<PerceptibleHandle, int>(
                    normalized.InitialMemoryCapacity,
                    Allocator.Persistent);
            }
            catch
            {
                if (_results.IsCreated)
                {
                    _results.Dispose();
                }

                if (_memoryEntries.IsCreated)
                {
                    _memoryEntries.Dispose();
                }

                if (_memoryLookup.IsCreated)
                {
                    _memoryLookup.Dispose();
                }

                throw;
            }
        }

        public int ResultCount => _results.IsCreated ? _results.Length : 0;
        public int MemoryCount => _memoryEntries.IsCreated ? _memoryEntries.Length : 0;
        public bool HasResults => ResultCount > 0;

        public void BeginUpdate()
        {
            ThrowIfDisposed();
            _results.Clear();
        }

        public bool TryAddLive(in DetectionResult result)
        {
            ThrowIfDisposed();
            if (_results.Length >= _maximumResults)
            {
                return false;
            }

            _results.Add(result);
            return true;
        }

        public SensorUpdateStatus Commit(
            double timestamp,
            float memoryDuration,
            SensorType sensorType,
            SensorUpdateStatus status)
        {
            ThrowIfDisposed();
            int liveCount = _results.Length;
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

                if (_memoryEntries.Length >= _maximumMemoryEntries)
                {
                    EvictOldestMemory();
                }

                EnsureMemoryLookupCapacity(_memoryEntries.Length + 1);
                var newEntry = new StimulusMemoryEntry
                {
                    Target = detection.Target,
                    LastKnownPosition = detection.LastKnownPosition,
                    LastDetectedTime = timestamp,
                    VisibilityAtLastDetection = detection.Visibility,
                    SensorType = sensorType,
                    DistanceAtDetection = detection.Distance,
                    RefreshVersion = refreshVersion
                };
                int newIndex = _memoryEntries.Length;
                _memoryEntries.Add(newEntry);
                if (!_memoryLookup.TryAdd(detection.Target, newIndex))
                {
                    _memoryEntries.RemoveAt(newIndex);
                    throw new InvalidOperationException("Stimulus-memory lookup rejected a unique perceptible handle.");
                }
            }

            bool resultCapacityExceeded = false;
            for (int i = _memoryEntries.Length - 1; i >= 0; i--)
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

                if (_results.Length >= _maximumResults)
                {
                    resultCapacityExceeded = true;
                    continue;
                }

                _results.Add(new DetectionResult
                {
                    Target = entry.Target,
                    Distance = entry.DistanceAtDetection,
                    LastKnownPosition = entry.LastKnownPosition,
                    DetectionTime = entry.LastDetectedTime,
                    Visibility = visibility,
                    SensorType = entry.SensorType,
                    IsFromMemory = true
                });
            }

            SortResults();
            return resultCapacityExceeded &&
                   (status == SensorUpdateStatus.Ready || status == SensorUpdateStatus.NoTargets)
                ? SensorUpdateStatus.ResultCapacityExceeded
                : status;
        }

        public bool TryGetResult(int index, out DetectionResult result)
        {
            if (_disposed || !_results.IsCreated || index < 0 || index >= _results.Length)
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
            for (int i = 0; i < _results.Length; i++)
            {
                destination.Add(_results[i]);
            }
        }

        public void CopyHandlesTo(ref NativeList<PerceptibleHandle> destination)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _results.Length; i++)
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

            _results.Clear();
            ClearMemory();
        }

        private uint NextRefreshVersion()
        {
            _refreshVersion = unchecked(_refreshVersion + 1u);
            if (_refreshVersion == 0u)
            {
                _refreshVersion = 1u;
                for (int i = 0; i < _memoryEntries.Length; i++)
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
            if (_memoryEntries.Length == 0)
            {
                return;
            }

            int oldestIndex = 0;
            StimulusMemoryEntry oldest = _memoryEntries[0];
            for (int i = 1; i < _memoryEntries.Length; i++)
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
        }

        private void RemoveMemoryAt(int index)
        {
            int lastIndex = _memoryEntries.Length - 1;
            PerceptibleHandle removed = _memoryEntries[index].Target;
            _memoryLookup.Remove(removed);

            if (index != lastIndex)
            {
                StimulusMemoryEntry moved = _memoryEntries[lastIndex];
                _memoryEntries.RemoveAtSwapBack(index);
                _memoryLookup[moved.Target] = index;
            }
            else
            {
                _memoryEntries.RemoveAt(lastIndex);
            }
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
            _memoryEntries.Clear();
            _memoryLookup.Clear();
        }

        private void SortResults()
        {
            if (_results.Length > 1)
            {
                _results.AsArray().Sort(new DetectionResultComparer());
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
            if (_results.IsCreated)
            {
                _results.Dispose();
            }

            if (_memoryEntries.IsCreated)
            {
                _memoryEntries.Dispose();
            }

            if (_memoryLookup.IsCreated)
            {
                _memoryLookup.Dispose();
            }
        }
    }
}
