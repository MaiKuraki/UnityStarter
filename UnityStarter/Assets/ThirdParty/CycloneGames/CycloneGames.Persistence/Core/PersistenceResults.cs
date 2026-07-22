using System;

namespace CycloneGames.Persistence
{
    public enum PersistenceErrorCode
    {
        None = 0,
        ReadFailed = 1,
        PayloadTooLarge = 2,
        RecordFormatMismatch = 3,
        UnsupportedRecordVersion = 4,
        MalformedRecord = 5,
        IntegrityCheckFailed = 6,
        CodecMismatch = 7,
        TransformMismatch = 8,
        FutureContentVersion = 9,
        DeserializeFailed = 10,
        SerializationFailed = 11,
        WriteFailed = 12,
        DeleteFailed = 13,
        Cancelled = 14
    }

    public enum PersistenceLoadStatus
    {
        Uninitialized = 0,
        Failed = 1,
        Missing = 2,
        Loaded = 3
    }

    public enum PersistenceOperationStatus
    {
        Uninitialized = 0,
        Succeeded = 1,
        Failed = 2
    }

    /// <summary>
    /// Result returned by a storage adapter. Found transfers ownership of Content to the caller.
    /// </summary>
    public readonly struct PersistenceStorageReadResult
    {
        private PersistenceStorageReadResult(bool isMissing, byte[] content)
        {
            IsMissing = isMissing;
            Content = content;
        }

        public bool IsMissing { get; }

        public byte[] Content { get; }

        public static PersistenceStorageReadResult Missing()
        {
            return new PersistenceStorageReadResult(true, null);
        }

        public static PersistenceStorageReadResult Found(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new PersistenceStorageReadResult(false, content);
        }
    }

    public readonly struct PersistenceOperationResult
    {
        private PersistenceOperationResult(
            PersistenceOperationStatus status,
            PersistenceErrorCode errorCode,
            Exception exception)
        {
            Status = status;
            ErrorCode = errorCode;
            Exception = exception;
        }

        public PersistenceOperationStatus Status { get; }

        public bool IsSuccess => Status == PersistenceOperationStatus.Succeeded;

        public PersistenceErrorCode ErrorCode { get; }

        /// <summary>
        /// Diagnostic exception. Sanitize paths and messages before telemetry or player-facing output.
        /// </summary>
        public Exception Exception { get; }

        public static PersistenceOperationResult Success()
        {
            return new PersistenceOperationResult(
                PersistenceOperationStatus.Succeeded,
                PersistenceErrorCode.None,
                null);
        }

        public static PersistenceOperationResult Failure(
            PersistenceErrorCode errorCode,
            Exception exception = null)
        {
            if (errorCode <= PersistenceErrorCode.None
                || errorCode > PersistenceErrorCode.Cancelled)
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }

            return new PersistenceOperationResult(
                PersistenceOperationStatus.Failed,
                errorCode,
                exception);
        }
    }

    public readonly struct PersistenceLoadResult<T>
    {
        private PersistenceLoadResult(
            PersistenceLoadStatus status,
            T value,
            int contentVersion,
            PersistenceErrorCode errorCode,
            Exception exception)
        {
            Status = status;
            Value = value;
            ContentVersion = contentVersion;
            ErrorCode = errorCode;
            Exception = exception;
        }

        public PersistenceLoadStatus Status { get; }

        public bool IsSuccess => Status == PersistenceLoadStatus.Loaded;

        public bool IsMissing => Status == PersistenceLoadStatus.Missing;

        public T Value { get; }

        public int ContentVersion { get; }

        public PersistenceErrorCode ErrorCode { get; }

        /// <summary>
        /// Diagnostic exception. Sanitize paths and messages before telemetry or player-facing output.
        /// </summary>
        public Exception Exception { get; }

        public static PersistenceLoadResult<T> Loaded(T value, int contentVersion)
        {
            if (contentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contentVersion));
            }

            return new PersistenceLoadResult<T>(
                PersistenceLoadStatus.Loaded,
                value,
                contentVersion,
                PersistenceErrorCode.None,
                null);
        }

        public static PersistenceLoadResult<T> Missing()
        {
            return new PersistenceLoadResult<T>(
                PersistenceLoadStatus.Missing,
                default,
                0,
                PersistenceErrorCode.None,
                null);
        }

        public static PersistenceLoadResult<T> Failure(
            PersistenceErrorCode errorCode,
            Exception exception = null)
        {
            if (errorCode <= PersistenceErrorCode.None
                || errorCode > PersistenceErrorCode.Cancelled)
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }

            return new PersistenceLoadResult<T>(
                PersistenceLoadStatus.Failed,
                default,
                0,
                errorCode,
                exception);
        }
    }
}
