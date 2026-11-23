using System;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public interface IMovementDeterministicStep
    {
        bool Step(float deltaTime);
    }

    public readonly struct MovementLayerFrame
    {
        public readonly uint Sequence;
        public readonly float DeltaTime;
        public readonly MovementVector3 Velocity;
        public readonly MovementVector3 WorldUp;
        public readonly bool IsGrounded;

        public MovementLayerFrame(uint sequence, float deltaTime, MovementVector3 velocity,
            MovementVector3 worldUp, bool isGrounded)
        {
            Sequence = sequence;
            DeltaTime = deltaTime > 0f && !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime) ? deltaTime : 0f;
            Velocity = velocity.IsFinite ? velocity : MovementVector3.Zero;
            WorldUp = worldUp.IsFinite ? worldUp : MovementVector3.Up;
            IsGrounded = isGrounded;
        }
    }

    public struct MovementLayerOutput
    {
        public MovementVector3 VelocityDelta;
        public MovementVector3 PositionDelta;
        public float Weight;

        public void Add(in MovementLayerOutput other)
        {
            VelocityDelta = VelocityDelta + other.VelocityDelta;
            PositionDelta = PositionDelta + other.PositionDelta;
            Weight += other.Weight;
        }
    }

    public interface IMovementStateLayer
    {
        MovementSemanticId Id { get; }
        bool Enabled { get; }
        float Priority { get; }
        void Tick(in MovementLayerFrame frame, ref MovementLayerOutput output);
    }

    public sealed class MovementLayerArbiter
    {
        private const int CapacityValue = 8;
        private readonly IMovementStateLayer[] _layers = new IMovementStateLayer[CapacityValue];
        private int _count;

        public static int Capacity { get { return CapacityValue; } }
        public int Count { get { return _count; } }

        public bool TryAdd(IMovementStateLayer layer)
        {
            if (layer == null || _count >= CapacityValue) return false;
            for (int i = 0; i < _count; i++)
                if (_layers[i].Id.Equals(layer.Id)) return false;
            _layers[_count++] = layer;
            return true;
        }

        public bool TryRemove(MovementSemanticId id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (!_layers[i].Id.Equals(id)) continue;
                for (int j = i + 1; j < _count; j++) _layers[j - 1] = _layers[j];
                _count--;
                _layers[_count] = null;
                return true;
            }
            return false;
        }

        public MovementLayerOutput Resolve(in MovementLayerFrame frame)
        {
            var result = new MovementLayerOutput();
            for (int i = 0; i < _count; i++)
            {
                IMovementStateLayer layer = _layers[i];
                if (layer == null || !layer.Enabled) continue;
                var contribution = new MovementLayerOutput();
                layer.Tick(in frame, ref contribution);
                float priority = layer.Priority;
                if (float.IsNaN(priority) || float.IsInfinity(priority) || priority < 0f) priority = 0f;
                if (priority > 1f) priority = 1f;
                contribution.VelocityDelta = contribution.VelocityDelta * priority;
                contribution.PositionDelta = contribution.PositionDelta * priority;
                contribution.Weight = priority;
                result.Add(in contribution);
            }
            if (!result.VelocityDelta.IsFinite) result.VelocityDelta = MovementVector3.Zero;
            if (!result.PositionDelta.IsFinite) result.PositionDelta = MovementVector3.Zero;
            return result;
        }

        // Registration order defines the save order; restore reverses it so
        // layers behave like a stack during rollback.
        public void SaveReplayState()
        {
            for (int i = 0; i < _count; i++)
            {
                IMovementLayerReplay replay = _layers[i] as IMovementLayerReplay;
                if (replay != null) replay.SaveReplayState();
            }
        }

        public void RestoreReplayState()
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                IMovementLayerReplay replay = _layers[i] as IMovementLayerReplay;
                if (replay != null) replay.RestoreReplayState();
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _layers[i] = null;
            _count = 0;
        }
    }

    public interface IMovementLayerReplay
    {
        void SaveReplayState();
        void RestoreReplayState();
    }

    public sealed class MovementRemotePresentationSession
    {
        private readonly MovementRemoteInterpolator _interpolator;
        private readonly MovementRemotePresentationSink _sink;
        private IMovementPresentationBudget _budget;
        private bool _connected;
        private bool _interested = true;
        private long _accepted;
        private long _presented;
        private long _suppressed;

        public MovementRemotePresentationSession(IMovementLocomotionSink target,
            int capacity = 32, double interpolationDelay = 0.1,
            double maxExtrapolation = 0.25)
        {
            _interpolator = new MovementRemoteInterpolator(
                capacity, interpolationDelay, maxExtrapolation, 0.08f);
            _sink = new MovementRemotePresentationSink(target, _interpolator);
            _connected = target != null;
        }

        public bool IsConnected { get { return _connected; } }
        public long AcceptedCount { get { return _accepted; } }
        public long PresentedCount { get { return _presented; } }
        public long SuppressedCount { get { return _suppressed; } }

        public void SetBudget(IMovementPresentationBudget budget) { _budget = budget; }

        public void SetInterest(bool interested) { _interested = interested; }

        public bool Reconnect()
        {
            _connected = true;
            return true;
        }

        public void Disconnect()
        {
            _connected = false;
            _sink.Reset();
        }

        public bool TryAccept(in MovementRemoteSample sample)
        {
            if (!_connected) return false;
            if (!_sink.Enqueue(in sample)) return false;
            _accepted++;
            return true;
        }

        public bool Present(double renderTime, float deltaTime)
        {
            if (!_connected || !_interested)
            {
                _suppressed++;
                return false;
            }
            if (_budget != null)
            {
                var probe = new MovementAnimationState(0u, deltaTime,
                    MovementStateType.Run, MovementAnimationFlags.None,
                    MovementVector3.Zero, MovementVector3.Zero, MovementVector3.Up,
                    MovementVector3.Up, MovementVector3.Zero);
                if (!_budget.ShouldPublish(in probe, 0f))
                {
                    _suppressed++;
                    return false;
                }
            }
            if (!_sink.Present(renderTime, deltaTime)) return false;
            _presented++;
            return true;
        }

    }
}
