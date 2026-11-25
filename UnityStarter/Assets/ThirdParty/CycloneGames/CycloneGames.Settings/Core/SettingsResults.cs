using System;

namespace CycloneGames.Settings
{
    public readonly struct SettingsValidationResult
    {
        private readonly string _message;

        private SettingsValidationResult(bool isValid, string message)
        {
            IsInitialized = true;
            IsValid = isValid;
            _message = message ?? string.Empty;
        }

        public bool IsInitialized { get; }

        public bool IsValid { get; }

        public string Message => _message ?? string.Empty;

        public static SettingsValidationResult Valid()
        {
            return new SettingsValidationResult(true, string.Empty);
        }

        public static SettingsValidationResult Invalid(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A validation failure requires a non-empty message.", nameof(message));
            }

            return new SettingsValidationResult(false, message);
        }
    }

    public enum SettingsUpdateError
    {
        Uninitialized = 0,
        None = 1,
        DefaultCreationFailed = 2,
        CandidateCloneFailed = 3,
        UpdateCallbackFailed = 4,
        ValidationFailed = 5,
        CommitCloneFailed = 6,
        RevisionConflict = 7
    }

    public readonly struct SettingsUpdateResult
    {
        private readonly string _message;

        private SettingsUpdateResult(
            bool succeeded,
            bool committed,
            SettingsUpdateError error,
            string message,
            Exception exception,
            int observerFailureCount,
            Exception firstObserverException)
        {
            Succeeded = succeeded;
            Committed = committed;
            Error = error;
            _message = message ?? string.Empty;
            Exception = exception;
            ObserverFailureCount = observerFailureCount;
            FirstObserverException = firstObserverException;
        }

        public bool Succeeded { get; }

        public bool Committed { get; }

        public SettingsUpdateError Error { get; }

        public bool IsInitialized => Error != SettingsUpdateError.Uninitialized;

        public string Message => _message ?? string.Empty;

        public Exception Exception { get; }

        public int ObserverFailureCount { get; }

        public Exception FirstObserverException { get; }

        public bool HasObserverWarnings => ObserverFailureCount > 0;

        internal static SettingsUpdateResult Success(
            int observerFailureCount,
            Exception firstObserverException)
        {
            return new SettingsUpdateResult(
                true,
                true,
                SettingsUpdateError.None,
                string.Empty,
                null,
                observerFailureCount,
                firstObserverException);
        }

        internal static SettingsUpdateResult Failure(
            SettingsUpdateError error,
            string message,
            Exception exception = null)
        {
            if (error == SettingsUpdateError.Uninitialized
                || error == SettingsUpdateError.None)
            {
                throw new ArgumentOutOfRangeException(nameof(error));
            }

            return new SettingsUpdateResult(
                false,
                false,
                error,
                message,
                exception,
                0,
                null);
        }
    }
}
