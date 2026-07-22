using System;
using System.Buffers;
using CycloneGames.Persistence;
using CycloneGames.Persistence.VYaml;
using CycloneGames.Tests.Persistence.VYamlFixtures;
using NUnit.Framework;
using VYaml.Annotations;
using VYaml.Serialization;

namespace CycloneGames.Persistence.Tests.VYaml
{
    [TestFixture]
    public sealed class VYamlPersistenceCodecTests
    {
        [Test]
        public void Codec_UsesStableVersionedIdentifier()
        {
            var codec = new VYamlPersistenceCodec<YamlClassContract>(
                GeneratedResolver.Instance);

            Assert.That(codec.CodecId.Value, Is.EqualTo("vyaml/1"));
        }

        [Test]
        public void Codec_RoundTripsGeneratedClassAndEmitsCanonicalUtf8()
        {
            var codec = new VYamlPersistenceCodec<YamlClassContract>(
                GeneratedResolver.Instance);
            var expected = new YamlClassContract
            {
                Revision = 7,
                DisplayName = "pilot"
            };
            using var writer = new BoundedTestBufferWriter(4096);
            PersistenceWriteContext writeContext = default;

            codec.Serialize(in expected, writer, in writeContext);

            ReadOnlySpan<byte> output = writer.WrittenSpan;
            Assert.That(output.Length, Is.GreaterThan(3));
            Assert.That(
                output[0] == 0xef && output[1] == 0xbb && output[2] == 0xbf,
                Is.False,
                "VYaml persistence payloads must not contain a UTF-8 BOM.");
            Assert.That(output.IndexOf((byte)'\r'), Is.EqualTo(-1));

            PersistenceReadContext readContext = default;
            YamlClassContract actual = codec.Deserialize(
                writer.WrittenMemory,
                in readContext);
            Assert.That(actual.Revision, Is.EqualTo(expected.Revision));
            Assert.That(actual.DisplayName, Is.EqualTo(expected.DisplayName));
        }

        [Test]
        public void Codec_RoundTripsGeneratedStruct()
        {
            var codec = new VYamlPersistenceCodec<YamlStructContract>(
                GeneratedResolver.Instance);
            var expected = new YamlStructContract
            {
                Level = 13,
                Enabled = true
            };
            using var writer = new BoundedTestBufferWriter(4096);
            PersistenceWriteContext writeContext = default;
            codec.Serialize(in expected, writer, in writeContext);

            PersistenceReadContext readContext = default;
            YamlStructContract actual = codec.Deserialize(
                writer.WrittenMemory,
                in readContext);

            Assert.That(actual.Level, Is.EqualTo(expected.Level));
            Assert.That(actual.Enabled, Is.EqualTo(expected.Enabled));
        }

        [Test]
        public void Codec_ObservesDestinationBudgetWithoutAllocatingAnExactCopy()
        {
            var codec = new VYamlPersistenceCodec<YamlClassContract>(
                GeneratedResolver.Instance);
            var value = new YamlClassContract
            {
                Revision = 1,
                DisplayName = "payload-that-does-not-fit"
            };
            using var writer = new BoundedTestBufferWriter(8);
            PersistenceWriteContext context = default;

            Assert.Throws<PersistencePayloadBudgetExceededException>(() =>
                codec.Serialize(value, writer, context));
        }

        [Test]
        public void Codec_RejectsResolverWithoutGeneratedFormatter()
        {
            Assert.Throws<ArgumentException>(() =>
                new VYamlPersistenceCodec<YamlClassContract>(
                    MissingFormatterResolver.Instance));
        }

        [Test]
        public void Codec_DeserializeFailure_DoesNotPoisonLaterUse()
        {
            var codec = new VYamlPersistenceCodec<YamlClassContract>(
                GeneratedResolver.Instance);
            PersistenceReadContext readContext = default;
            Assert.Catch<Exception>(() =>
                codec.Deserialize(new byte[] { (byte)'[', (byte)'1' }, readContext));

            var expected = new YamlClassContract
            {
                Revision = 2,
                DisplayName = "recovered"
            };
            using var writer = new BoundedTestBufferWriter(4096);
            PersistenceWriteContext writeContext = default;
            codec.Serialize(in expected, writer, in writeContext);
            YamlClassContract actual = codec.Deserialize(writer.WrittenMemory, in readContext);

            Assert.That(actual.DisplayName, Is.EqualTo(expected.DisplayName));
        }

        private sealed class MissingFormatterResolver : IYamlFormatterResolver
        {
            internal static readonly MissingFormatterResolver Instance =
                new MissingFormatterResolver();

            public IYamlFormatter<TValue> GetFormatter<TValue>()
            {
                return null;
            }
        }

        private sealed class BoundedTestBufferWriter : IBufferWriter<byte>, IDisposable
        {
            private byte[] _buffer;
            private int _writtenCount;

            internal BoundedTestBufferWriter(int maximumByteCount)
            {
                MaximumByteCount = maximumByteCount;
                _buffer = new byte[maximumByteCount];
            }

            internal int MaximumByteCount { get; }

            internal ReadOnlyMemory<byte> WrittenMemory =>
                new ReadOnlyMemory<byte>(_buffer, 0, _writtenCount);

            internal ReadOnlySpan<byte> WrittenSpan =>
                new ReadOnlySpan<byte>(_buffer, 0, _writtenCount);

            public void Advance(int count)
            {
                ThrowIfDisposed();
                if (count < 0 || count > MaximumByteCount - _writtenCount)
                {
                    throw new PersistencePayloadBudgetExceededException(MaximumByteCount);
                }

                _writtenCount += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Memory<byte>(
                    _buffer,
                    _writtenCount,
                    MaximumByteCount - _writtenCount);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return new Span<byte>(
                    _buffer,
                    _writtenCount,
                    MaximumByteCount - _writtenCount);
            }

            public void Dispose()
            {
                if (_buffer == null)
                {
                    return;
                }

                Array.Clear(_buffer, 0, _buffer.Length);
                _buffer = null;
                _writtenCount = 0;
            }

            private void EnsureCapacity(int sizeHint)
            {
                ThrowIfDisposed();
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                int requiredCount = Math.Max(sizeHint, 1);
                if (requiredCount > MaximumByteCount - _writtenCount)
                {
                    throw new PersistencePayloadBudgetExceededException(MaximumByteCount);
                }
            }

            private void ThrowIfDisposed()
            {
                if (_buffer == null)
                {
                    throw new ObjectDisposedException(nameof(BoundedTestBufferWriter));
                }
            }
        }
    }

}

// VYaml's generated formatters use non-global VYaml.* references. Keeping generated
// fixtures outside CycloneGames.Persistence prevents the provider namespace from
// shadowing the dependency's root namespace.
namespace CycloneGames.Tests.Persistence.VYamlFixtures
{
    [YamlObject]
    public sealed partial class YamlClassContract
    {
        public int Revision { get; set; }

        public string DisplayName { get; set; }
    }

    [YamlObject]
    public partial struct YamlStructContract
    {
        public int Level;

        public bool Enabled;
    }
}
