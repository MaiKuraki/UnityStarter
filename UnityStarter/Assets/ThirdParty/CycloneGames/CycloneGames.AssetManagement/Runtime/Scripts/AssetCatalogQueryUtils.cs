using System;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Shared helpers for <see cref="IAssetCatalogQuery"/> provider implementations.
    /// </summary>
    internal static class AssetCatalogQueryUtils
    {
        /// <summary>
        /// Appends <paramref name="location"/> to <paramref name="results"/> only when it is non-empty and not
        /// already present (ordinal comparison). The linear scan is intended for low-frequency catalog queries
        /// over small-to-moderate result sets, not for gameplay/UI hot paths.
        /// </summary>
        internal static void AddUniqueLocation(List<string> results, string location)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (string.IsNullOrEmpty(location))
            {
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (string.Equals(results[i], location, StringComparison.Ordinal))
                {
                    return;
                }
            }

            results.Add(location);
        }
    }
}
