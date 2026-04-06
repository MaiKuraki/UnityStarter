using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// GGPO-style rollback netcode for fast-paced competitive games (fighting games, action games).
    /// 
    /// Unlike pure lockstep (which stalls on missing inputs), rollback netcode:
    /// 1. Predicts remote inputs (repeats last known input)
    /// 2. Advances simulation immediately without waiting
    /// 3. When actual remote input arrives, rolls back to that frame and re-simulates
    /// 
    /// Provides nearly lag-free local experience at cost of occasional visual corrections.
    /// Use cases: Street Fighter, Mortal Kombat, Rocket League, fast co-op action.
    /// </summary>
    public sealed class RollbackNetcode<TInput, TState>
        where TInput : unmanaged, IEquatable<TInput>
        where TState : unmanaged
    {
        public interface IRollbackSimulation
        {
            TInput PredictInput(int peerId, TInput lastKnownInput);
            TState SaveState();
            void LoadState(in TState state);
            void Simulate(ReadOnlySpan<TInput> peerInputs, FPInt64 deltaTime);
            void OnRollback(int framesToRollback);
        }

        private readonly int _peerCount;
        private readonly int _localPeerId;
        private readonly int _maxRollbackFrames;
        private readonly IRollbackSimulation _simulation;

        // Ring buffers indexed by frame
        private readonly TInput[,] _confirmedInputs;    // Actual inputs received
        private readonly TInput[,] _predictedInputs;    // Predicted inputs used for simulation
        private readonly bool[,] _inputConfirmed;       // Whether real input has been received
        private readonly TState[] _stateHistory;         // State snapshots for rollback
        private readonly int _bufferSize;
        private readonly int _bufferMask;

        private int _currentFrame;
        private int _lastConfirmedFrame;      // All peers confirmed up to this frame
        private int _rollbackCount;            // Total rollbacks performed
        private int _maxRollbackDepth;         // Deepest rollback in current session
        private readonly FPInt64 _deltaTime;

        // Per-peer tracking
        private readonly int[] _peerLastConfirmedFrame;
        private readonly TInput[] _peerLastInput;

        public int CurrentFrame => _currentFrame;
        public int LastConfirmedFrame => _lastConfirmedFrame;
        public int RollbackCount => _rollbackCount;
        public int MaxRollbackDepth => _maxRollbackDepth;
        public int FrameAdvantage => _currentFrame - _lastConfirmedFrame;

        public event Action<int, int> OnRollbackOccurred;   // (fromFrame, toFrame)
        public event Action<int> OnFrameAdvanced;

        /// <param name="peerCount">Total peers including local</param>
        /// <param name="localPeerId">Local peer index</param>
        /// <param name="simulation">Game simulation callbacks</param>
        /// <param name="maxRollbackFrames">Maximum frames to roll back (7-10 typical for fighting games)</param>
        /// <param name="tickRate">Simulation tick rate for fixed delta time</param>
        public RollbackNetcode(int peerCount, int localPeerId, IRollbackSimulation simulation,
            int maxRollbackFrames = 8, int tickRate = 60)
        {
            if (peerCount < 1 || peerCount > 64)
                throw new ArgumentOutOfRangeException(nameof(peerCount));
            if (localPeerId < 0 || localPeerId >= peerCount)
                throw new ArgumentOutOfRangeException(nameof(localPeerId));

            _peerCount = peerCount;
            _localPeerId = localPeerId;
            _simulation = simulation;
            _maxRollbackFrames = maxRollbackFrames;
            _deltaTime = FPInt64.FromDouble(1.0 / tickRate);

            // Buffer must be at least maxRollbackFrames * 2 and power of 2
            _bufferSize = 1;
            while (_bufferSize < maxRollbackFrames * 4) _bufferSize <<= 1;
            _bufferMask = _bufferSize - 1;

            _confirmedInputs = new TInput[_bufferSize, peerCount];
            _predictedInputs = new TInput[_bufferSize, peerCount];
            _inputConfirmed = new bool[_bufferSize, peerCount];
            _stateHistory = new TState[_bufferSize];

            _peerLastConfirmedFrame = new int[peerCount];
            _peerLastInput = new TInput[peerCount];

            for (int i = 0; i < peerCount; i++)
                _peerLastConfirmedFrame[i] = -1;

            _lastConfirmedFrame = -1;
        }

        /// <summary>
        /// Submit local input and advance simulation by one frame.
        /// </summary>
        public void AdvanceFrame(in TInput localInput)
        {
            int frame = _currentFrame;
            int slot = frame & _bufferMask;

            // Save state before simulation for potential rollback
            _stateHistory[slot] = _simulation.SaveState();

            // Record local input as confirmed
            _confirmedInputs[slot, _localPeerId] = localInput;
            _predictedInputs[slot, _localPeerId] = localInput;
            _inputConfirmed[slot, _localPeerId] = true;
            _peerLastInput[_localPeerId] = localInput;
            _peerLastConfirmedFrame[_localPeerId] = frame;

            // For remote peers, use prediction if not yet confirmed
            Span<TInput> frameInputs = stackalloc TInput[_peerCount];
            frameInputs[_localPeerId] = localInput;

            for (int i = 0; i < _peerCount; i++)
            {
                if (i == _localPeerId) continue;

                if (_inputConfirmed[slot, i])
                {
                    frameInputs[i] = _confirmedInputs[slot, i];
                    _predictedInputs[slot, i] = _confirmedInputs[slot, i];
                }
                else
                {
                    // Predict: repeat last known input
                    TInput predicted = _simulation.PredictInput(i, _peerLastInput[i]);
                    _predictedInputs[slot, i] = predicted;
                    frameInputs[i] = predicted;
                }
            }

            // Simulate
            _simulation.Simulate(frameInputs, _deltaTime);
            _currentFrame++;

            OnFrameAdvanced?.Invoke(_currentFrame);
        }

        /// <summary>
        /// Receive confirmed input from a remote peer.
        /// If the input differs from our prediction, triggers rollback + re-simulation.
        /// </summary>
        public void ReceiveRemoteInput(int peerId, int frame, in TInput input)
        {
            if (peerId < 0 || peerId >= _peerCount || peerId == _localPeerId) return;

            int slot = frame & _bufferMask;

            // Store confirmed input
            _confirmedInputs[slot, peerId] = input;
            _peerLastInput[peerId] = input;

            if (frame > _peerLastConfirmedFrame[peerId])
                _peerLastConfirmedFrame[peerId] = frame;

            // Check if this frame was already simulated with a prediction
            if (frame < _currentFrame)
            {
                TInput predicted = _predictedInputs[slot, peerId];

                // Misprediction: need rollback
                if (!predicted.Equals(input))
                {
                    int rollbackDepth = _currentFrame - frame;
                    if (rollbackDepth > _maxRollbackFrames)
                    {
                        // Too far back, can't rollback — log and notify desync
                        UnityEngine.Debug.LogWarning(
                            $"[RollbackNetcode] Rollback depth {rollbackDepth} exceeds max {_maxRollbackFrames}. " +
                            $"Peer {peerId} frame {frame} vs current {_currentFrame}. Possible desync.");
                        return;
                    }

                    Rollback(frame);
                }
            }

            _inputConfirmed[slot, peerId] = true;

            // Update global confirmed frame
            UpdateLastConfirmedFrame();
        }

        /// <summary>
        /// Check if the simulation is too far ahead of confirmations.
        /// Returns true if we should wait (not advance) this frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldStall()
        {
            return FrameAdvantage >= _maxRollbackFrames;
        }

        /// <summary>
        /// Reset to initial state.
        /// </summary>
        public void Reset()
        {
            _currentFrame = 0;
            _lastConfirmedFrame = -1;
            _rollbackCount = 0;
            _maxRollbackDepth = 0;

            Array.Clear(_inputConfirmed, 0, _inputConfirmed.Length);
            for (int i = 0; i < _peerCount; i++)
            {
                _peerLastConfirmedFrame[i] = -1;
                _peerLastInput[i] = default;
            }
        }

        private void Rollback(int toFrame)
        {
            int fromFrame = _currentFrame;
            int depth = fromFrame - toFrame;

            if (depth > _maxRollbackDepth)
                _maxRollbackDepth = depth;

            _rollbackCount++;
            _simulation.OnRollback(depth);

            // Restore state at the rollback target frame
            int targetSlot = toFrame & _bufferMask;
            _simulation.LoadState(_stateHistory[targetSlot]);

            // Re-simulate from toFrame to currentFrame
            Span<TInput> inputs = stackalloc TInput[_peerCount];
            for (int f = toFrame; f < fromFrame; f++)
            {
                int slot = f & _bufferMask;

                // Re-save state
                _stateHistory[slot] = _simulation.SaveState();

                for (int i = 0; i < _peerCount; i++)
                {
                    if (_inputConfirmed[slot, i])
                    {
                        inputs[i] = _confirmedInputs[slot, i];
                        _predictedInputs[slot, i] = _confirmedInputs[slot, i];
                    }
                    else
                    {
                        inputs[i] = _simulation.PredictInput(i, _peerLastInput[i]);
                        _predictedInputs[slot, i] = inputs[i];
                    }
                }

                _simulation.Simulate(inputs, _deltaTime);
            }

            OnRollbackOccurred?.Invoke(fromFrame, toFrame);
        }

        private void UpdateLastConfirmedFrame()
        {
            int minConfirmed = int.MaxValue;
            for (int i = 0; i < _peerCount; i++)
            {
                if (_peerLastConfirmedFrame[i] < minConfirmed)
                    minConfirmed = _peerLastConfirmedFrame[i];
            }

            if (minConfirmed > _lastConfirmedFrame)
                _lastConfirmedFrame = minConfirmed;
        }
    }
}
