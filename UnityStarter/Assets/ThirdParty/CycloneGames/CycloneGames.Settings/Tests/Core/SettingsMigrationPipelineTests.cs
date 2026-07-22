using System;
using NUnit.Framework;

namespace CycloneGames.Settings.Tests
{
    public sealed class SettingsMigrationPipelineTests
    {
        [Test]
        public void ResultDefaults_AreExplicitlyUninitializedAndNullSafe()
        {
            SettingsMigrationResult step = default;
            SettingsMigrationPipelineResult pipeline = default;

            Assert.That(step.IsInitialized, Is.False);
            Assert.That(step.Succeeded, Is.False);
            Assert.That(step.Message, Is.EqualTo(string.Empty));
            Assert.That(pipeline.IsInitialized, Is.False);
            Assert.That(pipeline.Succeeded, Is.False);
            Assert.That(pipeline.Error, Is.EqualTo(SettingsMigrationError.Uninitialized));
            Assert.That(pipeline.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Constructor_OrdersACompleteForwardChain()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(3),
                new AddMigration(2, 3, 4),
                new AddMigration(0, 1, 1),
                new AddMigration(1, 2, 2));
            var candidate = new MigrationSettings(0);

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.AppliedStepCount, Is.EqualTo(3));
            Assert.That(candidate.Value, Is.EqualTo(7));
        }

        [Test]
        public void Constructor_RejectsDuplicateOrAmbiguousSource()
        {
            Assert.Throws<ArgumentException>(
                () => new SettingsMigrationPipeline<MigrationSettings>(
                    new MigrationSchema(2),
                    new AddMigration(0, 1, 1),
                    new AddMigration(0, 1, 2)));
        }

        [Test]
        public void Constructor_RejectsMissingStepGap()
        {
            Assert.Throws<ArgumentException>(
                () => new SettingsMigrationPipeline<MigrationSettings>(
                    new MigrationSchema(3),
                    new AddMigration(0, 1, 1),
                    new AddMigration(2, 3, 1)));
        }

        [Test]
        public void Constructor_RejectsSelfMigration()
        {
            Assert.Throws<ArgumentException>(
                () => new SettingsMigrationPipeline<MigrationSettings>(
                    new MigrationSchema(1),
                    new AddMigration(0, 0, 1)));
        }

        [Test]
        public void Constructor_RejectsBackwardMigration()
        {
            Assert.Throws<ArgumentException>(
                () => new SettingsMigrationPipeline<MigrationSettings>(
                    new MigrationSchema(2),
                    1,
                    new AddMigration(1, 0, 1)));
        }

        [Test]
        public void Constructor_RejectsVersionJump()
        {
            Assert.Throws<ArgumentException>(
                () => new SettingsMigrationPipeline<MigrationSettings>(
                    new MigrationSchema(2),
                    new AddMigration(0, 2, 1),
                    new AddMigration(1, 2, 1)));
        }

        [Test]
        public void Constructor_AllowsExplicitSupportedVersionWindow()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(5),
                3,
                new AddMigration(3, 4, 2),
                new AddMigration(4, 5, 3));
            var candidate = new MigrationSettings(1);

            var result = pipeline.Migrate(3, ref candidate);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(candidate.Value, Is.EqualTo(6));
        }

        [Test]
        public void Migrate_UnsupportedSourceLeavesCandidateUnchanged()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(2),
                1,
                new AddMigration(1, 2, 1));
            var candidate = new MigrationSettings(8);

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsMigrationError.UnsupportedSourceVersion));
            Assert.That(candidate.Value, Is.EqualTo(8));
        }

        [Test]
        public void Migrate_StepFailureLeavesCallerOwnedClassUnchanged()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(2),
                new AddMigration(0, 1, 5),
                new FailingMigration(1, 2));
            var candidate = new MigrationSettings(10);

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsMigrationError.StepFailed));
            Assert.That(result.AppliedStepCount, Is.EqualTo(1));
            Assert.That(candidate.Value, Is.EqualTo(10));
        }

        [Test]
        public void Migrate_ThrownStepLeavesCallerOwnedClassUnchanged()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(1),
                new ThrowingMigration(0, 1));
            var candidate = new MigrationSettings(10);

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Exception, Is.TypeOf<TestException>());
            Assert.That(candidate.Value, Is.EqualTo(10));
        }

        [Test]
        public void Migrate_UninitializedStepResultLeavesCallerOwnedClassUnchanged()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(1),
                new UninitializedMigration(0, 1));
            var candidate = new MigrationSettings(10);

            SettingsMigrationPipelineResult result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsMigrationError.StepFailed));
            Assert.That(result.Message, Does.Contain("uninitialized"));
            Assert.That(candidate.Value, Is.EqualTo(10));
        }

        [Test]
        public void Migrate_FinalValidationFailureLeavesCandidateUnchanged()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(1),
                new AddMigration(0, 1, 200));
            var candidate = new MigrationSettings(5);

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsMigrationError.ValidationFailed));
            Assert.That(result.AppliedStepCount, Is.EqualTo(1));
            Assert.That(result.MigrationApplied, Is.False);
            Assert.That(candidate.Value, Is.EqualTo(5));
        }

        [Test]
        public void Migrate_CurrentVersionStillClonesAndValidatesCandidate()
        {
            var pipeline = new SettingsMigrationPipeline<MigrationSettings>(
                new MigrationSchema(0));
            var candidate = new MigrationSettings(15);
            var original = candidate;

            var result = pipeline.Migrate(0, ref candidate);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.AppliedStepCount, Is.Zero);
            Assert.That(candidate.Value, Is.EqualTo(15));
            Assert.That(candidate, Is.Not.SameAs(original));
        }

        private sealed class MigrationSettings
        {
            public MigrationSettings(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        private sealed class MigrationSchema : ISettingsSchema<MigrationSettings>
        {
            private readonly int _currentVersion;

            public MigrationSchema(int currentVersion)
            {
                _currentVersion = currentVersion;
            }

            public int CurrentVersion => _currentVersion;

            public MigrationSettings CreateDefault()
            {
                return new MigrationSettings(0);
            }

            public MigrationSettings Clone(in MigrationSettings value)
            {
                return new MigrationSettings(value.Value);
            }

            public SettingsValidationResult Validate(in MigrationSettings value)
            {
                return value != null && value.Value >= 0 && value.Value <= 100
                    ? SettingsValidationResult.Valid()
                    : SettingsValidationResult.Invalid("Value must be between zero and one hundred.");
            }
        }

        private sealed class AddMigration : ISettingsMigration<MigrationSettings>
        {
            private readonly int _amount;

            public AddMigration(int sourceVersion, int targetVersion, int amount)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
                _amount = amount;
            }

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public SettingsMigrationResult Apply(ref MigrationSettings candidate)
            {
                candidate.Value += _amount;
                return SettingsMigrationResult.Success();
            }
        }

        private sealed class FailingMigration : ISettingsMigration<MigrationSettings>
        {
            public FailingMigration(int sourceVersion, int targetVersion)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
            }

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public SettingsMigrationResult Apply(ref MigrationSettings candidate)
            {
                candidate.Value += 50;
                return SettingsMigrationResult.Failure("expected failure");
            }
        }

        private sealed class ThrowingMigration : ISettingsMigration<MigrationSettings>
        {
            public ThrowingMigration(int sourceVersion, int targetVersion)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
            }

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public SettingsMigrationResult Apply(ref MigrationSettings candidate)
            {
                candidate.Value += 50;
                throw new TestException("expected");
            }
        }

        private sealed class UninitializedMigration : ISettingsMigration<MigrationSettings>
        {
            public UninitializedMigration(int sourceVersion, int targetVersion)
            {
                SourceVersion = sourceVersion;
                TargetVersion = targetVersion;
            }

            public int SourceVersion { get; }

            public int TargetVersion { get; }

            public SettingsMigrationResult Apply(ref MigrationSettings candidate)
            {
                candidate.Value += 50;
                return default;
            }
        }

        private sealed class TestException : Exception
        {
            public TestException(string message)
                : base(message)
            {
            }
        }
    }
}
