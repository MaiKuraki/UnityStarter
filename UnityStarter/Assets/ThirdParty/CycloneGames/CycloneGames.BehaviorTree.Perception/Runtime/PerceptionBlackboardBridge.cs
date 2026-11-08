using System;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Runtime.Integrations.Perception
{
    /// <summary>One tracked target, copied out of the sensor result for a single update.</summary>
    public readonly struct PerceptionTarget
    {
        public PerceptionTarget(
            PerceptibleHandle handle,
            float3 position,
            float distance,
            float visibility,
            bool isFromMemory)
        {
            Handle = handle;
            Position = position;
            Distance = distance;
            Visibility = visibility;
            IsFromMemory = isFromMemory;
        }

        public readonly PerceptibleHandle Handle;

        /// <summary>Last known position, in world space.</summary>
        public readonly float3 Position;

        /// <summary>Distance at the last detection, in the unit the sensor reports.</summary>
        public readonly float Distance;

        /// <summary>Visibility at the last detection, 0..1. Fades while a target is only remembered.</summary>
        public readonly float Visibility;

        /// <summary>True when the sensor reports this target from memory rather than from a live detection.</summary>
        public readonly bool IsFromMemory;
    }

    /// <summary>
    /// Reads one sensor and publishes its nearest target into blackboard slots.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Update"/> on the creating thread, after the perception update, then the
    /// <c>Publish*</c> methods. Buffers are allocated in the constructor; update and publish allocate
    /// nothing. Blackboard change detection drops unchanged writes.
    ///
    /// Sensor results are ordered by distance, ascending.
    /// </remarks>
    public sealed class PerceptionBlackboardBridge : IDisposable
    {
        private static readonly PerceptionTarget[] NoTargets = Array.Empty<PerceptionTarget>();

        private readonly ISensor _sensor;
        private readonly int _ownerThreadId;
        private PerceptionTarget[] _targets;
        private int _count;
        private bool _truncated;
        private bool _disposed;

        /// <param name="sensor">Sensor to read. The caller owns its lifetime.</param>
        /// <param name="capacity">Upper bound on tracked targets per update.</param>
        public PerceptionBlackboardBridge(ISensor sensor, int capacity = 8)
        {
            _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "At least one target must be tracked.");
            }

            _targets = new PerceptionTarget[capacity];
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public ISensor Sensor => _sensor;

        public int Capacity => _targets.Length;

        /// <summary>Targets captured by the last <see cref="Update"/>, nearest first.</summary>
        public int Count => _count;

        /// <summary>True when the sensor reported more targets than the capacity allows.</summary>
        public bool IsTruncated => _truncated;

        /// <summary>Nearest target of the last update; the handle is invalid when nothing was detected.</summary>
        public PerceptionTarget Nearest => _count > 0 ? _targets[0] : default;

        /// <summary>Captured targets, nearest first.</summary>
        public ReadOnlySpan<PerceptionTarget> Targets => new ReadOnlySpan<PerceptionTarget>(_targets, 0, _count);

        /// <summary>
        /// Copies the sensor result into the internal buffer.
        /// </summary>
        /// <returns>True when at least one target was captured.</returns>
        public bool Update()
        {
            ThrowIfDisposed();
            EnsureOwnerThread();

            int detected = _sensor.DetectedCount;
            int count = math.min(detected, _targets.Length);
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                if (!_sensor.TryGetResult(i, out DetectionResult result))
                {
                    break;
                }

                _targets[written++] = new PerceptionTarget(
                    result.Target,
                    result.LastKnownPosition,
                    result.Distance,
                    result.Visibility,
                    result.IsFromMemory);
            }

            _count = written;
            _truncated = detected > _targets.Length;
            return _count > 0;
        }

        /// <summary>
        /// Writes visibility and distance of the nearest target. An empty result writes false and a
        /// zero distance.
        /// </summary>
        public void PublishNearest(RuntimeBlackboard blackboard, int visibleKey, int distanceKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetBool(visibleKey, _count > 0);
            blackboard.SetFloat(distanceKey, _count > 0 ? _targets[0].Distance : 0f);
        }

        /// <summary>Writes the nearest target position as an engine-free float triple.</summary>
        public void PublishNearestPosition(RuntimeBlackboard blackboard, int positionKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            float3 position = _count > 0 ? _targets[0].Position : float3.zero;
            blackboard.SetFloat3(positionKey, new BTFloat3(position.x, position.y, position.z));
        }

        /// <summary>
        /// Writes the nearest target identity as a triple: registry, id and generation.
        /// </summary>
        /// <remarks>
        /// The slot holds three longs: registry, id and generation. Without a target it is zeroed, which
        /// <see cref="TryUnpackHandle"/> reports as no target.
        /// </remarks>
        public void PublishNearestHandle(RuntimeBlackboard blackboard, int handleKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetLong3(handleKey, PackHandle(_count > 0 ? _targets[0].Handle : default));
        }

        /// <summary>Writes whether the nearest target comes from memory rather than a live detection.</summary>
        public void PublishNearestFromMemory(RuntimeBlackboard blackboard, int fromMemoryKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetBool(fromMemoryKey, _count > 0 && _targets[0].IsFromMemory);
        }

        /// <summary>Packs a handle into the three-slot form written by <see cref="PublishNearestHandle"/>.</summary>
        public static RuntimeBlackboardLong3 PackHandle(in PerceptibleHandle handle)
        {
            return new RuntimeBlackboardLong3(handle.RegistryId, handle.Id, handle.Generation);
        }

        /// <summary>Reads a handle written by <see cref="PublishNearestHandle"/>; false means "no target".</summary>
        public static bool TryUnpackHandle(in RuntimeBlackboardLong3 packed, out PerceptibleHandle handle)
        {
            if (packed.Y < int.MinValue || packed.Y > int.MaxValue ||
                packed.X < int.MinValue || packed.X > int.MaxValue ||
                packed.Z < int.MinValue || packed.Z > int.MaxValue)
            {
                handle = PerceptibleHandle.Invalid;
                return false;
            }

            var candidate = new PerceptibleHandle((int)packed.X, (int)packed.Y, (int)packed.Z);
            if (!candidate.IsValid)
            {
                handle = PerceptibleHandle.Invalid;
                return false;
            }

            handle = candidate;
            return true;
        }

        /// <summary>Writes how many targets the last update captured.</summary>
        public void PublishCount(RuntimeBlackboard blackboard, int countKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetInt(countKey, _count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _count = 0;
            _truncated = false;
            _targets = NoTargets;
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "PerceptionBlackboardBridge must be used from the thread that created it; sensor results are committed there.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PerceptionBlackboardBridge));
            }
        }
    }
}
