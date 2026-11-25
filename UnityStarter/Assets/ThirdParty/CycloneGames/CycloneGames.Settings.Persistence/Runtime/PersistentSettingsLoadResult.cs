using System;
using CycloneGames.Persistence;

namespace CycloneGames.Settings.Persistence
{
    public enum PersistentSettingsLoadStatus
    {
        Uninitialized = 0,
        Failed = 1,
        Missing = 2,
        Loaded = 3
    }

    public enum PersistentSettingsLoadError
    {
        None = 0,
        PersistenceFailed = 1,
        StateCommitFailed = 2
    }

    public readonly struct PersistentSettingsLoadResult
    {
        private readonly string _message;

        private PersistentSettingsLoadResult(
            PersistentSettingsLoadStatus status,
            PersistentSettingsLoadError error,
            PersistenceErrorCode persistenceError,
            SettingsUpdateError stateError,
            string message,
            Exception exception,
            int observerFailureCount,
            Exception firstObserverException)
        {
            Status = status;
            Error = error;
            PersistenceError = persistenceError;
            StateError = stateError;
            _message = message ?? string.Empty;
            Exception = exception;
            ObserverFailureCount = observerFailureCount;
            FirstObserverException = firstObserverException;
        }

        public PersistentSettingsLoadStatus Status { get; }

        public PersistentSettingsLoadError Error { get; }

        public bool Completed => Status == PersistentSettingsLoadStatus.Missing
            || Status == PersistentSettingsLoadStatus.Loaded;

        public bool IsLoaded => Status == PersistentSettingsLoadStatus.Loaded;

        public bool IsMissing => Status == PersistentSettingsLoadStatus.Missing;

        public PersistenceErrorCode PersistenceError { get; }

        public SettingsUpdateError StateError { get; }

        public string Message => _message ?? string.Empty;

        public Exception Exception { get; }

        public int ObserverFailureCount { get; }

        public Exception FirstObserverException { get; }

        public bool HasObserverWarnings => ObserverFailureCount > 0;

        internal static PersistentSettingsLoadResult Missing()
        {
            return new PersistentSettingsLoadResult(
                PersistentSettingsLoadStatus.Missing,
                PersistentSettingsLoadError.None,
                PersistenceErrorCode.None,
                SettingsUpdateError.Uninitialized,
                string.Empty,
                null,
                0,
                null);
        }

        internal static PersistentSettingsLoadResult Loaded(
            int observerFailureCount,
            Exception firstObserverException)
        {
            return new PersistentSettingsLoadResult(
                PersistentSettingsLoadStatus.Loaded,
                PersistentSettingsLoadError.None,
                PersistenceErrorCode.None,
                SettingsUpdateError.Uninitialized,
                string.Empty,
                null,
                observerFailureCount,
                firstObserverException);
        }

        internal static PersistentSettingsLoadResult PersistenceFailure(
            PersistenceErrorCode error,
            Exception exception)
        {
            return new PersistentSettingsLoadResult(
                PersistentSettingsLoadStatus.Failed,
                PersistentSettingsLoadError.PersistenceFailed,
                error,
                SettingsUpdateError.Uninitialized,
                string.Empty,
                exception,
                0,
                null);
        }

        internal static PersistentSettingsLoadResult StateCommitFailure(
            in SettingsUpdateResult result)
        {
            return new PersistentSettingsLoadResult(
                PersistentSettingsLoadStatus.Failed,
                PersistentSettingsLoadError.StateCommitFailed,
                PersistenceErrorCode.None,
                result.Error,
                result.Message,
                result.Exception,
                0,
                null);
        }
    }
}
