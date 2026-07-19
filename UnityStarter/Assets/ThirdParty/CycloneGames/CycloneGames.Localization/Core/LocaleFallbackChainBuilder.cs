using System;
using System.Collections.Generic;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Builds bounded, flattened fallback chains from pure locale fallback nodes.
    /// </summary>
    public static class LocaleFallbackChainBuilder
    {
        public const int DefaultMaxLocales = 256;

        public static LocaleId[] Build<TNode>(TNode root)
            where TNode : class, ILocaleFallbackNode<TNode>
        {
            return Build(root, DefaultMaxLocales);
        }

        public static LocaleId[] Build<TNode>(TNode root, int maxLocales)
            where TNode : class, ILocaleFallbackNode<TNode>
        {
            if (root == null) return Array.Empty<LocaleId>();
            if (maxLocales <= 0) throw new ArgumentOutOfRangeException(nameof(maxLocales));

            LocaleId rootId = root.Id;
            if (!rootId.IsValid) return Array.Empty<LocaleId>();

            var result = new List<LocaleId>(Math.Min(maxLocales, 4)) { rootId };
            var visited = new HashSet<string>(Math.Min(maxLocales, 4), StringComparer.Ordinal)
            {
                rootId.Code,
            };
            var queue = new Queue<TNode>(Math.Min(maxLocales, 4));
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                TNode node = queue.Dequeue();
                int count = node.FallbackCount;
                if (count < 0 || count > maxLocales)
                    throw new InvalidOperationException("A locale fallback list exceeds the configured limit.");

                for (int i = 0; i < count; i++)
                {
                    TNode fallback = node.GetFallback(i);
                    if (fallback == null) continue;

                    LocaleId fallbackId = fallback.Id;
                    if (!fallbackId.IsValid || !visited.Add(fallbackId.Code)) continue;
                    if (result.Count >= maxLocales)
                        throw new InvalidOperationException("The locale fallback graph exceeds the configured limit.");

                    result.Add(fallbackId);
                    queue.Enqueue(fallback);
                }
            }

            return result.ToArray();
        }
    }
}
