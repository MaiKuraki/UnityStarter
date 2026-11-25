using System;
using System.Buffers;
using System.IO;
using System.Threading;
using MessagePack;

namespace CycloneGames.Persistence.MessagePack
{
    public sealed class MessagePackPersistenceCodec<T> : IPersistenceCodec<T>
    {
        private static readonly PersistenceCodecId StableCodecId =
            new PersistenceCodecId("messagepack-nocompress");

        private readonly MessagePackSerializerOptions _options;

        public MessagePackPersistenceCodec(
            IFormatterResolver resolver,
            MessagePackSecurity security)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (resolver.GetFormatter<T>() == null)
            {
                throw new ArgumentException(
                    $"The MessagePack resolver does not contain an explicit formatter for {typeof(T).FullName}.",
                    nameof(resolver));
            }

            if (security == null)
            {
                throw new ArgumentNullException(nameof(security));
            }

            if (!security.HashCollisionResistant
                || security.MaximumObjectGraphDepth <= 0
                || security.MaximumDecompressedSize <= 0
                || security.MaximumDecompressedSize > PersistenceLimits.HardMaximumPayloadBytes)
            {
                throw new ArgumentException(
                    "MessagePack security must use collision-resistant hashing and positive graph and decompression limits no larger than the persistence hard limit.",
                    nameof(security));
            }

            // The wire profile is bound to the messagepack-nocompress token: uncompressed, current
            // spec, strict assembly versions. Options are fixed at construction and never mutated.
            _options = new MessagePackSerializerOptions(resolver)
                .WithCompression(MessagePackCompression.None)
                .WithOldSpec(false)
                .WithOmitAssemblyVersion(false)
                .WithAllowAssemblyVersionMismatch(false)
                .WithSecurity(security);
        }

        public PersistenceCodecId CodecId => StableCodecId;

        public void Serialize(
            in T value,
            IBufferWriter<byte> destination,
            in PersistenceWriteContext context)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            MessagePackSerializer.Serialize(
                destination,
                value,
                _options,
                context.CancellationToken);
        }

        public T Deserialize(
            ReadOnlyMemory<byte> payload,
            in PersistenceReadContext context)
        {
            T value = MessagePackSerializer.Deserialize<T>(
                payload,
                _options,
                out int bytesRead,
                context.CancellationToken);
            // One record carries exactly one value; trailing bytes indicate a framing or encoder error.
            if (bytesRead != payload.Length)
            {
                throw new InvalidDataException(
                    $"The MessagePack payload contains {payload.Length - bytesRead} trailing bytes.");
            }

            return value;
        }
    }
}
