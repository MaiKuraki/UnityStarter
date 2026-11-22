using System;
using System.Threading;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>Stable identity for a feedback target supplied by the input/product layer.</summary>
    public readonly struct FeedbackDeviceId : IEquatable<FeedbackDeviceId>
    {
        public FeedbackDeviceId(int value) { Value = value; }
        public int Value { get; }
        public static FeedbackDeviceId Default => default(FeedbackDeviceId);
        public bool Equals(FeedbackDeviceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FeedbackDeviceId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(FeedbackDeviceId a, FeedbackDeviceId b) => a.Equals(b);
        public static bool operator !=(FeedbackDeviceId a, FeedbackDeviceId b) => !a.Equals(b);
    }

    public enum FeedbackPriority : byte
    {
        Immediate = 0,
        Normal = 1,
        Background = 2
    }

    public enum FeedbackCommandKind : byte
    {
        Play = 0,
        PlayPreset = 1,
        PlayClipSnapshot = 2,
        VibrateMilliseconds = 3,
        Cancel = 4
    }

    /// <summary>
    /// Allocation-free value payload for cross-thread submission. All fields are
    /// value types; no closures, no delegates, no Unity objects.
    /// </summary>
    public struct FeedbackCommand
    {
        public FeedbackCommandKind Kind;
        public FeedbackPriority Priority;
        public FeedbackDeviceId DeviceId;
        public float A;
        public float B;
        public float C;
        public long DurationMilliseconds;
        public int Index;
        public HapticClipSnapshot Snapshot;
    }

    /// <summary>Pre-baked dispatch data for one HapticPreset.</summary>
    internal readonly struct PresetPayload
    {
        public PresetPayload(
            float intensity, float sharpness, long durationMilliseconds,
            int fallbackMilliseconds, int primitiveId, float primitiveScale,
            bool isNotification, bool hasPattern)
        {
            Intensity = intensity;
            Sharpness = sharpness;
            DurationMilliseconds = durationMilliseconds;
            FallbackMilliseconds = fallbackMilliseconds;
            PrimitiveId = primitiveId;
            PrimitiveScale = primitiveScale;
            IsNotification = isNotification;
            HasPattern = hasPattern;
        }

        public readonly float Intensity;
        public readonly float Sharpness;
        public readonly long DurationMilliseconds;
        public readonly int FallbackMilliseconds;
        public readonly int PrimitiveId;
        public readonly float PrimitiveScale;
        public readonly bool IsNotification;
        public readonly bool HasPattern;
    }

    internal static class PresetTable
    {
        public static readonly PresetPayload[] Payloads =
        {
            new PresetPayload(0.25f, 0.8f, 20L, 20, 7, 0.4f, false, false),   // Light
            new PresetPayload(0.5f, 0.5f, 40L, 40, 1, 0.6f, false, false),    // Medium
            new PresetPayload(1f, 0.25f, 80L, 80, 3, 1f, false, false),        // Heavy
            new PresetPayload(0.65f, 0.7f, 60L, 60, 1, 0.8f, true, false),     // Success
            new PresetPayload(0.55f, 0.4f, 50L, 50, 1, 0.5f, true, true),      // Warning
            new PresetPayload(0.9f, 0.9f, 80L, 80, 3, 0.7f, true, true),       // Error
            new PresetPayload(0.2f, 1f, 10L, 10, 8, 0.3f, false, false)        // Selection
        };

    }

    public readonly struct DeviceFeedbackCommandStats
    {
        public DeviceFeedbackCommandStats(int capacity, long enqueued, long rejected, long drained, long deferred, int pending)
        {
            Capacity = capacity; EnqueuedCount = enqueued; RejectedCount = rejected;
            DrainedCount = drained; DeferredCount = deferred; PendingCount = pending;
        }
        public int Capacity { get; }
        public long EnqueuedCount { get; }
        public long RejectedCount { get; }
        public long DrainedCount { get; }
        public long DeferredCount { get; }
        public int PendingCount { get; }
    }

    /// <summary>
    /// Bounded multi-producer / single-consumer command ring.
    /// TryEnqueue is safe from any thread; Drain is owner-thread only. Each slot
    /// has a free / writing / ready state, so a producer still writing defers that
    /// slot to the next drain. Capacity is fixed; overflow is refused and counted.
    /// Synchronous playback on the owner thread never enters the ring.
    /// </summary>
    internal sealed class FeedbackCommandQueue
    {
        private const int StateFree = 0;
        private const int StateWriting = 1;
        private const int StateReady = 2;

        private readonly FeedbackCommand[] _slots;
        private readonly int[] _states;
        private readonly int _mask;
        private int _writeCursor;
        private int _readCursor;
        private long _enqueued;
        private long _rejected;
        private long _drained;
        private long _deferred;

        public FeedbackCommandQueue(int capacityPowerOfTwo)
        {
            if (capacityPowerOfTwo < 2 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityPowerOfTwo));
            }
            _slots = new FeedbackCommand[capacityPowerOfTwo];
            _states = new int[capacityPowerOfTwo];
            _mask = capacityPowerOfTwo - 1;
        }

        public int Capacity => _slots.Length;
        public long EnqueuedCount => Interlocked.Read(ref _enqueued);
        public long RejectedCount => Interlocked.Read(ref _rejected);
        public long DrainedCount => Interlocked.Read(ref _drained);
        public long DeferredCount => Interlocked.Read(ref _deferred);

        public DeviceFeedbackCommandStats GetStats()
        {
            int pending = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (Volatile.Read(ref _states[i]) == StateReady) pending++;
            }
            return new DeviceFeedbackCommandStats(
                _slots.Length,
                Interlocked.Read(ref _enqueued),
                Interlocked.Read(ref _rejected),
                Interlocked.Read(ref _drained),
                Interlocked.Read(ref _deferred),
                pending);
        }

        public bool TryEnqueue(in FeedbackCommand command)
        {
            int ticket = unchecked(Interlocked.Increment(ref _writeCursor) - 1);
            int index = ticket & _mask;
            if (Interlocked.CompareExchange(ref _states[index], StateWriting, StateFree) != StateFree)
            {
                Interlocked.Increment(ref _rejected);
                return false;
            }
            _slots[index] = command;
            Volatile.Write(ref _states[index], StateReady);
            Interlocked.Increment(ref _enqueued);
            return true;
        }

        public int Drain(Action<FeedbackCommand> dispatch, int maxCommands)
        {
            if (dispatch == null || maxCommands <= 0) return 0;
            int processed = 0;
            while (processed < maxCommands)
            {
                int index = _readCursor & _mask;
                int state = Volatile.Read(ref _states[index]);
                if (state == StateWriting)
                {
                    Interlocked.Increment(ref _deferred);
                    break;
                }
                if (state != StateReady) break;

                FeedbackCommand command = _slots[index];
                _slots[index] = default(FeedbackCommand);
                Volatile.Write(ref _states[index], StateFree);
                _readCursor++;
                processed++;
                dispatch(command);
                Interlocked.Increment(ref _drained);
            }
            return processed;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = default(FeedbackCommand);
                Volatile.Write(ref _states[i], StateFree);
            }
            _readCursor = 0;
            Interlocked.Exchange(ref _writeCursor, 0);
        }
    }
}
