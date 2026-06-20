using System;
using System.Buffers;
using System.Security.Cryptography;

namespace CycloneGames.Networking.Security
{
    public interface INetworkCryptoProvider
    {
        bool IsEnabled { get; }
        int GetMaxProtectedBytes(int plaintextBytes);
        bool TryProtect(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> plaintext,
            Span<byte> protectedPayload,
            out int writtenBytes);
        bool TryUnprotect(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> protectedPayload,
            Span<byte> plaintext,
            out int writtenBytes);
    }

    public sealed class NoopNetworkCryptoProvider : INetworkCryptoProvider
    {
        public static readonly NoopNetworkCryptoProvider Instance = new NoopNetworkCryptoProvider();

        private NoopNetworkCryptoProvider()
        {
        }

        public bool IsEnabled => false;

        public int GetMaxProtectedBytes(int plaintextBytes)
        {
            return Math.Max(0, plaintextBytes);
        }

        public bool TryProtect(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> plaintext,
            Span<byte> protectedPayload,
            out int writtenBytes)
        {
            if (protectedPayload.Length < plaintext.Length)
            {
                writtenBytes = 0;
                return false;
            }

            plaintext.CopyTo(protectedPayload);
            writtenBytes = plaintext.Length;
            return true;
        }

        public bool TryUnprotect(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> protectedPayload,
            Span<byte> plaintext,
            out int writtenBytes)
        {
            if (plaintext.Length < protectedPayload.Length)
            {
                writtenBytes = 0;
                return false;
            }

            protectedPayload.CopyTo(plaintext);
            writtenBytes = protectedPayload.Length;
            return true;
        }
    }

    public interface INetworkMessageSigner
    {
        bool IsEnabled { get; }
        int SignatureLength { get; }
        bool TrySign(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            Span<byte> signature,
            out int writtenBytes);
        bool TryVerify(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> signature);
    }

    public sealed class NoopNetworkMessageSigner : INetworkMessageSigner
    {
        public static readonly NoopNetworkMessageSigner Instance = new NoopNetworkMessageSigner();

        private NoopNetworkMessageSigner()
        {
        }

        public bool IsEnabled => false;
        public int SignatureLength => 0;

        public bool TrySign(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            Span<byte> signature,
            out int writtenBytes)
        {
            writtenBytes = 0;
            return false;
        }

        public bool TryVerify(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> signature)
        {
            return false;
        }
    }

    public sealed class HmacSha256NetworkMessageSigner : INetworkMessageSigner, IDisposable
    {
        public const int SIGNATURE_LENGTH = 32;

        private readonly object _syncRoot = new object();
        private readonly HMACSHA256 _hmac;
        private bool _disposed;

        public HmacSha256NetworkMessageSigner(ReadOnlySpan<byte> key)
        {
            if (key.Length == 0)
            {
                throw new ArgumentException("HMAC key must not be empty.", nameof(key));
            }

            byte[] keyCopy = key.ToArray();
            _hmac = new HMACSHA256(keyCopy);
            Array.Clear(keyCopy, 0, keyCopy.Length);
        }

        public bool IsEnabled => !_disposed;
        public int SignatureLength => SIGNATURE_LENGTH;

        public bool TrySign(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            Span<byte> signature,
            out int writtenBytes)
        {
            if (_disposed || signature.Length < SIGNATURE_LENGTH)
            {
                writtenBytes = 0;
                return false;
            }

            byte[] rented = RentCanonicalMessage(envelope, payload, out int byteCount);
            try
            {
                lock (_syncRoot)
                {
                    return _hmac.TryComputeHash(new ReadOnlySpan<byte>(rented, 0, byteCount), signature, out writtenBytes);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }

        public bool TryVerify(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> signature)
        {
            if (_disposed || signature.Length != SIGNATURE_LENGTH)
            {
                return false;
            }

            Span<byte> expected = stackalloc byte[SIGNATURE_LENGTH];
            return TrySign(connection, envelope, payload, expected, out int writtenBytes)
                   && writtenBytes == SIGNATURE_LENGTH
                   && FixedTimeEquals(expected, signature);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _hmac.Dispose();
        }

        private static byte[] RentCanonicalMessage(
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            out int byteCount)
        {
            const int metadataBytes = 16;
            byteCount = metadataBytes + payload.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);

            rented[0] = envelope.Version;
            WriteUShort(rented, 1, envelope.MessageId);
            rented[3] = (byte)envelope.Direction;
            rented[4] = (byte)envelope.Channel;
            WriteUShort(rented, 5, (ushort)envelope.Flags);
            WriteInt(rented, 7, envelope.PayloadLength);
            WriteUInt(rented, 11, envelope.Sequence);
            rented[15] = 0;
            payload.CopyTo(new Span<byte>(rented, metadataBytes, payload.Length));
            return rented;
        }

        private static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }

        private static void WriteUShort(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt(byte[] buffer, int offset, uint value)
        {
            WriteInt(buffer, offset, unchecked((int)value));
        }
    }
}
