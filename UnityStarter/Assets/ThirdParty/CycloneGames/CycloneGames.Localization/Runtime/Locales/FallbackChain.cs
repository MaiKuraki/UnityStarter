using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Builds a flattened, deduplicated fallback chain from a Locale's hierarchy.
    /// Cached per locale to avoid repeated traversal.
    /// </summary>
    public sealed class FallbackChain
    {
        private readonly Dictionary<string, LocaleId[]> _cache =
            new Dictionary<string, LocaleId[]>(8, StringComparer.Ordinal);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocaleId[] Resolve(Locale locale)
        {
            if (locale == null) return Array.Empty<LocaleId>();

            string key = locale.Id.Code;
            if (string.IsNullOrEmpty(key)) return Array.Empty<LocaleId>();
            if (_cache.TryGetValue(key, out var chain)) return chain;

            chain = LocaleFallbackChainBuilder.Build(locale);
            _cache[key] = chain;
            return chain;
        }

        public void Clear() => _cache.Clear();
    }
}
