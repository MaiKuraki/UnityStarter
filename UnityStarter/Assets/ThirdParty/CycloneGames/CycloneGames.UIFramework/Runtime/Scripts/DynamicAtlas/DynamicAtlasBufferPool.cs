using System;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Thread-safe buffer pool for zero-GC texture operations.
    /// Provides platform-specific implementations for optimal performance.
    /// </summary>
    public sealed class DynamicAtlasBufferPool : IDisposable
    {
        private static DynamicAtlasBufferPool _instance;
        private static readonly object _instanceLock = new object();

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static DynamicAtlasBufferPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DynamicAtlasBufferPool();
                        }
                    }
                }
                return _instance;
            }
        }

        private bool _disposed;

#if !UNITY_WEBGL || UNITY_EDITOR
        // Native buffer pool for non-WebGL platforms
        private readonly ConcurrentQueue<NativeBufferEntry> _nativeBufferPool = new ConcurrentQueue<NativeBufferEntry>();
        private const int MaxNativePoolSize = 4;
        private int _nativePoolCount = 0;

        private struct NativeBufferEntry
        {
            public NativeArray<Color32> Buffer;
            public int Capacity;
        }
#endif

        // Managed buffer pool (used for WebGL or as fallback)
        private readonly object _managedLock = new object();
        private Color32[][] _managedBuffers = new Color32[4][];
        private int[] _managedCapacities = new int[4];
        private bool[] _managedInUse = new bool[4];
        private const int MaxManagedPoolSize = 4;

        private DynamicAtlasBufferPool() { }

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>
        /// Gets a native buffer with at least the specified capacity.
        /// Returns true if a buffer was obtained, false if native buffers are not supported.
        /// </summary>
        public bool TryGetNativeBuffer(int requiredCapacity, out NativeArray<Color32> buffer)
        {
            if (_disposed)
            {
                buffer = default;
                return false;
            }

            // Try to get from pool
            while (_nativeBufferPool.TryDequeue(out var entry))
            {
                System.Threading.Interlocked.Decrement(ref _nativePoolCount);

                if (entry.Capacity >= requiredCapacity)
                {
                    buffer = entry.Buffer;
                    return true;
                }

                // Buffer too small, dispose it
                if (entry.Buffer.IsCreated)
                {
                    entry.Buffer.Dispose();
                }
            }

            // Create new buffer
            try
            {
                buffer = new NativeArray<Color32>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                return true;
            }
            catch (Exception)
            {
                buffer = default;
                return false;
            }
        }

        /// <summary>
        /// Returns a native buffer to the pool for reuse.
        /// </summary>
        public void ReturnNativeBuffer(NativeArray<Color32> buffer)
        {
            if (_disposed || !buffer.IsCreated)
            {
                if (buffer.IsCreated)
                    buffer.Dispose();
                return;
            }

            if (_nativePoolCount < MaxNativePoolSize)
            {
                _nativeBufferPool.Enqueue(new NativeBufferEntry { Buffer = buffer, Capacity = buffer.Length });
                System.Threading.Interlocked.Increment(ref _nativePoolCount);
            }
            else
            {
                buffer.Dispose();
            }
        }

        /// <summary>
        /// Gets a pointer to the native buffer data for unsafe operations.
        /// </summary>
        public static unsafe Color32* GetNativeBufferPtr(NativeArray<Color32> buffer)
        {
            return (Color32*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
        }
#endif

        /// <summary>
        /// Gets a managed buffer with at least the specified capacity.
        /// Thread-safe and works on all platforms including WebGL.
        /// </summary>
        public Color32[] GetManagedBuffer(int requiredCapacity)
        {
            if (_disposed) return new Color32[requiredCapacity];

            lock (_managedLock)
            {
                // Find an available buffer that's large enough
                for (int i = 0; i < MaxManagedPoolSize; i++)
                {
                    if (!_managedInUse[i] && _managedCapacities[i] >= requiredCapacity)
                    {
                        _managedInUse[i] = true;
                        return _managedBuffers[i];
                    }
                }

                // Find an unused slot or replace smallest
                int targetSlot = -1;
                int smallestCapacity = int.MaxValue;

                for (int i = 0; i < MaxManagedPoolSize; i++)
                {
                    if (!_managedInUse[i])
                    {
                        if (_managedCapacities[i] < smallestCapacity)
                        {
                            targetSlot = i;
                            smallestCapacity = _managedCapacities[i];
                        }
                    }
                }

                if (targetSlot >= 0)
                {
                    // Allocate new buffer (will cause GC, but cached for reuse)
                    _managedBuffers[targetSlot] = new Color32[requiredCapacity];
                    _managedCapacities[targetSlot] = requiredCapacity;
                    _managedInUse[targetSlot] = true;
                    return _managedBuffers[targetSlot];
                }
            }

            // All slots in use, allocate temporary (not pooled)
            return new Color32[requiredCapacity];
        }

        /// <summary>
        /// Returns a managed buffer to the pool.
        /// </summary>
        public void ReturnManagedBuffer(Color32[] buffer)
        {
            if (_disposed || buffer == null) return;

            lock (_managedLock)
            {
                for (int i = 0; i < MaxManagedPoolSize; i++)
                {
                    if (_managedBuffers[i] == buffer)
                    {
                        _managedInUse[i] = false;
                        return;
                    }
                }
            }
            // Buffer not from pool, will be GC'd
        }

        /// <summary>
        /// Clears all pooled buffers.
        /// </summary>
        public void Clear()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            while (_nativeBufferPool.TryDequeue(out var entry))
            {
                if (entry.Buffer.IsCreated)
                    entry.Buffer.Dispose();
            }
            _nativePoolCount = 0;
#endif

            lock (_managedLock)
            {
                for (int i = 0; i < MaxManagedPoolSize; i++)
                {
                    _managedBuffers[i] = null;
                    _managedCapacities[i] = 0;
                    _managedInUse[i] = false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();

            lock (_instanceLock)
            {
                if (_instance == this)
                    _instance = null;
            }
        }

        /// <summary>
        /// Resets the singleton instance. Use with caution.
        /// </summary>
        public static void ResetInstance()
        {
            lock (_instanceLock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }
    }
}
