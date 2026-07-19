using System;
using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// Detects simulation desync across peers by comparing deterministic state hashes.
    /// Each frame, all game state is incrementally hashed; peers exchange hashes to verify consistency.
    ///
    /// <para>Hash algorithm is pluggable via <typeparamref name="THasher"/>
    /// (<c>struct</c> constraint = JIT monomorphization, zero virtual-call overhead).</para>
    ///
    /// Implement <see cref="IStateHasher"/> and select the algorithm explicitly through
    /// <typeparamref name="THasher"/>.
    /// </summary>
    public sealed class DesyncDetector<THasher> where THasher : struct, IStateHasher
    {
        private THasher _hasher;
        private ulong _currentHash;
        private int _currentFrame;

        // Rolling history for delayed validation
        private readonly ulong[] _hashHistory;
        private readonly int _historyMask;

        public int CurrentFrame => _currentFrame;
        public ulong CurrentHash => _currentHash;

        public event Action<int, ulong, ulong> OnDesyncDetected; // (frame, localHash, remoteHash)

        /// <param name="historySize">Must be power of 2 (default 256)</param>
        public DesyncDetector(int historySize = 256)
        {
            if (historySize <= 0 || (historySize & (historySize - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(historySize), "History size must be a positive power of two.");

            _hashHistory = new ulong[historySize];
            _historyMask = historySize - 1;
            _hasher = default;
            _hasher.Reset();
            _currentHash = _hasher.GetDigest();
        }

        /// <summary>
        /// Begin hashing a new frame. Call at the start of deterministic simulation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrame(int frame)
        {
            _currentFrame = frame;
            _hasher.Reset();
        }

        /// <summary>
        /// Hash an integer value into this frame's state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashInt(int value) => _hasher.HashInt(value);

        /// <summary>
        /// Hash a long value into this frame's state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashLong(long value) => _hasher.HashLong(value);

        /// <summary>
        /// Hash a fixed-point value (for deterministic simulations).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFP(FPInt64 value) => _hasher.HashLong(value.RawValue);

        /// <summary>
        /// Hash a fixed-point 2D vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFPVector2(in FPVector2 v)
        {
            _hasher.HashLong(v.X.RawValue);
            _hasher.HashLong(v.Y.RawValue);
        }

        /// <summary>
        /// Hash a fixed-point 3D vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFPVector3(in FPVector3 v)
        {
            _hasher.HashLong(v.X.RawValue);
            _hasher.HashLong(v.Y.RawValue);
            _hasher.HashLong(v.Z.RawValue);
        }

        /// <summary>
        /// Hash a byte span (for arbitrary serialized state).
        /// </summary>
        public void HashBytes(ReadOnlySpan<byte> data) => _hasher.HashBytes(data);

        /// <summary>
        /// Hash a boolean value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashBool(bool value) => _hasher.HashBool(value);

        /// <summary>
        /// Finalize this frame's hash and store in history. Call at end of deterministic simulation.
        /// Returns the final hash value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong EndFrame()
        {
            _currentHash = _hasher.GetDigest();
            _hashHistory[_currentFrame & _historyMask] = _currentHash;
            return _currentHash;
        }

        /// <summary>
        /// Validate a remote peer's hash for a given frame.
        /// Returns true if hashes match, or if the frame is in the future
        /// (not yet computed locally).
        /// Returns true with a warning if the frame has aged out of the
        /// history window—the caller should treat this as a potential blind spot.
        /// </summary>
        public bool ValidateRemoteHash(int frame, ulong remoteHash)
        {
            int slot = frame & _historyMask;
            ulong localHash = _hashHistory[slot];

            // If we haven't computed this frame's hash yet, can't validate
            if (frame > _currentFrame) return true;

            // Frame has aged out of the history window. The ring buffer slot
            // may now contain a hash from a different frame that happens to
            // hash-collide with the remote value, silently accepting a desync.
            // We still return true (can't validate what we no longer have),
            // but fire a diagnostic event so the caller can log or react.
            if (frame <= _currentFrame - _hashHistory.Length)
            {
                OnDesyncDetected?.Invoke(frame, localHash, remoteHash);
                return true;
            }

            if (localHash != remoteHash)
            {
                OnDesyncDetected?.Invoke(frame, localHash, remoteHash);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Get the stored hash for a specific frame (for sending to peers).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetFrameHash(int frame) => _hashHistory[frame & _historyMask];

        /// <summary>
        /// Reset detector state.
        /// </summary>
        public void Reset()
        {
            _currentFrame = 0;
            _hasher.Reset();
            _currentHash = _hasher.GetDigest();
            Array.Clear(_hashHistory, 0, _hashHistory.Length);
        }
    }

}
