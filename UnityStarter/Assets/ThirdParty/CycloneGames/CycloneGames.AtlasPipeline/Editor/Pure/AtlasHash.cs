using System;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Allocation-free stable hashing for atlas keys, sprite identities and manifest fingerprints.
    /// FNV-1a is used because it is deterministic across processes, runtimes and platforms, which
    /// is required when a hash produced on an artist machine is compared against one produced by CI.
    /// Do not use <see cref="string.GetHashCode"/>: on modern .NET its value is randomized per
    /// process, so it can never be persisted, compared across machines, or used in a manifest.
    /// </summary>
    public static class AtlasHash
    {
        /// <summary>Hash returned for null or empty input. Distinct from every non-empty value.</summary>
        public const int NullHash = 0;

        private const uint FnvOffsetBasis32 = 2166136261u;
        private const uint FnvPrime32 = 16777619u;
        private const ulong FnvOffsetBasis64 = 14695981039346656037ul;
        private const ulong FnvPrime64 = 1099511628211ul;

        public static int BeginFnv1a()
        {
            // The offset basis exceeds int.MaxValue, so the conversion must be unchecked. The value
            // is reinterpreted as a signed bit pattern and returned unchanged by the streaming
            // overloads, which cast back to uint before use.
            return unchecked((int)FnvOffsetBasis32);
        }

        public static int ComputeFnv1a(string value)
        {
            return value == null ? NullHash : ComputeFnv1a(value, 0, value.Length);
        }

        public static int ComputeFnv1a(string value, int startIndex, int length)
        {
            if (value == null || length <= 0 || startIndex >= value.Length)
            {
                return NullHash;
            }

            if (startIndex < 0)
            {
                startIndex = 0;
            }

            uint hash = FnvOffsetBasis32;
            int end = startIndex + length;
            if (end > value.Length)
            {
                end = value.Length;
            }

            for (int i = startIndex; i < end; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime32;
            }

            return (int)hash;
        }

        /// <summary>
        /// Case-insensitive, separator-normalizing hash for Assets/-relative paths. Windows and macOS
        /// editors disagree on path casing and separator, so both are folded before hashing;
        /// otherwise the same asset hashes differently per machine and every atlas looks dirty.
        /// </summary>
        public static int ComputePathFnv1a(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return NullHash;
            }

            uint hash = FnvOffsetBasis32;
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '\\')
                {
                    c = '/';
                }

                hash ^= ToLowerInvariantAscii(c);
                hash *= FnvPrime32;
            }

            return (int)hash;
        }

        public static long ComputeFnv1a64(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return NullHash;
            }

            ulong hash = FnvOffsetBasis64;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime64;
            }

            return (long)hash;
        }

        public static long BeginFnv1a64()
        {
            // Same unchecked reinterpretation as BeginFnv1a: the 64-bit offset basis exceeds
            // long.MaxValue.
            return unchecked((long)FnvOffsetBasis64);
        }

        /// <summary>
        /// Folds a string into a running 64-bit FNV-1a hash. Used to fingerprint an atlas member
        /// list without concatenating the paths into one buffer.
        /// Callers must append an explicit separator between fields; without it, the sequences
        /// ("ab", "c") and ("a", "bc") would hash to the same value.
        /// </summary>
        public static void AppendFnv1a64(ref long hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            ulong working = hash == NullHash ? FnvOffsetBasis64 : (ulong)hash;
            for (int i = 0; i < value.Length; i++)
            {
                working ^= value[i];
                working *= FnvPrime64;
            }

            hash = (long)working;
        }

        public static void AppendFnv1a64(ref long hash, char value)
        {
            ulong working = hash == NullHash ? FnvOffsetBasis64 : (ulong)hash;
            working ^= value;
            working *= FnvPrime64;
            hash = (long)working;
        }

        /// <summary>
        /// Folds a byte buffer into a running 64-bit FNV-1a hash. This is what makes it possible to
        /// fingerprint a source image by reading its bytes instead of importing it: decoding every
        /// sprite just to learn whether it changed is the dominant cost of a cold atlas rebuild.
        /// </summary>
        public static void AppendFnv1a64(ref long hash, byte[] buffer, int offset, int count)
        {
            if (buffer == null || count <= 0 || offset >= buffer.Length)
            {
                return;
            }

            ulong working = hash == NullHash ? FnvOffsetBasis64 : (ulong)hash;
            int end = offset + count;
            if (end > buffer.Length)
            {
                end = buffer.Length;
            }

            for (int i = offset; i < end; i++)
            {
                working ^= buffer[i];
                working *= FnvPrime64;
            }

            hash = (long)working;
        }

        /// <summary>
        /// Full-buffer 64-bit hash. Deliberately not an overload of
        /// <see cref="ComputeFnv1a64(string)"/>: the two differ only in parameter type, so a null or
        /// an untyped literal would become ambiguous at every call site. Hash the buffer by starting
        /// from <see cref="BeginFnv1a64"/> and appending.
        /// </summary>

        /// <summary>
        /// Order-sensitive combination of two hashes. Used to fold sprite identity fields into one
        /// value without allocating a tuple or a string.
        /// </summary>
        public static int Combine(int left, int right)
        {
            uint hash = (uint)left;
            hash ^= (uint)right;
            hash *= FnvPrime32;
            hash ^= hash >> 13;
            return (int)hash;
        }

        /// <summary>
        /// Order-sensitive combination of two 64-bit hashes. Used to fold the order-sensitive
        /// membership fingerprint together with an order-independent source-content fingerprint.
        /// </summary>
        public static long Combine64(long left, long right)
        {
            // Multiply before mixing in the second value. A plain XOR of the two inputs is
            // commutative and would throw away the order entirely.
            ulong hash = (ulong)left;
            hash *= FnvPrime64;
            hash ^= (ulong)right;
            hash *= FnvPrime64;
            hash ^= hash >> 29;
            return (long)hash;
        }

        /// <summary>
        /// Folds a string into a running 32-bit FNV-1a hash without allocating. Callers must append
        /// an explicit separator between fields, otherwise ("ab", "c") and ("a", "bc") collide.
        /// </summary>
        public static void AppendFnv1a(ref int hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            uint working = hash == NullHash ? FnvOffsetBasis32 : (uint)hash;
            for (int i = 0; i < value.Length; i++)
            {
                working ^= value[i];
                working *= FnvPrime32;
            }

            hash = (int)working;
        }

        public static void AppendFnv1a(ref int hash, char c)
        {
            uint working = hash == NullHash ? FnvOffsetBasis32 : (uint)hash;
            working ^= c;
            working *= FnvPrime32;
            hash = (int)working;
        }

        /// <summary>
        /// Formats a hash as lowercase hex. Used by the manifest, which must stay diff-friendly and
        /// culture-independent: <see cref="string.Format"/> with "{0:x8}" is culture-sensitive for
        /// some numeric types and allocates a box, so the digits are emitted directly.
        /// </summary>
        public static string ToHex(int value)
        {
            return ToHex((uint)value, 8);
        }

        public static string ToHex(long value)
        {
            return ToHex((ulong)value, 16);
        }

        private static string ToHex(ulong value, int digitCount)
        {
            char[] buffer = new char[digitCount];
            for (int i = digitCount - 1; i >= 0; i--)
            {
                uint nibble = (uint)(value & 0xFu);
                buffer[i] = nibble < 10u ? (char)('0' + nibble) : (char)('a' + (nibble - 10u));
                value >>= 4;
            }

            return new string(buffer);
        }

        private static char ToLowerInvariantAscii(char c)
        {
            return c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
        }
    }
}
