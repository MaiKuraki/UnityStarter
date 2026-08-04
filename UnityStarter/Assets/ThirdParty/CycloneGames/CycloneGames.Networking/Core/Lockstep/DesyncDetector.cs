using System;
using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// Outcome of comparing a remote state hash with retained local history.
    /// </summary>
    public enum DesyncValidationVerdict : byte
    {
        Invalid = 0,
        HashMatch = 1,
        HashMismatch = 2,
        FrameUnavailable = 3,
        Expired = 4,
    }

    /// <summary>
    /// Allocation-free result of a remote state-hash comparison.
    /// </summary>
    public readonly struct DesyncValidationResult
    {
        internal DesyncValidationResult(
            int frame,
            ulong localHash,
            ulong remoteHash,
            DesyncValidationVerdict verdict)
        {
            Frame = frame;
            LocalHash = localHash;
            RemoteHash = remoteHash;
            Verdict = verdict;
        }

        public int Frame { get; }
        public ulong LocalHash { get; }
        public ulong RemoteHash { get; }
        public DesyncValidationVerdict Verdict { get; }
        public bool HasLocalHash =>
            Verdict == DesyncValidationVerdict.HashMatch ||
            Verdict == DesyncValidationVerdict.HashMismatch;
        public bool IsMatch => Verdict == DesyncValidationVerdict.HashMatch;
    }

    /// <summary>
    /// Detects simulation desync across peers by comparing deterministic state hashes.
    /// Each frame, all game state is incrementally hashed; peers exchange hashes to verify consistency.
    ///
    /// <para>Hash algorithm is pluggable via <typeparamref name="THasher"/>
    /// (<c>struct</c> constraint = JIT monomorphization, zero virtual-call overhead).</para>
    ///
    /// Implement <see cref="IStateHasher"/> and select the algorithm explicitly through
    /// <typeparamref name="THasher"/>.
    ///
    /// The constructing thread owns this detector. All hashing, history access, validation,
    /// reset operations, and callbacks must remain on that thread. Editor and Development
    /// builds fail fast on violations; no lock or implicit cross-thread queue is provided.
    /// </summary>
    public sealed class DesyncDetector<THasher> where THasher : struct, IStateHasher
    {
        private const long InvalidFrameStamp = long.MinValue;

        private THasher _hasher;
        private ulong _currentHash;
        private int _currentFrame;

        // Rolling history for delayed validation
        private readonly ulong[] _hashHistory;
        private readonly long[] _frameHistory;
        private readonly int _historyMask;
        private readonly DevelopmentThreadGuard _threadGuard;

        public int CurrentFrame
        {
            get
            {
                _threadGuard.AssertOwnerThread();
                return _currentFrame;
            }
        }

        public ulong CurrentHash
        {
            get
            {
                _threadGuard.AssertOwnerThread();
                return _currentHash;
            }
        }

        public event Action<int, ulong, ulong> OnDesyncDetected; // (frame, localHash, remoteHash)

        /// <summary>
        /// Raised inline when an exact local hash is unavailable. This is diagnostic evidence
        /// loss rather than a confirmed desync; inspect the verdict before choosing retry,
        /// snapshot, or disconnect policy.
        /// </summary>
        public event Action<int, DesyncValidationVerdict> OnValidationUnavailable;

        /// <param name="historySize">Must be power of 2 (default 256)</param>
        public DesyncDetector(int historySize = 256)
        {
            if (historySize <= 0 || (historySize & (historySize - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(historySize), "History size must be a positive power of two.");

            _hashHistory = new ulong[historySize];
            _frameHistory = new long[historySize];
            _historyMask = historySize - 1;
            _threadGuard = new DevelopmentThreadGuard(nameof(DesyncDetector<THasher>));
            _hasher = default;
            _hasher.Reset();
            _currentHash = _hasher.GetDigest();
            FillFrameStamps(_frameHistory, InvalidFrameStamp);
        }

        /// <summary>
        /// Begin hashing a new frame. Call at the start of deterministic simulation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BeginFrame(int frame)
        {
            _threadGuard.AssertOwnerThread();
            _currentFrame = frame;
            _hasher.Reset();
        }

        /// <summary>
        /// Hash an integer value into this frame's state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashInt(int value)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashInt(value);
        }

        /// <summary>
        /// Hash a long value into this frame's state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashLong(long value)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashLong(value);
        }

        /// <summary>
        /// Hash a fixed-point value (for deterministic simulations).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFP(FPInt64 value)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashLong(value.RawValue);
        }

        /// <summary>
        /// Hash a fixed-point 2D vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFPVector2(in FPVector2 v)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashLong(v.X.RawValue);
            _hasher.HashLong(v.Y.RawValue);
        }

        /// <summary>
        /// Hash a fixed-point 3D vector.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashFPVector3(in FPVector3 v)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashLong(v.X.RawValue);
            _hasher.HashLong(v.Y.RawValue);
            _hasher.HashLong(v.Z.RawValue);
        }

        /// <summary>
        /// Hash a byte span (for arbitrary serialized state).
        /// </summary>
        public void HashBytes(ReadOnlySpan<byte> data)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashBytes(data);
        }

        /// <summary>
        /// Hash a boolean value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashBool(bool value)
        {
            _threadGuard.AssertOwnerThread();
            _hasher.HashBool(value);
        }

        /// <summary>
        /// Finalize this frame's hash and store in history. Call at end of deterministic simulation.
        /// Returns the final hash value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong EndFrame()
        {
            _threadGuard.AssertOwnerThread();
            _currentHash = _hasher.GetDigest();
            int slot = _currentFrame & _historyMask;
            _hashHistory[slot] = _currentHash;
            _frameHistory[slot] = _currentFrame;
            return _currentHash;
        }

        /// <summary>
        /// Compares a remote hash with retained local history and reports why validation
        /// did or did not occur. Only <see cref="DesyncValidationVerdict.HashMismatch"/>
        /// raises <see cref="OnDesyncDetected"/>.
        /// </summary>
        public DesyncValidationResult EvaluateRemoteHash(int frame, ulong remoteHash)
        {
            _threadGuard.AssertOwnerThread();

            // Frame numbers use modular int arithmetic. Distances in the forward half of the
            // uint range are treated as retained/past ages; distances in the other half mean
            // the requested frame is ahead. Callers must not compare frames separated by
            // 2^31 or more ticks because that ordering is inherently ambiguous.
            uint age = unchecked((uint)(_currentFrame - frame));
            if (age > int.MaxValue)
            {
                OnValidationUnavailable?.Invoke(
                    frame,
                    DesyncValidationVerdict.FrameUnavailable);
                return new DesyncValidationResult(
                    frame,
                    0UL,
                    remoteHash,
                    DesyncValidationVerdict.FrameUnavailable);
            }

            if (age >= (uint)_hashHistory.Length)
            {
                OnValidationUnavailable?.Invoke(
                    frame,
                    DesyncValidationVerdict.Expired);
                return new DesyncValidationResult(
                    frame,
                    0UL,
                    remoteHash,
                    DesyncValidationVerdict.Expired);
            }

            int slot = frame & _historyMask;
            if (_frameHistory[slot] != frame)
            {
                OnValidationUnavailable?.Invoke(
                    frame,
                    DesyncValidationVerdict.FrameUnavailable);
                return new DesyncValidationResult(
                    frame,
                    0UL,
                    remoteHash,
                    DesyncValidationVerdict.FrameUnavailable);
            }

            ulong localHash = _hashHistory[slot];
            if (localHash == remoteHash)
            {
                return new DesyncValidationResult(
                    frame,
                    localHash,
                    remoteHash,
                    DesyncValidationVerdict.HashMatch);
            }

            OnDesyncDetected?.Invoke(frame, localHash, remoteHash);
            return new DesyncValidationResult(
                frame,
                localHash,
                remoteHash,
                DesyncValidationVerdict.HashMismatch);
        }

        /// <summary>
        /// Validate a remote peer's hash for a given frame. This compatibility API returns
        /// <c>false</c> only for an explicit hash mismatch. Unavailable and expired frames
        /// return <c>true</c>; use <see cref="EvaluateRemoteHash"/> when that distinction
        /// affects protocol policy.
        /// </summary>
        public bool ValidateRemoteHash(int frame, ulong remoteHash)
        {
            return EvaluateRemoteHash(frame, remoteHash).Verdict !=
                   DesyncValidationVerdict.HashMismatch;
        }

        /// <summary>
        /// Tries to get the retained hash for an exact frame stamp.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetFrameHash(int frame, out ulong hash)
        {
            _threadGuard.AssertOwnerThread();
            int slot = frame & _historyMask;
            if (_frameHistory[slot] == frame)
            {
                hash = _hashHistory[slot];
                return true;
            }

            hash = 0UL;
            return false;
        }

        /// <summary>
        /// Gets the raw ring-buffer slot for compatibility. The returned value may belong
        /// to another frame after slot reuse; new callers should use
        /// <see cref="TryGetFrameHash"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetFrameHash(int frame)
        {
            _threadGuard.AssertOwnerThread();
            return _hashHistory[frame & _historyMask];
        }

        /// <summary>
        /// Reset detector state.
        /// </summary>
        public void Reset()
        {
            _threadGuard.AssertOwnerThread();
            _currentFrame = 0;
            _hasher.Reset();
            _currentHash = _hasher.GetDigest();
            Array.Clear(_hashHistory, 0, _hashHistory.Length);
            FillFrameStamps(_frameHistory, InvalidFrameStamp);
        }

        private static void FillFrameStamps(long[] stamps, long value)
        {
            for (int i = 0; i < stamps.Length; i++)
            {
                stamps[i] = value;
            }
        }
    }

}
