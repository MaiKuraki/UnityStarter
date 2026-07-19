using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    internal static class ContentTrustManifestValidation
    {
        // Hard ceilings bound cold-path CPU and memory before untrusted manifest data reaches providers.
        internal const int MAX_ENTRY_COUNT = 131072;
        internal const int MAX_JSON_CHAR_COUNT = 16 * 1024 * 1024;
        internal const int MAX_VERSION_UTF8_BYTES = 256;
        internal const int MAX_CONTENT_ROOT_UTF8_BYTES = 1024;
        internal const int MAX_ENTRY_LOCATION_UTF8_BYTES = 2048;
        internal const int MAX_SIGNATURE_UTF8_BYTES = 16 * 1024;
        internal const int MAX_HASH_ALGORITHM_TOKEN_LENGTH = 32;
        internal const long MAX_TOTAL_CONTENT_SIZE_BYTES = 64L * 1024L * 1024L * 1024L * 1024L;

        internal static readonly IComparer<ContentTrustFileEntry> CanonicalEntryComparer = new EntryComparer();

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static ContentTrustFileEntry[] CopyValidateAndSortEntries(
            IReadOnlyList<ContentTrustFileEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            int count = entries.Count;
            if (count < 0 || count > MAX_ENTRY_COUNT)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entries),
                    count,
                    $"Content trust manifest entry count must be between 0 and {MAX_ENTRY_COUNT}.");
            }

            if (count == 0)
            {
                return Array.Empty<ContentTrustFileEntry>();
            }

            var copy = new ContentTrustFileEntry[count];
            HashSet<string> locations = count > 1
                ? new HashSet<string>(count, StringComparer.OrdinalIgnoreCase)
                : null;
            long totalContentSizeBytes = 0L;
            for (int i = 0; i < count; i++)
            {
                ContentTrustFileEntry sourceEntry = entries[i];
                ContentTrustFileEntry entry = NormalizeEntry(in sourceEntry, i);
                if (locations != null && !locations.Add(entry.Location))
                {
                    throw new ArgumentException(
                        $"Duplicate content trust manifest entry location: {entry.Location}",
                        nameof(entries));
                }

                if (entry.SizeBytes > MAX_TOTAL_CONTENT_SIZE_BYTES - totalContentSizeBytes)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(entries),
                        $"Content trust manifest total content size cannot exceed {MAX_TOTAL_CONTENT_SIZE_BYTES} bytes.");
                }

                totalContentSizeBytes += entry.SizeBytes;
                copy[i] = entry;
            }

            Array.Sort(copy, CanonicalEntryComparer);
            return copy;
        }

        internal static ContentTrustFileEntry NormalizeEntry(in ContentTrustFileEntry entry, int index = -1)
        {
            string location = NormalizeRequiredPath(entry.Location, nameof(entry.Location));
            if (entry.SizeBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entry.SizeBytes),
                    entry.SizeBytes,
                    $"{GetEntryContext(index)} size cannot be negative.");
            }

            string expectedHashHex;
            switch (entry.HashAlgorithm)
            {
                case ContentTrustHashAlgorithm.None:
                    if (!string.IsNullOrEmpty(entry.ExpectedHashHex))
                    {
                        throw new ArgumentException(
                            $"{GetEntryContext(index)} cannot specify an expected hash when its hash algorithm is None.",
                            nameof(entry.ExpectedHashHex));
                    }

                    expectedHashHex = null;
                    break;
                case ContentTrustHashAlgorithm.Sha256:
                    expectedHashHex = NormalizeHash(entry.ExpectedHashHex, 64, index);
                    break;
                case ContentTrustHashAlgorithm.XxHash64:
                    expectedHashHex = NormalizeHash(entry.ExpectedHashHex, 16, index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(entry.HashAlgorithm),
                        entry.HashAlgorithm,
                        $"{GetEntryContext(index)} uses an unsupported hash algorithm.");
            }

            return new ContentTrustFileEntry(location, entry.SizeBytes, entry.HashAlgorithm, expectedHashHex);
        }

        internal static string NormalizeRequiredVersion(string value)
        {
            return NormalizeRequiredText(value, nameof(value), MAX_VERSION_UTF8_BYTES);
        }

        internal static string NormalizeOptionalContentRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return NormalizeRequiredPath(value, nameof(value), MAX_CONTENT_ROOT_UTF8_BYTES);
        }

        internal static string NormalizeRequiredPath(
            string value,
            string parameterName,
            int maxUtf8Bytes = MAX_ENTRY_LOCATION_UTF8_BYTES)
        {
            string normalized = NormalizeRequiredText(value, parameterName, maxUtf8Bytes).Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized[0] == '/' || HasDrivePrefix(normalized))
            {
                throw new ArgumentException("Content trust paths must be relative.", parameterName);
            }

            int segmentStart = 0;
            for (int i = 0; i < normalized.Length; i++)
            {
                char valueCharacter = normalized[i];
                if (valueCharacter == '/')
                {
                    ValidatePathSegment(normalized, segmentStart, i - segmentStart, parameterName);
                    segmentStart = i + 1;
                    continue;
                }

                if (char.IsControl(valueCharacter) || IsPortableInvalidPathCharacter(valueCharacter))
                {
                    throw new ArgumentException("Content trust paths contain a character that is not portable.", parameterName);
                }
            }

            ValidatePathSegment(normalized, segmentStart, normalized.Length - segmentStart, parameterName);
            return normalized;
        }

        internal static string NormalizeOptionalSignature(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            ValidateUtf8Length(value, nameof(value), MAX_SIGNATURE_UTF8_BYTES);
            return value;
        }

        internal static void ThrowIfUninitialized(in ContentTrustManifest manifest)
        {
            if (manifest.Entries == null || string.IsNullOrEmpty(manifest.Version))
            {
                throw new InvalidOperationException("Content trust manifest is not initialized.");
            }
        }

        internal static bool IsJsonCharCountWithinLimit(int charCount)
        {
            return charCount >= 0 && charCount <= MAX_JSON_CHAR_COUNT;
        }

        internal static int CompareEntries(ContentTrustFileEntry x, ContentTrustFileEntry y)
        {
            int location = string.CompareOrdinal(x.Location, y.Location);
            if (location != 0)
            {
                return location;
            }

            int size = x.SizeBytes.CompareTo(y.SizeBytes);
            if (size != 0)
            {
                return size;
            }

            int algorithm = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
            return algorithm != 0 ? algorithm : string.CompareOrdinal(x.ExpectedHashHex, y.ExpectedHashHex);
        }

        private static string NormalizeRequiredText(string value, string parameterName, int maxUtf8Bytes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Content trust manifest fields cannot be null, empty, or whitespace.", parameterName);
            }

            string trimmed = value.Trim();
            string normalized = trimmed.IsNormalized(NormalizationForm.FormC)
                ? trimmed
                : trimmed.Normalize(NormalizationForm.FormC);
            ValidateUtf8Length(normalized, parameterName, maxUtf8Bytes);
            ValidateNoControlCharacters(normalized, parameterName);
            return normalized;
        }

        private static string NormalizeHash(string value, int expectedLength, int index)
        {
            if (string.IsNullOrEmpty(value) || value.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"{GetEntryContext(index)} expected hash must contain exactly {expectedLength} hexadecimal characters.",
                    nameof(value));
            }

            bool containsUppercase = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isHex = (character >= '0' && character <= '9') ||
                             (character >= 'a' && character <= 'f') ||
                             (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    throw new ArgumentException(
                        $"{GetEntryContext(index)} expected hash is not hexadecimal.",
                        nameof(value));
                }

                containsUppercase |= character >= 'A' && character <= 'F';
            }

            return containsUppercase ? value.ToLowerInvariant() : value;
        }

        private static string GetEntryContext(int index)
        {
            return index < 0
                ? "Content trust manifest entry"
                : $"Content trust manifest entry at index {index}";
        }

        private static void ValidateUtf8Length(string value, string parameterName, int maxUtf8Bytes)
        {
            int utf8ByteCount;
            try
            {
                utf8ByteCount = StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException ex)
            {
                throw new ArgumentException("Content trust manifest fields must contain valid Unicode text.", parameterName, ex);
            }

            if (utf8ByteCount > maxUtf8Bytes)
            {
                throw new ArgumentException(
                    $"Content trust manifest field exceeds its {maxUtf8Bytes}-byte UTF-8 limit.",
                    parameterName);
            }
        }

        private static void ValidateNoControlCharacters(string value, string parameterName)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    throw new ArgumentException(
                        "Content trust manifest fields cannot contain control characters.",
                        parameterName);
                }
            }
        }

        private static void ValidatePathSegment(string path, int start, int length, string parameterName)
        {
            if (length <= 0)
            {
                throw new ArgumentException("Content trust paths cannot contain empty segments.", parameterName);
            }

            if ((length == 1 && path[start] == '.') ||
                (length == 2 && path[start] == '.' && path[start + 1] == '.'))
            {
                throw new ArgumentException("Content trust paths cannot contain relative traversal segments.", parameterName);
            }

            char finalCharacter = path[start + length - 1];
            if (finalCharacter == '.' || finalCharacter == ' ')
            {
                throw new ArgumentException("Content trust path segments cannot end with a period or space.", parameterName);
            }

            if (IsReservedWindowsDeviceName(path, start, length))
            {
                throw new ArgumentException("Content trust paths cannot use reserved device names.", parameterName);
            }
        }

        private static bool HasDrivePrefix(string path)
        {
            return path.Length >= 2 &&
                   ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) &&
                   path[1] == ':';
        }

        private static bool IsPortableInvalidPathCharacter(char value)
        {
            switch (value)
            {
                case ':':
                case '*':
                case '?':
                case '"':
                case '<':
                case '>':
                case '|':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsReservedWindowsDeviceName(string path, int start, int length)
        {
            int baseLength = length;
            int extensionSeparator = path.IndexOf('.', start, length);
            if (extensionSeparator >= 0)
            {
                baseLength = extensionSeparator - start;
            }

            if (baseLength == 3)
            {
                return EqualsOrdinalIgnoreCase(path, start, "CON") ||
                       EqualsOrdinalIgnoreCase(path, start, "PRN") ||
                       EqualsOrdinalIgnoreCase(path, start, "AUX") ||
                       EqualsOrdinalIgnoreCase(path, start, "NUL");
            }

            if (baseLength != 4)
            {
                return false;
            }

            bool isNumberedDevice = EqualsOrdinalIgnoreCase(path, start, "COM") ||
                                    EqualsOrdinalIgnoreCase(path, start, "LPT");
            char suffix = path[start + 3];
            return isNumberedDevice && suffix >= '1' && suffix <= '9';
        }

        private static bool EqualsOrdinalIgnoreCase(string value, int start, string expected)
        {
            if (start < 0 || start + expected.Length > value.Length)
            {
                return false;
            }

            return string.Compare(
                value,
                start,
                expected,
                0,
                expected.Length,
                StringComparison.OrdinalIgnoreCase) == 0;
        }

        private sealed class EntryComparer : IComparer<ContentTrustFileEntry>
        {
            public int Compare(ContentTrustFileEntry x, ContentTrustFileEntry y)
            {
                return CompareEntries(x, y);
            }
        }
    }
}
