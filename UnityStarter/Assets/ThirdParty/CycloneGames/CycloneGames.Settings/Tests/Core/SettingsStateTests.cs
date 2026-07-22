using System;
using NUnit.Framework;

namespace CycloneGames.Settings.Tests
{
    public sealed class SettingsStateTests
    {
        [Test]
        public void ResultDefaults_AreExplicitlyUninitializedAndNullSafe()
        {
            SettingsValidationResult validation = default;
            SettingsUpdateResult update = default;

            Assert.That(validation.IsInitialized, Is.False);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Message, Is.EqualTo(string.Empty));
            Assert.That(update.IsInitialized, Is.False);
            Assert.That(update.Succeeded, Is.False);
            Assert.That(update.Error, Is.EqualTo(SettingsUpdateError.Uninitialized));
            Assert.That(update.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Constructor_RejectsInvalidDefaults()
        {
            var schema = new MutableClassSchema
            {
                DefaultValue = -1
            };

            Assert.Throws<InvalidOperationException>(() => new SettingsState<MutableSettings>(schema));
        }

        [Test]
        public void Snapshot_ReturnsDeeplyIsolatedClassValue()
        {
            var state = new SettingsState<MutableSettings>(new MutableClassSchema());

            var first = state.Snapshot();
            first.Values[0] = 99;
            var second = state.Snapshot();

            Assert.That(second.Values[0], Is.EqualTo(10));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Values, Is.Not.SameAs(first.Values));
        }

        [Test]
        public void Update_CommitsValidatedStructCandidate()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());

            var result = state.Update((ref ValueSettings candidate) => candidate.Volume = 7);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Committed, Is.True);
            Assert.That(state.Snapshot().Volume, Is.EqualTo(7));
        }

        [Test]
        public void Update_InvalidClassCandidateDoesNotMutateAuthoritativeValue()
        {
            var state = new SettingsState<MutableSettings>(new MutableClassSchema());

            var result = state.Update((ref MutableSettings candidate) => candidate.Values[0] = -1);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsUpdateError.ValidationFailed));
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(10));
        }

        [Test]
        public void Update_RejectsUninitializedValidationResult()
        {
            var schema = new MutableClassSchema();
            var state = new SettingsState<MutableSettings>(schema);
            schema.ReturnUninitializedValidation = true;

            SettingsUpdateResult result = state.Update(
                (ref MutableSettings candidate) => candidate.Values[0] = 20);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsUpdateError.ValidationFailed));
            Assert.That(result.Message, Does.Contain("uninitialized"));
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(10));
        }

        [Test]
        public void Update_CallbackFailureDoesNotCommitPartialMutation()
        {
            var state = new SettingsState<MutableSettings>(new MutableClassSchema());

            var result = state.Update(
                (ref MutableSettings candidate) =>
                {
                    candidate.Values[0] = 25;
                    throw new TestException("expected");
                });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsUpdateError.UpdateCallbackFailed));
            Assert.That(result.Exception, Is.TypeOf<TestException>());
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(10));
        }

        [Test]
        public void Update_InvokesEveryObserverAndReportsFailuresAfterCommit()
        {
            var state = new SettingsState<MutableSettings>(new MutableClassSchema());
            var secondObserverCalled = false;
            var secondObserverValue = 0;

            state.Changed +=
                (in MutableSettings snapshot, SettingsChangeReason reason) =>
                {
                    snapshot.Values[0] = 500;
                    throw new TestException("observer failure");
                };
            state.Changed +=
                (in MutableSettings snapshot, SettingsChangeReason reason) =>
                {
                    secondObserverCalled = true;
                    secondObserverValue = snapshot.Values[0];
                };

            var result = state.Update((ref MutableSettings candidate) => candidate.Values[0] = 20);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Committed, Is.True);
            Assert.That(result.ObserverFailureCount, Is.EqualTo(1));
            Assert.That(result.FirstObserverException, Is.TypeOf<TestException>());
            Assert.That(secondObserverCalled, Is.True);
            Assert.That(secondObserverValue, Is.EqualTo(20));
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(20));
        }

        [Test]
        public void Update_FatalObserverExceptionPropagatesAfterCommit()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            var laterObserverCalled = false;
            state.Changed +=
                (in ValueSettings snapshot, SettingsChangeReason reason) =>
                    throw new AccessViolationException("fatal observer");
            state.Changed +=
                (in ValueSettings snapshot, SettingsChangeReason reason) =>
                    laterObserverCalled = true;

            Assert.Throws<AccessViolationException>(() =>
                state.Update((ref ValueSettings candidate) => candidate.Volume = 6));

            Assert.That(state.Snapshot().Volume, Is.EqualTo(6));
            Assert.That(laterObserverCalled, Is.False);
        }

        [Test]
        public void Update_RejectsReentrantStateOperationWithoutRollingBackCommit()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            Exception reentrantFailure = null;
            var laterObserverCalled = false;

            state.Changed +=
                (in ValueSettings snapshot, SettingsChangeReason reason) =>
                {
                    try
                    {
                        state.Snapshot();
                    }
                    catch (Exception exception)
                    {
                        reentrantFailure = exception;
                        throw;
                    }
                };
            state.Changed +=
                (in ValueSettings snapshot, SettingsChangeReason reason) => laterObserverCalled = true;

            var result = state.Update((ref ValueSettings candidate) => candidate.Volume = 8);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ObserverFailureCount, Is.EqualTo(1));
            Assert.That(reentrantFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(laterObserverCalled, Is.True);
            Assert.That(state.Snapshot().Volume, Is.EqualTo(8));
        }

        [Test]
        public void Update_RejectsNestedUpdateExplicitly()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            Exception nestedFailure = null;

            var result = state.Update(
                (ref ValueSettings candidate) =>
                {
                    try
                    {
                        state.Update((ref ValueSettings nested) => nested.Volume = 9);
                    }
                    catch (Exception exception)
                    {
                        nestedFailure = exception;
                    }

                    candidate.Volume = 6;
                });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(nestedFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(state.Snapshot().Volume, Is.EqualTo(6));
        }

        [Test]
        public void TryApplyLoaded_DoesNotRetainCallerOwnedClassReference()
        {
            var state = new SettingsState<MutableSettings>(new MutableClassSchema());
            var loaded = new MutableSettings(new[] { 40 });

            var result = state.TryApplyLoaded(in loaded, state.Revision);
            loaded.Values[0] = 900;

            Assert.That(result.Succeeded, Is.True);
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(40));
        }

        [Test]
        public void TryApplyLoaded_RejectsStaleRevisionWithoutReplacingState()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            long staleRevision = state.Revision;
            state.Update((ref ValueSettings candidate) => candidate.Volume = 7);
            var loaded = new ValueSettings { Volume = 3 };

            SettingsUpdateResult result = state.TryApplyLoaded(in loaded, staleRevision);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsUpdateError.RevisionConflict));
            Assert.That(state.Snapshot().Volume, Is.EqualTo(7));
        }

        [Test]
        public void SuccessfulCommits_AdvanceRevisionExactlyOnce()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            long initialRevision = state.Revision;

            state.Update((ref ValueSettings candidate) => candidate.Volume = 7);
            long afterUpdate = state.Revision;
            SettingsUpdateResult invalid = state.Update(
                (ref ValueSettings candidate) => candidate.Volume = 99);

            Assert.That(afterUpdate, Is.EqualTo(initialRevision + 1));
            Assert.That(invalid.Succeeded, Is.False);
            Assert.That(state.Revision, Is.EqualTo(afterUpdate));
        }

        [Test]
        public void ResetToDefaults_ReplacesStateAndPublishesReason()
        {
            var state = new SettingsState<ValueSettings>(new ValueSchema());
            var observedReason = SettingsChangeReason.Updated;
            state.Update((ref ValueSettings candidate) => candidate.Volume = 3);
            state.Changed +=
                (in ValueSettings snapshot, SettingsChangeReason reason) => observedReason = reason;

            var result = state.ResetToDefaults();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(state.Snapshot().Volume, Is.EqualTo(10));
            Assert.That(observedReason, Is.EqualTo(SettingsChangeReason.ResetToDefaults));
        }

        [Test]
        public void Update_CommitCloneFailureDoesNotReplaceState()
        {
            var schema = new MutableClassSchema();
            var state = new SettingsState<MutableSettings>(schema);
            schema.ThrowOnCloneNumber = schema.CloneCount + 2;

            var result = state.Update((ref MutableSettings candidate) => candidate.Values[0] = 33);
            schema.ThrowOnCloneNumber = -1;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo(SettingsUpdateError.CommitCloneFailed));
            Assert.That(state.Snapshot().Values[0], Is.EqualTo(10));
        }

        private sealed class MutableSettings
        {
            public MutableSettings(int[] values)
            {
                Values = values;
            }

            public int[] Values { get; }
        }

        private sealed class MutableClassSchema : ISettingsSchema<MutableSettings>
        {
            public int DefaultValue { get; set; } = 10;

            public bool ReturnUninitializedValidation { get; set; }

            public int ThrowOnCloneNumber { get; set; } = -1;

            public int CloneCount { get; private set; }

            public int CurrentVersion => 2;

            public MutableSettings CreateDefault()
            {
                return new MutableSettings(new[] { DefaultValue });
            }

            public MutableSettings Clone(in MutableSettings value)
            {
                CloneCount++;
                if (CloneCount == ThrowOnCloneNumber)
                {
                    throw new TestException("clone failure");
                }

                return new MutableSettings((int[])value.Values.Clone());
            }

            public SettingsValidationResult Validate(in MutableSettings value)
            {
                if (ReturnUninitializedValidation)
                {
                    return default;
                }

                if (value == null || value.Values == null || value.Values.Length != 1)
                {
                    return SettingsValidationResult.Invalid("One value is required.");
                }

                return value.Values[0] >= 0 && value.Values[0] <= 100
                    ? SettingsValidationResult.Valid()
                    : SettingsValidationResult.Invalid("The value must be between zero and one hundred.");
            }
        }

        private struct ValueSettings
        {
            public int Volume;
        }

        private sealed class ValueSchema : ISettingsSchema<ValueSettings>
        {
            public int CurrentVersion => 1;

            public ValueSettings CreateDefault()
            {
                return new ValueSettings { Volume = 10 };
            }

            public ValueSettings Clone(in ValueSettings value)
            {
                return value;
            }

            public SettingsValidationResult Validate(in ValueSettings value)
            {
                return value.Volume >= 0 && value.Volume <= 10
                    ? SettingsValidationResult.Valid()
                    : SettingsValidationResult.Invalid("Volume must be between zero and ten.");
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
