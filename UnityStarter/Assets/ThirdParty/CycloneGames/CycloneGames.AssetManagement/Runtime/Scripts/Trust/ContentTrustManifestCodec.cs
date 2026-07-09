using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestCodec
    {
        public const int SCHEMA_VERSION = 1;

        // Wire tokens are string literals by design; enum renames must not silently change persisted manifests.
        private static readonly HashAlgorithmToken[] HashAlgorithmTokens =
        {
            new HashAlgorithmToken(ContentTrustHashAlgorithm.None, "None"),
            new HashAlgorithmToken(ContentTrustHashAlgorithm.Sha256, "Sha256"),
            new HashAlgorithmToken(ContentTrustHashAlgorithm.XxHash64, "XxHash64")
        };

        public static string ToJson(in ContentTrustManifest manifest, bool includeSignature = true)
        {
            var builder = new StringBuilder(EstimateJsonCapacity(in manifest));
            AppendJson(builder, in manifest, includeSignature);
            return builder.ToString();
        }

        public static void AppendJson(
            StringBuilder builder,
            in ContentTrustManifest manifest,
            bool includeSignature = true,
            List<ContentTrustFileEntry> sortWorkspace = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            AppendManifestJson(builder, in manifest, includeSignature, sortWorkspace);
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

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Content trust manifest JSON cannot be null or empty.";
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
                var entries = new List<ContentTrustFileEntry>(sourceEntries.Length);
                for (int i = 0; i < sourceEntries.Length; i++)
                {
                    ContentTrustFileEntryDocument source = sourceEntries[i];
                    if (!TryParseHashAlgorithm(source.hashAlgorithm, out ContentTrustHashAlgorithm algorithm))
                    {
                        error = $"Unsupported content trust hash algorithm: {source.hashAlgorithm}.";
                        return false;
                    }

                    entries.Add(new ContentTrustFileEntry(
                        source.location,
                        source.sizeBytes,
                        algorithm,
                        source.expectedHashHex));
                }

                manifest = new ContentTrustManifest(
                    document.version,
                    entries,
                    document.minimumClientVersion,
                    document.rollbackVersion,
                    document.contentRoot,
                    document.signature);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static int EstimateJsonCapacity(in ContentTrustManifest manifest)
        {
            int count = manifest.Entries?.Count ?? 0;
            return 192 + (count * 128);
        }

        private static void AppendManifestJson(
            StringBuilder builder,
            in ContentTrustManifest manifest,
            bool includeSignature,
            List<ContentTrustFileEntry> sortWorkspace)
        {
            builder.Append('{');
            JsonBuilderUtility.AppendProperty(builder, "schemaVersion", SCHEMA_VERSION, appendComma: false);
            JsonBuilderUtility.AppendProperty(builder, "version", manifest.Version, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "minimumClientVersion", manifest.MinimumClientVersion, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "rollbackVersion", manifest.RollbackVersion, appendComma: true);
            JsonBuilderUtility.AppendProperty(builder, "contentRoot", manifest.ContentRoot, appendComma: true);
            if (includeSignature)
            {
                JsonBuilderUtility.AppendProperty(builder, "signature", manifest.Signature, appendComma: true);
            }

            builder.Append(",\"entries\":[");
            AppendEntriesJson(builder, manifest.Entries, sortWorkspace);
            builder.Append("]}");
        }

        private static void AppendEntriesJson(
            StringBuilder builder,
            IReadOnlyList<ContentTrustFileEntry> entries,
            List<ContentTrustFileEntry> sortWorkspace)
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
            {
                return;
            }

            List<ContentTrustFileEntry> sortedEntries = sortWorkspace ?? new List<ContentTrustFileEntry>(count);
            sortedEntries.Clear();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    sortedEntries.Add(entries[i]);
                }

                sortedEntries.Sort(CompareEntries);

                for (int i = 0; i < sortedEntries.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    ContentTrustFileEntry entry = sortedEntries[i];
                    builder.Append('{');
                    JsonBuilderUtility.AppendProperty(builder, "location", entry.Location, appendComma: false);
                    JsonBuilderUtility.AppendProperty(builder, "sizeBytes", entry.SizeBytes, appendComma: true);
                    JsonBuilderUtility.AppendProperty(builder, "hashAlgorithm", GetHashAlgorithmName(entry.HashAlgorithm), appendComma: true);
                    JsonBuilderUtility.AppendProperty(builder, "expectedHashHex", entry.ExpectedHashHex, appendComma: true);
                    builder.Append('}');
                }
            }
            finally
            {
                sortedEntries.Clear();
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

        private static int CompareEntries(ContentTrustFileEntry x, ContentTrustFileEntry y)
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

        [Serializable]
        private sealed class ContentTrustManifestDocument
        {
            public int schemaVersion;
            public string version;
            public string minimumClientVersion;
            public string rollbackVersion;
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
    }
}
