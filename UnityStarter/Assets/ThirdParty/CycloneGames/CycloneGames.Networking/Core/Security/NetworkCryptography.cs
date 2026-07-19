using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Threading;

namespace CycloneGames.Networking.Security
{
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

    /// <summary>
    /// Authenticates the stable wire envelope and payload with HMAC-SHA256.
    /// </summary>
    /// <remarks>
    /// Transport-local connection and player identifiers are not part of the signature because
    /// they can differ at each endpoint. Deployments that require cross-connection isolation must
    /// derive and scope keys per authenticated session or peer.
    /// </remarks>
    public sealed class HmacSha256NetworkMessageSigner : INetworkMessageSigner, IDisposable
    {
        public const int SIGNATURE_LENGTH = 32;
        public const int MINIMUM_KEY_LENGTH = 32;

        private readonly object _syncRoot = new object();
        private readonly HMACSHA256 _hmac;
        private bool _disposed;

        public HmacSha256NetworkMessageSigner(ReadOnlySpan<byte> key)
        {
            if (key.Length < MINIMUM_KEY_LENGTH)
            {
                throw new ArgumentException(
                    $"HMAC-SHA256 keys must contain at least {MINIMUM_KEY_LENGTH} bytes of cryptographic key material.",
                    nameof(key));
            }

            byte[] keyCopy = key.ToArray();
            try
            {
                _hmac = new HMACSHA256(keyCopy);
            }
            finally
            {
                Array.Clear(keyCopy, 0, keyCopy.Length);
            }
        }

        public bool IsEnabled => !Volatile.Read(ref _disposed);
        public int SignatureLength => SIGNATURE_LENGTH;

        public bool TrySign(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            Span<byte> signature,
            out int writtenBytes)
        {
            if (signature.Length < SIGNATURE_LENGTH)
            {
                writtenBytes = 0;
                return false;
            }

            if (!envelope.IsValid || envelope.PayloadLength != payload.Length)
            {
                writtenBytes = 0;
                return false;
            }

            var header = new NetworkEnvelopeHeader(
                envelope.MessageId,
                envelope.Channel,
                envelope.PayloadLength,
                envelope.Sequence,
                envelope.Checksum,
                envelope.Flags,
                envelope.Version);
            if (!header.IsSupported)
            {
                writtenBytes = 0;
                return false;
            }

            byte[] rented = RentCanonicalMessage(header, payload, out int byteCount);
            try
            {
                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        writtenBytes = 0;
                        return false;
                    }

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
            if (signature.Length != SIGNATURE_LENGTH)
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
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _hmac.Dispose();
            }
        }

        private static byte[] RentCanonicalMessage(
            in NetworkEnvelopeHeader header,
            ReadOnlySpan<byte> payload,
            out int byteCount)
        {
            // Authenticate the exact stable wire header. Direction and transport-local
            // identities are intentionally excluded because they are derived locally and
            // are not fields in the current wire protocol.
            const int metadataBytes = NetworkWireProtocol.HeaderLength;
            byteCount = metadataBytes + payload.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);

            NetworkFrameCodec.WriteHeader(rented, 0, header);
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

    }
}
