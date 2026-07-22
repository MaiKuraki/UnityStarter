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

    public readonly struct SettingsMigrationResult
    {
        private readonly string _message;

        private SettingsMigrationResult(bool succeeded, string message, Exception exception)
        {
            IsInitialized = true;
            Succeeded = succeeded;
            _message = message ?? string.Empty;
            Exception = exception;
        }

        public bool IsInitialized { get; }

        public bool Succeeded { get; }

        public string Message => _message ?? string.Empty;

        public Exception Exception { get; }

        public static SettingsMigrationResult Success()
        {
            return new SettingsMigrationResult(true, string.Empty, null);
        }

        public static SettingsMigrationResult Failure(string message, Exception exception = null)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A migration failure requires a non-empty message.", nameof(message));
            }

            return new SettingsMigrationResult(false, message, exception);
        }
    }

    public enum SettingsMigrationError
    {
        Uninitialized = 0,
        None = 1,
        UnsupportedSourceVersion = 2,
        CandidateCloneFailed = 3,
        StepFailed = 4,
        ValidationFailed = 5,
        ResultCloneFailed = 6
    }

    public readonly struct SettingsMigrationPipelineResult
    {
        private readonly string _message;

        private SettingsMigrationPipelineResult(
            bool succeeded,
            SettingsMigrationError error,
            int sourceVersion,
            int targetVersion,
            int appliedStepCount,
            string message,
            Exception exception)
        {
            Succeeded = succeeded;
            Error = error;
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;
            AppliedStepCount = appliedStepCount;
            _message = message ?? string.Empty;
            Exception = exception;
        }

        public bool Succeeded { get; }

        public SettingsMigrationError Error { get; }

        public bool IsInitialized => Error != SettingsMigrationError.Uninitialized;

        public int SourceVersion { get; }

        public int TargetVersion { get; }

        public int AppliedStepCount { get; }

        public bool MigrationApplied => Succeeded && AppliedStepCount > 0;

        public string Message => _message ?? string.Empty;

        public Exception Exception { get; }

        internal static SettingsMigrationPipelineResult Success(
            int sourceVersion,
            int targetVersion,
            int appliedStepCount)
        {
            return new SettingsMigrationPipelineResult(
                true,
                SettingsMigrationError.None,
                sourceVersion,
                targetVersion,
                appliedStepCount,
                string.Empty,
                null);
        }

        internal static SettingsMigrationPipelineResult Failure(
            SettingsMigrationError error,
            int sourceVersion,
            int targetVersion,
            int appliedStepCount,
            string message,
            Exception exception = null)
        {
            if (error == SettingsMigrationError.Uninitialized
                || error == SettingsMigrationError.None)
            {
                throw new ArgumentOutOfRangeException(nameof(error));
            }

            return new SettingsMigrationPipelineResult(
                false,
                error,
                sourceVersion,
                targetVersion,
                appliedStepCount,
                message,
                exception);
        }
    }
}
