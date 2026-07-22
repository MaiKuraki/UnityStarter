using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.Persistence.Tests
{
    public sealed class PersistenceStoreTests
    {
        [Test]
        public void DefaultOperationResult_IsNotSuccess()
        {
            PersistenceOperationResult result = default;

            Assert.That(result.Status, Is.EqualTo(PersistenceOperationStatus.Uninitialized));
            Assert.That(result.IsSuccess, Is.False);
        }

        [Test]
        public async Task SaveAndLoad_RoundTripValueAndVersion()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec("binary-int/1");
            var store = CreateStore(storage, codec);
            int value = 42;

            PersistenceOperationResult save = await store.SaveAsync(in value, 3);
            PersistenceLoadResult<int> load = await store.LoadAsync(3);

            Assert.That(save.IsSuccess, Is.True);
            Assert.That(load.IsSuccess, Is.True);
            Assert.That(load.Value, Is.EqualTo(42));
            Assert.That(load.ContentVersion, Is.EqualTo(3));
            Assert.That(codec.LastReadVersion, Is.EqualTo(3));
            Assert.That(storage.LastBorrowedWrite.All(item => item == 0), Is.True,
                "The store must clear its exact write array after the storage task completes.");
            Assert.That(storage.LastReturnedRead.All(item => item == 0), Is.True,
                "The store must clear the storage-owned read array after decoding completes.");
        }

        [Test]
        public async Task Missing_IsNotReportedAsFailure()
        {
            var store = CreateStore(new MemoryStorage(), new Int32Codec("binary-int/1"));

            PersistenceLoadResult<int> result = await store.LoadAsync(0);

            Assert.That(result.IsMissing, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.None));
        }

        [Test]
        public async Task CorruptedPayload_NeverInvokesCodec()
        {
            var storage = new MemoryStorage();
            var writerCodec = new Int32Codec("binary-int/1");
            var writer = CreateStore(storage, writerCodec);
            int value = 17;
            Assert.That((await writer.SaveAsync(in value, 1)).IsSuccess, Is.True);
            storage.MutateLastByte();

            var readerCodec = new Int32Codec("binary-int/1");
            PersistenceLoadResult<int> result = await CreateStore(storage, readerCodec).LoadAsync(1);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.IntegrityCheckFailed));
            Assert.That(readerCodec.DeserializeCount, Is.Zero);
        }

        [Test]
        public async Task CodecMismatch_IsRejectedBeforeDeserialize()
        {
            var storage = new MemoryStorage();
            int value = 9;
            Assert.That((await CreateStore(storage, new Int32Codec("binary-int/1"))
                .SaveAsync(in value, 1)).IsSuccess, Is.True);
            var readerCodec = new Int32Codec("other-int/1");

            PersistenceLoadResult<int> result = await CreateStore(storage, readerCodec).LoadAsync(1);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.CodecMismatch));
            Assert.That(readerCodec.DeserializeCount, Is.Zero);
        }

        [Test]
        public async Task FutureVersion_IsRejectedWithoutDeserialize()
        {
            var storage = new MemoryStorage();
            int value = 9;
            Assert.That((await CreateStore(storage, new Int32Codec("binary-int/1"))
                .SaveAsync(in value, 2)).IsSuccess, Is.True);
            var readerCodec = new Int32Codec("binary-int/1");

            PersistenceLoadResult<int> result = await CreateStore(storage, readerCodec).LoadAsync(1);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.FutureContentVersion));
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

            PersistenceOperationResult result = await store.SaveAsync(in value, 0);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.PayloadTooLarge));
            Assert.That(storage.WriteCount, Is.Zero);
        }

        [Test]
        public async Task CodecExceptions_AreMappedWithoutPoisoningStore()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec("binary-int/1") { ThrowOnSerialize = true };
            var store = CreateStore(storage, codec);
            int value = 1;

            PersistenceOperationResult failed = await store.SaveAsync(in value, 0);
            codec.ThrowOnSerialize = false;
            PersistenceOperationResult recovered = await store.SaveAsync(in value, 0);

            Assert.That(failed.ErrorCode, Is.EqualTo(PersistenceErrorCode.SerializationFailed));
            Assert.That(recovered.IsSuccess, Is.True);
        }

        [Test]
        public async Task DeserializeExceptions_AreMapped()
        {
            var storage = new MemoryStorage();
            int value = 1;
            Assert.That((await CreateStore(storage, new Int32Codec("binary-int/1"))
                .SaveAsync(in value, 0)).IsSuccess, Is.True);
            var codec = new Int32Codec("binary-int/1") { ThrowOnDeserialize = true };

            PersistenceLoadResult<int> result = await CreateStore(storage, codec).LoadAsync(0);

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.DeserializeFailed));
            Assert.That(storage.LastReturnedRead.All(item => item == 0), Is.True);
        }

        [Test]
        public async Task CodecCancellationWithoutCancelledCallerToken_IsCodecFailure()
        {
            var storage = new MemoryStorage();
            var saveCodec = new Int32Codec("binary-int/1")
            {
                SerializeException = new OperationCanceledException("codec")
            };
            int value = 1;

            PersistenceOperationResult save = await CreateStore(storage, saveCodec)
                .SaveAsync(in value, 0);

            Assert.That(save.ErrorCode, Is.EqualTo(PersistenceErrorCode.SerializationFailed));

            Assert.That((await CreateStore(storage, new Int32Codec("binary-int/1"))
                .SaveAsync(in value, 0)).IsSuccess, Is.True);
            var loadCodec = new Int32Codec("binary-int/1")
            {
                DeserializeException = new OperationCanceledException("codec")
            };
            PersistenceLoadResult<int> load = await CreateStore(storage, loadCodec).LoadAsync(0);

            Assert.That(load.ErrorCode, Is.EqualTo(PersistenceErrorCode.DeserializeFailed));
        }

        [Test]
        public void OverlappingOperation_ThrowsSynchronously()
        {
            var storage = new MemoryStorage { BlockReads = true };
            var store = CreateStore(storage, new Int32Codec("binary-int/1"));
            Task<PersistenceLoadResult<int>> pending = store.LoadAsync(0);

            Assert.Throws<InvalidOperationException>(() => store.DeleteAsync());

            storage.ReleaseRead();
            Assert.DoesNotThrowAsync(async () => await pending);
        }

        [Test]
        public async Task PendingWrite_BorrowsRecordAndRejectsOverlapUntilCompletion()
        {
            var storage = new MemoryStorage { BlockWrites = true };
            var store = CreateStore(storage, new Int32Codec("binary-int/1"));
            int value = 12;

            Task<PersistenceOperationResult> pending = store.SaveAsync(in value, 0);
            Assert.Throws<InvalidOperationException>(() => store.LoadAsync(0));
            Assert.That(storage.LastBorrowedWrite.Any(item => item != 0), Is.True);

            storage.ReleaseWrite();
            Assert.That((await pending).IsSuccess, Is.True);
            Assert.That(storage.LastBorrowedWrite.All(item => item == 0), Is.True);
        }

        [Test]
        public async Task FatalCodecException_ReleasesGateAndPropagates()
        {
            var storage = new MemoryStorage();
            var codec = new Int32Codec("binary-int/1")
            {
                SerializeException = new AccessViolationException("fatal")
            };
            var store = CreateStore(storage, codec);
            int value = 5;

            Assert.Throws<AccessViolationException>(() => store.SaveAsync(in value, 0));
            codec.SerializeException = null;

            Assert.That((await store.SaveAsync(in value, 0)).IsSuccess, Is.True);
        }

        [Test]
        public async Task PreCancelledOperations_ReturnCancelledWithoutStorageMutation()
        {
            var storage = new MemoryStorage();
            var store = CreateStore(storage, new Int32Codec("binary-int/1"));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                int value = 1;

                PersistenceOperationResult save = await store.SaveAsync(in value, 0, cancellation.Token);
                PersistenceLoadResult<int> load = await store.LoadAsync(0, cancellation.Token);
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
            var store = CreateStore(storage, new Int32Codec("binary-int/1"));
            using (var cancellation = new CancellationTokenSource())
            {
                Task<PersistenceLoadResult<int>> pending = store.LoadAsync(
                    0,
                    cancellation.Token);
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
            var store = CreateStore(storage, new Int32Codec("binary-int/1"));
            int value = 5;

            PersistenceOperationResult failed = await store.SaveAsync(in value, 0);
            storage.WriteException = null;
            PersistenceOperationResult recovered = await store.SaveAsync(in value, 0);

            Assert.That(failed.ErrorCode, Is.EqualTo(PersistenceErrorCode.WriteFailed));
            Assert.That(recovered.IsSuccess, Is.True);
        }

        [Test]
        public async Task Delete_DoesNotRequireEntryToExist()
        {
            var store = CreateStore(new MemoryStorage(), new Int32Codec("binary-int/1"));

            PersistenceOperationResult result = await store.DeleteAsync();

            Assert.That(result.IsSuccess, Is.True);
        }

        [Test]
        public void ParameterAndIdentifierErrors_Throw()
        {
            Assert.Throws<ArgumentException>(() => new PersistenceCodecId("Binary/1"));
            Assert.Throws<ArgumentException>(() => new PersistenceCodecId("binary"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PersistenceLimits(0));
            var store = CreateStore(new MemoryStorage(), new Int32Codec("binary-int/1"));
            int value = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveAsync(in value, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.LoadAsync(-1));
        }

        private static PersistenceStore<int> CreateStore(
            MemoryStorage storage,
            IPersistenceCodec<int> codec)
        {
            return new PersistenceStore<int>(storage, new PersistenceProfile<int>(codec));
        }
    }

    public sealed class PersistenceRecordV1Tests
    {
        [Test]
        public void Encode_MatchesCommittedGoldenBytes()
        {
            byte[] encoded = PersistenceRecordV1.Encode(
                Encoding.UTF8.GetBytes("value: 42\n"),
                3,
                new PersistenceCodecId("test-yaml/1"),
                PersistenceLimits.Default);

            byte[] fixture = ReadGoldenFixture();

            Assert.That(encoded, Is.EqualTo(fixture));
            Assert.That(
                Encoding.UTF8.GetString(fixture),
                Is.EqualTo(
                    "# cgp-record: 1\n" +
                    "# content-version: 3\n" +
                    "# codec-id: test-yaml/1\n" +
                    "# transform-id: identity/1\n" +
                    "# payload-bytes: 10\n" +
                    "# xxh64: 8C3CEB0DE230D196\n" +
                    "---\n" +
                    "value: 42\n"));
        }

        [Test]
        public void Encode_ProducesCanonicalHeaderAndParseRoundTrips()
        {
            byte[] payload = Encoding.UTF8.GetBytes("value: 42\n");
            byte[] record = PersistenceRecordV1.Encode(
                payload,
                3,
                new PersistenceCodecId("test-yaml/1"),
                PersistenceLimits.Default);

            string text = Encoding.UTF8.GetString(record);
            StringAssert.StartsWith(
                "# cgp-record: 1\n" +
                "# content-version: 3\n" +
                "# codec-id: test-yaml/1\n" +
                "# transform-id: identity/1\n" +
                "# payload-bytes: 10\n" +
                "# xxh64: ",
                text);
            StringAssert.EndsWith("\n---\nvalue: 42\n", text);
            Assert.That(record.Contains((byte)'\r'), Is.False);

            PersistenceRecordParseResult parsed = PersistenceRecordV1.Parse(
                record,
                new PersistenceCodecId("test-yaml/1"),
                3,
                PersistenceLimits.Default);

            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(parsed.ContentVersion, Is.EqualTo(3));
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
        public void UnknownRecordVersion_IsUnsupported()
        {
            byte[] record = ReplaceAscii(ValidRecord(), "# cgp-record: 1", "# cgp-record: 2");

            AssertError(record, PersistenceErrorCode.UnsupportedRecordVersion);
        }

        [TestCase("# content-version: 3\n")]
        [TestCase("# codec-id: test-yaml/1\n")]
        [TestCase("# transform-id: identity/1\n")]
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
        public void CrLfHeader_IsRejected()
        {
            byte[] record = Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(ValidRecord()).Replace("\n", "\r\n"));

            AssertError(record, PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void LeadingZeroAndWhitespace_AreRejected()
        {
            AssertError(
                ReplaceAscii(ValidRecord(), "# content-version: 3", "# content-version: 03"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(ValidRecord(), "# content-version: 3", "# content-version: 3 "),
                PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void NegativeOverflowAndIllegalIdentifiers_AreRejected()
        {
            AssertError(
                ReplaceAscii(ValidRecord(), "# content-version: 3", "# content-version: -1"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(
                    ValidRecord(),
                    "# content-version: 3",
                    "# content-version: 2147483648"),
                PersistenceErrorCode.MalformedRecord);
            AssertError(
                ReplaceAscii(ValidRecord(), "test-yaml/1", "Test-yaml/1"),
                PersistenceErrorCode.MalformedRecord);
        }

        [Test]
        public void HeaderWithoutLineFeed_IsRejectedWithinFixedBudget()
        {
            byte[] record = new byte[PersistenceLimits.HardMaximumPayloadBytes];
            byte[] prefix = Encoding.ASCII.GetBytes("# cgp-record: ");
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
            PersistenceRecordParseResult result = PersistenceRecordV1.Parse(
                record,
                new PersistenceCodecId("test-yaml/1"),
                10,
                new PersistenceLimits(9, 9));

            Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.PayloadTooLarge));
        }

        [Test]
        public void EmptyPayload_IsCanonicalAndRoundTrips()
        {
            byte[] record = PersistenceRecordV1.Encode(
                ReadOnlySpan<byte>.Empty,
                0,
                new PersistenceCodecId("empty/1"),
                PersistenceLimits.Default);

            PersistenceRecordParseResult result = PersistenceRecordV1.Parse(
                record,
                new PersistenceCodecId("empty/1"),
                0,
                PersistenceLimits.Default);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.PayloadLength, Is.Zero);
        }

        [Test]
        public void ValidMetadataMutation_IsIntegrityFailure()
        {
            byte[] record = ReplaceAscii(
                ValidRecord(),
                "# content-version: 3",
                "# content-version: 4");

            AssertError(record, PersistenceErrorCode.IntegrityCheckFailed);
        }

        [Test]
        public void LowercaseChecksum_IsMalformed()
        {
            string text = Encoding.UTF8.GetString(ValidRecord());
            const string prefix = "# xxh64: ";
            int checksum = text.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
            char[] characters = text.ToCharArray();
            for (int index = checksum; index < checksum + 16; index++)
            {
                if (characters[index] >= 'A' && characters[index] <= 'F')
                {
                    characters[index] = char.ToLowerInvariant(characters[index]);
                    AssertError(
                        Encoding.UTF8.GetBytes(new string(characters)),
                        PersistenceErrorCode.MalformedRecord);
                    return;
                }
            }

            Assert.Fail("Fixture checksum unexpectedly contained no alphabetic hex digit.");
        }

        private static byte[] ValidRecord()
        {
            return PersistenceRecordV1.Encode(
                Encoding.UTF8.GetBytes("value: 42\n"),
                3,
                new PersistenceCodecId("test-yaml/1"),
                PersistenceLimits.Default);
        }

        private static byte[] ReadGoldenFixture([CallerFilePath] string sourcePath = null)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            return File.ReadAllBytes(Path.Combine(
                directory,
                "Fixtures",
                "PersistenceRecordV1.yaml.bytes"));
        }

        private static void AssertError(byte[] record, PersistenceErrorCode expected)
        {
            PersistenceRecordParseResult result = PersistenceRecordV1.Parse(
                record,
                new PersistenceCodecId("test-yaml/1"),
                10,
                PersistenceLimits.Default);
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
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(1));
                Assert.That(writer.WrittenCount, Is.Zero);
            }
        }

        [Test]
        public void AdvanceBeyondGrantedSpan_Throws()
        {
            using (var writer = new BoundedByteBufferWriter(8, 32))
            {
                int grantedLength = writer.GetSpan(1).Length;
                Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(grantedLength + 1));
                Assert.That(writer.WrittenCount, Is.Zero);
            }
        }
    }

    internal sealed class Int32Codec : IPersistenceCodec<int>
    {
        internal Int32Codec(string id)
        {
            CodecId = new PersistenceCodecId(id);
        }

        public PersistenceCodecId CodecId { get; }

        internal bool ThrowOnSerialize { get; set; }

        internal bool ThrowOnDeserialize { get; set; }

        internal Exception SerializeException { get; set; }

        internal Exception DeserializeException { get; set; }

        internal int DeserializeCount { get; private set; }

        internal int LastReadVersion { get; private set; } = -1;

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
            LastReadVersion = context.ContentVersion;
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
        public PersistenceCodecId CodecId { get; } = new PersistenceCodecId("oversized/1");

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

        public string Location => "memory://settings";

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
