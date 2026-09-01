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
        /// True when <paramref name="inner"/> is a strict descendant of <paramref name="outer"/> —
        /// below it, but not equal. Equality matters: two rules sharing one output folder is the
        /// intended "two rules, one package" case, while one output folder nested inside another
        /// means a collector targeting the outer folder ships the inner rule's atlases and the two
        /// rules can no longer be updated independently.
        /// </summary>
        public static bool IsProperlyUnderFolder(string inner, string outer)
        {
            if (string.IsNullOrEmpty(inner) || string.IsNullOrEmpty(outer))
            {
                return false;
            }

            return inner.Length > outer.Length
                   && inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase)
                   && inner[outer.Length] == '/';
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

            string result = StripUnsafeCharacters(value);
            return string.IsNullOrEmpty(result) ? "Atlas" : result;
        }

        /// <summary>
        /// Reduces a string to letters, digits, underscores and dashes; runs of anything else
        /// collapse to a single underscore, and leading and trailing underscores are trimmed.
        /// Returns an empty string when nothing usable remains.
        /// <para>
        /// Shared by <see cref="SanitizePart"/> and <see cref="SanitizeSubfolder"/> so the two agree
        /// on what a legal character is. The empty-string-vs-fallback decision is left to the caller
        /// on purpose: "Atlas" is the right answer for an unusable atlas key — every atlas needs a
        /// name — but a path segment that sanitizes to nothing must be dropped, not turned into a
        /// folder called Atlas.
        /// </para>
        /// <para>
        /// <see cref="char.IsLetterOrDigit"/> rather than an ASCII test: it accepts CJK and other
        /// scripts, which is what a studio naming its packages in its own language expects.
        /// </para>
        /// </summary>
        private static string StripUnsafeCharacters(string value)
        {
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

            return builder.ToString().Trim('_');
        }

        /// <summary>
        /// Normalizes a rule's output subfolder into a safe relative path, or
        /// <see cref="string.Empty"/> when the atlas belongs directly in the output root.
        /// <para>
        /// This is the primitive that lets one project ship several asset packages: a rule writes
        /// into a folder of its choosing under the shared output root, and a path-based collector
        /// (YooAsset's CollectPath, xasset build entries) picks that folder up as one bundle. Rules
        /// that name the same subfolder share a package.
        /// </para>
        /// <para>
        /// A subfolder rather than a free path on purpose. Keeping every generated atlas under one
        /// root is an invariant the rest of the pipeline leans on — the global exclusion test and
        /// the orphan sweep both reason about the output tree — so a value that could escape it
        /// would quietly break them. Traversal segments ("..") are dropped rather than rejected, so
        /// a mistyped value degrades to a shallower folder instead of an error the artist cannot
        /// act on.
        /// </para>
        /// <para>
        /// Folder names are otherwise preserved as the user wrote them, spaces and non-ASCII
        /// included: the subfolder usually names a folder that already exists under the root, and
        /// generating into a sanitized twin ("UI Battle" becoming "UI_Battle") would silently
        /// create a second folder beside the one that was dragged. Only characters that are invalid
        /// in a path segment on at least one target platform are removed, and Windows's
        /// ignore-trailing-dots-and-spaces behaviour is neutralized by trimming them per segment —
        /// otherwise "UI." names one folder on macOS and another on Windows.
        /// </para>
        /// </summary>
        public static string SanitizeSubfolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string[] segments = value.Split(new[] { '/', '\\' }, System.StringSplitOptions.None);

            var builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                string raw = segments[i];

                // Dropped, not rewritten: SanitizePart maps an unusable segment to "Atlas", which
                // would silently turn ".." into a real folder called Atlas.
                if (raw.Length == 0 || raw == "." || raw == "..")
                {
                    continue;
                }

                string segment = SanitizeSubfolderSegment(raw);
                if (segment.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('/');
                }

                builder.Append(segment);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Makes one subfolder segment safe to create on every target platform without renaming the
        /// folder the user actually made: control characters and the Windows-reserved set
        /// (<c>: * ? " &lt; &gt; |</c>) are removed, and trailing dots and spaces — which Windows
        /// silently ignores, so "UI." and "UI" would be the same folder there and different ones
        /// everywhere else — are trimmed from both ends.
        /// </summary>
        private static string SanitizeSubfolderSegment(string segment)
        {
            var builder = new System.Text.StringBuilder(segment.Length);
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (char.IsControl(c)
                    || c == ':' || c == '*' || c == '?' || c == '"'
                    || c == '<' || c == '>' || c == '|')
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString().Trim(' ', '.');
        }
    }
}
