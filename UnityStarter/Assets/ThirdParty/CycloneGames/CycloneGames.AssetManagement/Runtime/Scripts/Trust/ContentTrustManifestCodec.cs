using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestCodec
    {
        public const int SCHEMA_VERSION = 2;

        private const string ENTRIES_PREFIX = ",\"entries\":[";

        // Wire tokens are string literals by design; enum renames must not silently change persisted manifests.
        private static readonly HashAlgorithmToken[] HashAlgorithmTokens =
        {
            new HashAlgorithmToken(ContentTrustHashAlgorithm.None, "None"),
            new HashAlgorithmToken(ContentTrustHashAlgorithm.Sha256, "Sha256"),
            new HashAlgorithmToken(ContentTrustHashAlgorithm.XxHash64, "XxHash64")
        };

        public static string ToJson(in ContentTrustManifest manifest, bool includeSignature = true)
        {
            int capacity = GetValidatedJsonCapacity(in manifest, includeSignature);
            var builder = new StringBuilder(capacity);
            AppendManifestJson(builder, in manifest, includeSignature);
            return builder.ToString();
        }

        public static void AppendJson(
            StringBuilder builder,
            in ContentTrustManifest manifest,
            bool includeSignature = true)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            GetValidatedJsonCapacity(in manifest, includeSignature);

            int originalLength = builder.Length;
            try
            {
                AppendManifestJson(builder, in manifest, includeSignature);
                if (!ContentTrustManifestValidation.IsJsonCharCountWithinLimit(builder.Length - originalLength))
                {
                    throw new InvalidOperationException(
                        $"Content trust manifest JSON cannot exceed {ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT} characters.");
                }
            }
            catch
            {
                builder.Length = originalLength;
                throw;
            }
        }

        public static string ToCanonicalPayload(in ContentTrustManifest manifest)
        {
            return Convert.ToBase64String(ToCanonicalPayloadBytes(in manifest));
        }

        public static byte[] ToCanonicalPayloadBytes(in ContentTrustManifest manifest)
        {
            return ContentTrustManifestCanonicalPayload.ToBytes(in manifest);
        }

        public static ContentTrustManifest FromJson(string json)
        {
            if (!TryFromJson(json, out ContentTrustManifest manifest, out string error))
            {
                throw new FormatException(error);
            }

            return manifest;
        }

        public static bool TryFromJson(string json, out ContentTrustManifest manifest, out string error)
        {
            manifest = default;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "Content trust manifest JSON cannot be null or empty.";
                return false;
            }

            if (!ContentTrustManifestValidation.IsJsonCharCountWithinLimit(json.Length))
            {
                error = $"Content trust manifest JSON cannot exceed {ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT} characters.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Content trust manifest JSON cannot contain only whitespace.";
                return false;
            }

            try
            {
                ContentTrustManifestDocument document = JsonUtility.FromJson<ContentTrustManifestDocument>(json);
                if (document == null)
                {
                    error = "Content trust manifest JSON could not be parsed.";
                    return false;
                }

                if (document.schemaVersion != SCHEMA_VERSION)
                {
                    error = $"Unsupported content trust manifest schema version: {document.schemaVersion}.";
                    return false;
                }

                ContentTrustFileEntryDocument[] sourceEntries = document.entries ?? Array.Empty<ContentTrustFileEntryDocument>();
                if (sourceEntries.Length > ContentTrustManifestValidation.MAX_ENTRY_COUNT)
                {
                    error = $"Content trust manifest entry count cannot exceed {ContentTrustManifestValidation.MAX_ENTRY_COUNT}.";
                    return false;
                }

                var entries = new ContentTrustFileEntry[sourceEntries.Length];
                for (int i = 0; i < sourceEntries.Length; i++)
                {
                    ContentTrustFileEntryDocument source = sourceEntries[i];
                    if (source == null)
                    {
                        error = $"Content trust manifest entry at index {i} cannot be null.";
                        return false;
                    }

                    if (string.IsNullOrEmpty(source.hashAlgorithm) ||
                        source.hashAlgorithm.Length > ContentTrustManifestValidation.MAX_HASH_ALGORITHM_TOKEN_LENGTH)
                    {
                        error = $"Unsupported content trust hash algorithm: {source.hashAlgorithm}.";
                        return false;
                    }

                    if (!TryParseHashAlgorithm(source.hashAlgorithm, out ContentTrustHashAlgorithm algorithm))
                    {
                        error = $"Unsupported content trust hash algorithm: {source.hashAlgorithm}.";
                        return false;
                    }

                    entries[i] = new ContentTrustFileEntry(
                        source.location,
                        source.sizeBytes,
                        algorithm,
                        source.expectedHashHex);
                }

                manifest = new ContentTrustManifest(
                    document.version,
                    entries,
                    document.contentRoot,
                    document.signature);
                return true;
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is OverflowException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int GetValidatedJsonCapacity(
            in ContentTrustManifest manifest,
            bool includeSignature)
        {
            ContentTrustManifestValidation.ThrowIfUninitialized(in manifest);

            long jsonCharCount = GetJsonCharCount(in manifest, includeSignature);
            if (jsonCharCount > ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT)
            {
                throw new InvalidOperationException(
                    $"Content trust manifest JSON cannot exceed {ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT} characters.");
            }

            return (int)jsonCharCount;
        }

        internal static long GetJsonCharCount(
            in ContentTrustManifest manifest,
            bool includeSignature)
        {
            long charCount = 1L;
            charCount += GetJsonNumberPropertyCharCount("schemaVersion", SCHEMA_VERSION, appendComma: false);
            charCount += GetJsonStringPropertyCharCount("version", manifest.Version, appendComma: true);
            charCount += GetJsonStringPropertyCharCount("contentRoot", manifest.ContentRoot, appendComma: true);
            if (includeSignature)
            {
                charCount += GetJsonStringPropertyCharCount("signature", manifest.Signature, appendComma: true);
            }

            charCount += ENTRIES_PREFIX.Length;
            int entryCount = manifest.Entries.Count;
            for (int i = 0; i < entryCount; i++)
            {
                if (i > 0)
                {
                    charCount++;
                }

                ContentTrustFileEntry entry = manifest.Entries[i];
                charCount++;
                charCount += GetJsonStringPropertyCharCount("location", entry.Location, appendComma: false);
                charCount += GetJsonNumberPropertyCharCount("sizeBytes", entry.SizeBytes, appendComma: true);
                charCount += GetJsonStringPropertyCharCount(
                    "hashAlgorithm",
                    GetHashAlgorithmName(entry.HashAlgorithm),
                    appendComma: true);
                charCount += GetJsonStringPropertyCharCount(
                    "expectedHashHex",
                    entry.ExpectedHashHex,
                    appendComma: true);
                charCount++;

                if (charCount > ContentTrustManifestValidation.MAX_JSON_CHAR_COUNT)
                {
                    return charCount;
                }
            }

            return charCount + 2L;
        }

        private static long GetJsonStringPropertyCharCount(
            string name,
            string value,
            bool appendComma)
        {
            return (appendComma ? 1L : 0L) +
                   GetJsonStringCharCount(name) +
                   1L +
                   GetJsonStringCharCount(value);
        }

        private static long GetJsonNumberPropertyCharCount(
            string name,
            long value,
            bool appendComma)
        {
            return (appendComma ? 1L : 0L) +
                   GetJsonStringCharCount(name) +
                   1L +
                   GetJsonIntegerCharCount(value);
        }

        private static int GetJsonIntegerCharCount(long value)
        {
            ulong magnitude;
            int charCount;
            if (value < 0L)
            {
                magnitude = (ulong)(-(value + 1L)) + 1UL;
                charCount = 1;
            }
            else
            {
                magnitude = (ulong)value;
                charCount = 0;
            }

            do
            {
                charCount++;
                magnitude /= 10UL;
            }
            while (magnitude != 0UL);

            return charCount;
        }

        private static long GetJsonStringCharCount(string value)
        {
            if (value == null)
            {
                return 4L;
            }

            long charCount = 2L;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                    case '\\':
                    case '\b':
                    case '\f':
                    case '\n':
                    case '\r':
                    case '\t':
                        charCount += 2L;
                        break;
                    default:
                        charCount += character < ' ' ? 6L : 1L;
                        break;
                }
            }

            return charCount;
        }

        private static void AppendManifestJson(
            StringBuilder builder,
            in ContentTrustManifest manifest,
            bool includeSignature)
        {
            builder.Append('{');
            JsonBuilderUtility.AppendProperty(builder, "schemaVersion", SCHEMA_VERSION, appendComma: false);
            JsonBuilderUtility.AppendProperty(builder, "version", manifest.Version, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "contentRoot", manifest.ContentRoot, appendComma: true);
            if (includeSignature)
            {
                JsonBuilderUtility.AppendProperty(builder, "signature", manifest.Signature, appendComma: true);
            }

            builder.Append(ENTRIES_PREFIX);
            AppendEntriesJson(builder, manifest.Entries);
            builder.Append("]}");
        }

        private static void AppendEntriesJson(
            StringBuilder builder,
            IReadOnlyList<ContentTrustFileEntry> entries)
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                ContentTrustFileEntry entry = entries[i];
                builder.Append('{');
                JsonBuilderUtility.AppendProperty(builder, "location", entry.Location, appendComma: false);
                JsonBuilderUtility.AppendProperty(builder, "sizeBytes", entry.SizeBytes, appendComma: true);
                JsonBuilderUtility.AppendProperty(builder, "hashAlgorithm", GetHashAlgorithmName(entry.HashAlgorithm), appendComma: true);
                JsonBuilderUtility.AppendProperty(builder, "expectedHashHex", entry.ExpectedHashHex, appendComma: true);
                builder.Append('}');
            }
        }

        private static bool TryParseHashAlgorithm(string value, out ContentTrustHashAlgorithm algorithm)
        {
            for (int i = 0; i < HashAlgorithmTokens.Length; i++)
            {
                if (string.Equals(HashAlgorithmTokens[i].Name, value, StringComparison.Ordinal))
                {
                    algorithm = HashAlgorithmTokens[i].Value;
                    return true;
                }
            }

            algorithm = default;
            return false;
        }

        private static string GetHashAlgorithmName(ContentTrustHashAlgorithm value)
        {
            for (int i = 0; i < HashAlgorithmTokens.Length; i++)
            {
                if (HashAlgorithmTokens[i].Value == value)
                {
                    return HashAlgorithmTokens[i].Name;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(value), "Unsupported content trust hash algorithm.");
        }

        private readonly struct HashAlgorithmToken
        {
            public readonly ContentTrustHashAlgorithm Value;
            public readonly string Name;

            public HashAlgorithmToken(ContentTrustHashAlgorithm value, string name)
            {
                Value = value;
                Name = name;
            }
        }

#pragma warning disable 0649 // JsonUtility populates these fields through Unity serialization.
        [Serializable]
        private sealed class ContentTrustManifestDocument
        {
            public int schemaVersion;
            public string version;
            public string contentRoot;
            public string signature;
            public ContentTrustFileEntryDocument[] entries;
        }

        [Serializable]
        private sealed class ContentTrustFileEntryDocument
        {
            public string location;
            public long sizeBytes;
            public string hashAlgorithm;
            public string expectedHashHex;
        }
#pragma warning restore 0649
    }
}
