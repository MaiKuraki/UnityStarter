using System;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Allocation-conscious helpers for Assets/-relative paths. Every "range" overload exists so a
    /// caller can hash or compare a path segment without materializing a substring.
    /// At tens of thousands of sprites the difference matters: the original pipeline built a new
    /// string per asset per rule just to ask "is this under that folder", which dominated the
    /// allocation profile of a full index rebuild.
    /// </summary>
    public static class AtlasPathUtility
    {
        public const string PngExtension = ".png";
        public const string JpgExtension = ".jpg";
        public const string JpegExtension = ".jpeg";

        /// <summary>
        /// True when the path ends with one of the supported source image extensions. Uses the
        /// ordinal-ignore-case overload, which compares in place and allocates nothing.
        /// </summary>
        public static bool IsSupportedImagePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < PngExtension.Length)
            {
                return false;
            }

            return path.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(JpgExtension, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(JpegExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/');
        }

        public static string NormalizeAndTrim(string path)
        {
            return Normalize(path).Trim().TrimEnd('/');
        }

        /// <summary>
        /// True when <paramref name="path"/> is <paramref name="folder"/> itself or lives directly
        /// or transitively below it. Performs no concatenation and allocates nothing.
        /// </summary>
        public static bool IsUnderFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder))
            {
                return false;
            }

            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.Length <= folder.Length
                || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return path[folder.Length] == '/';
        }

        /// <summary>
        /// True when two Assets/-relative folders are equal or one is an ancestor of the other.
        /// Used to keep the generated atlas output folder disjoint from every rule's source folder:
        /// when they overlap, every source image looks like an "intrusion" and a single confirmation
        /// would relocate an entire art directory.
        /// </summary>
        public static bool PathsOverlap(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            return IsUnderFolder(left, right) || IsUnderFolder(right, left);
        }

        public static int LastSeparatorIndex(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return -1;
            }

            int slash = path.LastIndexOf('/');
            int backslash = path.LastIndexOf('\\');
            return Math.Max(slash, backslash);
        }

        /// <summary>
        /// Locates the file stem inside <paramref name="path"/> without allocating, so the caller can
        /// hash it directly through <see cref="AtlasHash.ComputeFnv1a(string,int,int)"/>.
        /// </summary>
        public static void GetStemRange(string path, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            int begin = LastSeparatorIndex(path) + 1;
            int end = path.Length;
            int dot = path.LastIndexOf('.');
            if (dot > begin)
            {
                end = dot;
            }

            start = begin;
            length = end - begin;
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            GetStemRange(path, out int start, out int length);
            return length <= 0 ? string.Empty : path.Substring(start, length);
        }

        public static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            int start = LastSeparatorIndex(path) + 1;
            return start >= path.Length ? path : path.Substring(start);
        }

        /// <summary>
        /// Index of the first directory separator, or -1 when the path is a single segment. Used by
        /// per-child-folder granularity, which must not split into an array.
        /// </summary>
        public static int FirstSeparatorIndex(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return -1;
            }

            int slash = path.IndexOf('/');
            int backslash = path.IndexOf('\\');
            if (slash < 0)
            {
                return backslash;
            }

            return backslash < 0 ? slash : Math.Min(slash, backslash);
        }

        /// <summary>
        /// Replaces every character that is not a Unicode letter, digit, underscore or hyphen with a
        /// single underscore, then trims leading and trailing underscores. Returns "Atlas" when
        /// nothing usable remains.
        /// The fast path scans first and returns the input instance unchanged when it is already
        /// clean, which is the common case for groups such as "UI" or "icon_01" and avoids a
        /// StringBuilder allocation per atlas key.
        /// </summary>
        public static string SanitizePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Atlas";
            }

            bool needsSanitize = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    needsSanitize = true;
                    break;
                }
            }

            if (!needsSanitize && value[0] != '_' && value[value.Length - 1] != '_')
            {
                return value;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            bool previousWasSeparator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                    previousWasSeparator = false;
                    continue;
                }

                if (!previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
            }

            string result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "Atlas" : result;
        }
    }
}
