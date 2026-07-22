using System;
using System.Threading;

namespace CycloneGames.Settings
{
    /// <summary>
    /// Owns one validated settings value. Concurrent and reentrant operations are rejected.
    /// </summary>
    public sealed class SettingsState<T>
    {
        private static readonly SettingsChangedHandler<T>[] EmptyObservers =
            new SettingsChangedHandler<T>[0];

        private readonly ISettingsSchema<T> _schema;
        private readonly int _currentVersion;
        private SettingsChangedHandler<T>[] _observers;
        private T _value;
        private long _revision;
        private int _operationState;

        public SettingsState(ISettingsSchema<T> schema)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _currentVersion = schema.CurrentVersion;

            if (_currentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schema),
                    "The current settings version cannot be negative.");
            }

            _observers = EmptyObservers;
            _value = CreateValidatedDefaultOrThrow();
        }

        public int CurrentVersion => _currentVersion;

        public ISettingsSchema<T> Schema => _schema;

        /// <summary>
        /// Monotonic commit revision used to reject stale asynchronous candidates.
        /// </summary>
        public long Revision => Interlocked.Read(ref _revision);

        public event SettingsChangedHandler<T> Changed
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                EnterOperation();
                try
                {
                    var expanded = new SettingsChangedHandler<T>[_observers.Length + 1];
                    Array.Copy(_observers, expanded, _observers.Length);
                    expanded[expanded.Length - 1] = value;
                    _observers = expanded;
                }
                finally
                {
                    ExitOperation();
                }
            }
            remove
            {
                if (value == null)
                {
                    return;
                }

                EnterOperation();
                try
                {
                    var index = Array.LastIndexOf(_observers, value);
                    if (index < 0)
                    {
                        return;
                    }

                    if (_observers.Length == 1)
                    {
                        _observers = EmptyObservers;
                        return;
                    }

                    var reduced = new SettingsChangedHandler<T>[_observers.Length - 1];
                    if (index > 0)
                    {
                        Array.Copy(_observers, 0, reduced, 0, index);
                    }

                    if (index < _observers.Length - 1)
                    {
                        Array.Copy(
                            _observers,
                            index + 1,
                            reduced,
                            index,
                            _observers.Length - index - 1);
                    }

                    _observers = reduced;
                }
                finally
                {
                    ExitOperation();
                }
            }
        }

        public T Snapshot()
        {
            EnterOperation();
            try
            {
                return CloneOrThrow(in _value, "The settings schema failed to clone a snapshot.");
            }
            finally
            {
                ExitOperation();
            }
        }

        public SettingsUpdateResult Update(SettingsRefAction<T> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            EnterOperation();
            try
            {
                T candidate;
                try
                {
                    candidate = _schema.Clone(in _value);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsUpdateResult.Failure(
                        SettingsUpdateError.CandidateCloneFailed,
                        "The settings schema failed to clone an update candidate.",
                        exception);
                }

                try
                {
                    update(ref candidate);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsUpdateResult.Failure(
                        SettingsUpdateError.UpdateCallbackFailed,
                        "The settings update callback failed.",
                        exception);
                }

                return ValidateAndCommit(in candidate, SettingsChangeReason.Updated);
            }
            finally
            {
                ExitOperation();
            }
        }

        public SettingsUpdateResult ResetToDefaults()
        {
            EnterOperation();
            try
            {
                T candidate;
                try
                {
                    candidate = _schema.CreateDefault();
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsUpdateResult.Failure(
                        SettingsUpdateError.DefaultCreationFailed,
                        "The settings schema failed to create default settings.",
                        exception);
                }

                return ValidateAndCommit(in candidate, SettingsChangeReason.ResetToDefaults);
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <summary>
        /// Validates and commits a value decoded by an external persistence boundary.
        /// </summary>
        public SettingsUpdateResult TryApplyLoaded(in T value, long expectedRevision)
        {
            if (expectedRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            }

            if (!TryEnterOperation())
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.RevisionConflict,
                    "The settings state is being modified by another operation.");
            }

            try
            {
                if (_revision != expectedRevision)
                {
                    return SettingsUpdateResult.Failure(
                        SettingsUpdateError.RevisionConflict,
                        "The loaded settings candidate is stale because the state changed while loading.");
                }

                T candidate;
                try
                {
                    candidate = _schema.Clone(in value);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsUpdateResult.Failure(
                        SettingsUpdateError.CandidateCloneFailed,
                        "The settings schema failed to clone a loaded candidate.",
                        exception);
                }

                return ValidateAndCommit(in candidate, SettingsChangeReason.Loaded);
            }
            finally
            {
                ExitOperation();
            }
        }

        private T CreateValidatedDefaultOrThrow()
        {
            T defaults;
            try
            {
                defaults = _schema.CreateDefault();
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                throw new InvalidOperationException(
                    "The settings schema failed to create its initial default value.",
                    exception);
            }

            T isolatedDefaults;
            try
            {
                isolatedDefaults = _schema.Clone(in defaults);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                throw new InvalidOperationException(
                    "The settings schema failed to clone its initial default value.",
                    exception);
            }

            SettingsValidationResult validation;
            try
            {
                validation = _schema.Validate(in isolatedDefaults);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                throw new InvalidOperationException(
                    "The settings schema threw while validating its initial default value.",
                    exception);
            }

            if (!validation.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The settings schema returned an uninitialized validation result for its initial default value.");
            }

            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    "The settings schema produced invalid defaults: " + validation.Message);
            }

            return isolatedDefaults;
        }

        private SettingsUpdateResult ValidateAndCommit(
            in T candidate,
            SettingsChangeReason reason)
        {
            SettingsValidationResult validation;
            try
            {
                validation = _schema.Validate(in candidate);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    "The settings schema threw while validating the candidate.",
                    exception);
            }

            if (!validation.IsInitialized)
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    "The settings schema returned an uninitialized validation result for the candidate.");
            }

            if (!validation.IsValid)
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    validation.Message);
            }

            T committed;
            try
            {
                committed = _schema.Clone(in candidate);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.CommitCloneFailed,
                    "The settings schema failed to isolate the committed value.",
                    exception);
            }

            try
            {
                validation = _schema.Validate(in committed);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    "The settings schema threw while validating the isolated commit value.",
                    exception);
            }

            if (!validation.IsInitialized)
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    "The settings schema returned an uninitialized validation result for the isolated commit value.");
            }

            if (!validation.IsValid)
            {
                return SettingsUpdateResult.Failure(
                    SettingsUpdateError.ValidationFailed,
                    "The settings schema clone produced an invalid value: " + validation.Message);
            }

            long nextRevision = checked(_revision + 1);
            _value = committed;
            _revision = nextRevision;
            NotifyObservers(reason, out var failureCount, out var firstException);
            return SettingsUpdateResult.Success(failureCount, firstException);
        }

        private void NotifyObservers(
            SettingsChangeReason reason,
            out int failureCount,
            out Exception firstException)
        {
            failureCount = 0;
            firstException = null;

            for (var index = 0; index < _observers.Length; index++)
            {
                try
                {
                    var observerSnapshot = _schema.Clone(in _value);
                    _observers[index](in observerSnapshot, reason);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    failureCount++;
                    if (firstException == null)
                    {
                        firstException = exception;
                    }
                }
            }
        }

        private T CloneOrThrow(in T value, string message)
        {
            try
            {
                return _schema.Clone(in value);
            }
            catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
            {
                throw new InvalidOperationException(message, exception);
            }
        }

        private void EnterOperation()
        {
            if (!TryEnterOperation())
            {
                throw new InvalidOperationException(
                    "Concurrent or reentrant settings operations are not supported.");
            }
        }

        private bool TryEnterOperation()
        {
            return Interlocked.CompareExchange(ref _operationState, 1, 0) == 0;
        }

        private void ExitOperation()
        {
            Volatile.Write(ref _operationState, 0);
        }
    }
}
