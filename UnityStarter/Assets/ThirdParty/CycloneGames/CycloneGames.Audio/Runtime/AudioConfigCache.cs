// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    internal static class AudioConfigCache
    {
        public static bool HasUsableResult(bool hasSearched, UnityEngine.Object cachedConfig)
        {
            return hasSearched &&
                   (ReferenceEquals(cachedConfig, null) || cachedConfig != null);
        }

        public static T GetOrDiscover<T>(
            ref bool hasSearched,
            ref bool isSearching,
            ref T cachedConfig,
            Func<T> discover)
            where T : UnityEngine.Object
        {
            if (HasUsableResult(hasSearched, cachedConfig))
            {
                return cachedConfig;
            }
            if (isSearching)
            {
                return null;
            }
            if (discover == null)
            {
                throw new ArgumentNullException(nameof(discover));
            }

            hasSearched = false;
            isSearching = true;
            try
            {
                T discoveredConfig = discover();
                cachedConfig = discoveredConfig;
                hasSearched = true;
                return discoveredConfig;
            }
            catch
            {
                cachedConfig = null;
                hasSearched = false;
                throw;
            }
            finally
            {
                isSearching = false;
            }
        }
    }
}
