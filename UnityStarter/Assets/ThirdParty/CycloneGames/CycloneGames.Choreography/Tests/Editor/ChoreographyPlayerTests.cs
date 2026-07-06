using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    [TestFixture]
    public sealed class ChoreographyPlayerTests
    {
        [Test]
        public void Tick_ActivatesCompletesClips_AndDispatchesEvents()
        {
            ChoreographyEvent[] events = { new ChoreographyEvent("hit", 0.5f) };
            ChoreographySection section = TestFactory.Section(
                "s0", 2f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0f, 1f)) },
                events);
            TestChoreographyAsset asset = new TestChoreographyAsset("test", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            List<string> raisedEvents = new List<string>();
            bool completed = false;
            sink.EventRaised += invocation => raisedEvents.Add(invocation.Event.EventId);
            sink.PlaybackCompleted += _ => completed = true;

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();

            player.Tick(0.4f);
            Assert.Contains("a", provider.Begun, "Clip should begin once the playhead reaches its start.");
            Assert.IsEmpty(raisedEvents, "Event before the playhead must not fire yet.");

            player.Tick(0.2f); // t = 0.6, crosses event at 0.5
            Assert.Contains("hit", raisedEvents, "Event should fire when the playhead crosses it.");

            player.Tick(0.5f); // t = 1.1, clip ends at 1.0
            Assert.Contains("a", provider.Ended, "Clip should end after its duration.");
            Assert.AreEqual(1, provider.CompletedStops, "Clip end at duration is a natural completion.");

            player.Tick(1.0f); // t = 2.1 >= total 2.0
            Assert.AreEqual(PlaybackStatus.Completed, player.Status);
            Assert.IsTrue(completed, "Playback completion callback should fire at the end of the timeline.");
        }

        [Test]
        public void OneShotClip_FiresBeginAndEndInSameTick()
        {
            ChoreographySection section = TestFactory.Section(
                "s0", 1f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Vfx, TestFactory.Clip("burst", 0.2f, 0f)) });
            TestChoreographyAsset asset = new TestChoreographyAsset("oneshot", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();

            player.Tick(0.3f); // crosses the one-shot at 0.2
            Assert.Contains("burst", provider.Begun);
            Assert.Contains("burst", provider.Ended);
        }

        [Test]
        public void Stop_ReportsActiveClipAsInterrupted()
        {
            ChoreographySection section = TestFactory.Section(
                "s0", 5f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0f, 5f)) });
            TestChoreographyAsset asset = new TestChoreographyAsset("interrupt", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();
            player.Tick(0.1f);
            player.Stop();

            Assert.Contains("a", provider.Ended);
            Assert.AreEqual(0, provider.CompletedStops, "A stop before the end is an interruption, not a completion.");
            Assert.AreEqual(PlaybackStatus.Stopped, player.Status);
        }

        [Test]
        public void ClipChannel_IsForwardedToProviders()
        {
            ChoreographySection section = TestFactory.Section(
                "s0", 1f,
                new[] { TestFactory.Track(ChoreographyTrackKind.Audio, TestFactory.Clip("audio", 0f, 1f, channel: 7)) });
            TestChoreographyAsset asset = new TestChoreographyAsset("clip-channel", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();
            player.Tick(0.1f);

            Assert.AreEqual(7, provider.LastClipChannel["audio"]);
        }

        [Test]
        public void EventInvocation_CarriesScheduledAndDispatchTimes()
        {
            ChoreographyEvent[] events = { new ChoreographyEvent("hit", 0.5d) };
            ChoreographySection section = TestFactory.Section("s0", 1d, null, events);
            TestChoreographyAsset asset = new TestChoreographyAsset("timed-event", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            ChoreographyEventInvocation captured = default;
            sink.EventRaised += invocation => captured = invocation;

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();
            player.Tick(ChoreographyTimelineStep.FromDelta(0.6d, ChoreographyClockKind.FixedTick, 12, 60d));

            Assert.AreEqual("hit", captured.Event.EventId);
            Assert.AreEqual(0.5d, captured.ScheduledTime, 0.000000001d);
            Assert.AreEqual(0.6d, captured.DispatchTime, 0.000000001d);
            Assert.AreEqual(0.1d, captured.DispatchDelay, 0.000000001d);
            Assert.AreEqual(ChoreographyClockKind.FixedTick, captured.ClockKind);
            Assert.AreEqual(12, captured.TickIndex);
        }

        [Test]
        public void AbsoluteTimelineStep_DrivesPlayheadFromExternalAuthority()
        {
            ChoreographySection section = TestFactory.Section(
                "s0", 1d,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0.25d, 0.5d)) });
            TestChoreographyAsset asset = new TestChoreographyAsset("absolute", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();
            player.Tick(ChoreographyTimelineStep.FromAbsolute(0.3d, ChoreographyClockKind.Animation, 18, 60d));

            Assert.Contains("a", provider.Begun);
            Assert.AreEqual(0.3d, provider.LastTimelineTime["a"], 0.000000001d);
            Assert.AreEqual(0.05d, provider.LastLocalTime["a"], 0.000000001d);
            Assert.AreEqual(ChoreographyClockKind.Animation, provider.LastClockKind["a"]);
            Assert.AreEqual(18, provider.LastTickIndex["a"]);
        }
    }
}
