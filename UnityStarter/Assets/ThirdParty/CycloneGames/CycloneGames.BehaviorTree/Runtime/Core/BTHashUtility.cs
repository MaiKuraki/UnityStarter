using System;
using System.Runtime.CompilerServices;
using CycloneGames.Hash.Core;

namespace CycloneGames.BehaviorTree.Core
{
    /// <summary>
    /// Deterministic FNV-1a string hash for blackboard keys, independent of Unity APIs so
    /// hashing works on non-Unity and AOT targets. The case-insensitive variant is ASCII-only.
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
