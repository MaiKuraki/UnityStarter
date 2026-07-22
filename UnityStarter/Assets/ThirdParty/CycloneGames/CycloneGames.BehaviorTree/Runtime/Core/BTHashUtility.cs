using System;
using System.Runtime.CompilerServices;
using CycloneGames.Hash.Core;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Pure C# string hash utility for blackboard key hashing.
    /// Provides a deterministic FNV-1a hash as an alternative to
    /// Animator.StringToHash, enabling compilation on non-Unity targets.
    ///
    /// Usage:
    ///   int key = BTHash.FNV1A("Health");  // deterministic, same result every process
    ///   int key = BTHash.FNV1ACaseInsensitive("Health"); // ASCII case-insensitive variant
    ///
    /// Note: These produce different hash values than Animator.StringToHash.
    /// Choose one at project initialization and use consistently.
    /// </summary>
    public static class BTHash
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FNV1A(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return unchecked((int)Fnv1a32.ComputeUtf16Ordinal(value.AsSpan()));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FNV1ACaseInsensitive(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            uint hash = Fnv1a32.OffsetBasis;
            int len = value.Length;
            for (int i = 0; i < len; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= Fnv1a32.Prime;
            }
            return (int)hash;
        }
    }
}
