using System;
using System.Globalization;
using System.Text;
using CycloneGames.Hash.Core;

namespace CycloneGames.Services
{
    /// <summary>
    /// Single-owner, typed settings state with bounded reads, schema migration, validation,
    /// and one-file atomic persistence.
    /// </summary>
    public sealed class SettingsStore<T> : IDisposable where T : struct
    {
        private const int MaximumLegacyChecksumBytes = 256;
        private static readonly byte[] LegacyLineFeed = { (byte)'\n' };

        private static readonly SettingsChangedHandler<T>[] EmptyObservers =
            Array.Empty<SettingsChangedHandler<T>>();

        private readonly ISettingsStorage _storage;
        private readonly ISettingsCodec<T> _codec;
        private readonly ISettingsSchema<T> _schema;
        private readonly SettingsStoreOptions _options;

        private T _value;
        private SettingsChangedHandler<T>[] _observers = EmptyObservers;
        private bool _operationInProgress;
        private bool _disposed;

        public SettingsStore(
            ISettingsStorage storage,
            ISettingsCodec<T> codec,
            ISettingsSchema<T> schema,
            SettingsStoreOptions options = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _options = options ?? SettingsStoreOptions.Default;

            if (string.IsNullOrWhiteSpace(_storage.Location))
            {
                throw new ArgumentException(
                    "The settings storage must provide a diagnostic location.",
                    nameof(storage));
            }

            if (_schema.CurrentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schema),
                    "The current settings schema version cannot be negative.");
            }

            _value = CreateAndValidateDefaults();
        }

        public T Value
        {
            get
            {
                ThrowIfDisposed();
                return _schema.Clone(in _value);
            }
        }

        public string StorageLocation => _storage.Location;

        public event SettingsChangedHandler<T> Changed
        {
            add
            {
                ThrowIfDisposed();
                AddObserver(value);
            }
            remove
            {
                if (!_disposed)
                {
                    RemoveObserver(value);
                }
            }
        }

        public SettingsLoadResult Load()
        {
            BeginOperation();
            byte[] content = null;
            byte[] legacyChecksum = null;
            try
            {
                int maximumEnvelopeBytes = SettingsEnvelope.MaximumEnvelopeBytes(_options.MaxPayloadBytes);
                try
                {
                    long fileLength = _storage.GetLength();
                    if (fileLength < 0L)
                    {
                        return CreateLoadFailure(
                            SettingsIntegrity.NotChecked,
                            SettingsDataFormat.None,
                            SettingsErrorCode.ReadFailed,
                            "The settings storage returned an invalid negative length.",
                            null);
                    }

                    if (fileLength > maximumEnvelopeBytes)
                    {
                        return CreateLoadFailure(
                            SettingsIntegrity.NotChecked,
                            SettingsDataFormat.None,
                            SettingsErrorCode.PayloadTooLarge,
                            "The settings file exceeds the configured read budget.",
                            null);
                    }
                }
                catch (SettingsStorageEntryNotFoundException)
                {
                    return LoadDefaultsForMissingFile();
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.None,
                        SettingsErrorCode.ReadFailed,
                        "The settings file length could not be inspected.",
                        exception);
                }

                try
                {
                    content = _storage.Read(maximumEnvelopeBytes);
                }
                catch (SettingsStorageEntryNotFoundException)
                {
                    return LoadDefaultsForMissingFile();
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.None,
                        SettingsErrorCode.ReadFailed,
                        "The settings file could not be read within the configured budget.",
                        exception);
                }

                if (content == null)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.None,
                        SettingsErrorCode.ReadFailed,
                        "The settings storage returned a null read buffer.",
                        null);
                }

                if (content.Length > maximumEnvelopeBytes)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.None,
                        SettingsErrorCode.PayloadTooLarge,
                        "The settings storage returned content beyond the configured read budget.",
                        null);
                }

                SettingsDataFormat format;
                SettingsIntegrity integrity;
                ReadOnlyMemory<byte> payload;
                int sourceVersion;

                if (SettingsEnvelope.HasEnvelopeMagic(content))
                {
                    SettingsEnvelopeDecodeError decodeError = SettingsEnvelope.TryDecode(
                        content,
                        out SettingsEnvelopeData envelope,
                        out string envelopeMessage);
                    if (decodeError != SettingsEnvelopeDecodeError.None)
                    {
                        SettingsErrorCode errorCode = decodeError == SettingsEnvelopeDecodeError.UnsupportedFormat
                            ? SettingsErrorCode.UnsupportedFormat
                            : SettingsErrorCode.CorruptedEnvelope;
                        return CreateLoadFailure(
                            SettingsIntegrity.Corrupted,
                            SettingsDataFormat.EnvelopeV1,
                            errorCode,
                            envelopeMessage,
                            null);
                    }

                    format = SettingsDataFormat.EnvelopeV1;
                    integrity = envelope.Integrity;
                    payload = new ReadOnlyMemory<byte>(
                        content,
                        envelope.PayloadOffset,
                        envelope.PayloadLength);
                    sourceVersion = envelope.SchemaVersion;
                }
                else
                {
                    if (SettingsEnvelope.LooksLikeEnvelope(content))
                    {
                        return CreateLoadFailure(
                            SettingsIntegrity.Corrupted,
                            SettingsDataFormat.EnvelopeV1,
                            SettingsErrorCode.CorruptedEnvelope,
                            "The reserved settings envelope header is damaged.",
                            null);
                    }

                    if (!_options.AllowLegacyPayload)
                    {
                        return CreateLoadFailure(
                            SettingsIntegrity.Corrupted,
                            SettingsDataFormat.LegacyPayload,
                            SettingsErrorCode.UnsupportedFormat,
                            "Legacy settings payloads are disabled for this store.",
                            null);
                    }

                    if (content.Length > _options.MaxPayloadBytes)
                    {
                        return CreateLoadFailure(
                            SettingsIntegrity.NotChecked,
                            SettingsDataFormat.LegacyPayload,
                            SettingsErrorCode.PayloadTooLarge,
                            "The legacy settings payload exceeds the configured budget.",
                            null);
                    }

                    format = SettingsDataFormat.LegacyPayload;
                    payload = new ReadOnlyMemory<byte>(content);
                    if (!TryReadLegacyIntegrity(
                            content,
                            out integrity,
                            out legacyChecksum,
                            out SettingsLoadResult legacyFailure))
                    {
                        return legacyFailure;
                    }

                    sourceVersion = -1;
                }

                if (payload.Length > _options.MaxPayloadBytes)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.PayloadTooLarge,
                        "The settings payload exceeds the configured budget.",
                        null);
                }

                if (integrity == SettingsIntegrity.Modified && !_options.AllowModifiedPayload)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.Modified,
                        format,
                        SettingsErrorCode.IntegrityCheckFailed,
                        "The settings checksum does not match the payload. Modified payloads are disabled for this store.",
                        null,
                        sourceVersion);
                }

                if (sourceVersion > _schema.CurrentVersion)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.FutureSchemaVersion,
                        $"Settings schema {sourceVersion} is newer than supported schema {_schema.CurrentVersion}.",
                        null,
                        sourceVersion);
                }

                T candidate;
                try
                {
                    candidate = _codec.Deserialize(payload);
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.Corrupted,
                        format,
                        SettingsErrorCode.DeserializeFailed,
                        "The settings payload could not be deserialized.",
                        exception,
                        sourceVersion);
                }

                int payloadVersion;
                try
                {
                    payloadVersion = _schema.GetVersion(in candidate);
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.Corrupted,
                        format,
                        SettingsErrorCode.SchemaVersionMismatch,
                        "The settings schema version could not be read from the payload.",
                        exception,
                        sourceVersion);
                }

                if (format == SettingsDataFormat.EnvelopeV1 && payloadVersion != sourceVersion)
                {
                    return CreateLoadFailure(
                        SettingsIntegrity.Corrupted,
                        format,
                        SettingsErrorCode.SchemaVersionMismatch,
                        "The envelope schema version does not match the payload schema version.",
                        null,
                        sourceVersion);
                }

                if (format == SettingsDataFormat.LegacyPayload)
                {
                    sourceVersion = payloadVersion;
                }

                if (sourceVersion > _schema.CurrentVersion)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.FutureSchemaVersion,
                        $"Settings schema {sourceVersion} is newer than supported schema {_schema.CurrentVersion}.",
                        null,
                        sourceVersion);
                }

                bool migrationApplied = false;
                if (sourceVersion < _schema.CurrentVersion)
                {
                    SettingsMigrationResult migration;
                    try
                    {
                        migration = _schema.Migrate(
                            sourceVersion,
                            _schema.CurrentVersion,
                            ref candidate);
                    }
                    catch (Exception exception)
                    {
                        return CreateLoadFailure(
                            integrity,
                            format,
                            SettingsErrorCode.MigrationFailed,
                            "The settings migration threw an exception.",
                            exception,
                            sourceVersion);
                    }

                    if (!migration.Succeeded)
                    {
                        return CreateLoadFailure(
                            integrity,
                            format,
                            SettingsErrorCode.MigrationFailed,
                            migration.Message,
                            null,
                            sourceVersion);
                    }

                    int migratedVersion;
                    try
                    {
                        migratedVersion = _schema.GetVersion(in candidate);
                    }
                    catch (Exception exception)
                    {
                        return CreateLoadFailure(
                            integrity,
                            format,
                            SettingsErrorCode.SchemaVersionMismatch,
                            "The migrated settings schema version could not be read.",
                            exception,
                            sourceVersion,
                            true);
                    }

                    if (migratedVersion != _schema.CurrentVersion)
                    {
                        return CreateLoadFailure(
                            integrity,
                            format,
                            SettingsErrorCode.SchemaVersionMismatch,
                            "Migration did not produce the current settings schema version.",
                            null,
                            sourceVersion,
                            true);
                    }

                    migrationApplied = true;
                }

                SettingsValidationResult validation;
                try
                {
                    validation = _schema.Validate(in candidate);
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.ValidationFailed,
                        "The settings validator threw an exception.",
                        exception,
                        sourceVersion,
                        migrationApplied);
                }

                if (!validation.IsValid)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.ValidationFailed,
                        validation.Message,
                        null,
                        sourceVersion,
                        migrationApplied);
                }

                T committedCandidate;
                try
                {
                    committedCandidate = _schema.Clone(in candidate);
                }
                catch (Exception exception)
                {
                    return CreateLoadFailure(
                        integrity,
                        format,
                        SettingsErrorCode.SnapshotFailed,
                        "The validated settings value could not be cloned for isolated ownership.",
                        exception,
                        sourceVersion,
                        migrationApplied);
                }

                _value = committedCandidate;
                Exception notificationFailure = NotifyChanged(SettingsChangeReason.Loaded);
                SettingsErrorCode loadError;
                string loadMessage;
                if (notificationFailure != null)
                {
                    loadError = SettingsErrorCode.ObserverFailed;
                    loadMessage = integrity == SettingsIntegrity.Modified
                        ? "The modified settings payload was accepted, but a settings observer failed."
                        : "Settings were loaded, but a settings observer failed.";
                }
                else if (integrity == SettingsIntegrity.Modified)
                {
                    loadError = SettingsErrorCode.IntegrityCheckFailed;
                    loadMessage = "The modified settings payload was explicitly accepted after validation and should be rewritten.";
                }
                else
                {
                    loadError = SettingsErrorCode.None;
                    loadMessage = string.Empty;
                }

                SettingsLoadResult loadedResult = new SettingsLoadResult(
                    SettingsLoadStatus.Loaded,
                    integrity,
                    format,
                    loadError,
                    sourceVersion,
                    _schema.CurrentVersion,
                    migrationApplied,
                    format == SettingsDataFormat.LegacyPayload
                        || migrationApplied
                        || integrity == SettingsIntegrity.Modified,
                    loadMessage,
                    notificationFailure);
                return loadedResult;
            }
            finally
            {
                ClearBuffer(content);
                ClearBuffer(legacyChecksum);
                EndOperation();
            }
        }

        public SettingsOperationResult Save()
        {
            BeginOperation();
            byte[] payload = null;
            byte[] envelope = null;
            try
            {
                SettingsOperationResult validationResult = ValidateCurrentValue();
                if (!validationResult.Succeeded)
                {
                    return validationResult;
                }

                try
                {
                    payload = _codec.Serialize(in _value, _options.MaxPayloadBytes);
                }
                catch (SettingsPayloadBudgetExceededException exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.PayloadTooLarge,
                        "The settings value exceeds the configured serialization budget.",
                        exception);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.SerializationFailed,
                        "The settings value could not be serialized.",
                        exception);
                }

                if (payload == null)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.SerializationFailed,
                        "The settings codec returned a null payload.");
                }

                if (payload.Length > _options.MaxPayloadBytes)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.PayloadTooLarge,
                        "The serialized settings payload exceeds the configured budget.");
                }

                try
                {
                    envelope = SettingsEnvelope.Encode(_schema.CurrentVersion, payload);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.SerializationFailed,
                        "The settings payload could not be wrapped in the persistence envelope.",
                        exception);
                }

                try
                {
                    _storage.WriteAtomically(envelope);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.WriteFailed,
                        "The settings envelope could not be committed atomically.",
                        exception);
                }

                if (_storage is ILegacySettingsChecksumStorage legacyStorage)
                {
                    try
                    {
                        legacyStorage.DeleteLegacyChecksum();
                    }
                    catch (Exception exception)
                    {
                        return SettingsOperationResult.Warning(
                            false,
                            SettingsErrorCode.LegacyCleanupFailed,
                            "The settings envelope was committed, but the obsolete legacy checksum could not be removed.",
                            exception);
                    }
                }

                return SettingsOperationResult.Success();
            }
            finally
            {
                ClearBuffer(payload);
                ClearBuffer(envelope);
                EndOperation();
            }
        }

        public SettingsOperationResult Update(SettingsRefAction<T> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            BeginOperation();
            try
            {
                T candidate;
                try
                {
                    candidate = _schema.Clone(in _value);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.SnapshotFailed,
                        "The settings value could not be cloned for an isolated update.",
                        exception);
                }

                try
                {
                    update(ref candidate);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.UpdateCallbackFailed,
                        "The settings update callback failed. The previous value was preserved.",
                        exception);
                }

                SettingsOperationResult validationResult = ValidateCandidate(in candidate);
                if (!validationResult.Succeeded)
                {
                    return validationResult;
                }

                T committedValue;
                try
                {
                    committedValue = _schema.Clone(in candidate);
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.SnapshotFailed,
                        "The validated settings value could not be cloned for isolated ownership.",
                        exception);
                }

                _value = committedValue;
                Exception observerFailure = NotifyChanged(SettingsChangeReason.Updated);
                return observerFailure == null
                    ? SettingsOperationResult.Success(true)
                    : SettingsOperationResult.Warning(
                        true,
                        SettingsErrorCode.ObserverFailed,
                        "Settings were updated, but a settings observer failed.",
                        observerFailure);
            }
            finally
            {
                EndOperation();
            }
        }

        public SettingsOperationResult ResetToDefaults()
        {
            BeginOperation();
            try
            {
                T defaults;
                try
                {
                    defaults = CreateAndValidateDefaults();
                }
                catch (Exception exception)
                {
                    return SettingsOperationResult.Failure(
                        SettingsErrorCode.ValidationFailed,
                        "Default settings could not be created or validated.",
                        exception);
                }

                _value = defaults;
                Exception observerFailure = NotifyChanged(SettingsChangeReason.ResetToDefaults);
                return observerFailure == null
                    ? SettingsOperationResult.Success(true)
                    : SettingsOperationResult.Warning(
                        true,
                        SettingsErrorCode.ObserverFailed,
                        "Defaults were restored, but a settings observer failed.",
                        observerFailure);
            }
            finally
            {
                EndOperation();
            }
        }

        public void Dispose()
        {
            if (_operationInProgress)
            {
                throw new InvalidOperationException("The settings store cannot be disposed during an operation.");
            }

            _observers = EmptyObservers;
            if (_disposed)
            {
                return;
            }

            _value = default;
            _disposed = true;
        }

        private bool TryReadLegacyIntegrity(
            byte[] payload,
            out SettingsIntegrity integrity,
            out byte[] checksumContent,
            out SettingsLoadResult failure)
        {
            integrity = SettingsIntegrity.Missing;
            checksumContent = null;
            failure = default;
            if (!(_storage is ILegacySettingsChecksumStorage legacyStorage))
            {
                return true;
            }

            try
            {
                checksumContent = legacyStorage.ReadLegacyChecksum(MaximumLegacyChecksumBytes);
                if (checksumContent == null)
                {
                    failure = CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.LegacyPayload,
                        SettingsErrorCode.ReadFailed,
                        "The legacy checksum storage returned a null read buffer.",
                        null);
                    return false;
                }

                if (checksumContent.Length > MaximumLegacyChecksumBytes)
                {
                    failure = CreateLoadFailure(
                        SettingsIntegrity.NotChecked,
                        SettingsDataFormat.LegacyPayload,
                        SettingsErrorCode.PayloadTooLarge,
                        "The legacy checksum storage returned content beyond the configured read budget.",
                        null);
                    return false;
                }

                string storedHash = Encoding.ASCII.GetString(checksumContent).Trim();
                string actualHash = ComputeLegacyHash(payload);
                integrity = string.Equals(storedHash, actualHash, StringComparison.OrdinalIgnoreCase)
                    ? SettingsIntegrity.Valid
                    : SettingsIntegrity.Modified;
                return true;
            }
            catch (SettingsStorageEntryNotFoundException)
            {
                return true;
            }
            catch (Exception exception)
            {
                failure = CreateLoadFailure(
                    SettingsIntegrity.NotChecked,
                    SettingsDataFormat.LegacyPayload,
                    SettingsErrorCode.ReadFailed,
                    "The legacy settings checksum could not be read within the configured budget.",
                    exception);
                return false;
            }
        }

        private static string ComputeLegacyHash(byte[] payload)
        {
            bool containsCarriageReturn = false;
            for (int index = 0; index < payload.Length; index++)
            {
                if (payload[index] == (byte)'\r')
                {
                    containsCarriageReturn = true;
                    break;
                }
            }

            if (!containsCarriageReturn)
            {
                return XxHash64.HashToUInt64(payload)
                    .ToString("X16", CultureInfo.InvariantCulture);
            }

            XxHash64 hash = XxHash64.Create();
            int segmentStart = 0;
            for (int index = 0; index < payload.Length; index++)
            {
                if (payload[index] != (byte)'\r')
                {
                    continue;
                }

                if (index > segmentStart)
                {
                    hash.Append(new ReadOnlySpan<byte>(payload, segmentStart, index - segmentStart));
                }

                hash.Append(LegacyLineFeed);
                if (index + 1 < payload.Length && payload[index + 1] == (byte)'\n')
                {
                    index++;
                }

                segmentStart = index + 1;
            }

            if (segmentStart < payload.Length)
            {
                hash.Append(new ReadOnlySpan<byte>(payload, segmentStart, payload.Length - segmentStart));
            }

            return hash.GetDigest().ToString("X16", CultureInfo.InvariantCulture);
        }

        private SettingsOperationResult ValidateCurrentValue()
        {
            return ValidateCandidate(in _value);
        }

        private SettingsOperationResult ValidateCandidate(in T candidate)
        {
            int version;
            try
            {
                version = _schema.GetVersion(in candidate);
            }
            catch (Exception exception)
            {
                return SettingsOperationResult.Failure(
                    SettingsErrorCode.SchemaVersionMismatch,
                    "The settings schema version could not be read.",
                    exception);
            }

            if (version != _schema.CurrentVersion)
            {
                return SettingsOperationResult.Failure(
                    SettingsErrorCode.SchemaVersionMismatch,
                    $"Settings schema {version} does not match current schema {_schema.CurrentVersion}.");
            }

            SettingsValidationResult validation;
            try
            {
                validation = _schema.Validate(in candidate);
            }
            catch (Exception exception)
            {
                return SettingsOperationResult.Failure(
                    SettingsErrorCode.ValidationFailed,
                    "The settings validator threw an exception.",
                    exception);
            }

            return validation.IsValid
                ? SettingsOperationResult.Success()
                : SettingsOperationResult.Failure(
                    SettingsErrorCode.ValidationFailed,
                    validation.Message);
        }

        private T CreateAndValidateDefaults()
        {
            T defaults = _schema.CreateDefault();
            T ownedDefaults = _schema.Clone(in defaults);
            int defaultVersion = _schema.GetVersion(in ownedDefaults);
            if (defaultVersion != _schema.CurrentVersion)
            {
                throw new ArgumentException(
                    $"Default settings schema {defaultVersion} does not match current schema {_schema.CurrentVersion}.",
                    nameof(_schema));
            }

            SettingsValidationResult validation = _schema.Validate(in ownedDefaults);
            if (!validation.IsValid)
            {
                throw new ArgumentException(
                    $"Default settings are invalid: {validation.Message}",
                    nameof(_schema));
            }

            return ownedDefaults;
        }

        private Exception NotifyChanged(SettingsChangeReason reason)
        {
            SettingsChangedHandler<T>[] observers = _observers;
            if (observers.Length == 0)
            {
                return null;
            }

            Exception firstFailure = null;
            for (int index = 0; index < observers.Length; index++)
            {
                try
                {
                    T snapshot = _schema.Clone(in _value);
                    observers[index](in snapshot, reason);
                }
                catch (Exception exception)
                {
                    if (firstFailure == null)
                    {
                        firstFailure = exception;
                    }
                }
            }

            return firstFailure;
        }

        private void AddObserver(SettingsChangedHandler<T> observer)
        {
            if (observer == null)
            {
                return;
            }

            SettingsChangedHandler<T>[] current = _observers;
            var next = new SettingsChangedHandler<T>[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = observer;
            _observers = next;
        }

        private void RemoveObserver(SettingsChangedHandler<T> observer)
        {
            if (observer == null)
            {
                return;
            }

            SettingsChangedHandler<T>[] current = _observers;
            int removalIndex = -1;
            for (int index = current.Length - 1; index >= 0; index--)
            {
                if (current[index] == observer)
                {
                    removalIndex = index;
                    break;
                }
            }

            if (removalIndex < 0)
            {
                return;
            }

            if (current.Length == 1)
            {
                _observers = EmptyObservers;
                return;
            }

            var next = new SettingsChangedHandler<T>[current.Length - 1];
            if (removalIndex > 0)
            {
                Array.Copy(current, 0, next, 0, removalIndex);
            }

            if (removalIndex < current.Length - 1)
            {
                Array.Copy(
                    current,
                    removalIndex + 1,
                    next,
                    removalIndex,
                    current.Length - removalIndex - 1);
            }

            _observers = next;
        }

        private SettingsLoadResult CreateMissingResult(
            SettingsErrorCode errorCode,
            string message,
            Exception exception)
        {
            return new SettingsLoadResult(
                SettingsLoadStatus.Missing,
                SettingsIntegrity.Missing,
                SettingsDataFormat.None,
                errorCode,
                _schema.CurrentVersion,
                _schema.CurrentVersion,
                false,
                true,
                message,
                exception);
        }

        private SettingsLoadResult LoadDefaultsForMissingFile()
        {
            T defaults;
            try
            {
                defaults = CreateAndValidateDefaults();
            }
            catch (Exception exception)
            {
                return CreateLoadFailure(
                    SettingsIntegrity.Missing,
                    SettingsDataFormat.None,
                    SettingsErrorCode.ValidationFailed,
                    "Default settings could not be created or validated.",
                    exception);
            }

            _value = defaults;
            Exception observerException = NotifyChanged(SettingsChangeReason.ResetToDefaults);
            SettingsLoadResult missingResult = CreateMissingResult(
                observerException == null ? SettingsErrorCode.None : SettingsErrorCode.ObserverFailed,
                observerException == null
                    ? string.Empty
                    : "Defaults were committed, but a settings observer failed.",
                observerException);
            return missingResult;
        }

        private SettingsLoadResult CreateLoadFailure(
            SettingsIntegrity integrity,
            SettingsDataFormat format,
            SettingsErrorCode errorCode,
            string message,
            Exception exception,
            int sourceVersion = -1,
            bool migrationApplied = false)
        {
            return new SettingsLoadResult(
                SettingsLoadStatus.Failed,
                integrity,
                format,
                errorCode,
                sourceVersion,
                _schema.CurrentVersion,
                migrationApplied,
                false,
                message,
                exception);
        }

        private void BeginOperation()
        {
            ThrowIfDisposed();
            if (_operationInProgress)
            {
                throw new InvalidOperationException(
                    "Settings operations cannot be nested or called reentrantly from a Changed observer.");
            }

            _operationInProgress = true;
        }

        private void EndOperation()
        {
            _operationInProgress = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SettingsStore<T>));
            }
        }

        private void ClearBuffer(byte[] buffer)
        {
            if (_options.ClearTemporaryBuffers && buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }
    }
}
