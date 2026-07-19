using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public static class ContentTrustManifestCanonicalPayload
    {
        private const string MAGIC = "CycloneGames.AssetManagement.ContentTrustManifest";
        internal const int MAX_MATERIALIZED_PAYLOAD_BYTES = 8 * 1024 * 1024;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static byte[] ToBytes(in ContentTrustManifest manifest)
        {
            ContentTrustManifestValidation.ThrowIfUninitialized(in manifest);
            int byteCount = GetByteCount(in manifest);
            ThrowIfMaterializedPayloadTooLarge(byteCount);
            using (var stream = new MemoryStream(byteCount))
            {
                WriteTo(in manifest, stream);
                return stream.ToArray();
            }
        }

        public static void WriteTo(
            in ContentTrustManifest manifest,
            Stream destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            ContentTrustManifestValidation.ThrowIfUninitialized(in manifest);

            byte[] utf8Buffer = null;
            try
            {
                WriteString(destination, MAGIC, ref utf8Buffer);
                WriteInt32LittleEndian(destination, ContentTrustManifestCodec.SCHEMA_VERSION);
                WriteString(destination, manifest.Version, ref utf8Buffer);
                WriteString(destination, manifest.ContentRoot, ref utf8Buffer);

                int count = manifest.Entries?.Count ?? 0;
                WriteInt32LittleEndian(destination, count);
                if (count <= 0)
                {
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    ContentTrustFileEntry entry = manifest.Entries[i];
                    WriteString(destination, entry.Location, ref utf8Buffer);
                    WriteInt64LittleEndian(destination, entry.SizeBytes);
                    WriteInt32LittleEndian(destination, (int)entry.HashAlgorithm);
                    WriteString(destination, entry.ExpectedHashHex, ref utf8Buffer);
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

        internal static int GetByteCount(in ContentTrustManifest manifest)
        {
            ContentTrustManifestValidation.ThrowIfUninitialized(in manifest);

            long byteCount = GetStringByteCount(MAGIC) +
                             sizeof(int) +
                             GetStringByteCount(manifest.Version) +
                             GetStringByteCount(manifest.ContentRoot) +
                             sizeof(int);
            int entryCount = manifest.Entries.Count;
            for (int i = 0; i < entryCount; i++)
            {
                ContentTrustFileEntry entry = manifest.Entries[i];
                byteCount += GetStringByteCount(entry.Location) +
                             sizeof(long) +
                             sizeof(int) +
                             GetStringByteCount(entry.ExpectedHashHex);
            }

            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("Canonical content trust manifest payload exceeds the supported byte-array size.");
            }

            return (int)byteCount;
        }

        internal static void ThrowIfMaterializedPayloadTooLarge(int byteCount)
        {
            if (byteCount < 0 || byteCount > MAX_MATERIALIZED_PAYLOAD_BYTES)
            {
                throw new InvalidOperationException(
                    $"Materialized canonical payload APIs are limited to {MAX_MATERIALIZED_PAYLOAD_BYTES} bytes. Use WriteTo(Stream) or a streaming canonical signer for larger manifests.");
            }
        }

        private static int GetStringByteCount(string value)
        {
            return sizeof(int) + (value == null ? 0 : Utf8NoBom.GetByteCount(value));
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
