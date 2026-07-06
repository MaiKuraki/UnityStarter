using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    [TestFixture]
    public sealed class ChoreographySchedulerTests
    {
        private static TestChoreographyAsset SingleClipAsset(string assetId, string clipId, float duration, float weight = 1f)
        {
            ChoreographySection section = TestFactory.Section(
                "s0", duration,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip(clipId, 0f, duration, weight)) });
            return new TestChoreographyAsset(assetId, section);
        }

        [Test]
        public void Blend_NormalizesWeightsAcrossChannel()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            scheduler.Play(SingleClipAsset("A", "a", 2f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Blend));
            scheduler.Play(SingleClipAsset("B", "b", 2f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Blend));

            scheduler.Tick(0.1f);

            Assert.Contains("a", provider.Begun);
            Assert.Contains("b", provider.Begun);
            Assert.AreEqual(0.5f, provider.LastWeight["a"], 0.0001f);
            Assert.AreEqual(0.5f, provider.LastWeight["b"], 0.0001f);
        }

        [Test]
        public void Priority_HigherInterruptsInterruptibleDominant()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            scheduler.Play(SingleClipAsset("A", "a", 5f), new ChoreographyPlayRequest(0, 1, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            int idB = scheduler.Play(SingleClipAsset("B", "b", 5f), new ChoreographyPlayRequest(0, 2, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            Assert.AreNotEqual(ChoreographyScheduler.InvalidInstanceId, idB);
            Assert.AreEqual(1, scheduler.ActiveCount, "The interrupted instance should be recycled.");
            Assert.Contains("a", provider.Ended, "Interrupted clip should stop.");
            Assert.Contains("b", provider.Begun, "The higher-priority clip should start.");
        }

        [Test]
        public void Priority_LowerRequestIsRejected()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            scheduler.Play(SingleClipAsset("A", "a", 5f), new ChoreographyPlayRequest(0, 2, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            int idB = scheduler.Play(SingleClipAsset("B", "b", 5f), new ChoreographyPlayRequest(0, 1, ChoreographyPlaybackMode.Priority));

            Assert.AreEqual(ChoreographyScheduler.InvalidInstanceId, idB);
            Assert.AreEqual(1, scheduler.ActiveCount);
        }

        [Test]
        public void Priority_NonInterruptibleDominantQueuesHigherRequest()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            ChoreographySection windup = TestFactory.Section(
                "windup", 1f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0f, 1f)) },
                null, interruptible: false);
            TestChoreographyAsset committed = new TestChoreographyAsset("A", windup);

            scheduler.Play(committed, new ChoreographyPlayRequest(0, 1, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            int idB = scheduler.Play(SingleClipAsset("B", "b", 2f), new ChoreographyPlayRequest(0, 5, ChoreographyPlaybackMode.Priority));
            Assert.AreNotEqual(ChoreographyScheduler.InvalidInstanceId, idB);
            Assert.AreEqual(1, scheduler.QueuedCount, "A higher-priority request must queue behind a non-interruptible section.");

            scheduler.Tick(1.1f); // completes the windup, promotes the queued request
            scheduler.Tick(0.1f);

            Assert.AreEqual(0, scheduler.QueuedCount);
            Assert.Contains("b", provider.Begun);
        }

        [Test]
        public void SectionPreferredMode_QueuedWindupRequestPromotesWhenRecoveryAllowsAdmission()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            ChoreographySection windup = TestFactory.Section(
                "windup", 0.5f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("windup", 0f, 0.5f)) },
                null,
                interruptible: false,
                mode: ChoreographyPlaybackMode.Priority);
            ChoreographySection recovery = TestFactory.Section(
                "recovery", 1.5f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("recovery", 0f, 1.5f)) },
                null,
                interruptible: true,
                mode: ChoreographyPlaybackMode.Blend);
            TestChoreographyAsset committed = new TestChoreographyAsset("A", windup, recovery);

            scheduler.Play(committed, new ChoreographyPlayRequest(0, 1, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            int nextId = scheduler.Play(SingleClipAsset("B", "next", 1.5f), new ChoreographyPlayRequest(0, 5, ChoreographyPlaybackMode.Priority));
            Assert.AreEqual(1, scheduler.QueuedCount, "A request made during the committed windup should wait.");

            scheduler.Tick(0.5f);
            scheduler.Tick(0.1f);

            Assert.AreNotEqual(ChoreographyScheduler.InvalidInstanceId, nextId);
            Assert.AreEqual(0, scheduler.QueuedCount);
            Assert.AreEqual(2, scheduler.ActiveCount, "The recovery section should admit the queued request instead of waiting for full completion.");
            Assert.Contains("next", provider.Begun);
        }

        [Test]
        public void SectionPreferredMode_RecoveryCanBlendLowerPriorityRequest()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            ChoreographySection windup = TestFactory.Section(
                "windup", 0.5f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("windup", 0f, 0.5f)) },
                null,
                interruptible: false,
                mode: ChoreographyPlaybackMode.Priority);
            ChoreographySection recovery = TestFactory.Section(
                "recovery", 1.5f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("recovery", 0f, 1.5f)) },
                null,
                interruptible: true,
                mode: ChoreographyPlaybackMode.Blend);
            TestChoreographyAsset committed = new TestChoreographyAsset("A", windup, recovery);

            scheduler.Play(committed, new ChoreographyPlayRequest(0, 1, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.6f);

            int nextId = scheduler.Play(SingleClipAsset("B", "next", 1.5f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(0.1f);

            Assert.AreNotEqual(ChoreographyScheduler.InvalidInstanceId, nextId);
            Assert.AreEqual(2, scheduler.ActiveCount, "The recovery section should admit a lower-priority blend instead of rejecting it.");
            Assert.AreEqual(0.5f, provider.LastWeight["recovery"], 0.0001f);
            Assert.AreEqual(0.5f, provider.LastWeight["next"], 0.0001f);
        }

        [Test]
        public void Queue_DefersSecondUntilFirstCompletes()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            scheduler.Play(SingleClipAsset("A", "a", 1f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Queue));
            int idB = scheduler.Play(SingleClipAsset("B", "b", 1f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Queue));

            Assert.AreNotEqual(ChoreographyScheduler.InvalidInstanceId, idB);
            Assert.AreEqual(1, scheduler.QueuedCount);

            scheduler.Tick(1.1f); // A completes, B promoted
            scheduler.Tick(0.1f); // B ticks

            Assert.AreEqual(0, scheduler.QueuedCount);
            Assert.AreEqual(1, scheduler.ActiveCount);
            Assert.Contains("b", provider.Begun);
        }

        [Test]
        public void Tick_ForwardsClockAuthorityToResolvedSamples()
        {
            RecordingProvider provider = new RecordingProvider();
            ChoreographyScheduler scheduler = new ChoreographyScheduler(new RecordingProviderSet(provider));

            scheduler.Play(SingleClipAsset("A", "a", 1f), new ChoreographyPlayRequest(0, 0, ChoreographyPlaybackMode.Priority));
            scheduler.Tick(ChoreographyTimelineStep.FromDelta(1d / 120d, ChoreographyClockKind.FixedTick, 42, 120d));

            Assert.Contains("a", provider.Begun);
            Assert.AreEqual(42, provider.LastTickIndex["a"]);
            Assert.AreEqual(ChoreographyClockKind.FixedTick, provider.LastClockKind["a"]);
            Assert.AreEqual(1d / 120d, provider.LastTimelineTime["a"], 0.000000001d);
        }
    }
}
