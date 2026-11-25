using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;
using NUnit.Framework;

namespace CycloneGames.Settings.Persistence.Tests
{
    public sealed class PersistentSettingsTests
    {
        [Test]
        public void LoadResultDefault_IsExplicitlyUninitializedAndNullSafe()
        {
            PersistentSettingsLoadResult result = default;

            Assert.That(result.Status, Is.EqualTo(PersistentSettingsLoadStatus.Uninitialized));
            Assert.That(result.Completed, Is.False);
            Assert.That(result.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            var state = new SettingsState<TestSettings>(new TestSchema());
            Assert.Throws<ArgumentNullException>(() =>
                new PersistentSettings<TestSettings>(null, CreateStore(new MemoryStorage())));
            Assert.Throws<ArgumentNullException>(() =>
                new PersistentSettings<TestSettings>(state, null));
        }

        [Test]
        public async Task LoadAsync_MissingPreservesDefaults()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.Completed, Is.True);
            Assert.That(result.IsMissing, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(10));
        }

        [Test]
        public async Task LoadAsync_CommitsLoadedValue()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(35));
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.IsLoaded, Is.True);
            Assert.That(result.HasObserverWarnings, Is.False);
            Assert.That(state.Snapshot().Value, Is.EqualTo(35));
        }

        [Test]
        public async Task LoadAsync_ValidatesLoadedCandidateBeforeCommit()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(-5));
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Error, Is.EqualTo(PersistentSettingsLoadError.StateCommitFailed));
            Assert.That(result.StateError, Is.EqualTo(SettingsUpdateError.ValidationFailed));
            Assert.That(state.Snapshot().Value, Is.EqualTo(10));
        }

        [Test]
        public async Task LoadAsync_CorruptedRecordDoesNotReplaceState()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(35));
            storage.MutateLastByte();
            var state = new SettingsState<TestSettings>(new TestSchema());
            state.Update((ref TestSettings candidate) => candidate.Value = 20);
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.Completed, Is.False);
            Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.IntegrityCheckFailed));
            Assert.That(state.Snapshot().Value, Is.EqualTo(20));
        }

        [Test]
        public async Task LoadAsync_UnknownFormatDoesNotReplaceState()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(35));
            var state = new SettingsState<TestSettings>(new TestSchema());
            state.Update((ref TestSettings candidate) => candidate.Value = 25);
            var store = new PersistenceStore<TestSettings>(
                storage,
                new PersistenceProfile<TestSettings>(new TestCodec("other-settings")));
            var settings = new PersistentSettings<TestSettings>(state, store);

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.Completed, Is.False);
            Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.RecordFormatMismatch));
            Assert.That(state.Snapshot().Value, Is.EqualTo(25));
        }

        [Test]
        public async Task LoadAsync_ObserverFailureIsWarningAfterSuccessfulCommit()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(30));
            var state = new SettingsState<TestSettings>(new TestSchema());
            var laterObserverCalled = false;
            state.Changed +=
                (in TestSettings snapshot, SettingsChangeReason reason) =>
                    throw new TestException("expected observer failure");
            state.Changed +=
                (in TestSettings snapshot, SettingsChangeReason reason) => laterObserverCalled = true;
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistentSettingsLoadResult result = await settings.LoadAsync();

            Assert.That(result.IsLoaded, Is.True);
            Assert.That(result.HasObserverWarnings, Is.True);
            Assert.That(result.ObserverFailureCount, Is.EqualTo(1));
            Assert.That(result.FirstObserverException, Is.TypeOf<TestException>());
            Assert.That(laterObserverCalled, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(30));
        }

        [Test]
        public async Task SaveAsync_WritesCurrentStateSnapshot()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            state.Update((ref TestSettings candidate) => candidate.Value = 44);
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            PersistenceOperationResult saveResult = await settings.SaveAsync();
            PersistenceLoadResult<TestSettings> stored = await CreateStore(storage).LoadAsync();

            Assert.That(saveResult.IsSuccess, Is.True);
            Assert.That(stored.IsSuccess, Is.True);
            Assert.That(stored.Value.Value, Is.EqualTo(44));
        }

        [Test]
        public async Task DeleteStoredValueAsync_DoesNotResetRuntimeState()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            state.Update((ref TestSettings candidate) => candidate.Value = 70);
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));
            await settings.SaveAsync();

            PersistenceOperationResult deleteResult = await settings.DeleteStoredValueAsync();
            PersistenceLoadResult<TestSettings> stored = await CreateStore(storage).LoadAsync();

            Assert.That(deleteResult.IsSuccess, Is.True);
            Assert.That(stored.IsMissing, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(70));
        }

        [Test]
        public async Task LoadAsync_CancellationIsReportedAndStateIsUnchanged()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                PersistentSettingsLoadResult result = await settings.LoadAsync(cancellation.Token);

                Assert.That(result.Completed, Is.False);
                Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(state.Snapshot().Value, Is.EqualTo(10));
            }
        }

        [Test]
        public async Task LoadAsync_StateChangedWhileReadWasPendingReturnsConflict()
        {
            var storage = new GatedStorage();
            await SeedAsync(storage, new TestSettings(35));
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            Task<PersistentSettingsLoadResult> pending = settings.LoadAsync();
            state.Update((ref TestSettings candidate) => candidate.Value = 77);
            storage.CompleteStored();
            PersistentSettingsLoadResult result = await pending;

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Error, Is.EqualTo(PersistentSettingsLoadError.StateCommitFailed));
            Assert.That(result.StateError, Is.EqualTo(SettingsUpdateError.RevisionConflict));
            Assert.That(state.Snapshot().Value, Is.EqualTo(77));
        }

        [Test]
        public async Task SaveAsync_PreCancelledDoesNotCloneState()
        {
            var schema = new TestSchema();
            var state = new SettingsState<TestSettings>(schema);
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(new MemoryStorage()));
            int cloneCountBeforeSave = schema.CloneCount;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                PersistenceOperationResult result = await settings.SaveAsync(cancellation.Token);

                Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(schema.CloneCount, Is.EqualTo(cloneCountBeforeSave));
            }
        }

        [Test]
        public async Task LoadAsync_RejectsOverlappingIntegrationOperation()
        {
            var storage = new GatedStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            Task<PersistentSettingsLoadResult> firstLoad = settings.LoadAsync();
            Assert.Throws<InvalidOperationException>(() => settings.SaveAsync());

            PersistentSettingsMemorySnapshot active = settings.GetMemorySnapshot();
            Assert.That(active.IsOperationActive, Is.True);
            Assert.That(active.PersistenceOperationActive, Is.True);
            Assert.That(active.StartedLoadCount, Is.EqualTo(1));
            Assert.That(active.StartedSaveCount, Is.Zero);
            Assert.That(active.ConcurrentOperationRejectionCount, Is.EqualTo(1));
            storage.CompleteMissing();

            PersistentSettingsLoadResult result = await firstLoad;
            Assert.That(result.IsMissing, Is.True);

            PersistentSettingsMemorySnapshot completed = settings.GetMemorySnapshot();
            Assert.That(completed.IsOperationActive, Is.False);
            Assert.That(completed.PersistenceOperationActive, Is.False);
        }

        [Test]
        public async Task MemorySnapshot_TracksOperationCountsLimitsAndRecordBytes()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema());
            var settings = new PersistentSettings<TestSettings>(state, CreateStore(storage));

            await settings.SaveAsync();
            await settings.LoadAsync();
            await settings.DeleteStoredValueAsync();

            PersistentSettingsMemorySnapshot snapshot = settings.GetMemorySnapshot();
            Assert.That(snapshot.StateRevision, Is.EqualTo(state.Revision));
            Assert.That(snapshot.MaximumPayloadBytes, Is.EqualTo(new PersistenceLimits().MaximumPayloadBytes));
            Assert.That(snapshot.MaximumRecordBytes, Is.EqualTo(new PersistenceLimits().MaximumRecordBytes));
            Assert.That(snapshot.StartedLoadCount, Is.EqualTo(1));
            Assert.That(snapshot.StartedSaveCount, Is.EqualTo(1));
            Assert.That(snapshot.StartedDeleteCount, Is.EqualTo(1));
            Assert.That(snapshot.LastRecordBytes, Is.GreaterThan(0));
            Assert.That(snapshot.PeakRecordBytes, Is.GreaterThanOrEqualTo(snapshot.LastRecordBytes));
        }

        private static PersistenceStore<TestSettings> CreateStore(IPersistenceStorage storage)
        {
            return new PersistenceStore<TestSettings>(
                storage,
                new PersistenceProfile<TestSettings>(new TestCodec("test-settings")));
        }

        private static async Task SeedAsync(IPersistenceStorage storage, TestSettings value)
        {
            PersistenceOperationResult result = await CreateStore(storage).SaveAsync(in value);
            Assert.That(result.IsSuccess, Is.True);
        }

        private sealed class TestSettings
        {
            public TestSettings(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        private sealed class TestSchema : ISettingsSchema<TestSettings>
        {
            public int CloneCount { get; private set; }

            public TestSettings CreateDefault()
            {
                return new TestSettings(10);
            }

            public TestSettings Clone(in TestSettings value)
            {
                CloneCount++;
                return new TestSettings(value.Value);
            }

            public SettingsValidationResult Validate(in TestSettings value)
            {
                return value != null && value.Value >= 0 && value.Value <= 100
                    ? SettingsValidationResult.Valid()
                    : SettingsValidationResult.Invalid("Value must be between zero and one hundred.");
            }
        }

        private sealed class TestCodec : IPersistenceCodec<TestSettings>
        {
            private readonly PersistenceCodecId _id;

            internal TestCodec(string token)
            {
                _id = new PersistenceCodecId(token);
            }

            public PersistenceCodecId CodecId => _id;

            public void Serialize(
                in TestSettings value,
                IBufferWriter<byte> destination,
                in PersistenceWriteContext context)
            {
                Span<byte> span = destination.GetSpan(4);
                int number = value.Value;
                span[0] = (byte)number;
                span[1] = (byte)(number >> 8);
                span[2] = (byte)(number >> 16);
                span[3] = (byte)(number >> 24);
                destination.Advance(4);
            }

            public TestSettings Deserialize(
                ReadOnlyMemory<byte> payload,
                in PersistenceReadContext context)
            {
                if (payload.Length != 4)
                {
                    throw new FormatException("The test payload must contain four bytes.");
                }

                ReadOnlySpan<byte> span = payload.Span;
                int number = span[0]
                    | (span[1] << 8)
                    | (span[2] << 16)
                    | (span[3] << 24);
                return new TestSettings(number);
            }
        }

        private sealed class MemoryStorage : IPersistenceStorage
        {
            private byte[] _content;

            public string Location => "memory://settings";

            public Task<PersistenceStorageReadResult> ReadAsync(
                int maxByteCount,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    _content == null
                        ? PersistenceStorageReadResult.Missing()
                        : PersistenceStorageReadResult.Found((byte[])_content.Clone()));
            }

            public Task WriteAtomicallyAsync(
                byte[] content,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = (byte[])content.Clone();
                return Task.CompletedTask;
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = null;
                return Task.CompletedTask;
            }

            internal void MutateLastByte()
            {
                _content[_content.Length - 1] ^= 0x01;
            }
        }

        private sealed class GatedStorage : IPersistenceStorage
        {
            private readonly TaskCompletionSource<PersistenceStorageReadResult> _read =
                new TaskCompletionSource<PersistenceStorageReadResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            private byte[] _content;

            public string Location => "memory://gated-settings";

            public Task<PersistenceStorageReadResult> ReadAsync(
                int maxByteCount,
                CancellationToken cancellationToken = default)
            {
                return _read.Task;
            }

            public Task WriteAtomicallyAsync(
                byte[] content,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _content = (byte[])content.Clone();
                return Task.CompletedTask;
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            internal void CompleteMissing()
            {
                _read.SetResult(PersistenceStorageReadResult.Missing());
            }

            internal void CompleteStored()
            {
                _read.SetResult(PersistenceStorageReadResult.Found((byte[])_content.Clone()));
            }
        }

        private sealed class TestException : Exception
        {
            public TestException(string message)
                : base(message)
            {
            }
        }
    }
}
