using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Cache for Animator parameter hashes. Avoids per-frame StringToHash calls.
    /// </summary>
    public static class AnimationParameterCache
    {
        private static readonly Dictionary<string, int> _parameterHashes = new Dictionary<string, int>(32);

        public static int GetHash(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
                return 0;

            if (!_parameterHashes.TryGetValue(parameterName, out int hash))
            {
                hash = Animator.StringToHash(parameterName);
                _parameterHashes[parameterName] = hash;
            }
            return hash;
        }

        public static void PreWarm(params string[] parameterNames)
        {
            if (parameterNames == null) return;

            for (int i = 0; i < parameterNames.Length; i++)
            {
                string name = parameterNames[i];
                if (!string.IsNullOrEmpty(name) && !_parameterHashes.ContainsKey(name))
                {
                    _parameterHashes[name] = Animator.StringToHash(name);
                }
            }
        }

        public static void Clear() => _parameterHashes.Clear();
    }
}