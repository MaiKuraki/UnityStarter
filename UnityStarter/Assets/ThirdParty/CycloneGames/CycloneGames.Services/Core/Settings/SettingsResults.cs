using System;

namespace CycloneGames.Services
{
    public enum SettingsIntegrity
    {
        NotChecked = 0,
        Valid = 1,
        Missing = 2,
        Modified = 3,
        Corrupted = 4
    }

    public enum SettingsDataFormat
    {
        None = 0,
        EnvelopeV1 = 1,
        LegacyPayload = 2
    }

    public enum SettingsLoadStatus
    {
        Loaded = 0,
        Missing = 1,
        Failed = 2
    }

    public enum SettingsErrorCode
    {
        None = 0,
        ReadFailed = 1,
        PayloadTooLarge = 2,
        UnsupportedFormat = 3,
        CorruptedEnvelope = 4,
        DeserializeFailed = 5,
        SchemaVersionMismatch = 6,
        FutureSchemaVersion = 7,
        MigrationFailed = 8,
        ValidationFailed = 9,
        SerializationFailed = 10,
        WriteFailed = 11,
        UpdateCallbackFailed = 12,
        ObserverFailed = 13,
        SnapshotFailed = 14,
        LegacyCleanupFailed = 15,
        IntegrityCheckFailed = 16
    }

    public readonly struct SettingsValidationResult
    {
        private SettingsValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        public bool IsValid { get; }

        public string Message { get; }

        public static SettingsValidationResult Valid()
        {
            return new SettingsValidationResult(true, string.Empty);
        }

        public static SettingsValidationResult Invalid(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A validation failure message is required.", nameof(message));
            }

            return new SettingsValidationResult(false, message);
        }
    }

    public readonly struct SettingsMigrationResult
    {
        private SettingsMigrationResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        public static SettingsMigrationResult Success()
        {
            return new SettingsMigrationResult(true, string.Empty);
        }

        public static SettingsMigrationResult Failure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A migration failure message is required.", nameof(message));
            }

            return new SettingsMigrationResult(false, message);
        }
    }

    public readonly struct SettingsLoadResult
    {
        internal SettingsLoadResult(
            SettingsLoadStatus status,
            SettingsIntegrity integrity,
            SettingsDataFormat format,
            SettingsErrorCode errorCode,
            int sourceVersion,
            int targetVersion,
            bool migrationApplied,
            bool requiresSave,
            string message,
            Exception exception)
        {
            Status = status;
            Integrity = integrity;
            Format = format;
            ErrorCode = errorCode;
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;
            MigrationApplied = migrationApplied;
            RequiresSave = requiresSave;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public SettingsLoadStatus Status { get; }

        public SettingsIntegrity Integrity { get; }

        public SettingsDataFormat Format { get; }

        public SettingsErrorCode ErrorCode { get; }

        public int SourceVersion { get; }

        public int TargetVersion { get; }

        public bool MigrationApplied { get; }

        public bool RequiresSave { get; }

        public string Message { get; }

        public Exception Exception { get; }

        public bool Succeeded => Status != SettingsLoadStatus.Failed;

        public bool LoadedFromStorage => Status == SettingsLoadStatus.Loaded;

        public bool HasWarning => Succeeded
            && (ErrorCode != SettingsErrorCode.None || Integrity == SettingsIntegrity.Modified);
    }

    public readonly struct SettingsOperationResult
    {
        internal SettingsOperationResult(
            bool succeeded,
            bool stateChanged,
            SettingsErrorCode errorCode,
            string message,
            Exception exception)
        {
            Succeeded = succeeded;
            StateChanged = stateChanged;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public bool Succeeded { get; }

        public bool StateChanged { get; }

        public SettingsErrorCode ErrorCode { get; }

        public string Message { get; }

        public Exception Exception { get; }

        public bool HasWarning => Succeeded && ErrorCode != SettingsErrorCode.None;

        internal static SettingsOperationResult Success(bool stateChanged = false)
        {
            return new SettingsOperationResult(
                true,
                stateChanged,
                SettingsErrorCode.None,
                string.Empty,
                null);
        }

        internal static SettingsOperationResult Warning(
            bool stateChanged,
            SettingsErrorCode errorCode,
            string message,
            Exception exception)
        {
            return new SettingsOperationResult(
                true,
                stateChanged,
                errorCode,
                message,
                exception);
        }

        internal static SettingsOperationResult Failure(
            SettingsErrorCode errorCode,
            string message,
            Exception exception = null)
        {
            return new SettingsOperationResult(
                false,
                false,
                errorCode,
                message,
                exception);
        }
    }
}
