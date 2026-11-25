using System;
using System.Buffers.Binary;
using CycloneGames.Hash.Core;

namespace CycloneGames.Persistence
{
    // Encodes records and completes validation. Framing constants, grammar functions, and the
    // public format probe live in PersistenceRecordHeader so both paths share one syntax source.
    internal static class PersistenceRecord
    {
        internal static byte[] Encode(
            ReadOnlySpan<byte> payload,
            PersistenceCodecId codecId,
            PersistenceLimits limits)
        {
            if (payload.Length > limits.MaximumPayloadBytes)
            {
                throw new PersistencePayloadBudgetExceededException(limits.MaximumPayloadBytes);
            }

            string codecValue = codecId.Value;
            PersistenceCodecId.Validate(codecValue, nameof(codecId));
            ulong checksum = ComputeChecksum(codecValue.AsSpan(), payload);

            int headerLength = checked(
                PersistenceRecordHeader.Magic.Length
                + PersistenceRecordHeader.CodecIdPrefix.Length + codecValue.Length + 1
                + PersistenceRecordHeader.PayloadBytesPrefix.Length
                    + PersistenceRecordHeader.DecimalLength(payload.Length) + 1
                + PersistenceRecordHeader.ChecksumPrefix.Length + 16 + 1
                + PersistenceRecordHeader.Delimiter.Length);
            if (headerLength > PersistenceLimits.MaximumRecordHeaderBytes)
            {
                throw new InvalidOperationException("The persistence record header exceeded its fixed budget.");
            }

            byte[] record = new byte[checked(headerLength + payload.Length)];
            int offset = 0;
            WriteBytes(PersistenceRecordHeader.Magic, record, ref offset);
            WriteBytes(PersistenceRecordHeader.CodecIdPrefix, record, ref offset);
            WriteAscii(codecValue, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(PersistenceRecordHeader.PayloadBytesPrefix, record, ref offset);
            WriteDecimal(payload.Length, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(PersistenceRecordHeader.ChecksumPrefix, record, ref offset);
            WriteUpperHex(checksum, record, ref offset);
            record[offset++] = (byte)'\n';
            WriteBytes(PersistenceRecordHeader.Delimiter, record, ref offset);
            payload.CopyTo(new Span<byte>(record, offset, payload.Length));
            return record;
        }

        // Validation order: header syntax, declared length, checksum, then format identity.
        // The codec is called only after Parse succeeds, so a rejected record never reaches it.
        internal static PersistenceRecordParseResult Parse(
            byte[] record,
            PersistenceCodecId expectedCodecId,
            PersistenceLimits limits)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            PersistenceErrorCode headerError = PersistenceRecordHeader.Read(
                record,
                limits.MaximumPayloadBytes,
                out PersistenceRecordHeader.Fields fields);
            if (headerError != PersistenceErrorCode.None)
            {
                return PersistenceRecordParseResult.Failure(headerError);
            }

            if (ComputeChecksum(fields.FormatToken, fields.Payload) != fields.DeclaredChecksum)
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.IntegrityCheckFailed);
            }

            if (!AsciiEquals(fields.FormatToken, expectedCodecId.Value))
            {
                return PersistenceRecordParseResult.Failure(
                    PersistenceErrorCode.RecordFormatMismatch);
            }

            return PersistenceRecordParseResult.Success(fields.PayloadOffset, fields.PayloadLength);
        }

        // xxHash64 with a domain separator and little-endian length-prefixed fields. The codec
        // token is part of the digest, so substituting another wire format invalidates it.
        private static ulong ComputeChecksum(
            ReadOnlySpan<char> codecId,
            ReadOnlySpan<byte> payload)
        {
            Span<byte> codecBytes = stackalloc byte[PersistenceCodecId.MaximumTokenLength];
            for (int index = 0; index < codecId.Length; index++)
            {
                codecBytes[index] = (byte)codecId[index];
            }

            return ComputeChecksum(codecBytes.Slice(0, codecId.Length), payload);
        }

        private static ulong ComputeChecksum(
            ReadOnlySpan<byte> codecId,
            ReadOnlySpan<byte> payload)
        {
            XxHash64 checksum = XxHash64.Create();
            checksum.Append(PersistenceRecordHeader.ChecksumDomain);
            Span<byte> integer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(integer, codecId.Length);
            checksum.Append(integer);
            checksum.Append(codecId);
            BinaryPrimitives.WriteInt32LittleEndian(integer, payload.Length);
            checksum.Append(integer);
            checksum.Append(payload);
            return checksum.GetDigest();
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

        private static void WriteDecimal(int value, byte[] destination, ref int offset)
        {
            int length = PersistenceRecordHeader.DecimalLength(value);
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
    }

    internal readonly struct PersistenceRecordParseResult
    {
        private PersistenceRecordParseResult(
            bool isSuccess,
            PersistenceErrorCode errorCode,
            int payloadOffset,
            int payloadLength)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal bool IsSuccess { get; }

        internal PersistenceErrorCode ErrorCode { get; }

        internal int PayloadOffset { get; }

        internal int PayloadLength { get; }

        internal static PersistenceRecordParseResult Success(
            int payloadOffset,
            int payloadLength)
        {
            return new PersistenceRecordParseResult(
                true,
                PersistenceErrorCode.None,
                payloadOffset,
                payloadLength);
        }

        internal static PersistenceRecordParseResult Failure(PersistenceErrorCode errorCode)
        {
            return new PersistenceRecordParseResult(false, errorCode, 0, 0);
        }
    }
}
