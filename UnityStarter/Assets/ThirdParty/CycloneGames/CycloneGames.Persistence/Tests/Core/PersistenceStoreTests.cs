using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.Persistence.Tests
{
    public sealed class PersistenceStoreTests
    {
        private static readonly PersistenceCodecId FormatToken = new PersistenceCodecId("binary-int");

        [Test]
        public void DefaultOperationResult_IsNotSuccess()
        {
            PersistenceOperationResult result = default;

            Assert.That(result.Status, Is.EqualTo(PersistenceOperationStatus.Uninitialized));
            Assert.That(result.IsSuccess, Is.False);
        }

        [Test]
        public async Task SaveAndLoad_RoundTripValueAndClearsBuffers()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec(FormatToken);
            var store = CreateStore(storage, codec);
            int value = 42;

            PersistenceOperationResult save = await store.SaveAsync(in value);
            PersistenceLoadResult<int> load = await store.LoadAsync();

            Assert.That(save.IsSuccess, Is.True);
            Assert.That(load.IsSuccess, Is.True);
            Assert.That(load.Value, Is.EqualTo(42));
            Assert.That(AllZero(storage.LastBorrowedWrite), Is.True,
                "The store must clear its exact write array after the storage task completes.");
            Assert.That(AllZero(storage.LastReturnedRead), Is.True,
                "The store must clear the storage-owned read array after decoding completes.");
        }

        [Test]
        public async Task Missing_IsNotReportedAsFailure()
        {
            var store = CreateStore(new MemoryStorage(), new Int32Codec(FormatToken));

            PersistenceLoadResult<int> result = await store.LoadAsync();

            Assert.That(result.IsMissing, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.None));
        }

        [Test]
        public async Task CorruptedPayload_NeverInvokesCodec()
        {
            var storage = new MemoryStorage();
            int value = 17;
            Assert.That((await CreateStore(storage, new Int32Codec(FormatToken))
                .SaveAsync(in value)).IsSuccess, Is.True);
            storage.MutateLastByte();

            var readerCodec = new Int32Codec(FormatToken);
            PersistenceLoadResult<int> result = await CreateStore(storage, readerCodec).LoadAsync();

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.IntegrityCheckFailed));
            Assert.That(readerCodec.DeserializeCount, Is.Zero);
        }

        [Test]
        public async Task UnknownFormatToken_IsRejectedBeforeDeserialize()
        {
            var storage = new MemoryStorage();
            int value = 9;
            Assert.That((await CreateStore(storage, new Int32Codec(FormatToken))
                .SaveAsync(in value)).IsSuccess, Is.True);
            var readerCodec = new Int32Codec(new PersistenceCodecId("other-int"));

            PersistenceLoadResult<int> result = await CreateStore(storage, readerCodec).LoadAsync();

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.RecordFormatMismatch));
            Assert.That(readerCodec.DeserializeCount, Is.Zero);
        }

        [Test]
        public async Task SerializationBudget_IsEnforcedByDestination()
        {
            var storage = new MemoryStorage();
            var codec = new OversizedCodec();
            var profile = new PersistenceProfile<int>(codec, new PersistenceLimits(32, 8));
            var store = new PersistenceStore<int>(storage, profile);
            int value = 1;

            PersistenceOperationResult result = await store.SaveAsync(in value);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.PayloadTooLarge));
            Assert.That(storage.WriteCount, Is.Zero);
        }

        [Test]
        public async Task CodecExceptions_AreMappedWithoutPoisoningStore()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec(FormatToken) { ThrowOnSerialize = true };
            var store = CreateStore(storage, codec);
            int value = 1;

            PersistenceOperationResult failed = await store.SaveAsync(in value);
            codec.ThrowOnSerialize = false;
            PersistenceOperationResult recovered = await store.SaveAsync(in value);

            Assert.That(failed.ErrorCode, Is.EqualTo(PersistenceErrorCode.SerializationFailed));
            Assert.That(recovered.IsSuccess, Is.True);
        }

        [Test]
        public async Task DeserializeExceptions_AreMapped()
        {
            var storage = new MemoryStorage();
            int value = 1;
            Assert.That((await CreateStore(storage, new Int32Codec(FormatToken))
                .SaveAsync(in value)).IsSuccess, Is.True);
            var codec = new Int32Codec(FormatToken) { ThrowOnDeserialize = true };

            PersistenceLoadResult<int> result = await CreateStore(storage, codec).LoadAsync();

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.DeserializeFailed));
            Assert.That(AllZero(storage.LastReturnedRead), Is.True);
        }

        [Test]
        public async Task CodecCancellationWithoutCancelledCallerToken_IsCodecFailure()
        {
            var storage = new MemoryStorage();
            var saveCodec = new Int32Codec(FormatToken)
            {
                SerializeException = new OperationCanceledException("codec")
            };
            int value = 1;

            PersistenceOperationResult save = await CreateStore(storage, saveCodec)
                .SaveAsync(in value);

            Assert.That(save.ErrorCode, Is.EqualTo(PersistenceErrorCode.SerializationFailed));

            Assert.That((await CreateStore(storage, new Int32Codec(FormatToken))
                .SaveAsync(in value)).IsSuccess, Is.True);
            var loadCodec = new Int32Codec(FormatToken)
            {
                DeserializeException = new OperationCanceledException("codec")
            };
            PersistenceLoadResult<int> load = await CreateStore(storage, loadCodec).LoadAsync();

            Assert.That(load.ErrorCode, Is.EqualTo(PersistenceErrorCode.DeserializeFailed));
        }

        [Test]
        public void OverlappingOperation_ThrowsSynchronously()
        {
            var storage = new MemoryStorage { BlockReads = true };
            var store = CreateStore(storage, new Int32Codec(FormatToken));
            Task<PersistenceLoadResult<int>> pending = store.LoadAsync();

            Assert.Throws<InvalidOperationException>(() => store.DeleteAsync());

            storage.ReleaseRead();
            Assert.DoesNotThrowAsync(async () => await pending);
        }

        [Test]
        public async Task PendingWrite_BorrowsRecordAndRejectsOverlapUntilCompletion()
        {
            var storage = new MemoryStorage { BlockWrites = true };
            var store = CreateStore(storage, new Int32Codec(FormatToken));
            int value = 12;

            Task<PersistenceOperationResult> pending = store.SaveAsync(in value);
            Assert.Throws<InvalidOperationException>(() => store.LoadAsync());
            Assert.That(HasNonZero(storage.LastBorrowedWrite), Is.True);

            storage.ReleaseWrite();
            Assert.That((await pending).IsSuccess, Is.True);
            Assert.That(AllZero(storage.LastBorrowedWrite), Is.True);
        }

        [Test]
        public async Task FatalCodecException_ReleasesGateAndPropagates()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec(FormatToken)
            {
                SerializeException = new AccessViolationException("fatal")
            };
            var store = CreateStore(storage, codec);
            int value = 5;

            Assert.Throws<AccessViolationException>(() => store.SaveAsync(in value));
            codec.SerializeException = null;

            Assert.That((await store.SaveAsync(in value)).IsSuccess, Is.True);
        }

        [Test]
        public async Task PreCancelledOperations_ReturnCancelledWithoutStorageMutation()
        {
            var storage = new MemoryStorage();
            var store = CreateStore(storage, new Int32Codec(FormatToken));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                int value = 1;

                PersistenceOperationResult save = await store.SaveAsync(in value, cancellation.Token);
                PersistenceLoadResult<int> load = await store.LoadAsync(cancellation.Token);
                PersistenceOperationResult delete = await store.DeleteAsync(cancellation.Token);

                Assert.That(save.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(load.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(delete.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(storage.ReadCount, Is.Zero);
                Assert.That(storage.WriteCount, Is.Zero);
                Assert.That(storage.DeleteCount, Is.Zero);
            }
        }

        [Test]
        public async Task CancellationAfterNonCompliantDelayedMissingRead_ReturnsCancelled()
        {
            var storage = new MemoryStorage
            {
                BlockReads = true,
                IgnoreReadCancellation = true
            };
            var store = CreateStore(storage, new Int32Codec(FormatToken));
            using (var cancellation = new CancellationTokenSource())
            {
                Task<PersistenceLoadResult<int>> pending = store.LoadAsync(cancellation.Token);
                cancellation.Cancel();
                storage.ReleaseRead();

                PersistenceLoadResult<int> result = await pending;

                Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(result.IsMissing, Is.False);
            }
        }

        [Test]
        public async Task StorageFailures_AreClassifiedAndStoreRecovers()
        {
            var storage = new MemoryStorage { WriteException = new InvalidOperationException("write") };
            var store = CreateStore(storage, new Int32Codec(FormatToken));
            int value = 5;

            PersistenceOperationResult failed = await store.SaveAsync(in value);
            storage.WriteException = null;
            PersistenceOperationResult recovered = await store.SaveAsync(in value);

            Assert.That(failed.ErrorCode, Is.EqualTo(PersistenceErrorCode.WriteFailed));
            Assert.That(recovered.IsSuccess, Is.True);
        }

        [Test]
        public async Task Delete_DoesNotRequireEntryToExist()
        {
            var store = CreateStore(new MemoryStorage(), new Int32Codec(FormatToken));

            PersistenceOperationResult result = await store.DeleteAsync();

            Assert.That(result.IsSuccess, Is.True);
        }

        [Test]
        public void ParameterAndIdentifierErrors_Throw()
        {
            Assert.Throws<ArgumentException>(() => new PersistenceCodecId("Binary"));
            Assert.Throws<ArgumentException>(() => new PersistenceCodecId("binary/1"));
            Assert.Throws<ArgumentException>(() => new PersistenceCodecId("binary-"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PersistenceLimits(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PersistenceLimits(1, 2));
        }

        private static bool AllZero(byte[] buffer)
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                if (buffer[index] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasNonZero(byte[] buffer)
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                if (buffer[index] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static PersistenceStore<int> CreateStore(
            MemoryStorage storage,
            IPersistenceCodec<int> codec)
        {
            return new PersistenceStore<int>(storage, new PersistenceProfile<int>(codec));
        }
    }

    public sealed class PersistenceRecordTests
    {
        private static readonly PersistenceCodecId FormatToken = new PersistenceCodecId("test-yaml");

        [Test]
        public void Encode_MatchesCommittedGoldenBytes()
        {
            byte[] encoded = PersistenceRecord.Encode(
                Encoding.UTF8.GetBytes("value: 42\n"),
                FormatToken,
                new PersistenceLimits());

            byte[] fixture = ReadGoldenFixture();

            Assert.That(encoded, Is.EqualTo(fixture));
        }

        [Test]
        public void Encode_ProducesCanonicalHeaderAndParseRoundTrips()
        {
            byte[] payload = Encoding.UTF8.GetBytes("value: 42\n");
            byte[] record = PersistenceRecord.Encode(payload, FormatToken, new PersistenceLimits());

            string text = Encoding.UTF8.GetString(record);
            StringAssert.StartsWith(
                "# cgp-record\n" +
                "# codec-id: test-yaml\n" +
                "# payload-bytes: 10\n" +
                "# xxh64: ",
                text);
            StringAssert.EndsWith("\n---\nvalue: 42\n", text);
            Assert.That(HasByte(record, (byte)'\r'), Is.False);

            PersistenceRecordParseResult parsed = PersistenceRecord.Parse(
                record,
                FormatToken,
                new PersistenceLimits());

            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(new ReadOnlySpan<byte>(record, parsed.PayloadOffset, parsed.PayloadLength)
                .SequenceEqual(payload), Is.True);
        }

        [Test]
        public void WrongMagic_IsFormatMismatch()
        {
            byte[] record = ValidRecord();
            record[2] = (byte)'x';

            AssertError(record, PersistenceErrorCode.RecordFormatMismatch);
        }

        [Test]
        public void UnknownFormatToken_IsFormatMismatch()
        {
            byte[] record = ValidRecord();
            PersistenceRecordParseResult result = PersistenceRecord.Parse(
                record,
                new PersistenceCodecId("other-yaml"),
                new PersistenceLimits());

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.RecordFormatMismatch));
        }

        [TestCase("# codec-id: test-yaml\n")]
        [TestCase("# payload-bytes: 10\n")]
        [TestCase("# xxh64: ")]
        public void MissingOrReorderedFields_AreMalformed(string field)
        {
            byte[] source = ValidRecord();
            string text = Encoding.UTF8.GetString(source);
            int start = text.IndexOf(field, StringComparison.Ordinal);
            int end = text.IndexOf('\n', start) + 1;
            byte[] record = Encoding.UTF8.GetBytes(text.Remove(start, end - start));

            AssertError(record, PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void CrLfMagicLine_IsFormatMismatch()
        {
            byte[] record = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(ValidRecord()).Replace("\n", "\r\n"));

            AssertError(record, PersistenceErrorCode.RecordFormatMismatch);
        }

        [Test]
        public void CrLfHeaderLine_IsMalformed()
        {
            string text = Encoding.UTF8.GetString(ValidRecord());
            byte[] record = Encoding.UTF8.GetBytes(
                text.Replace("# codec-id: test-yaml\n", "# codec-id: test-yaml\r\n"));

            AssertError(record, PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void LeadingZeroAndWhitespace_AreRejected()
        {
            AssertError(
                ReplaceAscii(ValidRecord(), "# payload-bytes: 10", "# payload-bytes: 010"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(ValidRecord(), "# payload-bytes: 10", "# payload-bytes: 10 "),
                PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void NegativeOverflowAndIllegalIdentifiers_AreRejected()
        {
            AssertError(
                ReplaceAscii(ValidRecord(), "# payload-bytes: 10", "# payload-bytes: -1"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(ValidRecord(), "# payload-bytes: 10", "# payload-bytes: 2147483648"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(ValidRecord(), "test-yaml", "Test-yaml"),
                PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void HeaderWithoutLineFeed_IsRejectedWithinFixedBudget()
        {
            byte[] record = new byte[PersistenceLimits.HardMaximumPayloadBytes];
            byte[] prefix = Encoding.ASCII.GetBytes("# cgp-record\n");
            Buffer.BlockCopy(prefix, 0, record, 0, prefix.Length);
            for (int index = prefix.Length; index < record.Length; index++)
            {
                record[index] = (byte)'1';
            }

            AssertError(record, PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void PayloadLengthMismatchAndTrailingBytes_AreRejected()
        {
            AssertError(
                ReplaceAscii(ValidRecord(), "# payload-bytes: 10", "# payload-bytes: 9"),
                PersistenceErrorCode.MalformedRecord);
            byte[] valid = ValidRecord();
            byte[] trailing = new byte[valid.Length + 1];
            Buffer.BlockCopy(valid, 0, trailing, 0, valid.Length);
            trailing[trailing.Length - 1] = 1;
            AssertError(trailing, PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void PayloadAboveConfiguredLimit_IsRejectedBeforeChecksumDispatch()
        {
            byte[] record = ValidRecord();
            PersistenceRecordParseResult result = PersistenceRecord.Parse(
                record,
                FormatToken,
                new PersistenceLimits(9, 9));

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.PayloadTooLarge));
        }

        [Test]
        public void EmptyPayload_IsCanonicalAndRoundTrips()
        {
            byte[] record = PersistenceRecord.Encode(
                ReadOnlySpan<byte>.Empty,
                new PersistenceCodecId("empty"),
                new PersistenceLimits());

            PersistenceRecordParseResult result = PersistenceRecord.Parse(
                record,
                new PersistenceCodecId("empty"),
                new PersistenceLimits());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.PayloadLength, Is.Zero);
        }

        [Test]
        public void ValidMetadataMutation_IsIntegrityFailure()
        {
            byte[] record = ReplaceAscii(
                ValidRecord(),
                "# codec-id: test-yaml",
                "# codec-id: test-yamz");

            AssertError(record, PersistenceErrorCode.IntegrityCheckFailed);
        }

        [Test]
        public void LowercaseChecksum_IsMalformed()
        {
            string text = Encoding.UTF8.GetString(ValidRecord());
            const string prefix = "# xxh64: ";
            int checksum = text.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
            char[] characters = text.ToCharArray();
            characters[checksum] = 'a';

            AssertError(
                Encoding.UTF8.GetBytes(new string(characters)),
                PersistenceErrorCode.MalformedRecord);
        }

        private static bool HasByte(byte[] buffer, byte value)
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                if (buffer[index] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static byte[] ValidRecord()
        {
            return PersistenceRecord.Encode(
                Encoding.UTF8.GetBytes("value: 42\n"),
                FormatToken,
                new PersistenceLimits());
        }

        private static byte[] ReadGoldenFixture([CallerFilePath] string sourcePath = null)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            return File.ReadAllBytes(Path.Combine(
                directory,
                "Fixtures",
                "Record.yaml.bytes"));
        }

        private static void AssertError(byte[] record, PersistenceErrorCode expected)
        {
            PersistenceRecordParseResult result = PersistenceRecord.Parse(
                record,
                FormatToken,
                new PersistenceLimits());
            Assert.That(result.ErrorCode, Is.EqualTo(expected));
        }

        private static byte[] ReplaceAscii(byte[] source, string oldValue, string newValue)
        {
            string text = Encoding.UTF8.GetString(source);
            int index = text.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            return Encoding.UTF8.GetBytes(text.Remove(index, oldValue.Length).Insert(index, newValue));
        }
    }

    public sealed class BoundedByteBufferWriterTests
    {
        [Test]
        public void AdvanceWithoutGrant_ThrowsInsteadOfPersistingPooledBytes()
        {
            using (var writer = new BoundedByteBufferWriter(8, 32))
            {
                writer.Reset();
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(1));
                Assert.That(writer.WrittenCount, Is.Zero);
            }
        }

        [Test]
        public void AdvanceBeyondGrantedSpan_Throws()
        {
            using (var writer = new BoundedByteBufferWriter(8, 32))
            {
                writer.Reset();
                int grantedLength = writer.GetSpan(1).Length;
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(grantedLength + 1));
                Assert.That(writer.WrittenCount, Is.Zero);
            }
        }
    }

    public sealed class PersistenceRecordHeaderTests
    {
        private static readonly PersistenceCodecId Token = new PersistenceCodecId("test-yaml");

        [Test]
        public void TryRead_ReturnsTokenAndPayload()
        {
            byte[] payload = Encoding.UTF8.GetBytes("value: 42\n");
            byte[] record = PersistenceRecord.Encode(payload, Token, new PersistenceLimits());

            Assert.That(
                PersistenceRecordHeader.TryRead(
                    record,
                    out ReadOnlySpan<byte> token,
                    out ReadOnlySpan<byte> body),
                Is.True);
            Assert.That(Encoding.UTF8.GetString(token.ToArray()), Is.EqualTo("test-yaml"));
            Assert.That(body.SequenceEqual(payload), Is.True);
        }

        [Test]
        public void TryRead_DoesNotVerifyChecksum()
        {
            byte[] record = PersistenceRecord.Encode(
                Encoding.UTF8.GetBytes("value: 42\n"),
                Token,
                new PersistenceLimits());
            record[record.Length - 1] ^= 0x01;

            Assert.That(
                PersistenceRecordHeader.TryReadFormatToken(record, out ReadOnlySpan<byte> token),
                Is.True);
            Assert.That(Encoding.UTF8.GetString(token.ToArray()), Is.EqualTo("test-yaml"));
            Assert.That(
                PersistenceRecord.Parse(record, Token, new PersistenceLimits()).ErrorCode,
                Is.EqualTo(PersistenceErrorCode.IntegrityCheckFailed));
        }

        [Test]
        public void TryRead_RejectsMalformedRecords()
        {
            Assert.That(PersistenceRecordHeader.TryReadFormatToken(new byte[0], out _), Is.False);

            byte[] valid = PersistenceRecord.Encode(
                Encoding.UTF8.GetBytes("x"),
                Token,
                new PersistenceLimits());
            byte[] crlf = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(valid).Replace("\n", "\r\n"));
            Assert.That(PersistenceRecordHeader.TryReadFormatToken(crlf, out _), Is.False);
        }

        [Test]
        public void PeekAndParse_ShareOneGrammar()
        {
            for (int size = 0; size < 64; size++)
            {
                byte[] payload = new byte[size];
                for (int index = 0; index < size; index++)
                {
                    payload[index] = (byte)(index * 7 + 1);
                }

                byte[] record = PersistenceRecord.Encode(payload, Token, new PersistenceLimits());
                bool peek = PersistenceRecordHeader.TryRead(
                    record,
                    out _,
                    out ReadOnlySpan<byte> body);
                PersistenceRecordParseResult parsed = PersistenceRecord.Parse(
                    record,
                    Token,
                    new PersistenceLimits());

                Assert.That(peek, Is.EqualTo(parsed.IsSuccess));
                if (peek)
                {
                    Assert.That(body.Length, Is.EqualTo(parsed.PayloadLength));
                }
            }
        }
    }

    public sealed class PersistenceFormatRegistryTests
    {
        [Test]
        public void TryGet_MatchesBySpanAndByTypedFormat()
        {
            var first = new Entry("first");
            var second = new Entry("second");
            var registry = new PersistenceFormatRegistry<Entry>(
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("game-settings-a"), first),
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("game-settings-b"), second));

            byte[] record = PersistenceRecord.Encode(
                new byte[1],
                new PersistenceCodecId("game-settings-a"),
                new PersistenceLimits());
            Assert.That(
                PersistenceRecordHeader.TryReadFormatToken(record, out ReadOnlySpan<byte> token),
                Is.True);
            Assert.That(registry.TryGet(token, out Entry bySpan), Is.True);
            Assert.That(bySpan, Is.SameAs(first));
            Assert.That(
                registry.TryGet(new PersistenceCodecId("game-settings-b"), out Entry byFormat),
                Is.True);
            Assert.That(byFormat, Is.SameAs(second));
            Assert.That(registry.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryGet_UnknownTokenReturnsFalse()
        {
            var registry = new PersistenceFormatRegistry<Entry>(
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("game-settings-a"), new Entry("a")));

            Assert.That(registry.TryGet(new PersistenceCodecId("other"), out Entry entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void Constructor_RejectsDuplicateTokens()
        {
            Assert.Throws<ArgumentException>(() => new PersistenceFormatRegistry<Entry>(
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("same"), new Entry("a")),
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("same"), new Entry("b"))));
        }

        [Test]
        public void Constructor_SnapshotsRegistrations()
        {
            var source = new[]
            {
                new PersistenceFormatRegistration<Entry>(
                    new PersistenceCodecId("game-settings-a"), new Entry("a"))
            };
            var registry = new PersistenceFormatRegistry<Entry>(source);
            source[0] = new PersistenceFormatRegistration<Entry>(
                new PersistenceCodecId("other"), new Entry("b"));

            Assert.That(
                registry.TryGet(new PersistenceCodecId("game-settings-a"), out _),
                Is.True);
        }

        private sealed class Entry
        {
            internal Entry(string name)
            {
                Name = name;
            }

            internal string Name { get; }
        }
    }

    internal sealed class Int32Codec : IPersistenceCodec<int>
    {
        internal Int32Codec(PersistenceCodecId id)
        {
            CodecId = id;
        }

        public PersistenceCodecId CodecId { get; }

        internal bool ThrowOnSerialize { get; set; }

        internal bool ThrowOnDeserialize { get; set; }

        internal Exception SerializeException { get; set; }

        internal Exception DeserializeException { get; set; }

        internal int DeserializeCount { get; private set; }

        public void Serialize(
            in int value,
            IBufferWriter<byte> destination,
            in PersistenceWriteContext context)
        {
            if (ThrowOnSerialize)
            {
                throw new InvalidOperationException("serialize");
            }

            if (SerializeException != null)
            {
                throw SerializeException;
            }

            Span<byte> bytes = destination.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            destination.Advance(sizeof(int));
        }

        public int Deserialize(
            ReadOnlyMemory<byte> payload,
            in PersistenceReadContext context)
        {
            DeserializeCount++;
            if (ThrowOnDeserialize)
            {
                throw new InvalidOperationException("deserialize");
            }

            if (DeserializeException != null)
            {
                throw DeserializeException;
            }

            if (payload.Length != sizeof(int))
            {
                throw new FormatException("Expected one Int32.");
            }

            return BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
        }
    }

    internal sealed class OversizedCodec : IPersistenceCodec<int>
    {
        public PersistenceCodecId CodecId { get; } = new PersistenceCodecId("oversized");

        public void Serialize(
            in int value,
            IBufferWriter<byte> destination,
            in PersistenceWriteContext context)
        {
            destination.GetSpan(context.Limits.MaximumPayloadBytes + 1);
        }

        public int Deserialize(ReadOnlyMemory<byte> payload, in PersistenceReadContext context)
        {
            return 0;
        }
    }

    internal sealed class MemoryStorage : IPersistenceStorage
    {
        private readonly TaskCompletionSource<bool> _readGate =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _writeGate =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[] _content;

        public string Location => "memory://record";

        internal bool BlockReads { get; set; }

        internal bool IgnoreReadCancellation { get; set; }

        internal bool BlockWrites { get; set; }

        internal Exception WriteException { get; set; }

        internal int ReadCount { get; private set; }

        internal int WriteCount { get; private set; }

        internal int DeleteCount { get; private set; }

        internal byte[] LastBorrowedWrite { get; private set; }

        internal byte[] LastReturnedRead { get; private set; }

        public async Task<PersistenceStorageReadResult> ReadAsync(
            int maxByteCount,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (BlockReads)
            {
                await _readGate.Task.ConfigureAwait(false);
            }

            if (!IgnoreReadCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_content == null)
            {
                return PersistenceStorageReadResult.Missing();
            }

            if (_content.Length > maxByteCount)
            {
                throw new InvalidOperationException("bounded read");
            }

            LastReturnedRead = (byte[])_content.Clone();
            return PersistenceStorageReadResult.Found(LastReturnedRead);
        }

        public async Task WriteAtomicallyAsync(
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (WriteException != null)
            {
                throw WriteException;
            }

            LastBorrowedWrite = content;
            if (BlockWrites)
            {
                await _writeGate.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            _content = (byte[])content.Clone();
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            cancellationToken.ThrowIfCancellationRequested();
            _content = null;
            return Task.CompletedTask;
        }

        internal void MutateLastByte()
        {
            _content[_content.Length - 1] ^= 0x01;
        }

        internal void ReleaseRead()
        {
            _readGate.TrySetResult(true);
        }

        internal void ReleaseWrite()
        {
            _writeGate.TrySetResult(true);
        }
    }
}
