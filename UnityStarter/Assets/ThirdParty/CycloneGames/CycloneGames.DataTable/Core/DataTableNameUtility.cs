using System;
using System.Globalization;
using System.Text;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Produces portable relative identifiers. It rejects rooted paths, traversal segments,
    /// ambiguous empty segments, control characters, and characters invalid on common consoles
    /// and desktop file systems.
    /// </summary>
    public static class DataTableNameUtility
    {
        public static string NormalizeTableName(string tableName, string dataExtension = ".bytes")
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("A table name is required.", nameof(tableName));
            }

            string trimmedName = tableName.Trim();
            if (trimmedName.EndsWith("/", StringComparison.Ordinal) ||
                trimmedName.EndsWith("\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A table name cannot end with a directory separator.",
                    nameof(tableName));
            }

            string normalizedExtension = NormalizeDataExtension(dataExtension);
            string normalized = NormalizePath(trimmedName);
            if (!string.IsNullOrEmpty(normalizedExtension) &&
                normalized.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - normalizedExtension.Length);
                if (normalized.Length == 0 ||
                    normalized.EndsWith("/", StringComparison.Ordinal) ||
                    char.IsWhiteSpace(normalized[normalized.Length - 1]) ||
                    normalized[normalized.Length - 1] == '.')
                {
                    throw new ArgumentException(
                        "Removing the data extension cannot leave an empty or non-portable final segment.",
                        nameof(tableName));
                }

                normalized = NormalizePath(normalized);
            }

            return normalized;
        }

        public static string NormalizeDataExtension(string dataExtension)
        {
            if (string.IsNullOrWhiteSpace(dataExtension))
            {
                return string.Empty;
            }

            string normalizedExtension = dataExtension.Trim();
            if (normalizedExtension.IndexOf('/') >= 0 || normalizedExtension.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("A data extension cannot contain a path separator.", nameof(dataExtension));
            }

            if (!normalizedExtension.StartsWith(".", StringComparison.Ordinal))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            ValidatePortableCharacters(normalizedExtension, nameof(dataExtension), allowColon: false);
            normalizedExtension = normalizedExtension.Normalize(NormalizationForm.FormC);

            if (normalizedExtension.Length == 1)
            {
                throw new ArgumentException("A data extension must contain at least one character after '.'.", nameof(dataExtension));
            }

            if (normalizedExtension.EndsWith(".", StringComparison.Ordinal) ||
                normalizedExtension.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException(
                    "A data extension cannot end with '.' or contain an empty extension segment.",
                    nameof(dataExtension));
            }

            for (int i = 0; i < normalizedExtension.Length; i++)
            {
                if (char.IsWhiteSpace(normalizedExtension[i]))
                {
                    throw new ArgumentException("A data extension cannot contain whitespace.", nameof(dataExtension));
                }
            }

            ValidatePortableCharacters(normalizedExtension, nameof(dataExtension), allowColon: false);
            return normalizedExtension;
        }

        public static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (IsRootedPortablePath(trimmed))
            {
                throw new ArgumentException("Data-table paths must be relative.", nameof(value));
            }

            string normalized = trimmed.Replace('\\', '/').TrimEnd('/');
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            ValidatePortableCharacters(normalized, nameof(value), allowColon: false);
            normalized = normalized.Normalize(NormalizationForm.FormC);
            ValidatePortableCharacters(normalized, nameof(value), allowColon: false);

            int segmentStart = 0;
            for (int i = 0; i <= normalized.Length; i++)
            {
                if (i != normalized.Length && normalized[i] != '/')
                {
                    continue;
                }

                int segmentLength = i - segmentStart;
                if (segmentLength == 0)
                {
                    throw new ArgumentException("Data-table paths cannot contain empty segments.", nameof(value));
                }

                bool currentDirectory = segmentLength == 1 && normalized[segmentStart] == '.';
                bool parentDirectory = segmentLength == 2 &&
                                       normalized[segmentStart] == '.' &&
                                       normalized[segmentStart + 1] == '.';
                if (currentDirectory || parentDirectory)
                {
                    throw new ArgumentException("Data-table paths cannot contain '.' or '..' segments.", nameof(value));
                }

                if (char.IsWhiteSpace(normalized[segmentStart]) ||
                    char.IsWhiteSpace(normalized[i - 1]) ||
                    normalized[i - 1] == '.')
                {
                    throw new ArgumentException(
                        "Data-table path segments cannot start or end with whitespace or end with '.'.",
                        nameof(value));
                }

                if (IsReservedWindowsDeviceName(normalized, segmentStart, segmentLength))
                {
                    throw new ArgumentException(
                        "Data-table paths cannot use a reserved Windows device name.",
                        nameof(value));
                }

                segmentStart = i + 1;
            }

            return normalized;
        }

        private static bool IsRootedPortablePath(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ||
                   value.StartsWith("\\", StringComparison.Ordinal) ||
                   (value.Length >= 2 && value[1] == ':');
        }

        private static void ValidatePortableCharacters(string value, string parameterName, bool allowColon)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                UnicodeCategory category = char.GetUnicodeCategory(c);
                if (char.IsControl(c) ||
                    char.IsSurrogate(c) ||
                    category == UnicodeCategory.Format ||
                    c == '*' ||
                    c == '?' ||
                    c == '"' ||
                    c == '<' ||
                    c == '>' ||
                    c == '|' ||
                    (!allowColon && c == ':'))
                {
                    throw new ArgumentException(
                        $"Data-table name or path contains a non-portable character at index {i}.",
                        parameterName);
                }
            }
        }

        private static bool IsReservedWindowsDeviceName(string value, int start, int length)
        {
            int baseNameLength = length;
            for (int i = 0; i < length; i++)
            {
                if (value[start + i] == '.')
                {
                    baseNameLength = i;
                    break;
                }
            }

            if (SegmentEquals(value, start, baseNameLength, "CON") ||
                SegmentEquals(value, start, baseNameLength, "PRN") ||
                SegmentEquals(value, start, baseNameLength, "AUX") ||
                SegmentEquals(value, start, baseNameLength, "NUL") ||
                SegmentEquals(value, start, baseNameLength, "CLOCK$"))
            {
                return true;
            }

            if (baseNameLength == 4)
            {
                char digit = value[start + 3];
                bool numberedDevice = digit >= '1' && digit <= '9';
                return numberedDevice &&
                       (SegmentEquals(value, start, 3, "COM") ||
                        SegmentEquals(value, start, 3, "LPT"));
            }

            return false;
        }

        private static bool SegmentEquals(string value, int start, int length, string expected)
        {
            return length == expected.Length &&
                   string.Compare(value, start, expected, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
