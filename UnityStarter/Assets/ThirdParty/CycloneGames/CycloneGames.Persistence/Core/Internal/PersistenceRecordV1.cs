using System;
using System.Buffers.Binary;
using CycloneGames.Hash.Core;

namespace CycloneGames.Persistence
{
    internal static class PersistenceRecordV1
    {
        internal const int RecordVersion = 1;
        internal const string IdentityTransformId = "identity/1";

        private static readonly byte[] RecordPrefix = Ascii("# cgp-record: ");
        private static readonly byte[] ContentVersionPrefix = Ascii("# content-version: ");
        private static readonly byte[] CodecIdPrefix = Ascii("# codec-id: ");
        private static readonly byte[] TransformIdPrefix = Ascii("# transform-id: ");
        private static readonly byte[] PayloadBytesPrefix = Ascii("# payload-bytes: ");
        private static readonly byte[] ChecksumPrefix = Ascii("# xxh64: ");
        private static readonly byte[] Delimiter = Ascii("---\n");
        private static readonly byte[] ChecksumDomain = Ascii("CGP\0");
        private static readonly byte[] IdentityTransformBytes = Ascii(IdentityTransformId);

        internal static byte[] Encode(
            ReadOnlySpan<byte> payload,
            int contentVersion,
            PersistenceCodecId codecId,
            PersistenceLimits limits)
        {
            if (contentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contentVersion));
            }

            if (payload.Length > limits.MaximumPayloadBytes)
            {
                throw new PersistencePayloadBudgetExceededException(limits.MaximumPayloadBytes);
            }

            string codecValue = codecId.Value;
            PersistenceCodecId.Validate(codecValue, nameof(codecId));
            ulong checksum = ComputeChecksum(
                contentVersion,
                codecValue.AsSpan(),
                IdentityTransformId.AsSpan(),
                payload);

            int headerLength = checked(
                RecordPrefix.Length + DecimalLength(RecordVersion) + 1
                + ContentVersionPrefix.Length + DecimalLength(contentVersion) + 1
                + CodecIdPrefix.Length + codecValue.Length + 1
                + TransformIdPrefix.Length + IdentityTransformBytes.Length + 1
                + PayloadBytesPrefix.Length + DecimalLength(payload.Length) + 1
                + ChecksumPrefix.Length + 16 + 1
                + Delimiter.Length);
            if (headerLength > PersistenceLimits.MaximumRecordHeaderBytes)
            {
                throw new InvalidOperationException("Persistence Record V1 header exceeded its fixed budget.");
            }

            byte[] record = new byte[checked(headerLength + payload.Length)];
            int offset = 0;
            WriteBytes(RecordPrefix, record, ref offset);
            WriteDecimal(RecordVersion, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(ContentVersionPrefix, record, ref offset);
            WriteDecimal(contentVersion, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(CodecIdPrefix, record, ref offset);
            WriteAscii(codecValue, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(TransformIdPrefix, record, ref offset);
            WriteBytes(IdentityTransformBytes, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(PayloadBytesPrefix, record, ref offset);
            WriteDecimal(payload.Length, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(ChecksumPrefix, record, ref offset);
            WriteUpperHex(checksum, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(Delimiter, record, ref offset);
            payload.CopyTo(new Span<byte>(record, offset, payload.Length));
            return record;
        }

        internal static PersistenceRecordParseResult Parse(
            byte[] record,
            PersistenceCodecId expectedCodecId,
            int maximumSupportedContentVersion,
            PersistenceLimits limits)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (maximumSupportedContentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSupportedContentVersion));
            }

            ReadOnlySpan<byte> bytes = record;
            if (!StartsWith(bytes, RecordPrefix))
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.RecordFormatMismatch);
            }

            int cursor = 0;
            if (!TryReadValueLine(bytes, ref cursor, RecordPrefix, out int recordStart, out int recordLength)
                || !TryParseCanonicalInt(bytes.Slice(recordStart, recordLength), out int recordVersion))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (recordVersion != RecordVersion)
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.UnsupportedRecordVersion);
            }

            if (!TryReadValueLine(bytes, ref cursor, ContentVersionPrefix, out int versionStart, out int versionLength)
                || !TryParseCanonicalInt(bytes.Slice(versionStart, versionLength), out int contentVersion))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (!TryReadValueLine(bytes, ref cursor, CodecIdPrefix, out int codecStart, out int codecLength)
                || !IsValidIdentifier(bytes.Slice(codecStart, codecLength)))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (!TryReadValueLine(bytes, ref cursor, TransformIdPrefix, out int transformStart, out int transformLength)
                || !IsValidIdentifier(bytes.Slice(transformStart, transformLength)))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (!TryReadValueLine(bytes, ref cursor, PayloadBytesPrefix, out int lengthStart, out int lengthLength)
                || !TryParseCanonicalInt(bytes.Slice(lengthStart, lengthLength), out int payloadLength))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (!TryReadValueLine(bytes, ref cursor, ChecksumPrefix, out int checksumStart, out int checksumLength)
                || !TryParseUpperHex64(bytes.Slice(checksumStart, checksumLength), out ulong expectedChecksum))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (cursor > PersistenceLimits.MaximumRecordHeaderBytes
                || !TryConsume(bytes, ref cursor, Delimiter))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            if (cursor > PersistenceLimits.MaximumRecordHeaderBytes)
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            int actualPayloadLength = bytes.Length - cursor;
            if (payloadLength > limits.MaximumPayloadBytes)
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.PayloadTooLarge);
            }

            if (payloadLength != actualPayloadLength)
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.MalformedRecord);
            }

            ReadOnlySpan<byte> codecBytes = bytes.Slice(codecStart, codecLength);
            ReadOnlySpan<byte> transformBytes = bytes.Slice(transformStart, transformLength);
            ReadOnlySpan<byte> payload = bytes.Slice(cursor, payloadLength);
            ulong actualChecksum = ComputeChecksum(
                contentVersion,
                codecBytes,
                transformBytes,
                payload);
            if (actualChecksum != expectedChecksum)
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.IntegrityCheckFailed);
            }

            if (!AsciiEquals(codecBytes, expectedCodecId.Value))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.CodecMismatch);
            }

            if (!transformBytes.SequenceEqual(IdentityTransformBytes))
            {
                return PersistenceRecordParseResult.Failure(PersistenceErrorCode.TransformMismatch);
            }

            if (contentVersion > maximumSupportedContentVersion)
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.FutureContentVersion);
            }

            return PersistenceRecordParseResult.Success(contentVersion, cursor, payloadLength);
        }

        private static ulong ComputeChecksum(
            int contentVersion,
            ReadOnlySpan<char> codecId,
            ReadOnlySpan<char> transformId,
            ReadOnlySpan<byte> payload)
        {
            Span<byte> codecBytes = stackalloc byte[PersistenceCodecId.MaximumLength];
            Span<byte> transformBytes = stackalloc byte[PersistenceCodecId.MaximumLength];
            for (int index = 0; index < codecId.Length; index++)
            {
                codecBytes[index] = (byte)codecId[index];
            }

            for (int index = 0; index < transformId.Length; index++)
            {
                transformBytes[index] = (byte)transformId[index];
            }

            return ComputeChecksum(
                contentVersion,
                codecBytes.Slice(0, codecId.Length),
                transformBytes.Slice(0, transformId.Length),
                payload);
        }

        private static ulong ComputeChecksum(
            int contentVersion,
            ReadOnlySpan<byte> codecId,
            ReadOnlySpan<byte> transformId,
            ReadOnlySpan<byte> payload)
        {
            XxHash64 checksum = XxHash64.Create();
            checksum.Append(ChecksumDomain);
            Span<byte> integer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(integer, RecordVersion);
            checksum.Append(integer);
            BinaryPrimitives.WriteInt32LittleEndian(integer, contentVersion);
            checksum.Append(integer);
            BinaryPrimitives.WriteInt32LittleEndian(integer, codecId.Length);
            checksum.Append(integer);
            checksum.Append(codecId);
            BinaryPrimitives.WriteInt32LittleEndian(integer, transformId.Length);
            checksum.Append(integer);
            checksum.Append(transformId);
            BinaryPrimitives.WriteInt32LittleEndian(integer, payload.Length);
            checksum.Append(integer);
            checksum.Append(payload);
            return checksum.GetDigest();
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

        private static bool IsValidIdentifier(ReadOnlySpan<byte> value)
        {
            if (value.IsEmpty || value.Length > PersistenceCodecId.MaximumLength
                || !IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1]))
            {
                return false;
            }

            bool hasSeparator = false;
            for (int index = 0; index < value.Length; index++)
            {
                byte character = value[index];
                if (character == (byte)'/')
                {
                    if (hasSeparator)
                    {
                        return false;
                    }

                    hasSeparator = true;
                    continue;
                }

                if (!IsAlphaNumeric(character)
                    && character != (byte)'-'
                    && character != (byte)'_'
                    && character != (byte)'.')
                {
                    return false;
                }
            }

            return hasSeparator;
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

        private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string value)
        {
            if (value == null || bytes.Length != value.Length)
            {
                return false;
            }

            for (int index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != (byte)value[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int DecimalLength(int value)
        {
            int length = 1;
            while (value >= 10)
            {
                value /= 10;
                length++;
            }

            return length;
        }

        private static void WriteDecimal(int value, byte[] destination, ref int offset)
        {
            int length = DecimalLength(value);
            int end = offset + length;
            int cursor = end;
            do
            {
                destination[--cursor] = (byte)('0' + value % 10);
                value /= 10;
            }
            while (cursor > offset);

            offset = end;
        }

        private static void WriteUpperHex(ulong value, byte[] destination, ref int offset)
        {
            for (int shift = 60; shift >= 0; shift -= 4)
            {
                int digit = (int)((value >> shift) & 0xFUL);
                destination[offset++] = (byte)(digit < 10 ? '0' + digit : 'A' + digit - 10);
            }
        }

        private static void WriteAscii(string value, byte[] destination, ref int offset)
        {
            for (int index = 0; index < value.Length; index++)
            {
                destination[offset++] = (byte)value[index];
            }
        }

        private static void WriteBytes(ReadOnlySpan<byte> value, byte[] destination, ref int offset)
        {
            value.CopyTo(new Span<byte>(destination, offset, value.Length));
            offset += value.Length;
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

    internal readonly struct PersistenceRecordParseResult
    {
        private PersistenceRecordParseResult(
            bool isSuccess,
            PersistenceErrorCode errorCode,
            int contentVersion,
            int payloadOffset,
            int payloadLength)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            ContentVersion = contentVersion;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal bool IsSuccess { get; }

        internal PersistenceErrorCode ErrorCode { get; }

        internal int ContentVersion { get; }

        internal int PayloadOffset { get; }

        internal int PayloadLength { get; }

        internal static PersistenceRecordParseResult Success(
            int contentVersion,
            int payloadOffset,
            int payloadLength)
        {
            return new PersistenceRecordParseResult(
                true,
                PersistenceErrorCode.None,
                contentVersion,
                payloadOffset,
                payloadLength);
        }

        internal static PersistenceRecordParseResult Failure(PersistenceErrorCode errorCode)
        {
            return new PersistenceRecordParseResult(false, errorCode, 0, 0, 0);
        }
    }
}
