using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    [TestFixture]
    public sealed class PreloadRunnerTests
    {
        private static List<ChoreographyResourceReference> Refs(params ChoreographyResourceReference[] items)
        {
            return new List<ChoreographyResourceReference>(items);
        }

        [Test]
        public void Preload_CompletesWhenAllHandlesSucceed()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), PreloadOptions.Default);
            runner.Update();
            Assert.IsFalse(runner.IsDone, "Batch should still be loading until handles complete.");

            provider.Complete(r1, true);
            provider.Complete(r2, true);
            runner.Update();

            Assert.IsTrue(runner.IsDone);
            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(2, result.SucceededCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
        }

        [Test]
        public void Preload_ContinuePolicyReportsFailuresButCompletes()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), new PreloadOptions(PreloadFailurePolicy.Continue));
            provider.Complete(r1, true);
            provider.Complete(r2, false, "missing");
            runner.Update();

            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(1, result.SucceededCount);
            Assert.AreEqual(1, result.FailedCount);
        }

        [Test]
        public void Preload_AbortPolicyFailsFast()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);

            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r2), new PreloadOptions(PreloadFailurePolicy.Abort));
            provider.Complete(r2, false, "missing");
            runner.Update();

            Assert.AreEqual(PreloadStatus.Failed, result.Status);
            Assert.IsTrue(runner.IsDone);
        }

        [Test]
        public void Preload_EmptyBatchCompletesImmediately()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            PreloadRunner runner = new PreloadRunner(provider);

            runner.Begin(new List<ChoreographyResourceReference>(), PreloadOptions.Default);

            Assert.AreEqual(PreloadStatus.Completed, runner.Status);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
        }

        [Test]
        public void Preload_DeduplicatesReferencesBeforeLoading()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference r1 = new ChoreographyResourceReference("r1", ChoreographyResourceKind.AudioEvent);
            ChoreographyResourceReference r2 = new ChoreographyResourceReference("r2", ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(provider);
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(r1, r1, r2, r2), PreloadOptions.Default);
            provider.Complete(r1, true);
            provider.Complete(r2, true);
            runner.Update();

            Assert.AreEqual(2, provider.LoadCount);
            Assert.AreEqual(2, runner.TotalCount);
            Assert.AreEqual(2, result.TotalCount);
            Assert.AreEqual(2, result.SucceededCount);
        }

        [Test]
        public void Preload_NullProviderHandleReportsFailure()
        {
            ChoreographyResourceReference reference = new ChoreographyResourceReference("missing", ChoreographyResourceKind.Vfx);
            PreloadRunner runner = new PreloadRunner(new NullResourceProvider());
            PreloadResult result = default;
            runner.Completed += r => result = r;

            runner.Begin(Refs(reference), PreloadOptions.Default);
            runner.Update();

            Assert.AreEqual(PreloadStatus.Completed, result.Status);
            Assert.AreEqual(1, result.FailedCount);
            Assert.AreEqual(0, result.SucceededCount);
        }

        [Test]
        public void Preload_BeginsFromAssetWithoutCallerOwnedList()
        {
            FakeResourceProvider provider = new FakeResourceProvider();
            ChoreographyResourceReference resource = new ChoreographyResourceReference("shared", ChoreographyResourceKind.Animation);
            ChoreographySection section = TestFactory.Section(
                "s0",
                1d,
                new[]
                {
                    new ChoreographyTrack(
                        "body",
                        ChoreographyTrackKind.Animation,
                        new[]
                        {
                            new ChoreographyClip("a", resource, 0d, 0.5d),
                            new ChoreographyClip("b", resource, 0.5d, 0.5d)
                        })
                });
            TestChoreographyAsset asset = new TestChoreographyAsset("asset", section);
            PreloadRunner runner = new PreloadRunner(provider);

            runner.Begin(asset, PreloadOptions.Default);
            provider.Complete(resource, true);
            runner.Update();

            Assert.AreEqual(1, provider.LoadCount);
            Assert.AreEqual(1, runner.TotalCount);
            Assert.AreEqual(PreloadStatus.Completed, runner.Status);
        }

        [Test]
        public void ResourceReference_ProviderAndGroupParticipateInIdentity()
        {
            ChoreographyResourceReference left = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "CycloneGames.Audio",
                group: "Combat");
            ChoreographyResourceReference differentProvider = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "Wwise",
                group: "Combat");
            ChoreographyResourceReference differentGroup = new ChoreographyResourceReference(
                "Attack",
                ChoreographyResourceKind.BackendCue,
                provider: "CycloneGames.Audio",
                group: "UI");

            Assert.AreNotEqual(left, differentProvider);
            Assert.AreNotEqual(left, differentGroup);
        }
    }
}
