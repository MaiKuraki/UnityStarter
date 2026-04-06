using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// Pluggable hash algorithm for deterministic state hashing.
    /// Implement as a <c>struct</c> and use with <see cref="DesyncDetector{THasher}"/>
    /// to get zero-cost abstraction (JIT monomorphizes each struct type).
    ///
    /// <para><b>Built-in:</b> <see cref="Fnv1aHasher"/> (default, fastest for per-frame desync detection).</para>
    /// <para><b>Custom:</b> Wrap xxHash64, CRC32, or any non-cryptographic hash by implementing this interface.</para>
    /// </summary>
    public interface IStateHasher
    {
        /// <summary>Reset to initial state (called at the start of each frame).</summary>
        void Reset();

        /// <summary>Fold a 32-bit integer into the running hash.</summary>
        void HashInt(int value);

        /// <summary>Fold a 64-bit integer into the running hash.</summary>
        void HashLong(long value);

        /// <summary>Fold a boolean value into the running hash.</summary>
        void HashBool(bool value);

        /// <summary>Fold an arbitrary byte span into the running hash.</summary>
        void HashBytes(ReadOnlySpan<byte> data);

        /// <summary>Finalize and return the 64-bit digest.</summary>
        ulong GetDigest();
    }

    /// <summary>
    /// FNV-1a 64-bit hasher. Default implementation for <see cref="DesyncDetector{THasher}"/>.
    ///
    /// <para>Excellent for per-frame desync detection: ~1-2 ns per value, zero allocations,
    /// deterministic across all platforms. Not cryptographically secure — use SHA-256
    /// for tamper-proof scenarios (replay file signing, resource integrity).</para>
    /// </summary>
    public struct Fnv1aHasher : IStateHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private ulong _hash;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _hash = FnvOffsetBasis;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashInt(int value)
        {
            _hash ^= (ulong)value;
            _hash *= FnvPrime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashLong(long value)
        {
            _hash ^= (ulong)value;
            _hash *= FnvPrime;
            _hash ^= (ulong)(value >> 32);
            _hash *= FnvPrime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashBool(bool value)
        {
            _hash ^= value ? 1UL : 0UL;
            _hash *= FnvPrime;
        }

        public void HashBytes(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                _hash ^= data[i];
                _hash *= FnvPrime;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong GetDigest() => _hash;
    }
}
