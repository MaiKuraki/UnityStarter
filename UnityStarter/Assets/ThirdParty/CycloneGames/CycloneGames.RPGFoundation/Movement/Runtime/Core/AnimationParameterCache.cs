using System;
using System.Collections.Generic;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    /// <summary>
    /// Cache for Animator parameter hashes. Avoids per-frame StringToHash calls.
    /// </summary>
    public static class AnimationParameterCache
    {
        public const int MaximumEntryCount = 65_536;

        private static readonly Dictionary<string, int> _parameterHashes = new Dictionary<string, int>(32);
        private static long _rejectedAdmissionCount;

        public static int Count => _parameterHashes.Count;
        public static long RejectedAdmissionCount => _rejectedAdmissionCount;

        public static int GetHash(string parameterName)
        {
            if (!TryGetOrAddHash(parameterName, out int hash))
            {
                throw new InvalidOperationException(
                    $"Animation parameter cache reached the implementation ceiling of {MaximumEntryCount}.");
            }

            return hash;
        }

        /// <summary>
        /// Gets or retains a parameter hash. Returns false only when a new key cannot be retained
        /// at the implementation ceiling; <paramref name="hash"/> still contains the deterministic
        /// Animator hash so callers can explicitly choose an uncached operation.
        /// </summary>
        public static bool TryGetOrAddHash(string parameterName, out int hash)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                hash = 0;
                return true;
            }

            if (_parameterHashes.TryGetValue(parameterName, out hash))
            {
                return true;
            }

            hash = Animator.StringToHash(parameterName);
            if (_parameterHashes.Count >= MaximumEntryCount)
            {
                if (_rejectedAdmissionCount < long.MaxValue)
                {
                    _rejectedAdmissionCount++;
                }

                return false;
            }

            _parameterHashes.Add(parameterName, hash);
            return true;
        }

        public static void PreWarm(params string[] parameterNames)
        {
            if (!TryPreWarm(parameterNames))
            {
                throw new InvalidOperationException(
                    $"Animation parameter cache reached the implementation ceiling of {MaximumEntryCount}.");
            }
        }

        /// <summary>
        /// Attempts to retain every supplied parameter hash. Existing and empty names succeed;
        /// false means at least one new name was rejected after any earlier names were retained.
        /// </summary>
        public static bool TryPreWarm(params string[] parameterNames)
        {
            if (parameterNames == null)
            {
                return true;
            }

            for (int i = 0; i < parameterNames.Length; i++)
            {
                if (!TryGetOrAddHash(parameterNames[i], out _))
                {
                    return false;
                }
            }

            return true;
        }

        public static void Clear() => _parameterHashes.Clear();

        /// <summary>Returns an allocation-free O(1) cache admission snapshot.</summary>
        public static AnimationParameterCacheMemorySnapshot GetMemorySnapshot()
        {
            return new AnimationParameterCacheMemorySnapshot(
                _parameterHashes.Count,
                MaximumEntryCount,
                _rejectedAdmissionCount);
        }
    }
}
