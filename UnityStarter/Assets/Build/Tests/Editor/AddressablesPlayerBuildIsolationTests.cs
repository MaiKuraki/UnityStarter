using System;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AddressablesPlayerBuildIsolationTests
    {
        private static readonly PropertyInfo BuildWithPlayerProperty =
            typeof(FakeSettings).GetProperty(nameof(FakeSettings.BuildWithPlayer));
        private static readonly PropertyInfo ThrowingBuildWithPlayerProperty =
            typeof(ThrowingFakeSettings).GetProperty(
                nameof(ThrowingFakeSettings.BuildWithPlayer));
        private static readonly PropertyInfo StreamingAssetFilterProperty =
            typeof(FakePlayerProcessor).GetProperty(
                nameof(FakePlayerProcessor.AddPathToStreamingAssets),
                BindingFlags.Public | BindingFlags.Static);

        [TearDown]
        public void TearDown()
        {
            FakePlayerProcessor.AddPathToStreamingAssets = null;
        }

        [Test]
        public void SuppressedSession_DisablesOfficialBuildAndStaleStreamingAssetsThenRestores()
        {
            var settings = new FakeSettings
            {
                BuildWithPlayer = FakeBuildWithPlayer.BuildWithPlayer
            };
            Func<string, bool> originalFilter = _ => true;
            Func<string, bool> suppressionFilter = _ => false;
            FakePlayerProcessor.AddPathToStreamingAssets = originalFilter;
            var buildLock = new RecordingDisposable();
            bool transactionFinalized = false;

            using (AddressablesPlayerBuildIsolation.BeginForTesting(
                       settings,
                       BuildWithPlayerProperty,
                       FakeBuildWithPlayer.DoNotBuildWithPlayer,
                       StreamingAssetFilterProperty,
                       suppressionFilter,
                       () =>
                       {
                           transactionFinalized = true;
                           return null;
                       },
                       buildLock))
            {
                Assert.That(
                    settings.BuildWithPlayer,
                    Is.EqualTo(FakeBuildWithPlayer.DoNotBuildWithPlayer));
                Assert.That(
                    FakePlayerProcessor.AddPathToStreamingAssets("stale/addressables"),
                    Is.False);
                Assert.That(transactionFinalized, Is.False);
                Assert.That(buildLock.Disposed, Is.False);
            }

            Assert.That(
                settings.BuildWithPlayer,
                Is.EqualTo(FakeBuildWithPlayer.BuildWithPlayer));
            Assert.That(
                FakePlayerProcessor.AddPathToStreamingAssets,
                Is.SameAs(originalFilter));
            Assert.That(transactionFinalized, Is.True);
            Assert.That(buildLock.Disposed, Is.True);
        }

        [Test]
        public void ContentSession_DisablesImplicitRebuildButPreservesStreamingInjection()
        {
            var settings = new FakeSettings
            {
                BuildWithPlayer = FakeBuildWithPlayer.PreferencesValue
            };
            Func<string, bool> originalFilter = _ => true;
            FakePlayerProcessor.AddPathToStreamingAssets = originalFilter;
            var buildLock = new RecordingDisposable();

            using (AddressablesPlayerBuildIsolation.BeginForTesting(
                       settings,
                       BuildWithPlayerProperty,
                       FakeBuildWithPlayer.DoNotBuildWithPlayer,
                       streamingAssetFilterProperty: null,
                       suppressedStreamingAssetFilter: null,
                       finalizeSettingsTransaction: () => null,
                       buildLock))
            {
                Assert.That(
                    settings.BuildWithPlayer,
                    Is.EqualTo(FakeBuildWithPlayer.DoNotBuildWithPlayer));
                Assert.That(
                    FakePlayerProcessor.AddPathToStreamingAssets,
                    Is.SameAs(originalFilter));
                Assert.That(
                    FakePlayerProcessor.AddPathToStreamingAssets("current/addressables"),
                    Is.True);
            }

            Assert.That(
                settings.BuildWithPlayer,
                Is.EqualTo(FakeBuildWithPlayer.PreferencesValue));
            Assert.That(
                FakePlayerProcessor.AddPathToStreamingAssets,
                Is.SameAs(originalFilter));
            Assert.That(buildLock.Disposed, Is.True);
        }

        [Test]
        public void Dispose_WhenStreamingFilterChanges_PreservesForeignValueAndRestoresOtherState()
        {
            var settings = new FakeSettings
            {
                BuildWithPlayer = FakeBuildWithPlayer.BuildWithPlayer
            };
            Func<string, bool> originalFilter = _ => true;
            Func<string, bool> suppressionFilter = _ => false;
            Func<string, bool> foreignFilter = _ => true;
            FakePlayerProcessor.AddPathToStreamingAssets = originalFilter;
            var buildLock = new RecordingDisposable();
            bool transactionFinalized = false;
            IDisposable session = AddressablesPlayerBuildIsolation.BeginForTesting(
                settings,
                BuildWithPlayerProperty,
                FakeBuildWithPlayer.DoNotBuildWithPlayer,
                StreamingAssetFilterProperty,
                suppressionFilter,
                () =>
                {
                    transactionFinalized = true;
                    return null;
                },
                buildLock);
            FakePlayerProcessor.AddPathToStreamingAssets = foreignFilter;

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => session.Dispose());

            StringAssert.Contains("streaming-asset filter", exception.ToString());
            Assert.That(
                FakePlayerProcessor.AddPathToStreamingAssets,
                Is.SameAs(foreignFilter));
            Assert.That(
                settings.BuildWithPlayer,
                Is.EqualTo(FakeBuildWithPlayer.BuildWithPlayer));
            Assert.That(transactionFinalized, Is.True);
            Assert.That(buildLock.Disposed, Is.True);
        }

        [Test]
        public void Begin_WhenSettingMutationFails_FinalizesTransactionAndReleasesLock()
        {
            var settings = new ThrowingFakeSettings();
            var buildLock = new RecordingDisposable();
            bool transactionFinalized = false;

            Assert.Throws<TargetInvocationException>(() =>
                AddressablesPlayerBuildIsolation.BeginForTesting(
                    settings,
                    ThrowingBuildWithPlayerProperty,
                    FakeBuildWithPlayer.DoNotBuildWithPlayer,
                    streamingAssetFilterProperty: null,
                    suppressedStreamingAssetFilter: null,
                    finalizeSettingsTransaction: () =>
                    {
                        transactionFinalized = true;
                        return null;
                    },
                    buildLock));

            Assert.That(
                settings.BuildWithPlayer,
                Is.EqualTo(FakeBuildWithPlayer.BuildWithPlayer));
            Assert.That(transactionFinalized, Is.True);
            Assert.That(buildLock.Disposed, Is.True);
        }

        private enum FakeBuildWithPlayer
        {
            PreferencesValue,
            BuildWithPlayer,
            DoNotBuildWithPlayer
        }

        private sealed class FakeSettings
        {
            public FakeBuildWithPlayer BuildWithPlayer { get; set; }
        }

        private sealed class ThrowingFakeSettings
        {
            private FakeBuildWithPlayer value =
                FakeBuildWithPlayer.BuildWithPlayer;

            public FakeBuildWithPlayer BuildWithPlayer
            {
                get => value;
                set
                {
                    if (value == FakeBuildWithPlayer.DoNotBuildWithPlayer)
                    {
                        throw new InvalidOperationException("simulated mutation failure");
                    }

                    this.value = value;
                }
            }
        }

        private static class FakePlayerProcessor
        {
            public static Func<string, bool> AddPathToStreamingAssets { get; set; }
        }

        private sealed class RecordingDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
