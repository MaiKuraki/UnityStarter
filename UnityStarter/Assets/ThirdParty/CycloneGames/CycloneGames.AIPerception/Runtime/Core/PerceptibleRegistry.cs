using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Central registry for perceptible entities with O(1) lookup and NativeArray export for Jobs.
    /// </summary>
    public sealed class PerceptibleRegistry : IDisposable
    {
        private const int INITIAL_CAPACITY = 64;
        private const int MAX_CAPACITY = 4096;

        private static PerceptibleRegistry _instance;
        public static PerceptibleRegistry Instance => _instance ??= new PerceptibleRegistry();
        public static bool HasInstance => _instance != null && !_instance.IsDisposed;

        private IPerceptible[] _perceptibles;
        private int[] _generations;
        private int _count;
        private int _freeListHead = -1;

        // Shared array rebuilt each frame
        private PerceptibleData[] _managedData;
        private int _dataCount;
        private bool _isDirty = true;

        public int Count => _count;
        public bool IsDisposed { get; private set; }

        public PerceptibleRegistry()
        {
            _perceptibles = new IPerceptible[INITIAL_CAPACITY];
            _generations = new int[INITIAL_CAPACITY];
            _managedData = new PerceptibleData[INITIAL_CAPACITY];
        }

        public PerceptibleHandle Register(IPerceptible perceptible)
        {
            if (perceptible == null || IsDisposed) return PerceptibleHandle.Invalid;

            int index;
            if (_freeListHead >= 0)
            {
                index = _freeListHead;
                _freeListHead = _generations[index] < 0 ? -_generations[index] - 1 : -1;
                _generations[index] = Math.Abs(_generations[index]);
            }
            else
            {
                if (_count >= _perceptibles.Length)
                {
                    if (_count >= MAX_CAPACITY) return PerceptibleHandle.Invalid;
                    Grow();
                }
                index = _count;
            }

            _perceptibles[index] = perceptible;
            _generations[index]++;
            _count++;
            _isDirty = true;

            return new PerceptibleHandle(index, _generations[index]);
        }

        public void Unregister(PerceptibleHandle handle)
        {
            if (!handle.IsValid || handle.Id >= _generations.Length) return;
            if (_generations[handle.Id] != handle.Generation) return;

            _perceptibles[handle.Id] = null;
            _generations[handle.Id] = -(_freeListHead + 1);
            _freeListHead = handle.Id;
            _count--;
            _isDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IPerceptible Get(PerceptibleHandle handle)
        {
            if (!IsValid(handle)) return null;
            return _perceptibles[handle.Id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(PerceptibleHandle handle)
        {
            if (!handle.IsValid || handle.Id >= _generations.Length) return false;
            return _generations[handle.Id] == handle.Generation;
        }

        /// <summary>
        /// Rebuilds managed data array. Call once per frame before sensor updates.
        /// </summary>
        public void RebuildData()
        {
            if (!_isDirty && _dataCount > 0) return;

            if (_managedData.Length < _count)
            {
                _managedData = new PerceptibleData[Math.Max(_count, 32)];
            }

            _dataCount = 0;
            for (int i = 0; i < _perceptibles.Length && _dataCount < _count; i++)
            {
                var p = _perceptibles[i];
                if (p == null || !p.IsDetectable) continue;

                float loudness = 1f;
                if (p is PerceptibleComponent pc)
                {
                    loudness = pc.Loudness;
                }

                // Use slot index (i) as Id to match PerceptibleHandle
                _managedData[_dataCount++] = new PerceptibleData
                {
                    Id = i,  // Registry slot index, matches Handle.Id
                    Generation = _generations[i],
                    TypeId = p.PerceptibleTypeId,
                    Flags = p.IsDetectable ? (byte)1 : (byte)0,
                    DetectionRadius = p.DetectionRadius,
                    Loudness = loudness,
                    Position = p.Position,
                    LOSPoint = p.GetLOSPoint()
                };
            }

            _isDirty = false;
        }

        /// <summary>
        /// Creates a new TempJob NativeArray copy. CALLER MUST DISPOSE.
        /// </summary>
        public NativeArray<PerceptibleData> CreateNativeDataCopy(Allocator allocator = Allocator.TempJob)
        {
            RebuildData();

            int count = Math.Max(_dataCount, 1);
            var nativeData = new NativeArray<PerceptibleData>(count, allocator);

            for (int i = 0; i < _dataCount; i++)
            {
                nativeData[i] = _managedData[i];
            }

            return nativeData;
        }

        /// <summary>
        /// Gets the current data count after RebuildData().
        /// </summary>
        public int GetDataCount() => _dataCount;

        public void MarkDirty() => _isDirty = true;

        private void Grow()
        {
            int newCapacity = Mathf.Min(_perceptibles.Length * 2, MAX_CAPACITY);
            Array.Resize(ref _perceptibles, newCapacity);
            Array.Resize(ref _generations, newCapacity);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            _perceptibles = null;
            _generations = null;
            _managedData = null;

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
