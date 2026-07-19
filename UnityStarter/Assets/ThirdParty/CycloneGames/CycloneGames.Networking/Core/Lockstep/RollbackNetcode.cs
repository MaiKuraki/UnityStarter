using System;
using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

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

            /// <summary>
            /// Advances the simulation by exactly one fixed step using this frame's per-peer inputs. The step is
            /// fixed-size by contract (see <see cref="FixedDeltaTime"/>); any delta a step needs must be encoded
            /// inside <typeparamref name="TInput"/>, because a variable delta would break cross-platform determinism.
            /// </summary>
            void Simulate(ReadOnlySpan<TInput> peerInputs);
            void OnRollback(int framesToRollback);
        }

        private readonly int _peerCount;
        private readonly int _localPeerId;
        private readonly int _maxRollbackFrames;
        private readonly IRollbackSimulation _simulation;
        private readonly INetLogger _logger;

        // Ring buffers indexed by frame
        private readonly TInput[,] _confirmedInputs;    // Actual inputs received
        private readonly TInput[,] _predictedInputs;    // Predicted inputs used for simulation
        private readonly bool[,] _inputConfirmed;       // Whether real input has been received
        private readonly TInput[,] _predictionBaselines; // Last confirmed input visible before each frame
        private readonly TState[] _stateHistory;         // State snapshots for rollback
        private readonly int[] _slotFrames;
        private readonly TInput[] _frameScratch;
        private readonly TInput[] _rollbackScratch;
        private readonly TInput[] _rollbackLastKnownScratch;
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
        public int FrameAdvantage => _currentFrame - (_lastConfirmedFrame + 1);

        /// <summary>
        /// Fixed simulation step derived from the configured tick rate. Callers can use it to drive a fixed-step
        /// time accumulator; the simulation itself is fixed-step and never receives a per-call delta.
        /// </summary>
        public FPInt64 FixedDeltaTime => _deltaTime;

        public event Action<int, int> OnRollbackOccurred;   // (fromFrame, toFrame)
        public event Action<int> OnFrameAdvanced;

        /// <param name="peerCount">Total peers including local</param>
        /// <param name="localPeerId">Local peer index</param>
        /// <param name="simulation">Game simulation callbacks</param>
        /// <param name="maxRollbackFrames">Maximum frames to roll back (7-10 typical for fighting games)</param>
        /// <param name="tickRate">Simulation tick rate for fixed delta time</param>
        public RollbackNetcode(int peerCount, int localPeerId, IRollbackSimulation simulation,
            int maxRollbackFrames = 8, int tickRate = 60, INetLogger logger = null)
        {
            if (peerCount < 1 || peerCount > 64)
                throw new ArgumentOutOfRangeException(nameof(peerCount));
            if (localPeerId < 0 || localPeerId >= peerCount)
                throw new ArgumentOutOfRangeException(nameof(localPeerId));
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            if (maxRollbackFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRollbackFrames));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));

            _peerCount = peerCount;
            _localPeerId = localPeerId;
            _simulation = simulation;
            _logger = logger ?? NoopNetLogger.Instance;
            _maxRollbackFrames = maxRollbackFrames;
            _deltaTime = FPInt64.FromDouble(1.0 / tickRate);

            // Buffer must retain rollback history plus a bounded future-input window.
            int requiredBufferSize;
            try
            {
                requiredBufferSize = checked(maxRollbackFrames * 4);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRollbackFrames),
                    maxRollbackFrames,
                    "maxRollbackFrames is too large to represent a bounded rollback buffer.");
            }

            _bufferSize = 1;
            while (_bufferSize < requiredBufferSize)
            {
                if (_bufferSize > int.MaxValue / 2)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxRollbackFrames),
                        maxRollbackFrames,
                        "maxRollbackFrames requires a rollback buffer larger than the supported frame index space.");
                }

                _bufferSize <<= 1;
            }
            _bufferMask = _bufferSize - 1;

            _confirmedInputs = new TInput[_bufferSize, peerCount];
            _predictedInputs = new TInput[_bufferSize, peerCount];
            _inputConfirmed = new bool[_bufferSize, peerCount];
            _predictionBaselines = new TInput[_bufferSize, peerCount];
            _stateHistory = new TState[_bufferSize];
            _slotFrames = new int[_bufferSize];
            _frameScratch = new TInput[peerCount];
            _rollbackScratch = new TInput[peerCount];
            _rollbackLastKnownScratch = new TInput[peerCount];
            FillSlotFrames(-1);

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
            if (_currentFrame == int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Rollback frame index space is exhausted. Reset or resynchronize before advancing again.");
            }

            int frame = _currentFrame;
            int slot = frame & _bufferMask;
            PrepareSlot(slot, frame);

            // Save state before simulation for potential rollback
            _stateHistory[slot] = _simulation.SaveState();

            // Record local input as confirmed
            _confirmedInputs[slot, _localPeerId] = localInput;
            _predictedInputs[slot, _localPeerId] = localInput;
            _inputConfirmed[slot, _localPeerId] = true;
            _peerLastInput[_localPeerId] = localInput;
            _peerLastConfirmedFrame[_localPeerId] = frame;

            // For remote peers, use prediction if not yet confirmed
            Span<TInput> frameInputs = _frameScratch;
            frameInputs[_localPeerId] = localInput;

            for (int i = 0; i < _peerCount; i++)
            {
                if (i == _localPeerId) continue;

                _predictionBaselines[slot, i] = _peerLastInput[i];
                if (_inputConfirmed[slot, i])
                {
                    frameInputs[i] = _confirmedInputs[slot, i];
                    _predictedInputs[slot, i] = _confirmedInputs[slot, i];
                    _peerLastInput[i] = _confirmedInputs[slot, i];
                    _peerLastConfirmedFrame[i] = frame;
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
            _simulation.Simulate(frameInputs);
            _currentFrame++;
            UpdateLastConfirmedFrame();

            OnFrameAdvanced?.Invoke(_currentFrame);
        }

        /// <summary>
        /// Receive confirmed input from a remote peer.
        /// If the input differs from our prediction, triggers rollback + re-simulation.
        /// </summary>
        public void ReceiveRemoteInput(int peerId, int frame, in TInput input)
        {
            if (peerId < 0 || peerId >= _peerCount || peerId == _localPeerId) return;
            if (frame < 0) return;

            long relativeFrame = (long)frame - _currentFrame;
            if (relativeFrame < -_maxRollbackFrames || relativeFrame >= _bufferSize) return;

            int slot = frame & _bufferMask;
            int existingFrame = _slotFrames[slot];
            if (existingFrame != -1
                && existingFrame != frame
                && (long)existingFrame >= (long)_currentFrame - _maxRollbackFrames)
            {
                return;
            }

            PrepareSlot(slot, frame);

            if (_inputConfirmed[slot, peerId])
            {
                // A confirmed input is immutable. Exact duplicates are idempotent; a
                // conflicting duplicate must not amplify rollback work or rewrite history.
                return;
            }

            // Store confirmed input
            _confirmedInputs[slot, peerId] = input;
            _inputConfirmed[slot, peerId] = true;

            // Future input may arrive before earlier frames. It must not become the
            // prediction baseline until its frame has reached the simulation timeline.
            if (frame < _currentFrame && frame > _peerLastConfirmedFrame[peerId])
            {
                _peerLastInput[peerId] = input;
                _peerLastConfirmedFrame[peerId] = frame;
            }

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
                        // Too far back: cannot rollback; log and notify desync.
                        _logger.Log(LogLevel.Warning,
                            $"[RollbackNetcode] Rollback depth {rollbackDepth} exceeds max {_maxRollbackFrames}. " +
                            $"Peer {peerId} frame {frame} vs current {_currentFrame}. Possible desync.");
                        return;
                    }

                    Rollback(frame);
                }
            }

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
            FillSlotFrames(-1);
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
            Span<TInput> inputs = _rollbackScratch;
            Span<TInput> lastKnownInputs = _rollbackLastKnownScratch;
            for (int i = 0; i < _peerCount; i++)
                lastKnownInputs[i] = _predictionBaselines[targetSlot, i];

            for (int f = toFrame; f < fromFrame; f++)
            {
                int slot = f & _bufferMask;
                if (_slotFrames[slot] != f)
                    throw new InvalidOperationException("Rollback history was overwritten before re-simulation completed.");

                // Re-save state
                _stateHistory[slot] = _simulation.SaveState();

                for (int i = 0; i < _peerCount; i++)
                {
                    _predictionBaselines[slot, i] = lastKnownInputs[i];
                    if (_inputConfirmed[slot, i])
                    {
                        inputs[i] = _confirmedInputs[slot, i];
                        _predictedInputs[slot, i] = _confirmedInputs[slot, i];
                        lastKnownInputs[i] = _confirmedInputs[slot, i];
                    }
                    else
                    {
                        inputs[i] = _simulation.PredictInput(i, lastKnownInputs[i]);
                        _predictedInputs[slot, i] = inputs[i];
                    }
                }

                _simulation.Simulate(inputs);
            }

            OnRollbackOccurred?.Invoke(fromFrame, toFrame);
        }

        private void UpdateLastConfirmedFrame()
        {
            int candidate = _lastConfirmedFrame + 1;
            while (candidate < _currentFrame)
            {
                int slot = candidate & _bufferMask;
                if (_slotFrames[slot] != candidate)
                    break;

                bool complete = true;
                for (int i = 0; i < _peerCount; i++)
                {
                    if (!_inputConfirmed[slot, i])
                    {
                        complete = false;
                        break;
                    }
                }

                if (!complete)
                    break;

                _lastConfirmedFrame = candidate;
                candidate++;
            }
        }

        private void PrepareSlot(int slot, int frame)
        {
            if (_slotFrames[slot] == frame)
                return;

            for (int i = 0; i < _peerCount; i++)
            {
                _inputConfirmed[slot, i] = false;
                _confirmedInputs[slot, i] = default;
                _predictedInputs[slot, i] = default;
                _predictionBaselines[slot, i] = default;
            }

            _stateHistory[slot] = default;
            _slotFrames[slot] = frame;
        }

        private void FillSlotFrames(int value)
        {
            for (int i = 0; i < _slotFrames.Length; i++)
                _slotFrames[i] = value;
        }
    }
}
