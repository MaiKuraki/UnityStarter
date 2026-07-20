using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace CycloneGames.Services.Tests
{
    [TestFixture]
    public sealed class SettingsStorePersistenceTests
    {
        [Test]
        public void Load_MissingEntry_UsesDefaultsWithoutWritingStorage()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema(defaultValue: 19);
            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Missing));
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.RequiresSave, Is.True);
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Missing));
                Assert.That(store.Value.Value, Is.EqualTo(19));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
                Assert.That(storage.HasContent, Is.False);
            }
        }

        [Test]
        public void Save_CommitsOneEnvelopeThroughOneAtomicWrite()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema(defaultValue: 23);
            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(1));
                Assert.That(storage.LegacyDeleteCount, Is.EqualTo(1));
                Assert.That(storage.HasContent, Is.True);
                Assert.That(storage.HasLegacyChecksum, Is.False);
                string envelopePrefix = Encoding.ASCII.GetString(
                    storage.LastAtomicWriteContent,
                    0,
                    "# CycloneGames.Services Settings".Length);
                Assert.That(envelopePrefix, Is.EqualTo("# CycloneGames.Services Settings"));
            }
        }

        [Test]
        public void SaveThenLoad_RoundTripsEnvelopeAndReportsValidIntegrity()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema();
            var codec = new TestSettingsCodec();
            using (SettingsStore<TestSettings> writer = CreateStore(storage, codec, schema))
            {
                SettingsOperationResult update = writer.Update(
                    (ref TestSettings value) => value.Value = 42);
                Assert.That(update.Succeeded, Is.True);
                Assert.That(writer.Save().Succeeded, Is.True);
            }

            using (SettingsStore<TestSettings> reader = CreateStore(
                       storage,
                       new TestSettingsCodec(),
                       new TestSettingsSchema()))
            {
                SettingsLoadResult result = reader.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Valid));
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(result.MigrationApplied, Is.False);
                Assert.That(reader.Value.Version, Is.EqualTo(2));
                Assert.That(reader.Value.Value, Is.EqualTo(42));
            }
        }

        [Test]
        public void Load_LegacyPayload_IsDisabledByDefault()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec();
            storage.SetContent(SettingsStoreTestData.Payload(2, 29));

            using (SettingsStore<TestSettings> store = CreateStore(storage, codec))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(SettingsStoreOptions.Default.AllowLegacyPayload, Is.False);
                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.UnsupportedFormat));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.LegacyPayload));
                Assert.That(codec.DeserializeCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_LegacyOptIn_DoesNotAcceptModifiedPayloadWithoutSeparateOptIn()
        {
            FakeSettingsStorage storage = CreateLegacyStorage(
                includeChecksum: true,
                checksumMatches: false);
            var codec = new TestSettingsCodec();
            var options = new SettingsStoreOptions(allowLegacyPayload: true);

            using (SettingsStore<TestSettings> store = CreateStore(
                       storage,
                       codec,
                       options: options))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.IntegrityCheckFailed));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Modified));
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(codec.DeserializeCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_ModifiedEnvelope_IsRejectedBeforeDeserializationByDefault()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec();
            byte[] envelope = SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 31));
            envelope[envelope.Length - 1] = (byte)'2';
            storage.SetContent(envelope);

            using (SettingsStore<TestSettings> store = CreateStore(storage, codec))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.IntegrityCheckFailed));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Modified));
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(codec.DeserializeCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_ModifiedEnvelope_ExplicitOptInValidatesCommitsAndRequiresSave()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec();
            byte[] envelope = SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 31));
            envelope[envelope.Length - 1] = (byte)'2';
            storage.SetContent(envelope);
            var options = new SettingsStoreOptions(allowModifiedPayload: true);

            using (SettingsStore<TestSettings> store = CreateStore(storage, codec, options: options))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.IntegrityCheckFailed));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Modified));
                Assert.That(result.HasWarning, Is.True);
                Assert.That(result.RequiresSave, Is.True);
                Assert.That(codec.DeserializeCount, Is.EqualTo(1));
                Assert.That(store.Value.Value, Is.EqualTo(32));
            }
        }

        [Test]
        public void Load_LegacyPayloadWithMatchingChecksum_ReportsValidAndRequiresSave()
        {
            AssertLegacyLoad(SettingsIntegrity.Valid, includeChecksum: true, checksumMatches: true);
        }

        [Test]
        public void Load_LegacyPayloadWithoutChecksum_ReportsMissingAndRequiresSave()
        {
            AssertLegacyLoad(SettingsIntegrity.Missing, includeChecksum: false, checksumMatches: false);
        }

        [Test]
        public void Load_LegacyPayloadWithMismatchedChecksum_ReportsModifiedAndRequiresSave()
        {
            AssertLegacyLoad(SettingsIntegrity.Modified, includeChecksum: true, checksumMatches: false);
        }

        [Test]
        public void Load_MalformedEnvelope_ReturnsCorruptedEnvelopeWithoutChangingDefaults()
        {
            var storage = new FakeSettingsStorage();
            storage.SetContent(Encoding.ASCII.GetBytes(
                "# CycloneGames.Services Settings\n# format: 1\n"));
            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.CorruptedEnvelope));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Corrupted));
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }
        }

        [Test]
        public void Load_EnvelopeWithDamagedMagicByte_ReturnsCorruptedEnvelope()
        {
            var storage = new FakeSettingsStorage();
            byte[] envelope = SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 11));
            envelope[12] ^= 0x20;
            storage.SetContent(envelope);

            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.CorruptedEnvelope));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Corrupted));
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_EnvelopeWithCrLfHeader_PreservesPayloadAndValidChecksum()
        {
            var storage = new FakeSettingsStorage();
            byte[] payload = SettingsStoreTestData.Payload(2, 64);
            storage.SetContent(SettingsStoreTestData.EnvelopeWithCrLfHeader(2, payload));

            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Valid));
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(store.Value.Version, Is.EqualTo(2));
                Assert.That(store.Value.Value, Is.EqualTo(64));
            }
        }

        [Test]
        public void Load_UnsupportedEnvelopeFormat_ReturnsUnsupportedFormat()
        {
            var storage = new FakeSettingsStorage();
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 11),
                formatVersion: 99));
            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.UnsupportedFormat));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_FutureSchema_ReturnsFailureWithoutDeserializing()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec();
            storage.SetContent(SettingsStoreTestData.Envelope(
                3,
                SettingsStoreTestData.Payload(3, 11)));
            using (SettingsStore<TestSettings> store = CreateStore(storage, codec))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.FutureSchemaVersion));
                Assert.That(result.SourceVersion, Is.EqualTo(3));
                Assert.That(codec.DeserializeCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_DeserializeFailure_IsClassifiedAndPreservesDefaults()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec { ThrowOnDeserialize = true };
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 11)));

            using (SettingsStore<TestSettings> store = CreateStore(storage, codec))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.DeserializeFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_EnvelopeAndPayloadVersionMismatch_IsRejected()
        {
            var storage = new FakeSettingsStorage();
            storage.SetContent(SettingsStoreTestData.Envelope(
                1,
                SettingsStoreTestData.Payload(2, 11)));

            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.SchemaVersionMismatch));
                Assert.That(result.SourceVersion, Is.EqualTo(1));
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_OlderSchema_MigratesCandidateAndRequiresSave()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema(currentVersion: 2)
            {
                MigrationValueDelta = 100
            };
            storage.SetContent(SettingsStoreTestData.Envelope(
                1,
                SettingsStoreTestData.Payload(1, 5)));
            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
                Assert.That(result.MigrationApplied, Is.True);
                Assert.That(result.RequiresSave, Is.True);
                Assert.That(result.SourceVersion, Is.EqualTo(1));
                Assert.That(result.TargetVersion, Is.EqualTo(2));
                Assert.That(schema.MigrationCount, Is.EqualTo(1));
                Assert.That(schema.LastMigrationSourceVersion, Is.EqualTo(1));
                Assert.That(schema.LastMigrationTargetVersion, Is.EqualTo(2));
                Assert.That(store.Value.Version, Is.EqualTo(2));
                Assert.That(store.Value.Value, Is.EqualTo(105));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }
        }

        [Test]
        public void Load_PostMigrationValidationFailure_ReportsMigrationAppliedWithoutCommit()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema(currentVersion: 2);
            storage.SetContent(SettingsStoreTestData.Envelope(
                1,
                SettingsStoreTestData.Payload(1, 5)));

            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                schema.ThrowOnValidation = true;
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ValidationFailed));
                Assert.That(result.MigrationApplied, Is.True);
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(store.Value.Version, Is.EqualTo(2));
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_SnapshotFailure_IsClassifiedAndPreservesDefaults()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema();
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 11)));

            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                schema.ThrowOnClone = true;
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.SnapshotFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                schema.ThrowOnClone = false;
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_MigrationFailure_PreservesPreviousValueAndContent()
        {
            var storage = new FakeSettingsStorage();
            var schema = new TestSettingsSchema(currentVersion: 2, defaultValue: 17)
            {
                FailMigration = true
            };
            byte[] original = SettingsStoreTestData.Envelope(
                1,
                SettingsStoreTestData.Payload(1, 5));
            storage.SetContent(original);
            using (SettingsStore<TestSettings> store = CreateStore(storage, schema: schema))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.MigrationFailed));
                Assert.That(store.Value.Version, Is.EqualTo(2));
                Assert.That(store.Value.Value, Is.EqualTo(17));
                Assert.That(storage.GetContentCopy(), Is.EqualTo(original));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }
        }

        [Test]
        public void Load_ReadFailure_ReturnsReadFailedAndPreservesDefaults()
        {
            var storage = new FakeSettingsStorage();
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 31)));
            storage.ReadFailure = new IOException("Injected read failure.");
            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ReadFailed));
                Assert.That(result.Exception, Is.TypeOf<IOException>());
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }
        }

        [Test]
        public void Load_LengthAccessFailure_IsNotMisclassifiedAsMissing()
        {
            var storage = new FakeSettingsStorage
            {
                LengthFailure = new UnauthorizedAccessException("Injected access failure.")
            };
            int notificationCount = 0;

            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                store.Changed += CountNotification;
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ReadFailed));
                Assert.That(result.Exception, Is.TypeOf<UnauthorizedAccessException>());
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(notificationCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }

            void CountNotification(in TestSettings settings, SettingsChangeReason reason)
            {
                notificationCount++;
            }
        }

        [Test]
        public void Load_NegativeStorageLength_IsReadFailure()
        {
            var storage = new FakeSettingsStorage
            {
                LengthOverride = -1L
            };
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, 31)));

            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ReadFailed));
                Assert.That(result.RequiresSave, Is.False);
                Assert.That(storage.ReadCount, Is.Zero);
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Load_LegacyChecksumReadFailure_ReturnsReadFailed()
        {
            var storage = CreateLegacyStorage(includeChecksum: true);
            storage.LegacyReadFailure = new UnauthorizedAccessException(
                "Injected checksum access failure.");

            using (SettingsStore<TestSettings> store = CreateLegacyStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ReadFailed));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.LegacyPayload));
                Assert.That(result.Exception, Is.TypeOf<UnauthorizedAccessException>());
                Assert.That(store.Value.Value, Is.EqualTo(7));
            }
        }

        [Test]
        public void Save_AfterLegacyLoad_RemovesObsoleteChecksumAfterEnvelopeCommit()
        {
            var storage = CreateLegacyStorage(includeChecksum: true);
            using (SettingsStore<TestSettings> store = CreateLegacyStore(storage))
            {
                Assert.That(store.Load().Succeeded, Is.True);

                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.HasWarning, Is.False);
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(1));
                Assert.That(storage.LegacyDeleteCount, Is.EqualTo(1));
                Assert.That(storage.HasLegacyChecksum, Is.False);
                Assert.That(
                    Encoding.ASCII.GetString(
                        storage.GetContentCopy(),
                        0,
                        "# CycloneGames.Services Settings".Length),
                    Is.EqualTo("# CycloneGames.Services Settings"));
            }
        }

        [Test]
        public void Save_LegacyChecksumCleanupFailure_NewStoreCanRetryAfterLoadingEnvelope()
        {
            var storage = CreateLegacyStorage(includeChecksum: true);
            using (SettingsStore<TestSettings> store = CreateLegacyStore(storage))
            {
                Assert.That(store.Load().Succeeded, Is.True);
                storage.LegacyDeleteFailure = new IOException("Injected cleanup failure.");

                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.HasWarning, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.LegacyCleanupFailed));
                Assert.That(result.Exception, Is.TypeOf<IOException>());
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(1));
                Assert.That(storage.LegacyDeleteCount, Is.EqualTo(1));
                Assert.That(storage.HasLegacyChecksum, Is.True);
            }

            storage.LegacyDeleteFailure = null;
            using (SettingsStore<TestSettings> retryStore = CreateStore(storage))
            {
                SettingsLoadResult load = retryStore.Load();
                Assert.That(load.Succeeded, Is.True);
                Assert.That(load.Format, Is.EqualTo(SettingsDataFormat.EnvelopeV1));
                Assert.That(load.Integrity, Is.EqualTo(SettingsIntegrity.Valid));

                SettingsOperationResult retry = retryStore.Save();

                Assert.That(retry.Succeeded, Is.True);
                Assert.That(retry.HasWarning, Is.False);
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(2));
                Assert.That(storage.LegacyDeleteCount, Is.EqualTo(2));
                Assert.That(storage.HasLegacyChecksum, Is.False);
            }
        }

        [Test]
        public void Save_AtomicWriteFailure_ReturnsWriteFailedAndPreservesExistingContent()
        {
            var storage = new FakeSettingsStorage();
            byte[] original = Encoding.ASCII.GetBytes("previous");
            storage.SetContent(original);
            storage.AtomicWriteFailure = new IOException("Injected write failure.");
            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.WriteFailed));
                Assert.That(result.Exception, Is.TypeOf<IOException>());
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(1));
                Assert.That(storage.GetContentCopy(), Is.EqualTo(original));
            }
        }

        [Test]
        public void Save_SerializationFailure_IsClassifiedWithoutWriting()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec { ThrowOnSerialize = true };

            using (SettingsStore<TestSettings> store = CreateStore(storage, codec))
            {
                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.SerializationFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(storage.AtomicWriteCount, Is.Zero);
                Assert.That(storage.LegacyDeleteCount, Is.Zero);
            }
        }

        [Test]
        public void Load_LegacyPayloadOverBudget_FailsBeforeDeserialization()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec();
            storage.SetContent(new byte[SettingsStoreOptions.MinimumPayloadBytes + 1]);
            var options = new SettingsStoreOptions(
                maxPayloadBytes: SettingsStoreOptions.MinimumPayloadBytes,
                allowLegacyPayload: true);
            using (SettingsStore<TestSettings> store = CreateStore(storage, codec, options: options))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.PayloadTooLarge));
                Assert.That(codec.DeserializeCount, Is.Zero);
            }
        }

        [Test]
        public void Save_PayloadOverBudget_FailsWithoutWriting()
        {
            var storage = new FakeSettingsStorage();
            var codec = new TestSettingsCodec
            {
                SerializedPayloadOverride = new byte[SettingsStoreOptions.MinimumPayloadBytes + 1]
            };
            var options = new SettingsStoreOptions(
                maxPayloadBytes: SettingsStoreOptions.MinimumPayloadBytes);
            using (SettingsStore<TestSettings> store = CreateStore(storage, codec, options: options))
            {
                SettingsOperationResult result = store.Save();

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.PayloadTooLarge));
                Assert.That(result.Exception, Is.TypeOf<SettingsPayloadBudgetExceededException>());
                Assert.That(
                    ((SettingsPayloadBudgetExceededException)result.Exception).MaxByteCount,
                    Is.EqualTo(SettingsStoreOptions.MinimumPayloadBytes));
                Assert.That(
                    codec.LastSerializeMaxByteCount,
                    Is.EqualTo(SettingsStoreOptions.MinimumPayloadBytes));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
                Assert.That(storage.LegacyDeleteCount, Is.Zero);
                Assert.That(storage.HasContent, Is.False);
            }
        }

        [Test]
        public void Load_ObserverFailure_ReturnsWarningWithoutReclassifyingStoredData()
        {
            var storage = new FakeSettingsStorage();
            using (SettingsStore<TestSettings> writer = CreateStore(storage))
            {
                Assert.That(writer.Save().Succeeded, Is.True);
            }

            using (SettingsStore<TestSettings> reader = CreateStore(storage))
            {
                reader.Changed += ThrowingObserver;

                SettingsLoadResult result = reader.Load();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ObserverFailed));
                Assert.That(result.HasWarning, Is.True);
                Assert.That(result.Integrity, Is.EqualTo(SettingsIntegrity.Valid));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(reader.Value.Value, Is.EqualTo(7));
                Assert.That(storage.AtomicWriteCount, Is.EqualTo(1));
            }
        }

        private static void AssertLegacyLoad(
            SettingsIntegrity expectedIntegrity,
            bool includeChecksum,
            bool checksumMatches)
        {
            FakeSettingsStorage storage = CreateLegacyStorage(includeChecksum, checksumMatches);
            bool allowModifiedPayload = expectedIntegrity == SettingsIntegrity.Modified;
            using (SettingsStore<TestSettings> store = CreateLegacyStore(
                       storage,
                       allowModifiedPayload))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
                Assert.That(result.Format, Is.EqualTo(SettingsDataFormat.LegacyPayload));
                Assert.That(result.Integrity, Is.EqualTo(expectedIntegrity));
                Assert.That(
                    result.ErrorCode,
                    Is.EqualTo(allowModifiedPayload
                        ? SettingsErrorCode.IntegrityCheckFailed
                        : SettingsErrorCode.None));
                Assert.That(result.RequiresSave, Is.True);
                Assert.That(store.Value.Value, Is.EqualTo(29));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
                Assert.That(storage.LegacyReadCount, Is.EqualTo(1));
            }
        }

        private static FakeSettingsStorage CreateLegacyStorage(
            bool includeChecksum,
            bool checksumMatches = true)
        {
            var storage = new FakeSettingsStorage();
            byte[] payload = SettingsStoreTestData.Payload(2, 29);
            storage.SetContent(payload);
            if (includeChecksum)
            {
                storage.SetLegacyChecksum(SettingsStoreTestData.LegacyChecksum(
                    payload,
                    checksumMatches));
            }

            return storage;
        }

        private static SettingsStore<TestSettings> CreateLegacyStore(
            FakeSettingsStorage storage,
            bool allowModifiedPayload = false)
        {
            return CreateStore(
                storage,
                options: new SettingsStoreOptions(
                    allowLegacyPayload: true,
                    allowModifiedPayload: allowModifiedPayload));
        }

        private static SettingsStore<TestSettings> CreateStore(
            FakeSettingsStorage storage,
            TestSettingsCodec codec = null,
            TestSettingsSchema schema = null,
            SettingsStoreOptions options = null)
        {
            return new SettingsStore<TestSettings>(
                storage,
                codec ?? new TestSettingsCodec(),
                schema ?? new TestSettingsSchema(),
                options);
        }

        private static void ThrowingObserver(
            in TestSettings settings,
            SettingsChangeReason reason)
        {
            throw new InvalidOperationException("Injected observer failure.");
        }
    }
}
