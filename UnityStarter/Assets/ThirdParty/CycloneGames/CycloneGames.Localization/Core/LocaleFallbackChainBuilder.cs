using System;
using System.Collections.Generic;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Builds flattened, deduplicated fallback chains from pure locale fallback nodes.
    /// </summary>
    public static class LocaleFallbackChainBuilder
    {
        public static LocaleId[] Build<TNode>(TNode root) where TNode : class, ILocaleFallbackNode<TNode>
        {
            if (root == null) return Array.Empty<LocaleId>();

            var rootId = root.Id;
            if (!rootId.IsValid) return Array.Empty<LocaleId>();

            var result = new List<LocaleId>(4) { rootId };
            var visited = new HashSet<string>(4, StringComparer.Ordinal) { rootId.Code };
            var queue = new Queue<TNode>(4);

            EnqueueFallbacks(root, queue);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node == null) continue;

                var id = node.Id;
                if (!id.IsValid || !visited.Add(id.Code)) continue;

                result.Add(id);
                EnqueueFallbacks(node, queue);
            }

            return result.ToArray();
        }

        private static void EnqueueFallbacks<TNode>(TNode node, Queue<TNode> queue)
            where TNode : class, ILocaleFallbackNode<TNode>
        {
            int count = node.FallbackCount;
            for (int i = 0; i < count; i++)
            {
                var fallback = node.GetFallback(i);
                if (fallback != null)
                    queue.Enqueue(fallback);
            }
        }
    }
}
