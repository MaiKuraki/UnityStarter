using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// Deterministic lockstep simulation driver.
    /// All peers collect inputs, exchange them, and advance the simulation frame ONLY
    /// when every peer's input for that frame has been received.
    /// Used by RTS games (Red Alert, StarCraft, Age of Empires), fighting games, etc.
    /// 
    /// Key guarantees:
    /// - All peers process the same inputs on the same frame
    /// - Simulation only advances when consensus is reached
    /// - Stall detection when a peer is too far behind
    /// - Configurable input delay (command delay) to hide network latency
    /// </summary>
    public sealed class LockstepManager<TInput> where TInput : unmanaged
    {
        public delegate void SimulateFrameDelegate(int frame, ReadOnlySpan<TInput> peerInputs);

        private readonly int _peerCount;
        private readonly int _localPeerId;
        private readonly int _inputDelay;         // Frames of command delay (typically 2-4 for RTS)
        private readonly int _maxStallFrames;      // Max frames to wait before timeout

        // Inputs indexed by [frame % bufferSize][peerId]
        private readonly TInput[,] _inputBuffer;
        private readonly bool[,] _inputReceived;
        private readonly int _bufferSize;
        private readonly int _bufferMask;

        private int _currentFrame;
        private int _latestInputFrame;             // Highest frame for which we've submitted local input
        private int _stallCounter;

        // Per-frame state hash for desync detection
        private readonly ulong[] _stateHashes;
        private readonly bool[] _hashReceived;

        public int CurrentFrame => _currentFrame;
        public int InputDelay => _inputDelay;
        public int PeerCount => _peerCount;
        public int LocalPeerId => _localPeerId;
        public int StallCounter => _stallCounter;
        public bool IsStalled => _stallCounter > 0;

        public event Action<int> OnFrameAdvanced;
        public event Action<int, int> OnPeerStall;        // (peerId, stalledAtFrame)
        public event Action<int, int> OnDesyncDetected;    // (frame, peerId)
        public event SimulateFrameDelegate OnSimulateFrame;

        /// <param name="peerCount">Total number of peers (including local)</param>
        /// <param name="localPeerId">This peer's index (0-based)</param>
        /// <param name="inputDelay">Command delay in frames, hides latency (2-4 recommended for RTS)</param>
        /// <param name="bufferSize">Must be power of 2, default 256</param>
        /// <param name="maxStallFrames">Max frames to wait before stall event, default 300 (~5s at 60fps)</param>
        public LockstepManager(int peerCount, int localPeerId, int inputDelay = 2,
            int bufferSize = 256, int maxStallFrames = 300)
        {
            if (peerCount < 1 || peerCount > 64)
                throw new ArgumentOutOfRangeException(nameof(peerCount));
            if (localPeerId < 0 || localPeerId >= peerCount)
                throw new ArgumentOutOfRangeException(nameof(localPeerId));
            if ((bufferSize & (bufferSize - 1)) != 0)
                throw new ArgumentException("bufferSize must be power of 2");

            _peerCount = peerCount;
            _localPeerId = localPeerId;
            _inputDelay = inputDelay;
            _maxStallFrames = maxStallFrames;
            _bufferSize = bufferSize;
            _bufferMask = bufferSize - 1;

            _inputBuffer = new TInput[bufferSize, peerCount];
            _inputReceived = new bool[bufferSize, peerCount];
            _stateHashes = new ulong[bufferSize];
            _hashReceived = new bool[bufferSize];

            _currentFrame = 0;
            _latestInputFrame = -1;
        }

        /// <summary>
        /// Submit local input for the next frame.
        /// Input is scheduled for frame = currentFrame + inputDelay.
        /// Returns the target frame the input was assigned to.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SubmitLocalInput(in TInput input)
        {
            int targetFrame = _currentFrame + _inputDelay;
            int slot = targetFrame & _bufferMask;

            _inputBuffer[slot, _localPeerId] = input;
            _inputReceived[slot, _localPeerId] = true;
            _latestInputFrame = targetFrame;

            return targetFrame;
        }

        /// <summary>
        /// Receive a remote peer's input. Called when network message arrives.
        /// </summary>
        public void ReceiveRemoteInput(int peerId, int frame, in TInput input)
        {
            if (peerId < 0 || peerId >= _peerCount || peerId == _localPeerId) return;

            // Reject stale frames that are too old (would collide in ring buffer)
            if (frame < _currentFrame - _bufferSize) return;
            // Reject frames too far in the future
            if (frame > _currentFrame + _bufferSize) return;

            int slot = frame & _bufferMask;
            _inputBuffer[slot, peerId] = input;
            _inputReceived[slot, peerId] = true;
        }

        /// <summary>
        /// Try to advance the simulation. Call once per game update.
        /// Returns true if at least one frame was simulated.
        /// </summary>
        public bool Tick()
        {
            bool advanced = false;

            // Try to advance as many frames as possible in one call
            while (CanAdvance())
            {
                AdvanceOneFrame();
                advanced = true;
                _stallCounter = 0;
            }

            if (!advanced)
            {
                _stallCounter++;
                if (_stallCounter > 0 && _stallCounter % 60 == 0) // Report every ~1s at 60fps
                {
                    int missingPeer = FindMissingPeer(_currentFrame);
                    if (missingPeer >= 0)
                        OnPeerStall?.Invoke(missingPeer, _currentFrame);
                }

                if (_stallCounter >= _maxStallFrames)
                {
                    int missingPeer = FindMissingPeer(_currentFrame);
                    if (missingPeer >= 0)
                        OnPeerStall?.Invoke(missingPeer, _currentFrame);
                }
            }

            return advanced;
        }

        /// <summary>
        /// Submit local state hash for desync detection after simulation.
        /// </summary>
        public void SubmitStateHash(int frame, ulong hash)
        {
            int slot = frame & _bufferMask;
            _stateHashes[slot] = hash;
            _hashReceived[slot] = true;
        }

        /// <summary>
        /// Validate a remote peer's state hash against ours.
        /// </summary>
        public bool ValidateStateHash(int peerId, int frame, ulong remoteHash)
        {
            int slot = frame & _bufferMask;
            if (!_hashReceived[slot]) return true; // Can't validate yet

            if (_stateHashes[slot] != remoteHash)
            {
                OnDesyncDetected?.Invoke(frame, peerId);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get processed inputs for a specific frame (for replay/debugging).
        /// </summary>
        public bool TryGetFrameInputs(int frame, Span<TInput> outInputs)
        {
            if (outInputs.Length < _peerCount) return false;

            int slot = frame & _bufferMask;
            for (int i = 0; i < _peerCount; i++)
            {
                if (!_inputReceived[slot, i]) return false;
                outInputs[i] = _inputBuffer[slot, i];
            }
            return true;
        }

        /// <summary>
        /// Reset simulation state (e.g., for game restart).
        /// </summary>
        public void Reset()
        {
            _currentFrame = 0;
            _latestInputFrame = -1;
            _stallCounter = 0;
            Array.Clear(_inputReceived, 0, _inputReceived.Length);
            Array.Clear(_hashReceived, 0, _hashReceived.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanAdvance()
        {
            int slot = _currentFrame & _bufferMask;
            for (int i = 0; i < _peerCount; i++)
            {
                if (!_inputReceived[slot, i])
                    return false;
            }
            return true;
        }

        private void AdvanceOneFrame()
        {
            int slot = _currentFrame & _bufferMask;

            // Gather inputs into contiguous span for simulation callback
            Span<TInput> inputs = stackalloc TInput[_peerCount];
            for (int i = 0; i < _peerCount; i++)
                inputs[i] = _inputBuffer[slot, i];

            OnSimulateFrame?.Invoke(_currentFrame, inputs);

            // Clear slot for reuse
            for (int i = 0; i < _peerCount; i++)
                _inputReceived[slot, i] = false;

            _currentFrame++;
            OnFrameAdvanced?.Invoke(_currentFrame);
        }

        private int FindMissingPeer(int frame)
        {
            int slot = frame & _bufferMask;
            for (int i = 0; i < _peerCount; i++)
            {
                if (!_inputReceived[slot, i])
                    return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Lockstep input message sent between peers.
    /// </summary>
    public struct LockstepInputMessage<TInput> where TInput : unmanaged
    {
        public int PeerId;
        public int Frame;
        public TInput Input;
    }

    /// <summary>
    /// Lockstep state hash message for desync detection.
    /// </summary>
    public struct LockstepHashMessage
    {
        public int PeerId;
        public int Frame;
        public ulong StateHash;
    }
}
