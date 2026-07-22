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
        public async Task LoadAsync_MissingPreservesDefaultsAndRequiresSave()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var persistent = CreatePersistentSettings(state, storage);

            var result = await persistent.LoadAsync();

            Assert.That(result.Completed, Is.True);
            Assert.That(result.IsMissing, Is.True);
            Assert.That(result.RequiresSave, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(10));
        }

        [Test]
        public async Task LoadAsync_CurrentVersionCommitsLoadedValue()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(35), 2);
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var persistent = CreatePersistentSettings(state, storage);

            var result = await persistent.LoadAsync();

            Assert.That(result.IsLoaded, Is.True);
            Assert.That(result.MigrationApplied, Is.False);
            Assert.That(result.RequiresSave, Is.False);
            Assert.That(state.Snapshot().Value, Is.EqualTo(35));
        }

        [Test]
        public async Task LoadAsync_OldVersionMigratesCommitsAndRequiresExplicitSave()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(5), 0);
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var persistent = CreatePersistentSettings(state, storage);

            var result = await persistent.LoadAsync();

            Assert.That(result.IsLoaded, Is.True);
            Assert.That(result.MigrationApplied, Is.True);
            Assert.That(result.RequiresSave, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(8));

            var unchangedRecord = await CreateStore(storage).LoadAsync(2);
            Assert.That(unchangedRecord.ContentVersion, Is.EqualTo(0));
        }

        [Test]
        public async Task LoadAsync_MigrationValidationFailureLeavesStateUnchanged()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(10), 1);
            var schema = new TestSchema(2);
            var state = new SettingsState<TestSettings>(schema);
            var pipeline = new SettingsMigrationPipeline<TestSettings>(
                schema,
                1,
                new AddMigration(1, 2, 500));
            var persistent = new PersistentSettings<TestSettings>(
                state,
                pipeline,
                CreateStore(storage));

            var result = await persistent.LoadAsync();

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Error, Is.EqualTo(PersistentSettingsLoadError.MigrationFailed));
            Assert.That(result.MigrationError, Is.EqualTo(SettingsMigrationError.ValidationFailed));
            Assert.That(result.MigrationApplied, Is.False);
            Assert.That(state.Snapshot().Value, Is.EqualTo(10));
        }

        [Test]
        public async Task LoadAsync_FutureContentVersionFailsWithoutReplacingState()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(50), 3);
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            state.Update((ref TestSettings candidate) => candidate.Value = 25);
            var persistent = CreatePersistentSettings(state, storage);

            var result = await persistent.LoadAsync();

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Error, Is.EqualTo(PersistentSettingsLoadError.PersistenceFailed));
            Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.FutureContentVersion));
            Assert.That(state.Snapshot().Value, Is.EqualTo(25));
        }

        [Test]
        public async Task LoadAsync_ObserverFailureIsWarningAfterSuccessfulCommit()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(30), 2);
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var laterObserverCalled = false;
            state.Changed +=
                (in TestSettings snapshot, SettingsChangeReason reason) =>
                    throw new TestException("expected observer failure");
            state.Changed +=
                (in TestSettings snapshot, SettingsChangeReason reason) => laterObserverCalled = true;
            var persistent = CreatePersistentSettings(state, storage);

            var result = await persistent.LoadAsync();

            Assert.That(result.IsLoaded, Is.True);
            Assert.That(result.HasObserverWarnings, Is.True);
            Assert.That(result.ObserverFailureCount, Is.EqualTo(1));
            Assert.That(result.FirstObserverException, Is.TypeOf<TestException>());
            Assert.That(laterObserverCalled, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(30));
        }

        [Test]
        public async Task SaveAsync_WritesSnapshotWithCurrentSchemaVersion()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            state.Update((ref TestSettings candidate) => candidate.Value = 44);
            var persistent = CreatePersistentSettings(state, storage);

            var saveResult = await persistent.SaveAsync();
            var stored = await CreateStore(storage).LoadAsync(2);

            Assert.That(saveResult.IsSuccess, Is.True);
            Assert.That(stored.IsSuccess, Is.True);
            Assert.That(stored.ContentVersion, Is.EqualTo(2));
            Assert.That(stored.Value.Value, Is.EqualTo(44));
        }

        [Test]
        public async Task DeleteStoredValueAsync_DoesNotResetRuntimeState()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            state.Update((ref TestSettings candidate) => candidate.Value = 70);
            var persistent = CreatePersistentSettings(state, storage);
            await persistent.SaveAsync();

            var deleteResult = await persistent.DeleteStoredValueAsync();
            var stored = await CreateStore(storage).LoadAsync(2);

            Assert.That(deleteResult.IsSuccess, Is.True);
            Assert.That(stored.IsMissing, Is.True);
            Assert.That(state.Snapshot().Value, Is.EqualTo(70));
        }

        [Test]
        public async Task LoadAsync_CancellationIsReportedAndStateIsUnchanged()
        {
            var storage = new MemoryStorage();
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var persistent = CreatePersistentSettings(state, storage);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                var result = await persistent.LoadAsync(cancellation.Token);

                Assert.That(result.Completed, Is.False);
                Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(state.Snapshot().Value, Is.EqualTo(10));
            }
        }

        [Test]
        public async Task LoadAsync_StateChangedWhileReadWasPendingReturnsConflict()
        {
            var storage = new GatedStorage();
            await SeedAsync(storage, new TestSettings(35), 2);
            var schema = new TestSchema(2);
            var state = new SettingsState<TestSettings>(schema);
            var persistent = CreatePersistentSettings(state, storage);

            Task<PersistentSettingsLoadResult> pending = persistent.LoadAsync();
            state.Update((ref TestSettings candidate) => candidate.Value = 77);
            storage.CompleteStored();
            PersistentSettingsLoadResult result = await pending;

            Assert.That(result.Completed, Is.False);
            Assert.That(result.Error, Is.EqualTo(PersistentSettingsLoadError.StateCommitFailed));
            Assert.That(result.StateError, Is.EqualTo(SettingsUpdateError.RevisionConflict));
            Assert.That(state.Snapshot().Value, Is.EqualTo(77));
        }

        [Test]
        public async Task LoadAsync_CancellationDuringMigrationDoesNotCommit()
        {
            var storage = new MemoryStorage();
            await SeedAsync(storage, new TestSettings(20), 1);
            var schema = new TestSchema(2);
            var state = new SettingsState<TestSettings>(schema);
            using (var cancellation = new CancellationTokenSource())
            {
                var pipeline = new SettingsMigrationPipeline<TestSettings>(
                    schema,
                    1,
                    new CancelMigration(cancellation));
                var persistent = new PersistentSettings<TestSettings>(
                    state,
                    pipeline,
                    CreateStore(storage));

                PersistentSettingsLoadResult result = await persistent.LoadAsync(cancellation.Token);

                Assert.That(result.PersistenceError, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(state.Snapshot().Value, Is.EqualTo(10));
            }
        }

        [Test]
        public async Task SaveAsync_PreCancelledDoesNotCloneState()
        {
            var schema = new TestSchema(2);
            var state = new SettingsState<TestSettings>(schema);
            var persistent = CreatePersistentSettings(state, new MemoryStorage());
            int cloneCountBeforeSave = schema.CloneCount;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                PersistenceOperationResult result = await persistent.SaveAsync(cancellation.Token);

                Assert.That(result.ErrorCode, Is.EqualTo(PersistenceErrorCode.Cancelled));
                Assert.That(schema.CloneCount, Is.EqualTo(cloneCountBeforeSave));
            }
        }

        [Test]
        public async Task LoadAsync_RejectsOverlappingIntegrationOperation()
        {
            var storage = new GatedStorage();
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var persistent = CreatePersistentSettings(state, storage);

            var firstLoad = persistent.LoadAsync();
            Assert.Throws<InvalidOperationException>(() => persistent.SaveAsync());
            storage.CompleteMissing();

            var result = await firstLoad;
            Assert.That(result.IsMissing, Is.True);
        }

        [Test]
        public void Constructor_RejectsVersionMismatch()
        {
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var pipeline = new SettingsMigrationPipeline<TestSettings>(
                new TestSchema(1),
                new AddMigration(0, 1, 1));

            Assert.Throws<ArgumentException>(
                () => new PersistentSettings<TestSettings>(
                    state,
                    pipeline,
                    CreateStore(new MemoryStorage())));
        }

        [Test]
        public void Constructor_RejectsDifferentSchemaAuthoritiesAtSameVersion()
        {
            var state = new SettingsState<TestSettings>(new TestSchema(2));
            var pipeline = new SettingsMigrationPipeline<TestSettings>(
                new TestSchema(2),
                new AddMigration(0, 1, 1),
                new AddMigration(1, 2, 2));

            Assert.Throws<ArgumentException>(
                () => new PersistentSettings<TestSettings>(
                    state,
                    pipeline,
                    CreateStore(new MemoryStorage())));
        }

        private static PersistentSettings<TestSettings> CreatePersistentSettings(
            SettingsState<TestSettings> state,
            IPersistenceStorage storage)
        {
            var pipeline = new SettingsMigrationPipeline<TestSettings>(
                state.Schema,
                new AddMigration(0, 1, 1),
                new AddMigration(1, 2, 2));
            return new PersistentSettings<TestSettings>(
                state,
                pipeline,
                CreateStore(storage));
        }

        private static PersistenceStore<TestSettings> CreateStore(IPersistenceStorage storage)
        {
            return new PersistenceStore<TestSettings>(
                storage,
                new PersistenceProfile<TestSettings>(new TestCodec()));
        }

        private static async Task SeedAsync(
            IPersistenceStorage storage,
            TestSettings value,
            int contentVersion)
        {
            var result = await CreateStore(storage).SaveAsync(in value, contentVersion);
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
            private readonly int _currentVersion;

            public TestSchema(int currentVersion)
            {
                _currentVersion = currentVersion;
            }

            public int CurrentVersion => _currentVersion;

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

        private sealed class AddMigration : ISettingsMigration<TestSettings>
        {
            private readonly int _amount;

            public AddMigration(int sourceVersion, int targetVersion, int amount)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
                _amount = amount;
            }

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public SettingsMigrationResult Apply(ref TestSettings candidate)
            {
                candidate.Value += _amount;
                return SettingsMigrationResult.Success();
            }
        }

        private sealed class CancelMigration : ISettingsMigration<TestSettings>
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelMigration(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public int SourceVersion => 1;

            public int TargetVersion => 2;

            public SettingsMigrationResult Apply(ref TestSettings candidate)
            {
                candidate.Value++;
                _cancellation.Cancel();
                return SettingsMigrationResult.Success();
            }
        }

        private sealed class TestCodec : IPersistenceCodec<TestSettings>
        {
            private static readonly PersistenceCodecId Id = new PersistenceCodecId("test-settings/1");

            public PersistenceCodecId CodecId => Id;

            public void Serialize(
                in TestSettings value,
                IBufferWriter<byte> destination,
                in PersistenceWriteContext context)
            {
                var span = destination.GetSpan(4);
                var number = value.Value;
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

                var span = payload.Span;
                var number = span[0]
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

            public void CompleteMissing()
            {
                _read.SetResult(PersistenceStorageReadResult.Missing());
            }

            public void CompleteStored()
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
