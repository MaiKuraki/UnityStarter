using System;
using System.Buffers;
using System.IO;
using CycloneGames.Persistence.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;

namespace CycloneGames.Persistence.Tests.MessagePack
{
    [TestFixture]
    public sealed class MessagePackPersistenceCodecTests
    {
        private static readonly MessagePackSecurity Security =
            MessagePackSecurity.UntrustedData
                .WithMaximumObjectGraphDepth(64)
                .WithMaximumDecompressedSize(PersistenceLimits.HardMaximumPayloadBytes);

        [Test]
        public void Codec_RoundTripsGeneratedClassAndUsesStableIdentifier()
        {
            var codec = new MessagePackPersistenceCodec<MessagePackClassContract>(
                GeneratedMessagePackResolver.Instance,
                Security);
            var expected = new MessagePackClassContract
            {
                Revision = 3,
                Name = "pilot"
            };
            var writer = new ArrayBufferWriter<byte>();
            PersistenceWriteContext writeContext = default;

            codec.Serialize(in expected, writer, in writeContext);
            PersistenceReadContext readContext = default;
            MessagePackClassContract actual = codec.Deserialize(
                writer.WrittenMemory,
                in readContext);

            Assert.That(codec.CodecId.Value, Is.EqualTo("messagepack/1"));
            Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
        }

        [Test]
        public void Codec_RoundTripsGeneratedStruct()
        {
            var codec = new MessagePackPersistenceCodec<MessagePackStructContract>(
                GeneratedMessagePackResolver.Instance,
                Security);
            var expected = new MessagePackStructContract
            {
                Level = 17,
                Enabled = true
            };
            var writer = new ArrayBufferWriter<byte>();
            PersistenceWriteContext writeContext = default;
            codec.Serialize(in expected, writer, in writeContext);

            PersistenceReadContext readContext = default;
            MessagePackStructContract actual = codec.Deserialize(
                writer.WrittenMemory,
                in readContext);

            Assert.That(actual.Level, Is.EqualTo(expected.Level));
            Assert.That(actual.Enabled, Is.EqualTo(expected.Enabled));
        }

        [Test]
        public void Codec_RejectsTrailingAndTruncatedPayloads()
        {
            var codec = new MessagePackPersistenceCodec<MessagePackClassContract>(
                GeneratedMessagePackResolver.Instance,
                Security);
            var value = new MessagePackClassContract { Revision = 1, Name = "one" };
            var writer = new ArrayBufferWriter<byte>();
            PersistenceWriteContext writeContext = default;
            codec.Serialize(in value, writer, in writeContext);
            byte[] encoded = writer.WrittenSpan.ToArray();

            var trailing = new byte[encoded.Length + 1];
            Buffer.BlockCopy(encoded, 0, trailing, 0, encoded.Length);
            trailing[trailing.Length - 1] = 0xc0;
            var truncated = new byte[encoded.Length - 1];
            Buffer.BlockCopy(encoded, 0, truncated, 0, truncated.Length);
            PersistenceReadContext readContext = default;

            Assert.Throws<InvalidDataException>(() =>
                codec.Deserialize(trailing, readContext));
            Assert.Catch<Exception>(() =>
                codec.Deserialize(truncated, readContext));
        }

        [Test]
        public void Constructor_RejectsTrustedDataPolicy()
        {
            Assert.Throws<ArgumentException>(() =>
                new MessagePackPersistenceCodec<MessagePackClassContract>(
                    GeneratedMessagePackResolver.Instance,
                    MessagePackSecurity.TrustedData));
        }

        [Test]
        public void Codec_UsesStandardUncompressedWireProfile()
        {
            var value = new MessagePackClassContract { Revision = 5, Name = "stable" };
            var codec = new MessagePackPersistenceCodec<MessagePackClassContract>(
                GeneratedMessagePackResolver.Instance,
                Security);
            var writer = new ArrayBufferWriter<byte>();
            PersistenceWriteContext context = default;

            codec.Serialize(in value, writer, in context);
            byte[] expected = MessagePackSerializer.Serialize(
                value,
                new MessagePackSerializerOptions(GeneratedMessagePackResolver.Instance)
                    .WithCompression(MessagePackCompression.None)
                    .WithOldSpec(false)
                    .WithOmitAssemblyVersion(false)
                    .WithAllowAssemblyVersionMismatch(false)
                    .WithSecurity(Security));

            CollectionAssert.AreEqual(expected, writer.WrittenSpan.ToArray());
        }
    }

    [MessagePackObject]
    public sealed class MessagePackClassContract
    {
        [Key(0)]
        public int Revision { get; set; }

        [Key(1)]
        public string Name { get; set; }
    }

    [MessagePackObject]
    public struct MessagePackStructContract
    {
        [Key(0)]
        public int Level;

        [Key(1)]
        public bool Enabled;
    }
}
