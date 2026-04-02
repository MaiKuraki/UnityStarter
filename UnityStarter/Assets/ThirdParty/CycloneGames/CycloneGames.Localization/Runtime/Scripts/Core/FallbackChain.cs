using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Builds a flattened, deduplicated fallback chain from a Locale's hierarchy.
    /// Cached per locale to avoid repeated traversal.
    /// </summary>
    public sealed class FallbackChain
    {
        // LocaleId.Code is interned → safe as Dictionary key with O(1) lookup
        private readonly Dictionary<string, LocaleId[]> _cache = new Dictionary<string, LocaleId[]>(8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocaleId[] Resolve(Locale locale)
        {
            if (locale == null) return Array.Empty<LocaleId>();

            string key = locale.Id.Code;
            if (_cache.TryGetValue(key, out var chain)) return chain;

            chain = Build(locale);
            _cache[key] = chain;
            return chain;
        }

        public void Clear() => _cache.Clear();

        private static LocaleId[] Build(Locale root)
        {
            // Breadth-first traversal with deduplication
            var result = new List<LocaleId>(4) { root.Id };
            var visited = new HashSet<string>(4) { root.Id.Code };
            var queue = new Queue<Locale>(4);

            foreach (var fb in root.Fallbacks)
                if (fb != null) queue.Enqueue(fb);

            while (queue.Count > 0)
            {
                var locale = queue.Dequeue();
                if (!visited.Add(locale.Id.Code)) continue;

                result.Add(locale.Id);

                foreach (var fb in locale.Fallbacks)
                    if (fb != null) queue.Enqueue(fb);
            }

            return result.ToArray();
        }
    }
}
