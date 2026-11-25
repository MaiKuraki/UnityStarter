using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Reads record framing without decoding the payload or computing its checksum.
    /// Encoding and parsing share these constants and grammar functions.
    /// </summary>
    public static class PersistenceRecordHeader
    {
        private static readonly byte[] MagicBytes = Ascii("# cgp-record\n");
        private static readonly byte[] CodecIdPrefixBytes = Ascii("# codec-id: ");
        private static readonly byte[] PayloadBytesPrefixBytes = Ascii("# payload-bytes: ");
        private static readonly byte[] ChecksumPrefixBytes = Ascii("# xxh64: ");
        private static readonly byte[] DelimiterBytes = Ascii("---\n");
        private static readonly byte[] ChecksumDomainBytes = Ascii("CGP\0");

        // Callers receive read-only views. No shared array is exposed for mutation.
        internal static ReadOnlySpan<byte> Magic => MagicBytes;
        internal static ReadOnlySpan<byte> CodecIdPrefix => CodecIdPrefixBytes;
        internal static ReadOnlySpan<byte> PayloadBytesPrefix => PayloadBytesPrefixBytes;
        internal static ReadOnlySpan<byte> ChecksumPrefix => ChecksumPrefixBytes;
        internal static ReadOnlySpan<byte> Delimiter => DelimiterBytes;
        internal static ReadOnlySpan<byte> ChecksumDomain => ChecksumDomainBytes;

        /// <summary>
        /// Reads the format token and payload slice of a syntactically valid record.
        /// The checksum and the caller's expected format are not checked here.
        /// </summary>
        public static bool TryRead(
            ReadOnlySpan<byte> record,
            out ReadOnlySpan<byte> formatToken,
            out ReadOnlySpan<byte> payload)
        {
            PersistenceErrorCode error = Read(record, int.MaxValue, out Fields fields);
            formatToken = fields.FormatToken;
            payload = fields.Payload;
            return error == PersistenceErrorCode.None;
        }

        public static bool TryReadFormatToken(
            ReadOnlySpan<byte> record,
            out ReadOnlySpan<byte> formatToken)
        {
            return TryRead(record, out formatToken, out _);
        }

        // Single grammar source for both the public probe and the full record parser. Header
        // scanning stays inside the fixed header budget, so a missing line feed never forces a
        // scan over the payload. Returns None on success and the exact error otherwise.
        internal static PersistenceErrorCode Read(
            ReadOnlySpan<byte> bytes,
            int maximumPayloadBytes,
            out Fields fields)
        {
            fields = default;
            if (!StartsWith(bytes, Magic))
            {
                return PersistenceErrorCode.RecordFormatMismatch;
            }

            int cursor = Magic.Length;
            if (!TryReadValueLine(bytes, ref cursor, CodecIdPrefix, out int codecStart, out int codecLength)
                || !IsValidIdentifier(bytes.Slice(codecStart, codecLength)))
            {
                return PersistenceErrorCode.MalformedRecord;
            }

            if (!TryReadValueLine(bytes, ref cursor, PayloadBytesPrefix, out int lengthStart, out int lengthLength)
                || !TryParseCanonicalInt(bytes.Slice(lengthStart, lengthLength), out int payloadLength))
            {
                return PersistenceErrorCode.MalformedRecord;
            }

            if (!TryReadValueLine(bytes, ref cursor, ChecksumPrefix, out int checksumStart, out int checksumLength)
                || !TryParseUpperHex64(bytes.Slice(checksumStart, checksumLength), out ulong declaredChecksum))
            {
                return PersistenceErrorCode.MalformedRecord;
            }

            if (cursor > PersistenceLimits.MaximumRecordHeaderBytes
                || !TryConsume(bytes, ref cursor, Delimiter))
            {
                return PersistenceErrorCode.MalformedRecord;
            }

            if (payloadLength > maximumPayloadBytes)
            {
                return PersistenceErrorCode.PayloadTooLarge;
            }

            if (payloadLength != bytes.Length - cursor)
            {
                return PersistenceErrorCode.MalformedRecord;
            }

            fields = new Fields(
                bytes.Slice(codecStart, codecLength),
                bytes.Slice(cursor, payloadLength),
                cursor,
                payloadLength,
                declaredChecksum);
            return PersistenceErrorCode.None;
        }

        internal static int DecimalLength(int value)
        {
            int length = 1;
            while (value >= 10)
            {
                value /= 10;
                length++;
            }

            return length;
        }

        internal readonly ref struct Fields
        {
            internal Fields(
                ReadOnlySpan<byte> formatToken,
                ReadOnlySpan<byte> payload,
                int payloadOffset,
                int payloadLength,
                ulong declaredChecksum)
            {
                FormatToken = formatToken;
                Payload = payload;
                PayloadOffset = payloadOffset;
                PayloadLength = payloadLength;
                DeclaredChecksum = declaredChecksum;
            }

            internal ReadOnlySpan<byte> FormatToken { get; }

            internal ReadOnlySpan<byte> Payload { get; }

            internal int PayloadOffset { get; }

            internal int PayloadLength { get; }

            internal ulong DeclaredChecksum { get; }
        }

        private static bool TryReadValueLine(
            ReadOnlySpan<byte> bytes,
            ref int cursor,
            ReadOnlySpan<byte> prefix,
            out int valueStart,
            out int valueLength)
        {
            valueStart = 0;
            valueLength = 0;
            if (!TryConsume(bytes, ref cursor, prefix))
            {
                return false;
            }

            valueStart = cursor;
            int remainingHeaderBudget = PersistenceLimits.MaximumRecordHeaderBytes - cursor;
            if (remainingHeaderBudget <= 0)
            {
                return false;
            }

            int searchLength = Math.Min(bytes.Length - cursor, remainingHeaderBudget);
            int lineFeed = bytes.Slice(cursor, searchLength).IndexOf((byte)'\n');
            if (lineFeed < 0)
            {
                return false;
            }

            valueLength = lineFeed;
            if (valueLength == 0 || bytes[cursor + valueLength - 1] == (byte)'\r')
            {
                return false;
            }

            cursor += lineFeed + 1;
            return true;
        }

        private static bool TryConsume(
            ReadOnlySpan<byte> bytes,
            ref int cursor,
            ReadOnlySpan<byte> expected)
        {
            if (cursor < 0 || expected.Length > bytes.Length - cursor)
            {
                return false;
            }

            if (!bytes.Slice(cursor, expected.Length).SequenceEqual(expected))
            {
                return false;
            }

            cursor += expected.Length;
            return true;
        }

        private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix)
        {
            return bytes.Length >= prefix.Length && bytes.Slice(0, prefix.Length).SequenceEqual(prefix);
        }

        // Canonical decimal only: no sign, no whitespace, no leading zero, no Int32 overflow.
        private static bool TryParseCanonicalInt(ReadOnlySpan<byte> value, out int parsed)
        {
            parsed = 0;
            if (value.IsEmpty || (value.Length > 1 && value[0] == (byte)'0'))
            {
                return false;
            }

            int result = 0;
            for (int index = 0; index < value.Length; index++)
            {
                int digit = value[index] - (byte)'0';
                if ((uint)digit > 9U || result > (int.MaxValue - digit) / 10)
                {
                    return false;
                }

                result = result * 10 + digit;
            }

            parsed = result;
            return true;
        }

        // Mirrors PersistenceCodecId.Validate: [a-z0-9] edges, [a-z0-9._-] elsewhere.
        private static bool IsValidIdentifier(ReadOnlySpan<byte> value)
        {
            if (value.IsEmpty || value.Length > PersistenceCodecId.MaximumTokenLength
                || !IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1]))
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                byte character = value[index];
                if (!IsAlphaNumeric(character)
                    && character != (byte)'-'
                    && character != (byte)'_'
                    && character != (byte)'.')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAlphaNumeric(byte character)
        {
            return (character >= (byte)'a' && character <= (byte)'z')
                || (character >= (byte)'0' && character <= (byte)'9');
        }

        private static bool TryParseUpperHex64(ReadOnlySpan<byte> value, out ulong parsed)
        {
            parsed = 0UL;
            if (value.Length != 16)
            {
                return false;
            }

            ulong result = 0UL;
            for (int index = 0; index < value.Length; index++)
            {
                byte character = value[index];
                int digit;
                if (character >= (byte)'0' && character <= (byte)'9')
                {
                    digit = character - (byte)'0';
                }
                else if (character >= (byte)'A' && character <= (byte)'F')
                {
                    digit = character - (byte)'A' + 10;
                }
                else
                {
                    return false;
                }

                result = (result << 4) | (uint)digit;
            }

            parsed = result;
            return true;
        }

        private static byte[] Ascii(string value)
        {
            var bytes = new byte[value.Length];
            for (int index = 0; index < value.Length; index++)
            {
                bytes[index] = (byte)value[index];
            }

            return bytes;
        }
    }
}
