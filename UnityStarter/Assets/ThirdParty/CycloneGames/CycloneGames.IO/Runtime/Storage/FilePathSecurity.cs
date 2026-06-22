using System;
using System.IO;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Opt-in path validation helpers for sandbox enforcement and path-traversal defense.
    ///
    /// These are intentionally NOT applied automatically by <see cref="FileUtility"/> or
    /// <see cref="FileService"/>, because many legitimate internal paths use relative segments.
    /// Call them explicitly at trust boundaries, for example before opening a path derived from
    /// a downloaded manifest, server response, or user-provided content key.
    ///
    /// Symlinks are not resolved. Case sensitivity follows a per-platform default and may not
    /// match the actual filesystem in every configuration.
    /// </summary>
    public static class FilePathSecurity
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Returns true when <paramref name="path"/> resolves to a location inside
        /// <paramref name="rootDirectory"/>. Both are normalized to absolute paths first.
        /// </summary>
        public static bool IsWithinRoot(string rootDirectory, string path)
        {
            if (string.IsNullOrEmpty(rootDirectory) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedRootWithSeparator = AppendDirectorySeparator(Path.GetFullPath(rootDirectory));
            string normalizedPath = Path.GetFullPath(path);
            StringComparison comparison = GetPathComparison();

            if (normalizedPath.StartsWith(normalizedRootWithSeparator, comparison))
            {
                return true;
            }

            string normalizedRoot = normalizedRootWithSeparator.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedPath, normalizedRoot, comparison);
        }

        /// <summary>
        /// Normalizes <paramref name="path"/> and throws <see cref="UnauthorizedAccessException"/>
        /// when it escapes <paramref name="rootDirectory"/>. Returns the normalized absolute path.
        /// </summary>
        public static string EnsureWithinRoot(string rootDirectory, string path)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("Root directory cannot be null or empty.", nameof(rootDirectory));
            }

            if (!IsWithinRoot(rootDirectory, path))
            {
                throw new UnauthorizedAccessException($"Path '{path}' resolves outside the allowed root '{rootDirectory}'.");
            }

            return Path.GetFullPath(path);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.Length == 0)
            {
                return path;
            }

            char last = path[path.Length - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS
            // Windows (NTFS) and Apple default filesystems are case-insensitive, case-preserving.
            return StringComparison.OrdinalIgnoreCase;
#else
            // Linux/Android and most server filesystems are case-sensitive.
            return StringComparison.Ordinal;
#endif
        }
    }
}
