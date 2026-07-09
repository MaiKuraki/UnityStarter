using System;
using System.Buffers;
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
                WriteTo(in manifest, stream);
                return stream.ToArray();
            }
        }

        public static void WriteTo(
            in ContentTrustManifest manifest,
            Stream destination,
            List<ContentTrustFileEntry> sortWorkspace = null)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            byte[] utf8Buffer = null;
            try
            {
                WriteString(destination, MAGIC, ref utf8Buffer);
                WriteInt32LittleEndian(destination, ContentTrustManifestCodec.SCHEMA_VERSION);
                WriteString(destination, manifest.Version, ref utf8Buffer);
                WriteString(destination, manifest.MinimumClientVersion, ref utf8Buffer);
                WriteString(destination, manifest.RollbackVersion, ref utf8Buffer);
                WriteString(destination, manifest.ContentRoot, ref utf8Buffer);

                int count = manifest.Entries?.Count ?? 0;
                WriteInt32LittleEndian(destination, count);
                if (count <= 0)
                {
                    return;
                }

                List<ContentTrustFileEntry> sortedEntries = sortWorkspace ?? new List<ContentTrustFileEntry>(count);
                sortedEntries.Clear();

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        sortedEntries.Add(manifest.Entries[i]);
                    }

                    sortedEntries.Sort(CompareEntries);
                    for (int i = 0; i < sortedEntries.Count; i++)
                    {
                        ContentTrustFileEntry entry = sortedEntries[i];
                        WriteString(destination, entry.Location, ref utf8Buffer);
                        WriteInt64LittleEndian(destination, entry.SizeBytes);
                        WriteInt32LittleEndian(destination, (int)entry.HashAlgorithm);
                        WriteString(destination, entry.ExpectedHashHex, ref utf8Buffer);
                    }
                }
                finally
                {
                    sortedEntries.Clear();
                }
            }
            finally
            {
                if (utf8Buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(utf8Buffer);
                }
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

        private static void WriteString(Stream stream, string value, ref byte[] utf8Buffer)
        {
            if (value == null)
            {
                WriteInt32LittleEndian(stream, -1);
                return;
            }

            int byteCount = Utf8NoBom.GetByteCount(value);
            WriteInt32LittleEndian(stream, byteCount);
            if (byteCount == 0)
            {
                return;
            }

            if (utf8Buffer == null || utf8Buffer.Length < byteCount)
            {
                byte[] previousBuffer = utf8Buffer;
                utf8Buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                if (previousBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(previousBuffer);
                }
            }

            int written = Utf8NoBom.GetBytes(value, 0, value.Length, utf8Buffer, 0);
            stream.Write(utf8Buffer, 0, written);
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
