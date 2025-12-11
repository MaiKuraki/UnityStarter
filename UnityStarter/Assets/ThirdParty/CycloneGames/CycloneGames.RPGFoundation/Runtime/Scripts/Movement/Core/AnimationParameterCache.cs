using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public static class AnimationParameterCache
    {
        private static readonly Dictionary<string, int> _parameterHashes = new Dictionary<string, int>(16);

        /// <summary>
        /// Gets or creates a parameter hash from string name. 
        /// </summary>
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

        /// <summary>
        /// Pre-warms the cache with parameter names to avoid runtime allocations.
        /// Call during initialization for best performance.
        /// </summary>
        public static void PreWarm(params string[] parameterNames)
        {
            if (parameterNames == null || parameterNames.Length == 0)
                return;

            foreach (string name in parameterNames)
            {
                if (!string.IsNullOrEmpty(name) && !_parameterHashes.ContainsKey(name))
                {
                    _parameterHashes[name] = Animator.StringToHash(name);
                }
            }
        }

        /// <summary>
        /// Clears the cache. Use with caution in production.
        /// </summary>
        public static void Clear()
        {
            _parameterHashes.Clear();
        }
    }
}