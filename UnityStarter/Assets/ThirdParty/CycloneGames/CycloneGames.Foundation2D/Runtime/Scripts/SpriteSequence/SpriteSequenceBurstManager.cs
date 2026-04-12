using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

#if BURST_JOBS
using Unity.Burst;
#endif

namespace CycloneGames.Foundation2D.Runtime
{
#if BURST_JOBS
    [BurstCompile]
#endif
    public struct SpriteSequenceBatchUpdateJob : IJobParallelFor
    {
        public NativeArray<SpriteSequencePlaybackState> States;
        public NativeArray<byte> FrameChanged;
        [ReadOnly] public NativeArray<double> DeltaTimes;
        public double NowTime;

        public void Execute(int index)
        {
            SpriteSequencePlaybackState state = States[index];
            bool changed = state.Update(DeltaTimes[index], NowTime);
            States[index] = state;
            FrameChanged[index] = changed ? (byte)1 : (byte)0;
        }
    }

    public sealed class SpriteSequenceBurstManager : MonoBehaviour
    {
        private static readonly Dictionary<SpriteSequenceController, int> ManagedRefCounts = new(256);
        private static int _activeManagerCount;

        public static bool HasActiveManager => _activeManagerCount > 0;

        public static bool IsControllerManaged(SpriteSequenceController controller)
        {
            if (controller == null)
            {
                return false;
            }

            return ManagedRefCounts.TryGetValue(controller, out int refCount) && refCount > 0;
        }

        [SerializeField] private List<SpriteSequenceController> controllers = new();
        [SerializeField] private bool autoCollectChildren = true;

        private sealed class JobBuffer
        {
            public NativeArray<SpriteSequencePlaybackState> States;
            public NativeArray<byte> Changed;
            public NativeArray<double> Deltas;
            public readonly List<SpriteSequenceController> Controllers = new(256);
            public JobHandle Handle;
            public int Capacity;
            public int Count;
            public bool Scheduled;
        }

        private readonly JobBuffer[] _buffers = { new JobBuffer(), new JobBuffer() };
        private int _writeBufferIndex;
        private readonly HashSet<SpriteSequenceController> _registeredControllers = new(256);

        private void OnEnable()
        {
            _activeManagerCount++;
            RefreshControllers();
            EnsureCapacity(_buffers[0], controllers.Count);
            EnsureCapacity(_buffers[1], controllers.Count);
        }

        private void OnDisable()
        {
            UnregisterManagedControllers();
            _activeManagerCount = Mathf.Max(0, _activeManagerCount - 1);

            for (int i = 0; i < _buffers.Length; i++)
            {
                CompleteAndApply(_buffers[i]);
                DisposeArrays(_buffers[i]);
            }
        }

        private void Update()
        {
            if (autoCollectChildren && (controllers == null || controllers.Count == 0))
            {
                RefreshControllers();
                EnsureCapacity(_buffers[0], controllers.Count);
                EnsureCapacity(_buffers[1], controllers.Count);
            }

            int count = controllers.Count;
            if (count == 0)
            {
                CompleteAndApply(_buffers[0]);
                CompleteAndApply(_buffers[1]);
                return;
            }

            int readBufferIndex = 1 - _writeBufferIndex;
            JobBuffer readBuffer = _buffers[readBufferIndex];
            JobBuffer writeBuffer = _buffers[_writeBufferIndex];

            CompleteAndApply(readBuffer);
            CollectActiveControllerSnapshot(writeBuffer, count);

            if (writeBuffer.Count == 0)
            {
                return;
            }

            EnsureCapacity(writeBuffer, writeBuffer.Count);

            for (int i = 0; i < writeBuffer.Count; i++)
            {
                SpriteSequenceController c = writeBuffer.Controllers[i];
                writeBuffer.States[i] = c.GetPlaybackState();
                writeBuffer.Deltas[i] = c.IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            }

#if UNITY_2020_2_OR_NEWER
            double now = Time.timeAsDouble;
#else
        double now = Time.time;
#endif

            var job = new SpriteSequenceBatchUpdateJob
            {
                States = writeBuffer.States,
                FrameChanged = writeBuffer.Changed,
                DeltaTimes = writeBuffer.Deltas,
                NowTime = now
            };

            writeBuffer.Handle = job.Schedule(writeBuffer.Count, 32);
            writeBuffer.Scheduled = true;
            _writeBufferIndex = readBufferIndex;
        }

        public void RefreshControllers()
        {
            controllers.Clear();
            GetComponentsInChildren(true, controllers);

            if (controllers.Count == 0)
            {
#if UNITY_2023_1_OR_NEWER
            var allControllers = FindObjectsByType<SpriteSequenceController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                var allControllers = FindObjectsOfType<SpriteSequenceController>(true);
#endif
                if (allControllers != null && allControllers.Length > 0)
                {
                    controllers.AddRange(allControllers);
                }
            }

            RegisterManagedControllers();
        }

        private void CollectActiveControllerSnapshot(JobBuffer buffer, int controllerCount)
        {
            buffer.Controllers.Clear();
            for (int i = 0; i < controllerCount; i++)
            {
                SpriteSequenceController c = controllers[i];
                if (c == null || !c.IsBurstDriven)
                {
                    continue;
                }

                SpriteSequencePlaybackState state = c.GetPlaybackState();
                if (state.State != 1)
                {
                    continue;
                }

                buffer.Controllers.Add(c);
            }

            buffer.Count = buffer.Controllers.Count;
        }

        private static void CompleteAndApply(JobBuffer buffer)
        {
            if (!buffer.Scheduled)
            {
                return;
            }

            buffer.Handle.Complete();
            for (int i = 0; i < buffer.Count; i++)
            {
                SpriteSequenceController c = buffer.Controllers[i];
                if (c == null || !c.IsBurstDriven)
                {
                    continue;
                }

                c.SetPlaybackStateFromJob(buffer.States[i], buffer.Changed[i] == 1);
            }

            buffer.Scheduled = false;
            buffer.Count = 0;
            buffer.Controllers.Clear();
        }

        private static void EnsureCapacity(JobBuffer buffer, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (buffer.States.IsCreated && buffer.Capacity >= count)
            {
                return;
            }

            int newCapacity = buffer.Capacity <= 0 ? 16 : buffer.Capacity;
            while (newCapacity < count)
            {
                newCapacity <<= 1;
            }

            DisposeArrays(buffer);
            buffer.Capacity = newCapacity;
            buffer.States = new NativeArray<SpriteSequencePlaybackState>(buffer.Capacity, Allocator.Persistent);
            buffer.Changed = new NativeArray<byte>(buffer.Capacity, Allocator.Persistent);
            buffer.Deltas = new NativeArray<double>(buffer.Capacity, Allocator.Persistent);
        }

        private static void DisposeArrays(JobBuffer buffer)
        {
            buffer.Capacity = 0;
            buffer.Scheduled = false;
            buffer.Count = 0;
            buffer.Controllers.Clear();

            if (buffer.States.IsCreated)
            {
                buffer.States.Dispose();
            }

            if (buffer.Changed.IsCreated)
            {
                buffer.Changed.Dispose();
            }

            if (buffer.Deltas.IsCreated)
            {
                buffer.Deltas.Dispose();
            }
        }

        private void RegisterManagedControllers()
        {
            UnregisterManagedControllers();

            for (int i = 0; i < controllers.Count; i++)
            {
                SpriteSequenceController c = controllers[i];
                if (c == null)
                {
                    continue;
                }

                _registeredControllers.Add(c);
                if (ManagedRefCounts.TryGetValue(c, out int count))
                {
                    ManagedRefCounts[c] = count + 1;
                }
                else
                {
                    ManagedRefCounts[c] = 1;
                }
            }
        }

        private void UnregisterManagedControllers()
        {
            if (_registeredControllers.Count == 0)
            {
                return;
            }

            foreach (var c in _registeredControllers)
            {
                if (c == null)
                {
                    continue;
                }

                if (!ManagedRefCounts.TryGetValue(c, out int count))
                {
                    continue;
                }

                if (count <= 1)
                {
                    ManagedRefCounts.Remove(c);
                }
                else
                {
                    ManagedRefCounts[c] = count - 1;
                }
            }

            _registeredControllers.Clear();
        }
    }
}