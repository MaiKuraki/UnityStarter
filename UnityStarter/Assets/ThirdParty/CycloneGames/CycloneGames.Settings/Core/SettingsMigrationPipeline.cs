using System;
using System.Threading;

namespace CycloneGames.Settings
{
    /// <summary>
    /// Executes one validated, direct, forward-only migration chain.
    /// </summary>
    public sealed class SettingsMigrationPipeline<T>
    {
        private readonly ISettingsSchema<T> _schema;
        private readonly ISettingsMigration<T>[] _orderedMigrations;
        private readonly int _minimumSupportedVersion;
        private readonly int _currentVersion;
        private int _operationState;

        public SettingsMigrationPipeline(
            ISettingsSchema<T> schema,
            params ISettingsMigration<T>[] migrations)
            : this(schema, 0, migrations)
        {
        }

        public SettingsMigrationPipeline(
            ISettingsSchema<T> schema,
            int minimumSupportedVersion,
            params ISettingsMigration<T>[] migrations)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _currentVersion = schema.CurrentVersion;

            if (_currentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schema),
                    "The current settings version cannot be negative.");
            }

            if (minimumSupportedVersion < 0 || minimumSupportedVersion > _currentVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumSupportedVersion),
                    "The minimum supported version must be between zero and the current version.");
            }

            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            _minimumSupportedVersion = minimumSupportedVersion;
            _orderedMigrations = BuildAndValidateChain(migrations);
        }

        public int MinimumSupportedVersion => _minimumSupportedVersion;

        public int CurrentVersion => _currentVersion;

        public ISettingsSchema<T> Schema => _schema;

        public SettingsMigrationPipelineResult Migrate(
            int sourceVersion,
            ref T candidate,
            CancellationToken cancellationToken = default)
        {
            EnterOperation();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sourceVersion < _minimumSupportedVersion || sourceVersion > _currentVersion)
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.UnsupportedSourceVersion,
                        sourceVersion,
                        _currentVersion,
                        0,
                        "The source settings version is outside the supported migration window.");
                }

                T working;
                try
                {
                    working = _schema.Clone(in candidate);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.CandidateCloneFailed,
                        sourceVersion,
                        _currentVersion,
                        0,
                        "The settings schema failed to isolate the migration candidate.",
                        exception);
                }

                var appliedStepCount = 0;
                for (var version = sourceVersion; version < _currentVersion; version++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var migration = _orderedMigrations[version - _minimumSupportedVersion];
                    SettingsMigrationResult stepResult;
                    try
                    {
                        stepResult = migration.Apply(ref working);
                    }
                    catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                    {
                        return SettingsMigrationPipelineResult.Failure(
                            SettingsMigrationError.StepFailed,
                            sourceVersion,
                            _currentVersion,
                            appliedStepCount,
                            "A settings migration step threw an exception.",
                            exception);
                    }

                    if (!stepResult.IsInitialized)
                    {
                        return SettingsMigrationPipelineResult.Failure(
                            SettingsMigrationError.StepFailed,
                            sourceVersion,
                            _currentVersion,
                            appliedStepCount,
                            "A settings migration step returned an uninitialized result.");
                    }

                    if (!stepResult.Succeeded)
                    {
                        return SettingsMigrationPipelineResult.Failure(
                            SettingsMigrationError.StepFailed,
                            sourceVersion,
                            _currentVersion,
                            appliedStepCount,
                            stepResult.Message,
                            stepResult.Exception);
                    }

                    appliedStepCount++;
                }

                cancellationToken.ThrowIfCancellationRequested();
                T isolatedResult;
                try
                {
                    isolatedResult = _schema.Clone(in working);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.ResultCloneFailed,
                        sourceVersion,
                        _currentVersion,
                        appliedStepCount,
                        "The settings schema failed to isolate the migration result.",
                        exception);
                }

                SettingsValidationResult validation;
                try
                {
                    validation = _schema.Validate(in isolatedResult);
                }
                catch (Exception exception) when (SettingsExceptionPolicy.IsRecoverable(exception))
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.ValidationFailed,
                        sourceVersion,
                        _currentVersion,
                        appliedStepCount,
                        "The settings schema threw while validating the migration result.",
                        exception);
                }

                if (!validation.IsInitialized)
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.ValidationFailed,
                        sourceVersion,
                        _currentVersion,
                        appliedStepCount,
                        "The settings schema returned an uninitialized validation result for the migration result.");
                }

                if (!validation.IsValid)
                {
                    return SettingsMigrationPipelineResult.Failure(
                        SettingsMigrationError.ValidationFailed,
                        sourceVersion,
                        _currentVersion,
                        appliedStepCount,
                        validation.Message);
                }

                cancellationToken.ThrowIfCancellationRequested();
                candidate = isolatedResult;
                return SettingsMigrationPipelineResult.Success(
                    sourceVersion,
                    _currentVersion,
                    appliedStepCount);
            }
            finally
            {
                ExitOperation();
            }
        }

        private ISettingsMigration<T>[] BuildAndValidateChain(
            ISettingsMigration<T>[] migrations)
        {
            var requiredCount = _currentVersion - _minimumSupportedVersion;
            if (migrations.Length != requiredCount)
            {
                throw new ArgumentException(
                    "The migration set must contain exactly one step for every supported source version.",
                    nameof(migrations));
            }

            if (requiredCount == 0)
            {
                return new ISettingsMigration<T>[0];
            }

            var ordered = new ISettingsMigration<T>[requiredCount];
            for (var index = 0; index < migrations.Length; index++)
            {
                var migration = migrations[index];
                if (migration == null)
                {
                    throw new ArgumentException("Migration entries cannot be null.", nameof(migrations));
                }

                var sourceVersion = migration.SourceVersion;
                var targetVersion = migration.TargetVersion;
                if (sourceVersion < _minimumSupportedVersion || sourceVersion >= _currentVersion)
                {
                    throw new ArgumentException(
                        "A migration source version is outside the supported migration window.",
                        nameof(migrations));
                }

                if (targetVersion <= sourceVersion)
                {
                    throw new ArgumentException(
                        "Settings migrations must move forward and cannot target the source version.",
                        nameof(migrations));
                }

                if (targetVersion != sourceVersion + 1)
                {
                    throw new ArgumentException(
                        "Settings migrations must target the immediately following version.",
                        nameof(migrations));
                }

                var slot = sourceVersion - _minimumSupportedVersion;
                if (ordered[slot] != null)
                {
                    throw new ArgumentException(
                        "The migration set contains an ambiguous duplicate source version.",
                        nameof(migrations));
                }

                ordered[slot] = migration;
            }

            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index] == null)
                {
                    throw new ArgumentException(
                        "The migration set contains a gap in the supported version window.",
                        nameof(migrations));
                }
            }

            return ordered;
        }

        private void EnterOperation()
        {
            if (Interlocked.CompareExchange(ref _operationState, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "Concurrent or reentrant settings migrations are not supported.");
            }
        }

        private void ExitOperation()
        {
            Volatile.Write(ref _operationState, 0);
        }
    }
}
