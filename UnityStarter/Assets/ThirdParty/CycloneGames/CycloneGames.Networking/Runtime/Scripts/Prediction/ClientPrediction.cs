using CycloneGames.Networking.Simulation;

namespace CycloneGames.Networking.Prediction
{
    /// <summary>
    /// Interface for objects that support client-side prediction and server reconciliation.
    /// Implement on gameplay controllers (character, vehicle, etc.) for responsive input handling.
    /// 
    /// Workflow:
    /// 1. Client: CaptureInput -> SimulateStep (predicted) -> store in PredictionBuffer
    /// 2. Server: Receive input -> SimulateStep (authoritative) -> send state back
    /// 3. Client: Compare predicted vs server state -> if mismatch, Rollback + re-simulate
    /// </summary>
    public interface IPredictable<TInput, TState>
        where TInput : unmanaged
        where TState : unmanaged
    {
        TInput CaptureInput();
        TState CaptureState();
        void ApplyState(in TState state);
        void SimulateStep(in TInput input, float deltaTime);

        /// <summary>
        /// Return true if the two states are close enough to skip reconciliation.
        /// Use generous thresholds for visual-only differences.
        /// </summary>
        bool StatesMatch(in TState predicted, in TState authoritative);
    }

    /// <summary>
    /// Manages prediction + reconciliation loop for an IPredictable entity.
    /// Zero-allocation at steady state.
    /// </summary>
    public sealed class ClientPredictionSystem<TInput, TState>
        where TInput : unmanaged
        where TState : unmanaged
    {
        private readonly IPredictable<TInput, TState> _target;
        private readonly PredictionBuffer<TInput> _inputBuffer;
        private readonly PredictionBuffer<TState> _stateBuffer;
        private NetworkTick _lastServerTick;
        private int _mispredictionCount;

        public int MispredictionCount => _mispredictionCount;
        public NetworkTick LastServerTick => _lastServerTick;
        public PredictionBuffer<TInput> InputBuffer => _inputBuffer;

        public ClientPredictionSystem(IPredictable<TInput, TState> target, int bufferSize = NetworkConstants.MaxSnapshotBufferSize)
        {
            _target = target;
            _inputBuffer = new PredictionBuffer<TInput>(bufferSize);
            _stateBuffer = new PredictionBuffer<TState>(bufferSize);
        }

        /// <summary>
        /// Record input and predicted state for a tick. Call after SimulateStep on client.
        /// </summary>
        public void RecordPrediction(NetworkTick tick, in TInput input)
        {
            _inputBuffer.Set(tick, input);
            _stateBuffer.Set(tick, _target.CaptureState());
        }

        /// <summary>
        /// Process server authoritative state. Triggers reconciliation if mismatch detected.
        /// Returns true if rollback occurred.
        /// </summary>
        public bool ProcessServerState(NetworkTick serverTick, in TState serverState, NetworkTick currentTick, float tickDeltaTime)
        {
            _lastServerTick = serverTick;

            if (!_stateBuffer.TryGet(serverTick, out var predictedState))
                return false;

            if (_target.StatesMatch(predictedState, serverState))
                return false;

            // Mismatch detected: rollback and re-simulate
            _mispredictionCount++;
            _target.ApplyState(serverState);

            // Re-simulate from serverTick+1 to currentTick
            uint replayStart = serverTick.Value + 1;
            uint replayEnd = currentTick.Value;

            // Guard against overflow and excessively large replays
            if (replayStart > replayEnd || replayEnd - replayStart > (uint)_inputBuffer.Capacity)
                return true;

            for (uint t = replayStart; t <= replayEnd; t++)
            {
                var tick = new NetworkTick(t);
                if (_inputBuffer.TryGet(tick, out var input))
                {
                    _target.SimulateStep(input, tickDeltaTime);
                    _stateBuffer.Set(tick, _target.CaptureState());
                }
            }

            return true;
        }

        public void Reset()
        {
            _inputBuffer.Clear();
            _stateBuffer.Clear();
            _mispredictionCount = 0;
            _lastServerTick = NetworkTick.Zero;
        }
    }
}
