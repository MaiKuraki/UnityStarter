using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestCodec
    {
        public const int SCHEMA_VERSION = 1;

        public static string ToJson(in ContentTrustManifest manifest, bool includeSignature = true)
        {
            var builder = new StringBuilder(EstimateJsonCapacity(in manifest));
            AppendManifestJson(builder, in manifest, includeSignature);
            return builder.ToString();
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

        private static void AppendManifestJson(StringBuilder builder, in ContentTrustManifest manifest, bool includeSignature)
        {
            builder.Append('{');
            AppendJsonProperty(builder, "schemaVersion", SCHEMA_VERSION, appendComma: false);
            AppendJsonProperty(builder, "version", manifest.Version, appendComma: true);
            AppendJsonProperty(builder, "minimumClientVersion", manifest.MinimumClientVersion, appendComma: true);
            AppendJsonProperty(builder, "rollbackVersion", manifest.RollbackVersion, appendComma: true);
            AppendJsonProperty(builder, "contentRoot", manifest.ContentRoot, appendComma: true);
            if (includeSignature)
            {
                AppendJsonProperty(builder, "signature", manifest.Signature, appendComma: true);
            }

            builder.Append(",\"entries\":[");
            AppendEntriesJson(builder, manifest.Entries);
            builder.Append("]}");
        }

        private static void AppendEntriesJson(StringBuilder builder, IReadOnlyList<ContentTrustFileEntry> entries)
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
            {
                return;
            }

            var sortedEntries = new List<ContentTrustFileEntry>(count);
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
                AppendJsonProperty(builder, "location", entry.Location, appendComma: false);
                AppendJsonProperty(builder, "sizeBytes", entry.SizeBytes, appendComma: true);
                AppendJsonProperty(builder, "hashAlgorithm", entry.HashAlgorithm.ToString(), appendComma: true);
                AppendJsonProperty(builder, "expectedHashHex", entry.ExpectedHashHex, appendComma: true);
                builder.Append('}');
            }
        }

        private static bool TryParseHashAlgorithm(string value, out ContentTrustHashAlgorithm algorithm)
        {
            if (string.IsNullOrEmpty(value))
            {
                algorithm = ContentTrustHashAlgorithm.None;
                return true;
            }

            return Enum.TryParse(value, ignoreCase: false, out algorithm);
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

        private static void AppendJsonProperty(StringBuilder builder, string name, string value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonString(builder, value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, int value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        private static void AppendJsonProperty(StringBuilder builder, string name, long value, bool appendComma)
        {
            if (appendComma)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, name);
            builder.Append(':');
            builder.Append(value);
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
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
