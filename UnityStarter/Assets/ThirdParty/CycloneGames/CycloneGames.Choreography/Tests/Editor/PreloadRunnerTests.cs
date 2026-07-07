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
