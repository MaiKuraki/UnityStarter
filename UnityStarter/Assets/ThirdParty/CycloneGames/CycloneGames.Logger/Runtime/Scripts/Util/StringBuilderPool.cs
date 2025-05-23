using System.Collections.Concurrent;
using System.Text;

namespace CycloneGames.Logger
{
    public static class StringBuilderPool
    {
        private static readonly ConcurrentQueue<StringBuilder> _pool = new ConcurrentQueue<StringBuilder>();
        private const int DefaultCapacity = 256; // Initial capacity for new StringBuilders
        private const int MaxCapacityToRetain = 4096; // Don't pool builders that grew excessively large

        public static StringBuilder Get()
        {
            if (_pool.TryDequeue(out var sb))
            {
                return sb;
            }
            return new StringBuilder(DefaultCapacity);
        }

        public static void Return(StringBuilder sb)
        {
            if (sb == null || sb.Capacity > MaxCapacityToRetain)
            {
                // Optional: Log or handle excessively large SB discard if needed
                return;
            }
            sb.Clear(); // Essential for reuse
            _pool.Enqueue(sb);
        }

        /// <summary>
        /// Convenience method to get the string and return the StringBuilder to the pool.
        /// </summary>
        public static string GetStringAndReturn(StringBuilder sb)
        {
            string result = sb.ToString();
            Return(sb);
            return result;
        }
    }
}