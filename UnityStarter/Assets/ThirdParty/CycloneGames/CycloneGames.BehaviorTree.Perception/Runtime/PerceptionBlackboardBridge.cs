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
    /// Call <see cref="Update"/> once per owner-thread frame after the perception update, then the
    /// <c>Publish*</c> methods. Buffers are allocated in the constructor; update and publish allocate
    /// nothing. Writes go through the blackboard's change detection, so a stable target neither
    /// notifies observers nor enters the network delta.
    ///
    /// The sensor sorts its results by distance, so the first entry is the nearest target and the
    /// remaining entries are ordered behind it.
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
        /// zero distance, so a caller must read the flag before using the distance.
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
