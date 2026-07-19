using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

using Cysharp.Threading.Tasks;
using R3;

using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Tools
{
    public enum InputSampleValueKind : byte
    {
        Button,
        Vector2,
        Scalar
    }

    public enum InputSamplePhase : byte
    {
        Performed,
        ValueChanged
    }

    /// <summary>
    /// Immutable input sample captured on the Unity main thread.
    /// Tick and Order are the authoritative replay ordering keys. TimeSinceStartSeconds is diagnostic only.
    /// </summary>
    public readonly struct InputSample
    {
        public int PlayerId { get; }
        public int ActionId { get; }
        public string ContextName { get; }
        public string ActionMapName { get; }
        public string ActionName { get; }
        public InputSampleValueKind ValueKind { get; }
        public InputSamplePhase Phase { get; }
        public InputUpdateType UpdateType { get; }
        public long Tick { get; }
        public ulong Order { get; }
        public double TimeSinceStartSeconds { get; }
        public Vector2 Vector2Value { get; }
        public float ScalarValue { get; }

        internal InputSample(
            int playerId,
            int actionId,
            string contextName,
            string actionMapName,
            string actionName,
            InputSampleValueKind valueKind,
            InputSamplePhase phase,
            InputUpdateType updateType,
            long tick,
            ulong order,
            double timeSinceStartSeconds,
            Vector2 vector2Value,
            float scalarValue)
        {
            PlayerId = playerId;
            ActionId = actionId;
            ContextName = contextName;
            ActionMapName = actionMapName;
            ActionName = actionName;
            ValueKind = valueKind;
            Phase = phase;
            UpdateType = updateType;
            Tick = tick;
            Order = order;
            TimeSinceStartSeconds = timeSinceStartSeconds;
            Vector2Value = vector2Value;
            ScalarValue = scalarValue;
        }
    }

    /// <summary>
    /// Main-thread-only, opt-in recorder with a fixed sample capacity.
    /// Recording never grows its sample storage after construction.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "CycloneGames.InputSystem.Runtime", sourceAssembly: "CycloneGames.InputSystem.Tools.Runtime")]
    public sealed class InputRecorder : IDisposable
    {
        public const int UNKNOWN_PLAYER_ID = -1;
        public const int DEFAULT_SAMPLE_CAPACITY = 4096;
        public const int MAX_SAMPLE_CAPACITY = 1_048_576;
        public const int DEFAULT_ACTION_CAPACITY = 32;
        public const int MAX_ACTION_COUNT = 1024;

        private readonly List<RecordedAction> _actionsToRecord;
        private readonly InputSampleBuffer _sampleBuffer;
        private CompositeDisposable _subscriptions;
        private double _recordingStartTime;
        private ulong _nextOrder;
        private int _playerId;
        private int _ownerThreadId;
        private bool _isRecording;
        private bool _isDisposed;

        public bool IsRecording => _isRecording;
        public int SampleCapacity => _sampleBuffer.Capacity;
        public int RecordedSampleCount => _sampleBuffer.Count;
        public int DroppedSampleCount => _sampleBuffer.DroppedSampleCount;
        public bool HasOverflowed => _sampleBuffer.DroppedSampleCount > 0;

        public InputRecorder(
            int sampleCapacity = DEFAULT_SAMPLE_CAPACITY,
            int actionCapacity = DEFAULT_ACTION_CAPACITY)
        {
            if (sampleCapacity <= 0 || sampleCapacity > MAX_SAMPLE_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCapacity));
            }

            if (actionCapacity <= 0 || actionCapacity > MAX_ACTION_COUNT)
            {
                throw new ArgumentOutOfRangeException(nameof(actionCapacity));
            }

            _sampleBuffer = new InputSampleBuffer(sampleCapacity);
            _actionsToRecord = new List<RecordedAction>(actionCapacity);
        }

        public void RecordAction(
            string contextName,
            string actionMapName,
            string actionName)
        {
            ThrowIfDisposed();

            if (_isRecording)
            {
                throw new InvalidOperationException("Actions cannot be changed while recording.");
            }

            if (string.IsNullOrWhiteSpace(contextName))
            {
                throw new ArgumentException("Context name is required.", nameof(contextName));
            }

            if (string.IsNullOrWhiteSpace(actionMapName))
            {
                throw new ArgumentException("Action map name is required.", nameof(actionMapName));
            }

            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new ArgumentException("Action name is required.", nameof(actionName));
            }

            for (int i = 0; i < _actionsToRecord.Count; i++)
            {
                RecordedAction existing = _actionsToRecord[i];
                if (string.Equals(existing.ContextName, contextName, StringComparison.Ordinal) &&
                    string.Equals(existing.ActionMapName, actionMapName, StringComparison.Ordinal) &&
                    string.Equals(existing.ActionName, actionName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            if (_actionsToRecord.Count >= MAX_ACTION_COUNT)
            {
                throw new InvalidOperationException($"At most {MAX_ACTION_COUNT} actions can be recorded.");
            }

            _actionsToRecord.Add(new RecordedAction(
                contextName,
                actionMapName,
                actionName,
                InputHashUtility.GetActionId(contextName, actionMapName, actionName)));
        }

        public void StartRecording(IInputPlayer player)
        {
            int playerId = player?.PlayerId ?? UNKNOWN_PLAYER_ID;
            StartRecording(player, playerId);
        }

        public void StartRecording(IInputPlayer player, int playerId)
        {
            ThrowIfDisposed();

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            if (_isRecording)
            {
                throw new InvalidOperationException("The recorder is already running.");
            }

            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "InputRecorder must be started on the Unity main thread.");
            }

            _ownerThreadId = Environment.CurrentManagedThreadId;
            _playerId = playerId;
            _recordingStartTime = Time.realtimeSinceStartupAsDouble;
            _nextOrder = 0;
            _sampleBuffer.Clear();
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();
            _isRecording = true;

            try
            {
                for (int i = 0; i < _actionsToRecord.Count; i++)
                {
                    RecordedAction action = _actionsToRecord[i];

                    player.GetButtonObservable(
                            action.ContextName,
                            action.ActionMapName,
                            action.ActionName)
                        .Subscribe(_ => AppendSample(
                            action,
                            InputSampleValueKind.Button,
                            InputSamplePhase.Performed,
                            default,
                            0f))
                        .AddTo(_subscriptions);

                    player.GetVector2Observable(
                            action.ContextName,
                            action.ActionMapName,
                            action.ActionName)
                        .Subscribe(value => AppendSample(
                            action,
                            InputSampleValueKind.Vector2,
                            InputSamplePhase.ValueChanged,
                            value,
                            0f))
                        .AddTo(_subscriptions);

                    player.GetScalarObservable(
                            action.ContextName,
                            action.ActionMapName,
                            action.ActionName)
                        .Subscribe(value => AppendSample(
                            action,
                            InputSampleValueKind.Scalar,
                            InputSamplePhase.ValueChanged,
                            default,
                            value))
                        .AddTo(_subscriptions);
                }
            }
            catch
            {
                _isRecording = false;
                _subscriptions.Dispose();
                _subscriptions = null;
                _sampleBuffer.Clear();
                throw;
            }
        }

        public InputRecording StopRecording()
        {
            ThrowIfDisposed();

            if (!_isRecording)
            {
                return null;
            }

            EnsureOwnerThread();
            _isRecording = false;
            _subscriptions?.Dispose();
            _subscriptions = null;

            var recording = new InputRecording(
                _sampleBuffer.ToArray(),
                _sampleBuffer.Capacity,
                _sampleBuffer.DroppedSampleCount);
            _sampleBuffer.Clear();
            return recording;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isRecording)
            {
                EnsureOwnerThread();
            }

            _isRecording = false;
            _subscriptions?.Dispose();
            _subscriptions = null;
            _sampleBuffer.Clear();
            _isDisposed = true;
        }

        private void AppendSample(
            RecordedAction action,
            InputSampleValueKind valueKind,
            InputSamplePhase phase,
            Vector2 vector2Value,
            float scalarValue)
        {
            if (!_isRecording)
            {
                return;
            }

            EnsureOwnerThread();

            var sample = new InputSample(
                _playerId,
                action.ActionId,
                action.ContextName,
                action.ActionMapName,
                action.ActionName,
                valueKind,
                phase,
                InputState.currentUpdateType,
                InputSystemFrameProvider.BeforeUpdate.GetFrameCount(),
                _nextOrder,
                Time.realtimeSinceStartupAsDouble - _recordingStartTime,
                vector2Value,
                scalarValue);

            if (_sampleBuffer.TryAdd(sample))
            {
                _nextOrder++;
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "InputRecorder is main-thread confined and must be used from its recording owner thread.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(InputRecorder));
            }
        }

        private readonly struct RecordedAction
        {
            internal readonly string ContextName;
            internal readonly string ActionMapName;
            internal readonly string ActionName;
            internal readonly int ActionId;

            internal RecordedAction(
                string contextName,
                string actionMapName,
                string actionName,
                int actionId)
            {
                ContextName = contextName;
                ActionMapName = actionMapName;
                ActionName = actionName;
                ActionId = actionId;
            }
        }
    }

    internal sealed class InputSampleBuffer
    {
        private readonly List<InputSample> _samples;

        internal int Capacity { get; }
        internal int Count => _samples.Count;
        internal int DroppedSampleCount { get; private set; }

        internal InputSampleBuffer(int capacity)
        {
            Capacity = capacity;
            _samples = new List<InputSample>(capacity);
        }

        internal bool TryAdd(InputSample sample)
        {
            if (_samples.Count >= Capacity)
            {
                if (DroppedSampleCount < int.MaxValue)
                {
                    DroppedSampleCount++;
                }

                return false;
            }

            _samples.Add(sample);
            return true;
        }

        internal InputSample[] ToArray()
        {
            return _samples.ToArray();
        }

        internal void Clear()
        {
            _samples.Clear();
            DroppedSampleCount = 0;
        }
    }

    /// <summary>
    /// Immutable recording ordered by Tick and Order.
    /// Consumers drive replay explicitly through InputReplayCursor.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "CycloneGames.InputSystem.Runtime", sourceAssembly: "CycloneGames.InputSystem.Tools.Runtime")]
    public sealed class InputRecording
    {
        private readonly InputSample[] _samples;

        public int SampleCount => _samples.Length;
        public int TickCount { get; }
        [Obsolete("Use TickCount. Input recordings count Input System update ticks, not rendered frames.")]
        public int FrameCount => TickCount;
        public int Capacity { get; }
        public int DroppedSampleCount { get; }
        public bool WasTruncated => DroppedSampleCount > 0;
        public double DurationSeconds =>
            _samples.Length > 0 ? _samples[_samples.Length - 1].TimeSinceStartSeconds : 0d;
        public float Duration => (float)DurationSeconds;
        public ReadOnlySpan<InputSample> Samples => _samples;
        public InputSample this[int index] => _samples[index];

        internal InputRecording(InputSample[] samples, int capacity, int droppedSampleCount)
        {
            _samples = samples ?? throw new ArgumentNullException(nameof(samples));
            Capacity = capacity;
            DroppedSampleCount = droppedSampleCount;
            ValidateOrdering(_samples);
            TickCount = CountUniqueTicks(_samples);
        }

        public InputReplayCursor CreateCursor()
        {
            return new InputReplayCursor(this);
        }

        internal InputSample GetSample(int index)
        {
            return _samples[index];
        }

        private static void ValidateOrdering(InputSample[] samples)
        {
            for (int i = 1; i < samples.Length; i++)
            {
                InputSample previous = samples[i - 1];
                InputSample current = samples[i];
                if (current.Tick < previous.Tick ||
                    (current.Tick == previous.Tick && current.Order <= previous.Order))
                {
                    throw new ArgumentException(
                        "Input samples must be strictly ordered by tick and order.",
                        nameof(samples));
                }
            }
        }

        private static int CountUniqueTicks(InputSample[] samples)
        {
            if (samples.Length == 0)
            {
                return 0;
            }

            int count = 1;
            long previousTick = samples[0].Tick;
            for (int i = 1; i < samples.Length; i++)
            {
                long currentTick = samples[i].Tick;
                if (currentTick == previousTick)
                {
                    continue;
                }

                count++;
                previousTick = currentTick;
            }

            return count;
        }
    }

    /// <summary>
    /// Allocation-free cursor for deterministic, caller-driven replay.
    /// </summary>
    public struct InputReplayCursor
    {
        private readonly InputRecording _recording;
        private int _nextIndex;

        public int NextIndex => _nextIndex;
        public bool HasNext => _recording != null && _nextIndex < _recording.SampleCount;

        internal InputReplayCursor(InputRecording recording)
        {
            _recording = recording;
            _nextIndex = 0;
        }

        public bool TryReadNext(out InputSample sample)
        {
            if (!HasNext)
            {
                sample = default;
                return false;
            }

            sample = _recording.GetSample(_nextIndex);
            _nextIndex++;
            return true;
        }

        public bool TryReadNext(long maximumTickInclusive, out InputSample sample)
        {
            if (!HasNext)
            {
                sample = default;
                return false;
            }

            InputSample next = _recording.GetSample(_nextIndex);
            if (next.Tick > maximumTickInclusive)
            {
                sample = default;
                return false;
            }

            sample = next;
            _nextIndex++;
            return true;
        }
    }
}
