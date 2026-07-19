using System;
using System.Collections.Generic;
using CycloneGames.Localization.Core;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Owner-thread cache for immutable fallback chains, keyed by locale asset identity.
    /// </summary>
    public sealed class FallbackChain
    {
        private readonly Dictionary<Locale, LocaleId[]> _cache = new Dictionary<Locale, LocaleId[]>();

        public LocaleId[] Resolve(Locale locale, int maxLocales = LocaleFallbackChainBuilder.DefaultMaxLocales)
        {
            if (locale == null) return Array.Empty<LocaleId>();
            if (_cache.TryGetValue(locale, out LocaleId[] chain)) return chain;

            chain = LocaleFallbackChainBuilder.Build(locale, maxLocales);
            _cache.Add(locale, chain);
            return chain;
        }

        public void Clear() => _cache.Clear();
    }
}
