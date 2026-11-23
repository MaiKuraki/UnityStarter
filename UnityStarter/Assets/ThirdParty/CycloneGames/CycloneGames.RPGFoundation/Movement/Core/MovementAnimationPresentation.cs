using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public readonly struct MovementRemoteSample : IEquatable<MovementRemoteSample>
    {
        public readonly long ServerTick;
        public readonly double TimestampSeconds;
        public readonly MovementVector3 Position;
        public readonly MovementVector3 Velocity;
        public readonly MovementVector3 Facing;
        public readonly MovementStateType State;
        public readonly MovementAnimationFlags Flags;

        public MovementRemoteSample(
            long serverTick,
            double timestampSeconds,
            MovementVector3 position,
            MovementVector3 velocity,
            MovementVector3 facing,
            MovementStateType state,
            MovementAnimationFlags flags)
        {
            ServerTick = serverTick;
            TimestampSeconds = double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds)
                ? 0.0 : timestampSeconds;
            Position = position.IsFinite ? position : MovementVector3.Zero;
            Velocity = velocity.IsFinite ? velocity : MovementVector3.Zero;
            Facing = facing.IsFinite ? facing : MovementVector3.Zero;
            State = state;
            Flags = flags;
        }

        public bool IsValid
        {
            get { return TimestampSeconds >= 0.0 && Position.IsFinite && Velocity.IsFinite; }
        }

        public bool Equals(MovementRemoteSample other)
        {
            return ServerTick == other.ServerTick
                && TimestampSeconds.Equals(other.TimestampSeconds)
                && Position.Equals(other.Position)
                && Velocity.Equals(other.Velocity)
                && Facing.Equals(other.Facing)
                && State == other.State
                && Flags == other.Flags;
        }

        public override bool Equals(object obj)
        {
            return obj is MovementRemoteSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ServerTick.GetHashCode();
                hash = (hash * 397) ^ TimestampSeconds.GetHashCode();
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ Velocity.GetHashCode();
                hash = (hash * 397) ^ (int)State;
                return hash;
            }
        }
    }

    public sealed class MovementRemoteInterpolator
    {
        private const int MaximumCapacityValue = 64;
        private const int AdaptiveLinearThreshold = 8;

        public static int MaximumCapacity { get { return MaximumCapacityValue; } }

        private readonly MovementRemoteSample[] _samples;
        private readonly int _capacity;
        private int _head;
        private int _count;
        private long _lastAcceptedTick = long.MinValue;
        private double _lastAcceptedTimestamp = double.NegativeInfinity;
        private double _interpolationDelaySeconds = 0.1;
        private double _maxExtrapolationSeconds = 0.25;
        private float _correctionHalfLifeSeconds = 0.08f;
        private MovementVector3 _correctionOffset;
        private long _acceptedCount;
        private long _rejectedCount;
        private long _extrapolatedCount;

        public MovementRemoteInterpolator(int capacity = 32,
            double interpolationDelaySeconds = 0.1,
            double maxExtrapolationSeconds = 0.25,
            float correctionHalfLifeSeconds = 0.08f)
        {
            if (capacity < 2 || capacity > MaximumCapacityValue)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _samples = new MovementRemoteSample[capacity];
            Configure(interpolationDelaySeconds, maxExtrapolationSeconds, correctionHalfLifeSeconds);
        }

        public void Configure(double interpolationDelaySeconds,
            double maxExtrapolationSeconds, float correctionHalfLifeSeconds)
        {
            _interpolationDelaySeconds = Sanitize(interpolationDelaySeconds, 0.1, 0.0, 2.0);
            _maxExtrapolationSeconds = Sanitize(maxExtrapolationSeconds, 0.25, 0.0, 1.0);
            _correctionHalfLifeSeconds = float.IsNaN(correctionHalfLifeSeconds)
                || float.IsInfinity(correctionHalfLifeSeconds) || correctionHalfLifeSeconds < 0.001f
                ? 0.08f : correctionHalfLifeSeconds;
        }

        public int BufferedCount { get { return _count; } }
        public long AcceptedCount { get { return _acceptedCount; } }
        public long RejectedCount { get { return _rejectedCount; } }
        public long ExtrapolatedCount { get { return _extrapolatedCount; } }
        public MovementVector3 CorrectionOffset { get { return _correctionOffset; } }

        public bool Enqueue(in MovementRemoteSample sample)
        {
            if (!sample.IsValid || sample.ServerTick <= _lastAcceptedTick
                || sample.TimestampSeconds < _lastAcceptedTimestamp)
            {
                if (_rejectedCount < long.MaxValue) _rejectedCount++;
                return false;
            }
            _samples[_head] = sample;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
            _lastAcceptedTick = sample.ServerTick;
            _lastAcceptedTimestamp = sample.TimestampSeconds;
            if (_acceptedCount < long.MaxValue) _acceptedCount++;
            return true;
        }

        public bool TrySample(double renderTime, out MovementRemoteSample result, out bool extrapolated)
        {
            result = default;
            extrapolated = false;
            if (_count == 0 || double.IsNaN(renderTime) || double.IsInfinity(renderTime)) return false;

            double target = renderTime - _interpolationDelaySeconds;
            int newest = (_head - 1 + _capacity) % _capacity;
            MovementRemoteSample newestSample = _samples[newest];

            if (_count == 1 || target >= newestSample.TimestampSeconds)
            {
                double ahead = target - newestSample.TimestampSeconds;
                if (ahead > _maxExtrapolationSeconds) ahead = _maxExtrapolationSeconds;
                if (ahead < 0.0) ahead = 0.0;
                if (ahead > 0.0)
                {
                    extrapolated = true;
                    if (_extrapolatedCount < long.MaxValue) _extrapolatedCount++;
                    MovementVector3 projected = newestSample.Position + newestSample.Velocity * (float)ahead;
                    result = new MovementRemoteSample(
                        newestSample.ServerTick, target, projected, newestSample.Velocity,
                        newestSample.Facing, newestSample.State, newestSample.Flags);
                    return true;
                }
                result = newestSample;
                return true;
            }

            int oldest = (_head - _count + _capacity * 2) % _capacity;
            MovementRemoteSample oldestSample = _samples[oldest];
            if (target <= oldestSample.TimestampSeconds)
            {
                result = oldestSample;
                return true;
            }

            int logicalA;
            if (_count <= AdaptiveLinearThreshold)
            {
                logicalA = -1;
                for (int k = 0; k < _count - 1; k++)
                {
                    int bLogical = _count - 1 - k;
                    int aLogical = bLogical - 1;
                    if (target >= TimestampAt(aLogical) && target <= TimestampAt(bLogical))
                    {
                        logicalA = aLogical;
                        break;
                    }
                }
                if (logicalA < 0)
                {
                    result = newestSample;
                    return true;
                }
            }
            else
            {
                int lo = 0;
                int hi = _count - 1;
                while (lo + 1 < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (target < TimestampAt(mid)) hi = mid;
                    else lo = mid;
                }
                logicalA = lo;
            }

            MovementRemoteSample sampleA = SampleAt(logicalA);
            MovementRemoteSample sampleB = SampleAt(logicalA + 1);
            double span = sampleB.TimestampSeconds - sampleA.TimestampSeconds;
            float alpha = span > 1E-9 ? (float)((target - sampleA.TimestampSeconds) / span) : 0f;
            if (alpha < 0f) alpha = 0f;
            if (alpha > 1f) alpha = 1f;

            MovementVector3 position = Lerp(sampleA.Position, sampleB.Position, alpha);
            MovementVector3 velocity = Lerp(sampleA.Velocity, sampleB.Velocity, alpha);
            MovementVector3 facing = Lerp(sampleA.Facing, sampleB.Facing, alpha);
            result = new MovementRemoteSample(
                sampleA.ServerTick, target, position, velocity, facing,
                sampleA.State, sampleA.Flags);

            int keep = _count - logicalA + 1;
            if (keep > _count) keep = _count;
            if (keep < _count) _count = keep;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PhysicalIndex(int logical)
        {
            return (_head - _count + logical + _capacity * 2) % _capacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double TimestampAt(int logical)
        {
            return _samples[PhysicalIndex(logical)].TimestampSeconds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MovementRemoteSample SampleAt(int logical)
        {
            return _samples[PhysicalIndex(logical)];
        }

        public void BeginCorrection(MovementVector3 positionError)
        {
            if (!positionError.IsFinite) positionError = MovementVector3.Zero;
            float length = positionError.Length;
            if (length > 8f) positionError = positionError * (8f / length);
            _correctionOffset = positionError;
        }

        public void AdvanceCorrection(float deltaTime)
        {
            if (_correctionOffset.SqrMagnitude < 1E-10f)
            {
                _correctionOffset = MovementVector3.Zero;
                return;
            }
            float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f
                ? 0f : deltaTime;
            double decay = Math.Exp(-dt / _correctionHalfLifeSeconds);
            _correctionOffset = _correctionOffset * (float)decay;
        }

        public MovementVector3 ApplyCorrection(MovementVector3 raw)
        {
            return raw + _correctionOffset;
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _lastAcceptedTick = long.MinValue;
            _lastAcceptedTimestamp = double.NegativeInfinity;
            _correctionOffset = MovementVector3.Zero;
            _acceptedCount = 0;
            _rejectedCount = 0;
            _extrapolatedCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MovementVector3 Lerp(MovementVector3 a, MovementVector3 b, float alpha)
        {
            return new MovementVector3(
                a.X + (b.X - a.X) * alpha,
                a.Y + (b.Y - a.Y) * alpha,
                a.Z + (b.Z - a.Z) * alpha);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Sanitize(double value, double fallback, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    public sealed class MovementRemotePresentationSink
    {
        private readonly MovementRemoteInterpolator _interpolator;
        private readonly IMovementLocomotionSink _target;
        private readonly MovementAnimationFrameBuilder _frames = new MovementAnimationFrameBuilder();
        private uint _publishedCount;

        public MovementRemotePresentationSink(IMovementLocomotionSink target, MovementRemoteInterpolator interpolator)
        {
            _target = target;
            _interpolator = interpolator;
        }

        public uint PublishedCount { get { return _publishedCount; } }
        public MovementRemoteInterpolator Interpolator { get { return _interpolator; } }

        public bool Enqueue(in MovementRemoteSample sample)
        {
            return _interpolator.Enqueue(in sample);
        }

        public bool Present(double renderTime, float deltaTime)
        {
            MovementRemoteSample sample;
            bool extrapolated;
            if (!_interpolator.TrySample(renderTime, out sample, out extrapolated)) return false;
            _interpolator.AdvanceCorrection(deltaTime);

            uint sequence = _frames.AdvanceSequence();
            MovementVector3 planarInput = new MovementVector3(sample.Velocity.X, sample.Velocity.Z, 0f);
            float planarLength = planarInput.Length;
            if (planarLength > 1f) planarInput = planarInput * (1f / planarLength);

            var frame = new MovementAnimationState(
                sequence, deltaTime, sample.State, sample.Flags,
                sample.Velocity, planarInput, MovementVector3.Up, MovementVector3.Up, sample.Facing);
            if (_target != null) _target.Apply(in frame);
            if (_publishedCount < uint.MaxValue) _publishedCount++;
            return true;
        }

        public void Reset()
        {
            _interpolator.Reset();
            _frames.ResetBaseline();
            _publishedCount = 0;
        }
    }

    public sealed class MovementRootMotionAccumulator
    {
        private MovementVector3 _pendingPosition;
        private float _pendingYaw;
        private float _pendingTime;
        private MovementVector3 _worldUp = MovementVector3.Up;
        private uint _sequence;
        private uint _droppedWindows;

        public uint DroppedWindowCount { get { return _droppedWindows; } }
        public float PendingTime { get { return _pendingTime; } }
        public bool HasPending { get { return _pendingTime > 0f; } }

        public void Push(in MovementRootMotionDelta delta)
        {
            if (!delta.IsValid) return;
            _pendingPosition = _pendingPosition + delta.Position;
            _pendingYaw += delta.YawRadians;
            _pendingTime += delta.DeltaTime;
            _worldUp = delta.WorldUp;
        }

        public bool TryConsumeStep(float stepDeltaTime, out MovementRootMotionDelta step)
        {
            step = default;
            if (_pendingTime <= 0f || float.IsNaN(stepDeltaTime)
                || float.IsInfinity(stepDeltaTime) || stepDeltaTime <= 0f)
            {
                return false;
            }

            float consumed = stepDeltaTime < _pendingTime ? stepDeltaTime : _pendingTime;
            float ratio = consumed / _pendingTime;
            MovementVector3 position = _pendingPosition * ratio;
            float yaw = _pendingYaw * ratio;

            _pendingPosition = _pendingPosition - position;
            _pendingYaw -= yaw;
            _pendingTime -= consumed;
            if (_pendingTime < 1E-6f)
            {
                _pendingPosition = MovementVector3.Zero;
                _pendingYaw = 0f;
                _pendingTime = 0f;
            }

            if (_sequence < uint.MaxValue) _sequence++;
            step = new MovementRootMotionDelta(_sequence, position, yaw, consumed, _worldUp);
            return step.IsValid && (position.SqrMagnitude > 0f || Math.Abs(yaw) > 1E-8f);
        }

        public void Discard()
        {
            if (_pendingTime > 0f && _droppedWindows < uint.MaxValue) _droppedWindows++;
            _pendingPosition = MovementVector3.Zero;
            _pendingYaw = 0f;
            _pendingTime = 0f;
        }
    }

    public sealed class CompositeMovementAnimationSink : IMovementLocomotionSink, IMovementActionSink
    {
        private readonly IMovementLocomotionSink[] _locomotion;
        private readonly IMovementActionSink[] _actions;
        private int _locomotionCount;
        private int _actionCount;

        public CompositeMovementAnimationSink(int locomotionCapacity = 4, int actionCapacity = 4)
        {
            if (locomotionCapacity < 1 || locomotionCapacity > 32)
                throw new ArgumentOutOfRangeException(nameof(locomotionCapacity));
            if (actionCapacity < 1 || actionCapacity > 32)
                throw new ArgumentOutOfRangeException(nameof(actionCapacity));
            _locomotion = new IMovementLocomotionSink[locomotionCapacity];
            _actions = new IMovementActionSink[actionCapacity];
        }

        public int LocomotionSinkCount { get { return _locomotionCount; } }
        public int ActionSinkCount { get { return _actionCount; } }

        public bool AddLocomotionSink(IMovementLocomotionSink sink)
        {
            if (sink == null || _locomotionCount >= _locomotion.Length) return false;
            for (int i = 0; i < _locomotionCount; i++)
                if (ReferenceEquals(_locomotion[i], sink)) return false;
            _locomotion[_locomotionCount++] = sink;
            return true;
        }

        public bool AddActionSink(IMovementActionSink sink)
        {
            if (sink == null || _actionCount >= _actions.Length) return false;
            for (int i = 0; i < _actionCount; i++)
                if (ReferenceEquals(_actions[i], sink)) return false;
            _actions[_actionCount++] = sink;
            return true;
        }

        public void Apply(in MovementAnimationState state)
        {
            for (int i = 0; i < _locomotionCount; i++) _locomotion[i].Apply(in state);
        }

        public void Play(in MovementAnimationAction action)
        {
            for (int i = 0; i < _actionCount; i++) _actions[i].Play(in action);
        }

        public void Clear()
        {
            for (int i = 0; i < _locomotionCount; i++) _locomotion[i] = null;
            for (int i = 0; i < _actionCount; i++) _actions[i] = null;
            _locomotionCount = 0;
            _actionCount = 0;
        }
    }

    public readonly struct MovementAnimationLayer : IEquatable<MovementAnimationLayer>
    {
        public readonly uint Id;
        public readonly MovementAnimationFlags Flags;
        public readonly float Weight;
        public readonly uint Sequence;

        public MovementAnimationLayer(uint id, MovementAnimationFlags flags, float weight, uint sequence)
        {
            Id = id;
            Flags = flags;
            Weight = float.IsNaN(weight) || float.IsInfinity(weight) ? 0f : weight;
            Sequence = sequence;
        }

        public bool Equals(MovementAnimationLayer other)
        {
            return Id == other.Id && Flags == other.Flags
                && Weight.Equals(other.Weight) && Sequence == other.Sequence;
        }

        public override bool Equals(object obj)
        {
            return obj is MovementAnimationLayer other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Id;
                hash = (hash * 397) ^ (int)Flags;
                hash = (hash * 397) ^ Weight.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class MovementLayerStack
    {
        private const int Capacity = 8;
        private readonly MovementAnimationLayer[] _layers = new MovementAnimationLayer[Capacity];
        private int _count;

        public int Count { get { return _count; } }

        public bool TrySet(uint id, MovementAnimationFlags flags, float weight, uint sequence)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_layers[i].Id != id) continue;
                _layers[i] = new MovementAnimationLayer(id, flags, weight, sequence);
                return true;
            }
            if (_count >= Capacity) return false;
            _layers[_count++] = new MovementAnimationLayer(id, flags, weight, sequence);
            return true;
        }

        public bool TryClear(uint id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_layers[i].Id != id) continue;
                for (int j = i + 1; j < _count; j++) _layers[j - 1] = _layers[j];
                _count--;
                return true;
            }
            return false;
        }

        public bool TryGet(uint id, out MovementAnimationLayer layer)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_layers[i].Id != id) continue;
                layer = _layers[i];
                return true;
            }
            layer = default;
            return false;
        }

        public MovementAnimationFlags CombinedFlags
        {
            get
            {
                MovementAnimationFlags flags = MovementAnimationFlags.None;
                for (int i = 0; i < _count; i++) flags |= _layers[i].Flags;
                return flags;
            }
        }

        public int Write(Span<MovementAnimationLayer> destination)
        {
            int n = _count < destination.Length ? _count : destination.Length;
            for (int i = 0; i < n; i++) destination[i] = _layers[i];
            return n;
        }

        public void Clear() { _count = 0; }
    }

    public static class MovementAnimationBlendWeights
    {
        // Accumulate in double: many float weights can overflow before the
        // inverse is applied.
        public static float Normalize(Span<float> weights)
        {
            double sum = 0.0;
            for (int i = 0; i < weights.Length; i++)
            {
                float value = weights[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) value = 0f;
                weights[i] = value;
                sum += value;
            }
            if (!(sum > 1E-6))
            {
                for (int i = 0; i < weights.Length; i++) weights[i] = 0f;
                return 0f;
            }
            double inverse = 1.0 / sum;
            for (int i = 0; i < weights.Length; i++)
                weights[i] = (float)((double)weights[i] * inverse);
            return sum > (double)float.MaxValue ? float.MaxValue : (float)sum;
        }

        public static float LayerWeight(float activeSum)
        {
            if (float.IsNaN(activeSum) || float.IsInfinity(activeSum) || activeSum <= 0f) return 0f;
            return activeSum > 1f ? 1f : activeSum;
        }

        public static float PlaceholderWeight(float normalizedActiveWeight)
        {
            float residual = 1f - normalizedActiveWeight;
            if (float.IsNaN(residual) || float.IsInfinity(residual)) return 0f;
            if (residual < 0f) return 0f;
            if (residual > 1f) return 1f;
            return residual;
        }
    }

    public static class MovementAnimationBackendOwnership
    {
        public static bool IsOwner(int backendCount, int selfOrder)
        {
            if (backendCount <= 1) return true;
            if (selfOrder < 0) return false;
            return selfOrder == 0;
        }
    }

    public readonly struct MovementBackendClaim : IEquatable<MovementBackendClaim>
    {
        public readonly int BackendCount;
        public readonly int SelfOrder;
        public readonly bool IsOwner;

        public MovementBackendClaim(int backendCount, int selfOrder)
        {
            BackendCount = backendCount;
            SelfOrder = selfOrder;
            IsOwner = MovementAnimationBackendOwnership.IsOwner(backendCount, selfOrder);
        }

        public bool Equals(MovementBackendClaim other)
        {
            return BackendCount == other.BackendCount
                && SelfOrder == other.SelfOrder
                && IsOwner == other.IsOwner;
        }

        public override bool Equals(object obj)
        {
            return obj is MovementBackendClaim other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BackendCount;
                hash = (hash * 397) ^ SelfOrder;
                hash = (hash * 397) ^ (IsOwner ? 1 : 0);
                return hash;
            }
        }
    }
}
