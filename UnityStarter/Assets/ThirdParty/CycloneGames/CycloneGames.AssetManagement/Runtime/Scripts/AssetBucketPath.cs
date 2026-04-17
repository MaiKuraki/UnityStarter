using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Utility helpers for building hierarchical asset bucket names without hard-coding string concatenation everywhere.
    /// </summary>
    public static class AssetBucketPath
    {
        public const char Separator = '.';
        private const string SeparatorString = ".";

        public static string Combine(string parent, string child)
        {
            if (string.IsNullOrEmpty(parent)) return child ?? string.Empty;
            if (string.IsNullOrEmpty(child)) return parent;
            return string.Concat(parent, SeparatorString, child);
        }

        public static string Combine(string root, string middle, string leaf)
        {
            return Combine(Combine(root, middle), leaf);
        }

        public static bool IsPrefixMatch(string bucket, string prefix)
        {
            if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(prefix)) return false;
            if (bucket.Equals(prefix, StringComparison.Ordinal)) return true;
            if (bucket.Length <= prefix.Length) return false;
            if (!bucket.StartsWith(prefix, StringComparison.Ordinal)) return false;
            return bucket[prefix.Length] == Separator;
        }
    }
}
