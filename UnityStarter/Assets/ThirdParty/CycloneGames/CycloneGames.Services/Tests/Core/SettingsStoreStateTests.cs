using System;
using NUnit.Framework;

namespace CycloneGames.Services.Tests
{
    [TestFixture]
    public sealed class SettingsStoreStateTests
    {
        [Test]
        public void Load_InvalidCandidate_ReturnsValidationFailureAndPreservesDefaults()
        {
            var storage = new FakeSettingsStorage();
            storage.SetContent(SettingsStoreTestData.Envelope(
                2,
                SettingsStoreTestData.Payload(2, -1)));
            using (SettingsStore<TestSettings> store = CreateStore(storage))
            {
                SettingsLoadResult result = store.Load();

                Assert.That(result.Status, Is.EqualTo(SettingsLoadStatus.Failed));
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ValidationFailed));
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(storage.AtomicWriteCount, Is.Zero);
            }
        }

        [Test]
        public void Update_InvalidCandidate_PreservesPreviousStateAndDoesNotNotify()
        {
            using (SettingsStore<TestSettings> store = CreateStore())
            {
                int notificationCount = 0;
                store.Changed += CountObserver;

                SettingsOperationResult result = store.Update(
                    (ref TestSettings value) => value.Value = -1);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ValidationFailed));
                Assert.That(result.StateChanged, Is.False);
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(notificationCount, Is.Zero);

                void CountObserver(in TestSettings settings, SettingsChangeReason reason)
                {
                    notificationCount++;
                }
            }
        }

        [Test]
        public void Update_CallbackFailure_PreservesPreviousStateAndDoesNotNotify()
        {
            using (SettingsStore<TestSettings> store = CreateStore())
            {
                int notificationCount = 0;
                store.Changed += CountObserver;

                SettingsOperationResult result = store.Update(ThrowingUpdate);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.UpdateCallbackFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(result.StateChanged, Is.False);
                Assert.That(store.Value.Value, Is.EqualTo(7));
                Assert.That(notificationCount, Is.Zero);

                void CountObserver(in TestSettings settings, SettingsChangeReason reason)
                {
                    notificationCount++;
                }
            }
        }

        [Test]
        public void Update_ObserverFailure_ReturnsWarningAfterCommittingState()
        {
            using (SettingsStore<TestSettings> store = CreateStore())
            {
                int successfulObserverCount = 0;
                store.Changed += ThrowingObserver;
                store.Changed += CountSuccessfulObserver;

                SettingsOperationResult result = store.Update(
                    (ref TestSettings value) => value.Value = 41);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.HasWarning, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ObserverFailed));
                Assert.That(result.StateChanged, Is.True);
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(store.Value.Value, Is.EqualTo(41));
                Assert.That(successfulObserverCount, Is.EqualTo(1));

                void CountSuccessfulObserver(
                    in TestSettings settings,
                    SettingsChangeReason reason)
                {
                    successfulObserverCount++;
                }
            }
        }

        [Test]
        public void Update_ReentrantObserverOperation_IsRejectedAndReportedAsObserverFailure()
        {
            using (SettingsStore<TestSettings> store = CreateStore())
            {
                store.Changed += ReenterStore;

                SettingsOperationResult result = store.Update(
                    (ref TestSettings value) => value.Value = 51);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.HasWarning, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ObserverFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(store.Value.Value, Is.EqualTo(51));

                void ReenterStore(in TestSettings settings, SettingsChangeReason reason)
                {
                    store.Update((ref TestSettings value) => value.Value = 99);
                }
            }
        }

        [Test]
        public void ResetToDefaults_CommitsDefaultsAndReportsObserverFailureAsWarning()
        {
            using (SettingsStore<TestSettings> store = CreateStore(defaultValue: 13))
            {
                Assert.That(store.Update(
                    (ref TestSettings value) => value.Value = 88).Succeeded, Is.True);
                store.Changed += ThrowingObserver;

                SettingsOperationResult result = store.ResetToDefaults();

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.StateChanged, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ObserverFailed));
                Assert.That(store.Value.Value, Is.EqualTo(13));
            }
        }

        [Test]
        public void Value_WithMutableReferences_ReturnsIndependentDeepSnapshots()
        {
            using (SettingsStore<MutableSettings> store = CreateMutableStore())
            {
                MutableSettings first = store.Value;
                MutableSettings second = store.Value;

                Assert.That(first.Values, Is.Not.SameAs(second.Values));
                first.Values[0] = 999;

                Assert.That(second.Values[0], Is.EqualTo(1));
                Assert.That(store.Value.Values[0], Is.EqualTo(1));
            }
        }

        [Test]
        public void Update_CallbackFailureAfterMutatingReference_PreservesStoredValue()
        {
            using (SettingsStore<MutableSettings> store = CreateMutableStore())
            {
                int[] failedCandidateValues = null;

                SettingsOperationResult result = store.Update(
                    (ref MutableSettings value) =>
                    {
                        failedCandidateValues = value.Values;
                        value.Values[0] = 999;
                        throw new InvalidOperationException("Injected mutable update failure.");
                    });

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.UpdateCallbackFailed));
                Assert.That(store.Value.Values[0], Is.EqualTo(1));

                failedCandidateValues[1] = 888;
                Assert.That(store.Value.Values[1], Is.EqualTo(2));
            }
        }

        [Test]
        public void Update_InvalidMutableCandidate_PreservesStoredValue()
        {
            using (SettingsStore<MutableSettings> store = CreateMutableStore())
            {
                SettingsOperationResult result = store.Update(
                    (ref MutableSettings value) => value.Values[0] = -1);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ValidationFailed));
                Assert.That(store.Value.Values[0], Is.EqualTo(1));
            }
        }

        [Test]
        public void Update_SuccessfulCallbackRetainedReference_CannotMutateStoredValue()
        {
            using (SettingsStore<MutableSettings> store = CreateMutableStore())
            {
                int[] retainedCandidateValues = null;

                SettingsOperationResult result = store.Update(
                    (ref MutableSettings value) =>
                    {
                        value.Values[0] = 42;
                        retainedCandidateValues = value.Values;
                    });

                Assert.That(result.Succeeded, Is.True);
                Assert.That(store.Value.Values[0], Is.EqualTo(42));

                retainedCandidateValues[0] = 999;
                Assert.That(store.Value.Values[0], Is.EqualTo(42));
            }
        }

        [Test]
        public void Update_MutableObserverSnapshots_AreIsolatedFromEachOtherAndStore()
        {
            using (SettingsStore<MutableSettings> store = CreateMutableStore())
            {
                int firstObserverValueBeforeMutation = 0;
                int secondObserverValue = 0;
                int[] firstObserverSnapshot = null;
                int[] secondObserverSnapshot = null;
                store.Changed += MutateFirstSnapshot;
                store.Changed += ReadSecondSnapshot;

                SettingsOperationResult result = store.Update(
                    (ref MutableSettings value) => value.Values[0] = 42);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(firstObserverValueBeforeMutation, Is.EqualTo(42));
                Assert.That(secondObserverValue, Is.EqualTo(42));
                Assert.That(firstObserverSnapshot[0], Is.EqualTo(999));
                Assert.That(secondObserverSnapshot[0], Is.EqualTo(42));
                Assert.That(firstObserverSnapshot, Is.Not.SameAs(secondObserverSnapshot));
                Assert.That(store.Value.Values[0], Is.EqualTo(42));

                void MutateFirstSnapshot(
                    in MutableSettings settings,
                    SettingsChangeReason reason)
                {
                    firstObserverSnapshot = settings.Values;
                    firstObserverValueBeforeMutation = settings.Values[0];
                    settings.Values[0] = 999;
                }

                void ReadSecondSnapshot(
                    in MutableSettings settings,
                    SettingsChangeReason reason)
                {
                    secondObserverSnapshot = settings.Values;
                    secondObserverValue = settings.Values[0];
                }
            }
        }

        [Test]
        public void Dispose_IsIdempotentAndRejectsSubsequentOperations()
        {
            var store = CreateStore();

            store.Dispose();
            Assert.DoesNotThrow(store.Dispose);

            Assert.Throws<ObjectDisposedException>(() => ReadValue(store));
            Assert.Throws<ObjectDisposedException>(() => store.Load());
            Assert.Throws<ObjectDisposedException>(() => store.Save());
            Assert.Throws<ObjectDisposedException>(() => store.ResetToDefaults());
            Assert.Throws<ObjectDisposedException>(() => store.Update(NoOpUpdate));
            Assert.Throws<ObjectDisposedException>(() => store.Changed += NoOpObserver);
            Assert.DoesNotThrow(() => store.Changed -= NoOpObserver);
        }

        [Test]
        public void Dispose_FromObserver_IsRejectedWithoutRollingBackCommittedState()
        {
            using (SettingsStore<TestSettings> store = CreateStore())
            {
                store.Changed += DisposeStore;

                SettingsOperationResult result = store.Update(
                    (ref TestSettings value) => value.Value = 61);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(SettingsErrorCode.ObserverFailed));
                Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(store.Value.Value, Is.EqualTo(61));

                void DisposeStore(in TestSettings settings, SettingsChangeReason reason)
                {
                    store.Dispose();
                }
            }
        }

        private static SettingsStore<TestSettings> CreateStore(int defaultValue = 7)
        {
            return CreateStore(new FakeSettingsStorage(), defaultValue);
        }

        private static SettingsStore<TestSettings> CreateStore(
            FakeSettingsStorage storage,
            int defaultValue = 7)
        {
            return new SettingsStore<TestSettings>(
                storage,
                new TestSettingsCodec(),
                new TestSettingsSchema(defaultValue: defaultValue));
        }

        private static SettingsStore<MutableSettings> CreateMutableStore()
        {
            return new SettingsStore<MutableSettings>(
                new FakeSettingsStorage(),
                new MutableSettingsCodec(),
                new MutableSettingsSchema(1, 2, 3));
        }

        private static void ThrowingUpdate(ref TestSettings settings)
        {
            settings.Value = 99;
            throw new InvalidOperationException("Injected update failure.");
        }

        private static void NoOpUpdate(ref TestSettings settings)
        {
        }

        private static void ThrowingObserver(
            in TestSettings settings,
            SettingsChangeReason reason)
        {
            throw new InvalidOperationException("Injected observer failure.");
        }

        private static void NoOpObserver(
            in TestSettings settings,
            SettingsChangeReason reason)
        {
        }

        private static int ReadValue(SettingsStore<TestSettings> store)
        {
            return store.Value.Value;
        }
    }
}
