using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestCanonicalPayload
    {
        private const string MAGIC = "CycloneGames.AssetManagement.ContentTrustManifest";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static byte[] ToBytes(in ContentTrustManifest manifest)
        {
            using (var stream = new MemoryStream(EstimateCapacity(in manifest)))
            {
                WriteString(stream, MAGIC);
                WriteInt32LittleEndian(stream, ContentTrustManifestCodec.SCHEMA_VERSION);
                WriteString(stream, manifest.Version);
                WriteString(stream, manifest.MinimumClientVersion);
                WriteString(stream, manifest.RollbackVersion);
                WriteString(stream, manifest.ContentRoot);

                int count = manifest.Entries?.Count ?? 0;
                WriteInt32LittleEndian(stream, count);
                if (count > 0)
                {
                    var sortedEntries = new List<ContentTrustFileEntry>(count);
                    for (int i = 0; i < count; i++)
                    {
                        sortedEntries.Add(manifest.Entries[i]);
                    }

                    sortedEntries.Sort(CompareEntries);
                    for (int i = 0; i < sortedEntries.Count; i++)
                    {
                        ContentTrustFileEntry entry = sortedEntries[i];
                        WriteString(stream, entry.Location);
                        WriteInt64LittleEndian(stream, entry.SizeBytes);
                        WriteInt32LittleEndian(stream, (int)entry.HashAlgorithm);
                        WriteString(stream, entry.ExpectedHashHex);
                    }
                }

                return stream.ToArray();
            }
        }

        private static int EstimateCapacity(in ContentTrustManifest manifest)
        {
            int count = manifest.Entries?.Count ?? 0;
            return 128 + (count * 96);
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

        private static void WriteString(Stream stream, string value)
        {
            if (value == null)
            {
                WriteInt32LittleEndian(stream, -1);
                return;
            }

            byte[] bytes = Utf8NoBom.GetBytes(value);
            WriteInt32LittleEndian(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt32LittleEndian(Stream stream, int value)
        {
            unchecked
            {
                stream.WriteByte((byte)value);
                stream.WriteByte((byte)(value >> 8));
                stream.WriteByte((byte)(value >> 16));
                stream.WriteByte((byte)(value >> 24));
            }
        }

        private static void WriteInt64LittleEndian(Stream stream, long value)
        {
            unchecked
            {
                stream.WriteByte((byte)value);
                stream.WriteByte((byte)(value >> 8));
                stream.WriteByte((byte)(value >> 16));
                stream.WriteByte((byte)(value >> 24));
                stream.WriteByte((byte)(value >> 32));
                stream.WriteByte((byte)(value >> 40));
                stream.WriteByte((byte)(value >> 48));
                stream.WriteByte((byte)(value >> 56));
            }
        }
    }
}
