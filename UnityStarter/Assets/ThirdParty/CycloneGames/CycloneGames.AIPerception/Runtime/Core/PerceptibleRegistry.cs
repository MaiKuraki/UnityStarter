using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Main-thread world registry and immutable frame-snapshot owner. Stable runtime handles are
    /// scoped to one registry instance and must not be persisted or sent over the network.
    /// </summary>
    public sealed class PerceptibleRegistry : IDisposable
    {
        private const int InitialCapacity = 64;
        private const int DefaultMaximumCapacity = 16384;
        private const float WarningThreshold = 0.75f;
        private const float DefaultCellSize = 20f;

        private static PerceptibleRegistry _instance;
        private static int _nextRegistryId;

        private readonly int _registryId;
        private readonly int _ownerThreadId;
        private readonly SpatialGrid _spatialGrid;
        private readonly List<SensorManager> _sensorManagers = new List<SensorManager>(1);
        private IPerceptible[] _perceptibles;
        private PerceptibleData[] _slotData;
        private int[] _generations;
        private int[] _freeNext;
        private int[] _activeIds;
        private int[] _activeIndexBySlot;
        private PerceptibleData[] _managedData;
        private NativeArray<PerceptibleData> _nativeData;
        private int _count;
        private int _nextUnusedSlot;
        private int _freeListHead = -1;
        private int _maximumCapacity;
        private int _dataCount;
        private int _snapshotVersion;
        private float _maximumDetectionRadius;
        private float _maximumLoudness;
        private bool _isDirty = true;
        private bool _warningEmitted;
        private bool _isDrainingManagers;

        public static PerceptibleRegistry Instance => _instance ??= new PerceptibleRegistry();
        public static bool HasInstance => _instance != null && !_instance.IsDisposed;

        public int Count => _count;
        public int RegistryId => _registryId;
        public int SnapshotVersion => _snapshotVersion;
        public int MaximumCapacity => _maximumCapacity;
        public bool IsDisposed { get; private set; }

        internal float MaximumDetectionRadius => _maximumDetectionRadius;
        internal float MaximumLoudness => _maximumLoudness;
        internal NativeArray<PerceptibleData> NativeData => _nativeData;

        public PerceptibleRegistry(
            int initialCapacity = InitialCapacity,
            int maximumCapacity = DefaultMaximumCapacity,
            float cellSize = DefaultCellSize)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            if (maximumCapacity < 0 || (maximumCapacity > 0 && maximumCapacity < initialCapacity))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
            }

            _registryId = NextRegistryId();
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _maximumCapacity = maximumCapacity;
            _perceptibles = new IPerceptible[initialCapacity];
            _slotData = new PerceptibleData[initialCapacity];
            _generations = new int[initialCapacity];
            _freeNext = new int[initialCapacity];
            _activeIds = new int[initialCapacity];
            _activeIndexBySlot = new int[initialCapacity];
            _managedData = new PerceptibleData[initialCapacity];
            _spatialGrid = new SpatialGrid(cellSize);
            Fill(_freeNext, -1);
            Fill(_activeIndexBySlot, -1);
        }

        /// <summary>
        /// Sets the hard world capacity. Zero means unbounded safe-point growth. The limit cannot
        /// be lowered below the current active count.
        /// </summary>
        public void SetMaxCapacity(int maximumCapacity)
        {
            if (!TrySetMaxCapacity(maximumCapacity))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
            }
        }

        public bool TrySetMaxCapacity(int maximumCapacity)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            if (maximumCapacity < 0 || (maximumCapacity > 0 && maximumCapacity < _count))
            {
                return false;
            }

            _maximumCapacity = maximumCapacity;
            _warningEmitted = false;
            return true;
        }

        public void SetSpatialCellSize(float cellSize)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            if (_spatialGrid.CellSize.Equals(cellSize))
            {
                return;
            }

            _spatialGrid.SetCellSize(cellSize);
            _isDirty = true;
        }

        public PerceptibleHandle Register(IPerceptible perceptible)
        {
            EnsureOwnerThread();
            if (perceptible == null || IsDisposed)
            {
                return PerceptibleHandle.Invalid;
            }

            for (int i = 0; i < _count; i++)
            {
                int activeSlot = _activeIds[i];
                if (ReferenceEquals(_perceptibles[activeSlot], perceptible))
                {
                    return new PerceptibleHandle(_registryId, activeSlot, _generations[activeSlot]);
                }
            }

            if (_maximumCapacity > 0 && _count >= _maximumCapacity)
            {
                Debug.LogError($"[AIPerception] Registry capacity exhausted ({_maximumCapacity}).");
                return PerceptibleHandle.Invalid;
            }

            int index;
            if (_freeListHead >= 0)
            {
                index = _freeListHead;
                _freeListHead = _freeNext[index];
                _freeNext[index] = -1;
            }
            else
            {
                if (_nextUnusedSlot >= _perceptibles.Length)
                {
                    Grow();
                }

                index = _nextUnusedSlot++;
                _generations[index] = NextGeneration(_generations[index]);
            }

            _perceptibles[index] = perceptible;
            _activeIndexBySlot[index] = _count;
            _activeIds[_count] = index;
            _count++;
            _isDirty = true;
            CheckCapacityWarning();
            return new PerceptibleHandle(_registryId, index, _generations[index]);
        }

        public bool Unregister(PerceptibleHandle handle)
        {
            EnsureOwnerThread();
            if (!IsValid(handle))
            {
                return false;
            }

            int index = handle.Id;
            int activeIndex = _activeIndexBySlot[index];
            int lastActiveIndex = _count - 1;
            if (activeIndex != lastActiveIndex)
            {
                int movedSlot = _activeIds[lastActiveIndex];
                _activeIds[activeIndex] = movedSlot;
                _activeIndexBySlot[movedSlot] = activeIndex;
            }

            _activeIds[lastActiveIndex] = 0;
            _activeIndexBySlot[index] = -1;
            _perceptibles[index] = null;
            _slotData[index] = default;
            _generations[index] = NextGeneration(_generations[index]);
            _freeNext[index] = _freeListHead;
            _freeListHead = index;
            _count--;
            _isDirty = true;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IPerceptible Get(PerceptibleHandle handle)
        {
            return IsValid(handle) ? _perceptibles[handle.Id] : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(PerceptibleHandle handle)
        {
            return !IsDisposed &&
                   handle.IsValid &&
                   handle.RegistryId == _registryId &&
                   handle.Id < _nextUnusedSlot &&
                   _perceptibles[handle.Id] != null &&
                   _generations[handle.Id] == handle.Generation;
        }

        /// <summary>
        /// Captures dynamic values and publishes one sorted immutable snapshot. The O(N) capture
        /// runs each manager tick; sorting and native copy occur only when a value changed.
        /// </summary>
        public void RebuildData()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            DrainAttachedManagers();
            RebuildDataCore();
        }

        internal void RebuildDataForSensorManager(SensorManager requester)
        {
            EnsureOwnerThread();
            if (requester == null)
            {
                throw new ArgumentNullException(nameof(requester));
            }

            if (!_sensorManagers.Contains(requester))
            {
                throw new InvalidOperationException("The sensor manager is not attached to this perception registry.");
            }

            DrainAttachedManagers();
            RebuildDataCore();
        }

        internal void AttachSensorManager(SensorManager manager)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnsureManagerCollectionMutationAllowed();
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            if (!_sensorManagers.Contains(manager))
            {
                _sensorManagers.Add(manager);
            }
        }

        internal void DetachSensorManager(SensorManager manager)
        {
            EnsureOwnerThread();
            EnsureManagerCollectionMutationAllowed();
            _sensorManagers.Remove(manager);
        }

        internal void EnsureManagerCollectionMutationAllowed()
        {
            if (_isDrainingManagers)
            {
                throw new InvalidOperationException(
                    "SensorManager attachment, detachment, and nested registry rebuild are not allowed while pending work is draining.");
            }
        }

        private void DrainAttachedManagers()
        {
            EnsureManagerCollectionMutationAllowed();
            _isDrainingManagers = true;
            try
            {
                for (int i = 0; i < _sensorManagers.Count; i++)
                {
                    _sensorManagers[i]?.DrainPendingWork();
                }
            }
            finally
            {
                _isDrainingManagers = false;
            }
        }

        private void RebuildDataCore()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();

            for (int i = 0; i < _count; i++)
            {
                int slot = _activeIds[i];
                PerceptibleData captured = Capture(slot, _perceptibles[slot]);
                if (!DataEquals(in captured, in _slotData[slot]))
                {
                    _slotData[slot] = captured;
                    _isDirty = true;
                }
            }

            if (!_isDirty)
            {
                return;
            }

            EnsureManagedCapacity(_count);
            _dataCount = 0;
            _maximumDetectionRadius = 0f;
            _maximumLoudness = 0f;
            for (int i = 0; i < _count; i++)
            {
                PerceptibleData data = _slotData[_activeIds[i]];
                if (!data.IsDetectable)
                {
                    continue;
                }

                _managedData[_dataCount++] = data;
                _maximumDetectionRadius = math.max(_maximumDetectionRadius, data.DetectionRadius);
                if (data.IsSoundSource)
                {
                    _maximumLoudness = math.max(_maximumLoudness, data.Loudness);
                }
            }

            _spatialGrid.Rebuild(_managedData, _dataCount);
            EnsureNativeCapacity(_dataCount);
            for (int i = 0; i < _dataCount; i++)
            {
                _nativeData[i] = _managedData[i];
            }

            _isDirty = false;
            _snapshotVersion = unchecked(_snapshotVersion + 1);
            if (_snapshotVersion == 0)
            {
                _snapshotVersion = 1;
            }
        }

        public NativeArray<PerceptibleData> CreateNativeDataCopy(Allocator allocator = Allocator.TempJob)
        {
            RebuildData();
            var copy = new NativeArray<PerceptibleData>(_dataCount, allocator);
            for (int i = 0; i < _dataCount; i++)
            {
                copy[i] = _managedData[i];
            }

            return copy;
        }

        public NativeArray<PerceptibleData> CreateNativeDataCopyInRange(
            float3 origin,
            float range,
            Allocator allocator = Allocator.TempJob)
        {
            RebuildData();
            var indices = new NativeList<int>(math.max(1, _dataCount), Allocator.Temp);
            try
            {
                bool success = _spatialGrid.CollectIndices(
                    _managedData,
                    _dataCount,
                    origin,
                    range,
                    ref indices,
                    math.max(1, _dataCount));
                if (!success)
                {
                    throw new InvalidOperationException("The range query rejected invalid bounds or capacity.");
                }

                var copy = new NativeArray<PerceptibleData>(indices.Length, allocator);
                for (int i = 0; i < indices.Length; i++)
                {
                    copy[i] = _managedData[indices[i]];
                }

                return copy;
            }
            finally
            {
                indices.Dispose();
            }
        }

        internal bool CollectCandidateIndices(
            float3 origin,
            float range,
            ref NativeList<int> results,
            int maximumResults)
        {
            return _spatialGrid.CollectIndices(
                _managedData,
                _dataCount,
                origin,
                range,
                ref results,
                maximumResults);
        }

        public int GetDataCount() => _dataCount;

        public void MarkDirty()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            _isDirty = true;
        }

        private PerceptibleData Capture(int slot, IPerceptible perceptible)
        {
            float3 position = perceptible.Position;
            float3 lineOfSightPoint = perceptible.GetLOSPoint();
            float radius = perceptible.DetectionRadius;
            float loudness = perceptible.Loudness;
            bool finite = math.all(math.isfinite(position)) &&
                          math.all(math.isfinite(lineOfSightPoint)) &&
                          math.isfinite(radius) &&
                          math.isfinite(loudness);
            bool detectable = perceptible.IsDetectable && finite;
            byte flags = detectable ? PerceptibleData.DetectableFlag : (byte)0;
            if (detectable && perceptible.IsSoundSource)
            {
                flags |= PerceptibleData.SoundSourceFlag;
            }

            return new PerceptibleData
            {
                RegistryId = _registryId,
                Id = slot,
                Generation = _generations[slot],
                TypeId = perceptible.PerceptibleTypeId,
                Flags = flags,
                DetectionRadius = finite ? math.max(0f, radius) : 0f,
                Loudness = finite ? math.max(0f, loudness) : 0f,
                Position = finite ? position : float3.zero,
                LOSPoint = finite ? lineOfSightPoint : float3.zero
            };
        }

        private static bool DataEquals(in PerceptibleData left, in PerceptibleData right)
        {
            return left.RegistryId == right.RegistryId &&
                   left.Id == right.Id &&
                   left.Generation == right.Generation &&
                   left.TypeId == right.TypeId &&
                   left.Flags == right.Flags &&
                   left.DetectionRadius.Equals(right.DetectionRadius) &&
                   left.Loudness.Equals(right.Loudness) &&
                   math.all(left.Position == right.Position) &&
                   math.all(left.LOSPoint == right.LOSPoint);
        }

        private void CheckCapacityWarning()
        {
            if (_warningEmitted || _maximumCapacity <= 0)
            {
                return;
            }

            int warningCount = (int)math.ceil(_maximumCapacity * WarningThreshold);
            if (_count < warningCount)
            {
                return;
            }

            Debug.LogWarning($"[AIPerception] Registry at {_count}/{_maximumCapacity}. Review the world capacity budget.");
            _warningEmitted = true;
        }

        private void Grow()
        {
            int current = _perceptibles.Length;
            int proposed = current <= int.MaxValue / 2 ? current * 2 : int.MaxValue;
            int newCapacity = _maximumCapacity > 0 ? math.min(proposed, _maximumCapacity) : proposed;
            if (newCapacity <= current)
            {
                throw new InvalidOperationException("Perceptible registry cannot grow beyond its configured capacity.");
            }

            Array.Resize(ref _perceptibles, newCapacity);
            Array.Resize(ref _slotData, newCapacity);
            Array.Resize(ref _generations, newCapacity);
            ResizeAndFill(ref _freeNext, newCapacity, -1);
            Array.Resize(ref _activeIds, newCapacity);
            ResizeAndFill(ref _activeIndexBySlot, newCapacity, -1);
        }

        private void EnsureManagedCapacity(int required)
        {
            if (_managedData.Length >= required)
            {
                return;
            }

            int doubled = _managedData.Length <= int.MaxValue / 2
                ? _managedData.Length * 2
                : int.MaxValue;
            int capacity = math.max(required, doubled);
            Array.Resize(ref _managedData, capacity);
        }

        private void EnsureNativeCapacity(int required)
        {
            if (_nativeData.IsCreated && _nativeData.Length >= required)
            {
                return;
            }

            int current = _nativeData.IsCreated ? _nativeData.Length : 0;
            int doubled = current <= int.MaxValue / 2 ? current * 2 : int.MaxValue;
            int capacity = math.max(1, math.max(required, current > 0 ? doubled : InitialCapacity));
            var replacement = new NativeArray<PerceptibleData>(capacity, Allocator.Persistent);
            if (_nativeData.IsCreated)
            {
                _nativeData.Dispose();
            }

            _nativeData = replacement;
        }

        private static int NextRegistryId()
        {
            int id = Interlocked.Increment(ref _nextRegistryId);
            if (id == 0)
            {
                id = Interlocked.Increment(ref _nextRegistryId);
            }

            return id;
        }

        private static int NextGeneration(int generation)
        {
            int next = unchecked(generation + 1);
            return next <= 0 ? 1 : next;
        }

        private static void Fill(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = value;
            }
        }

        private static void ResizeAndFill(ref int[] values, int newLength, int fillValue)
        {
            int oldLength = values.Length;
            Array.Resize(ref values, newLength);
            for (int i = oldLength; i < newLength; i++)
            {
                values[i] = fillValue;
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("PerceptibleRegistry is main-owner-thread affine and is not synchronized.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(PerceptibleRegistry));
            }
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (IsDisposed)
            {
                return;
            }

            DrainAttachedManagers();
            if (_sensorManagers.Count != 0)
            {
                throw new InvalidOperationException(
                    "Dispose all SensorManager owners before disposing their PerceptibleRegistry.");
            }

            IsDisposed = true;
            if (_nativeData.IsCreated)
            {
                _nativeData.Dispose();
            }

            _perceptibles = null;
            _slotData = null;
            _generations = null;
            _freeNext = null;
            _activeIds = null;
            _activeIndexBySlot = null;
            _managedData = null;
            _sensorManagers.Clear();
            _spatialGrid.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void ResetInstance()
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}
