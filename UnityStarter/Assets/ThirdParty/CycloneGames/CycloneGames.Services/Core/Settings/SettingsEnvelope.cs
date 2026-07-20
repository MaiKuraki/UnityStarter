using System;
using System.Globalization;
using System.Text;
using CycloneGames.Hash.Core;

namespace CycloneGames.Services
{
    internal enum SettingsEnvelopeDecodeError
    {
        None = 0,
        UnsupportedFormat = 1,
        Corrupted = 2
    }

    internal readonly struct SettingsEnvelopeData
    {
        public SettingsEnvelopeData(
            int schemaVersion,
            int payloadOffset,
            int payloadLength,
            SettingsIntegrity integrity)
        {
            SchemaVersion = schemaVersion;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
            Integrity = integrity;
        }

        public int SchemaVersion { get; }

        public int PayloadOffset { get; }

        public int PayloadLength { get; }

        public SettingsIntegrity Integrity { get; }
    }

    internal static class SettingsEnvelope
    {
        private const int CurrentFormatVersion = 1;
        private const int MaximumHeaderBytes = 256;
        private const string MagicLine = "# CycloneGames.Services Settings";
        private const string FormatPrefix = "# format: ";
        private const string SchemaPrefix = "# schema: ";
        private const string ChecksumPrefix = "# xxh64: ";
        private const string SeparatorLine = "---";

        private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(MagicLine);
        private static readonly byte[] ReservedPrefixBytes =
            Encoding.ASCII.GetBytes("# CycloneGames.Services");
        private static readonly byte[] FormatMarkerBytes = Encoding.ASCII.GetBytes("# format: ");
        private static readonly byte[] SchemaMarkerBytes = Encoding.ASCII.GetBytes("# schema: ");
        private static readonly byte[] ChecksumMarkerBytes = Encoding.ASCII.GetBytes("# xxh64: ");
        private static readonly byte[] SeparatorMarkerBytes = Encoding.ASCII.GetBytes("\n---");

        public static int MaximumEnvelopeBytes(int maximumPayloadBytes)
        {
            return checked(maximumPayloadBytes + MaximumHeaderBytes);
        }

        public static bool HasEnvelopeMagic(ReadOnlySpan<byte> content)
        {
            return content.Length >= MagicBytes.Length
                && content.Slice(0, MagicBytes.Length).SequenceEqual(MagicBytes);
        }

        public static bool LooksLikeEnvelope(ReadOnlySpan<byte> content)
        {
            ReadOnlySpan<byte> header = content.Slice(0, Math.Min(content.Length, MaximumHeaderBytes));
            if (header.Length >= ReservedPrefixBytes.Length
                && header.Slice(0, ReservedPrefixBytes.Length).SequenceEqual(ReservedPrefixBytes))
            {
                return true;
            }

            return Contains(header, FormatMarkerBytes)
                && Contains(header, SchemaMarkerBytes)
                && Contains(header, ChecksumMarkerBytes)
                && Contains(header, SeparatorMarkerBytes);
        }

        public static byte[] Encode(int schemaVersion, ReadOnlySpan<byte> payload)
        {
            if (schemaVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            ulong checksum = XxHash64.HashToUInt64(payload);
            string header = string.Concat(
                MagicLine,
                "\n",
                FormatPrefix,
                CurrentFormatVersion.ToString(CultureInfo.InvariantCulture),
                "\n",
                SchemaPrefix,
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                "\n",
                ChecksumPrefix,
                checksum.ToString("X16", CultureInfo.InvariantCulture),
                "\n",
                SeparatorLine,
                "\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] envelope = new byte[checked(headerBytes.Length + payload.Length)];
            Buffer.BlockCopy(headerBytes, 0, envelope, 0, headerBytes.Length);
            payload.CopyTo(envelope.AsSpan(headerBytes.Length));
            return envelope;
        }

        public static SettingsEnvelopeDecodeError TryDecode(
            byte[] content,
            out SettingsEnvelopeData data,
            out string message)
        {
            data = default;
            message = string.Empty;

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (!TryFindSeparator(content, out int headerLength, out int payloadOffset))
            {
                message = "The settings envelope header is incomplete.";
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            string header;
            try
            {
                header = Encoding.ASCII.GetString(content, 0, headerLength)
                    .Replace("\r\n", "\n");
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            string[] lines = header.Split('\n');
            if (lines.Length != 5
                || !string.Equals(lines[0], MagicLine, StringComparison.Ordinal)
                || !lines[1].StartsWith(FormatPrefix, StringComparison.Ordinal)
                || !lines[2].StartsWith(SchemaPrefix, StringComparison.Ordinal)
                || !lines[3].StartsWith(ChecksumPrefix, StringComparison.Ordinal)
                || !string.Equals(lines[4], SeparatorLine, StringComparison.Ordinal))
            {
                message = "The settings envelope header is malformed.";
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            if (!int.TryParse(
                    lines[1].Substring(FormatPrefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int formatVersion))
            {
                message = "The settings envelope format version is invalid.";
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            if (formatVersion != CurrentFormatVersion)
            {
                message = $"Settings envelope format {formatVersion} is not supported.";
                return SettingsEnvelopeDecodeError.UnsupportedFormat;
            }

            if (!int.TryParse(
                    lines[2].Substring(SchemaPrefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int schemaVersion)
                || schemaVersion < 0)
            {
                message = "The settings schema version is invalid.";
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            string checksumText = lines[3].Substring(ChecksumPrefix.Length);
            if (checksumText.Length != 16
                || !ulong.TryParse(
                    checksumText,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out ulong storedChecksum))
            {
                message = "The settings payload checksum is invalid.";
                return SettingsEnvelopeDecodeError.Corrupted;
            }

            int payloadLength = content.Length - payloadOffset;
            ulong actualChecksum = XxHash64.HashToUInt64(
                new ReadOnlySpan<byte>(content, payloadOffset, payloadLength));
            SettingsIntegrity integrity = storedChecksum == actualChecksum
                ? SettingsIntegrity.Valid
                : SettingsIntegrity.Modified;

            data = new SettingsEnvelopeData(
                schemaVersion,
                payloadOffset,
                payloadLength,
                integrity);
            return SettingsEnvelopeDecodeError.None;
        }

        private static bool TryFindSeparator(
            byte[] content,
            out int headerLength,
            out int payloadOffset)
        {
            headerLength = 0;
            payloadOffset = 0;
            int scanLength = Math.Min(content.Length, MaximumHeaderBytes);
            for (int index = 0; index <= scanLength - 4; index++)
            {
                if (content[index] == (byte)'-'
                    && content[index + 1] == (byte)'-'
                    && content[index + 2] == (byte)'-'
                    && content[index + 3] == (byte)'\n')
                {
                    headerLength = index + 3;
                    payloadOffset = index + 4;
                    return true;
                }

                if (index <= scanLength - 5
                    && content[index] == (byte)'-'
                    && content[index + 1] == (byte)'-'
                    && content[index + 2] == (byte)'-'
                    && content[index + 3] == (byte)'\r'
                    && content[index + 4] == (byte)'\n')
                {
                    headerLength = index + 3;
                    payloadOffset = index + 5;
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            if (needle.Length == 0)
            {
                return true;
            }

            for (int offset = 0; offset <= haystack.Length - needle.Length; offset++)
            {
                if (haystack.Slice(offset, needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
