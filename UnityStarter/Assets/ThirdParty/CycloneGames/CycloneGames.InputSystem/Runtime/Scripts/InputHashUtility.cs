using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Provides deterministic, cross-process hashing for named input identities. The folding is delegated to
    /// <see cref="Fnv1a32.ComputeUtf16Ordinal(ReadOnlySpan{char}, uint)"/>, which continues a running FNV-1a state;
    /// the wrappers here only add the reserved-zero contract and the segment boundaries.
    /// </summary>
    public static class InputHashUtility
    {
        private const string SegmentSeparator = "/";

        /// <summary>
        /// Computes a deterministic 32-bit FNV-1a hash for a string. Null and empty input map to the reserved value
        /// zero instead of the FNV offset basis, so callers can treat zero as "unset".
        /// </summary>
        public static int GetDeterministicHashCode(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            return unchecked((int)Fnv1a32.ComputeUtf16Ordinal(str.AsSpan()));
        }

        /// <summary>
        /// Generates a composite Action ID from map and action names without string concatenation.
        /// Logically equivalent to hashing "mapName/actionName".
        /// </summary>
        public static int GetActionId(string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                return 0;

            unchecked
            {
                uint hash = Fnv1a32.OffsetBasis;
                hash = Fnv1a32.ComputeUtf16Ordinal(mapName.AsSpan(), hash);
                hash = FoldSeparator(hash);
                hash = Fnv1a32.ComputeUtf16Ordinal(actionName.AsSpan(), hash);
                return (int)hash;
            }
        }

        /// <summary>
        /// Generates a composite Action ID from context, map and action names without string concatenation.
        /// Logically equivalent to hashing "contextName/mapName/actionName", so different contexts with the same
        /// action map and action name produce different IDs.
        /// </summary>
        public static int GetActionId(string contextName, string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(contextName) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
                return 0;

            unchecked
            {
                uint hash = Fnv1a32.OffsetBasis;
                hash = Fnv1a32.ComputeUtf16Ordinal(contextName.AsSpan(), hash);
                hash = FoldSeparator(hash);
                hash = Fnv1a32.ComputeUtf16Ordinal(mapName.AsSpan(), hash);
                hash = FoldSeparator(hash);
                hash = Fnv1a32.ComputeUtf16Ordinal(actionName.AsSpan(), hash);
                return (int)hash;
            }
        }

        /// <summary>
        /// Folds the segment separator as a single UTF-16 code unit. A one-character span continuation performs
        /// exactly one FNV-1a step, which matches the per-code-unit contract used for the folded identifiers.
        /// </summary>
        private static uint FoldSeparator(uint state)
        {
            return Fnv1a32.ComputeUtf16Ordinal(SegmentSeparator.AsSpan(), state);
        }
    }
}
