using System;

namespace CycloneGames.Persistence
{
    public enum PersistenceErrorCode
    {
        None = 0,
        ReadFailed = 1,
        PayloadTooLarge = 2,
        RecordFormatMismatch = 3,
        MalformedRecord = 4,
        IntegrityCheckFailed = 5,
        DeserializeFailed = 6,
        SerializationFailed = 7,
        WriteFailed = 8,
        DeleteFailed = 9,
        Cancelled = 10
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
            PersistenceErrorCode errorCode,
            Exception exception)
        {
            Status = status;
            Value = value;
            ErrorCode = errorCode;
            Exception = exception;
        }

        public PersistenceLoadStatus Status { get; }

        public bool IsSuccess => Status == PersistenceLoadStatus.Loaded;

        public bool IsMissing => Status == PersistenceLoadStatus.Missing;

        public T Value { get; }

        public PersistenceErrorCode ErrorCode { get; }

        public Exception Exception { get; }

        public static PersistenceLoadResult<T> Loaded(T value)
        {
            return new PersistenceLoadResult<T>(
                PersistenceLoadStatus.Loaded,
                value,
                PersistenceErrorCode.None,
                null);
        }

        public static PersistenceLoadResult<T> Missing()
        {
            return new PersistenceLoadResult<T>(
                PersistenceLoadStatus.Missing,
                default,
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
                errorCode,
                exception);
        }
    }
}
