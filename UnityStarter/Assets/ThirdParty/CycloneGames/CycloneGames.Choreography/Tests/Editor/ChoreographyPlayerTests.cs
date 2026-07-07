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

        [Test]
        public void EventState_NormalFlow_DispatchesBeginUpdateEnd()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "Hitbox", 0.2d, 0.6d);
            ChoreographySection section = TestFactory.Section("s0", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("event-state", section);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();
            ChoreographyPlayer player = CreatePlayer(asset, signals);

            player.Tick(0.25d);
            player.Tick(0.1d);
            player.Tick(0.3d);

            Assert.AreEqual(3, signals.Count);
            Assert.AreEqual(EventStatePhase.Begin, signals[0].Phase);
            Assert.AreEqual(EventStatePhase.Update, signals[1].Phase);
            Assert.AreEqual(EventStatePhase.End, signals[2].Phase);
            Assert.IsFalse(signals[2].Interrupted);
        }

        [Test]
        public void EventState_BackwardSeek_EndsInterrupted()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "Armor", 0.2d, 0.8d);
            ChoreographySection section = TestFactory.Section("s0", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("seek-state", section);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();
            ChoreographyPlayer player = CreatePlayer(asset, signals);

            player.Tick(ChoreographyTimelineStep.FromAbsolute(0.4d));
            player.Tick(ChoreographyTimelineStep.FromAbsolute(0.1d));

            Assert.AreEqual(2, signals.Count);
            Assert.AreEqual(EventStatePhase.Begin, signals[0].Phase);
            Assert.AreEqual(EventStatePhase.End, signals[1].Phase);
            Assert.IsTrue(signals[1].Interrupted);
        }

        [Test]
        public void EventState_Stop_EndsInterrupted()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "DamageZone", 0.1d, 0.9d);
            ChoreographySection section = TestFactory.Section("s0", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("stop-state", section);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();
            ChoreographyPlayer player = CreatePlayer(asset, signals);

            player.Tick(0.2d);
            player.Stop();

            Assert.AreEqual(2, signals.Count);
            Assert.AreEqual(EventStatePhase.End, signals[1].Phase);
            Assert.IsTrue(signals[1].Interrupted);
        }

        [Test]
        public void EventState_Complete_EndsNaturally()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "SuperArmor", 0.1d, 1d);
            ChoreographySection section = TestFactory.Section("s0", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("complete-state", section);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();
            ChoreographyPlayer player = CreatePlayer(asset, signals);

            player.Tick(0.2d);
            player.Tick(1d);

            Assert.AreEqual(2, signals.Count);
            Assert.AreEqual(EventStatePhase.End, signals[1].Phase);
            Assert.IsFalse(signals[1].Interrupted);
        }

        [Test]
        public void EventState_LoopReset_EndsAndBeginsAgain()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "Window", 0d, 0.2d);
            ChoreographySection section = TestFactory.Section("s0", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("loop-state", section);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            sink.EventStateRaised += signal => signals.Add(signal);
            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, new ChoreographyPlaybackContext(0, 0, 1d, true), sink);
            player.Play();

            player.Tick(0.1d);
            player.Tick(1d);

            Assert.AreEqual(3, signals.Count);
            Assert.AreEqual(EventStatePhase.Begin, signals[0].Phase);
            Assert.AreEqual(EventStatePhase.End, signals[1].Phase);
            Assert.AreEqual(EventStatePhase.Begin, signals[2].Phase);
            Assert.IsFalse(signals[1].Interrupted);
        }

        [Test]
        public void EventState_CrossSection_UsesAbsoluteTimelineTime()
        {
            ChoreographyEventState state = new ChoreographyEventState("state", "SecondSectionState", 0.2d, 0.4d);
            ChoreographySection first = TestFactory.Section("s0", 1d, null);
            ChoreographySection second = TestFactory.Section("s1", 1d, null, eventStates: new[] { state });
            TestChoreographyAsset asset = new TestChoreographyAsset("section-state", first, second);
            List<ChoreographyEventStateSignal> signals = new List<ChoreographyEventStateSignal>();
            ChoreographyPlayer player = CreatePlayer(asset, signals);

            player.Tick(ChoreographyTimelineStep.FromAbsolute(1.25d, ChoreographyClockKind.Animation, 3, 60d));

            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual(EventStatePhase.Begin, signals[0].Phase);
            Assert.AreEqual(1.25d, signals[0].TimelineTime, 0.000000001d);
            Assert.AreEqual(0.05d, signals[0].StateLocalTime, 0.000000001d);
            Assert.AreEqual(ChoreographyClockKind.Animation, signals[0].ClockKind);
            Assert.AreEqual(3, signals[0].TickIndex);
        }

        [Test]
        public void ClockDriver_FixedFrameSection_QuantizesRepeatedSmallDeltas()
        {
            ChoreographySectionClock clock = new ChoreographySectionClock(ChoreographySectionClockSource.FixedFrame, frameRate: 10d);
            ChoreographySection section = TestFactory.Section(
                "s0",
                1d,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0d, 1d)) },
                clock: clock);
            TestChoreographyAsset asset = new TestChoreographyAsset("fixed-clock", section);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            ChoreographyPlayer player = new ChoreographyPlayer();
            SectionClockDriver driver = new SectionClockDriver();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            driver.Reset(player.ClockState);
            player.Play();

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.03d));
            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.03d));
            Assert.AreEqual(0d, player.Time, 0.000000001d, "Small deltas before the first frame boundary should not advance.");

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.05d));
            Assert.AreEqual(0.1d, player.Time, 0.000000001d);
            Assert.AreEqual(1, provider.LastTickIndex["a"]);
            Assert.AreEqual(ChoreographyClockKind.FixedTick, provider.LastClockKind["a"]);
        }

        [Test]
        public void ClockDriver_FixedFrameSection_SynchronizesAfterInternalSection()
        {
            ChoreographySection first = TestFactory.Section("intro", 1d, null);
            ChoreographySectionClock clock = new ChoreographySectionClock(ChoreographySectionClockSource.FixedFrame, frameRate: 10d);
            ChoreographySection second = TestFactory.Section(
                "fixed",
                1d,
                new[] { TestFactory.Track(ChoreographyTrackKind.Animation, TestFactory.Clip("a", 0d, 1d)) },
                clock: clock);
            TestChoreographyAsset asset = new TestChoreographyAsset("fixed-after-internal", first, second);

            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            ChoreographyPlayer player = new ChoreographyPlayer();
            SectionClockDriver driver = new SectionClockDriver();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            driver.Reset(player.ClockState);
            player.Play();

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(1d));
            Assert.AreEqual(1d, player.Time, 0.000000001d);

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.02d));
            Assert.AreEqual(1d, player.Time, 0.000000001d);

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.09d));
            Assert.AreEqual(1.1d, player.Time, 0.000000001d);
            Assert.AreEqual(11, provider.LastTickIndex["a"]);
            Assert.AreEqual(ChoreographyClockKind.FixedTick, provider.LastClockKind["a"]);
        }

        [Test]
        public void ClockDriver_ExternalSectionContinueInternal_AllowsEventOnlyTailSection()
        {
            ChoreographySectionClock externalClock = new ChoreographySectionClock(
                ChoreographySectionClockSource.Audio,
                ChoreographyExternalClockEndPolicy.ContinueInternal);
            ChoreographySection first = TestFactory.Section("audio", 1d, null, clock: externalClock);
            ChoreographySection second = TestFactory.Section(
                "tail",
                1d,
                null,
                new[] { new ChoreographyEvent("tail-event", 0.2d) });
            TestChoreographyAsset asset = new TestChoreographyAsset("external-tail", first, second);

            FakeExternalClockSource source = new FakeExternalClockSource();
            ExternalSectionClockDriver driver = new ExternalSectionClockDriver(source, ChoreographyClockKind.AudioDspTime);
            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            List<string> raisedEvents = new List<string>();
            sink.EventRaised += invocation => raisedEvents.Add(invocation.Event.EventId);

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            driver.Reset(player.ClockState);
            player.Play();

            source.Sample = ChoreographyExternalClockSample.FromLocalTime(0.8d, completed: true, sourceTime: 12d, tickIndex: 48, tickRate: 60d);
            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.1d));
            Assert.AreEqual(0.1d, player.Time, 0.000000001d, "ContinueInternal falls back to internal delta when the external source ends early.");

            source.Sample = ChoreographyExternalClockSample.Unavailable;
            player.Tick(driver, ChoreographyTimelineStep.FromDelta(1.2d));
            Assert.Contains("tail-event", raisedEvents, "The event-only tail section should still execute after the external section.");
        }

        [Test]
        public void ClockDriver_ExternalSectionCompleteSection_JumpsToNextSectionBoundary()
        {
            ChoreographySectionClock externalClock = new ChoreographySectionClock(
                ChoreographySectionClockSource.Animation,
                ChoreographyExternalClockEndPolicy.CompleteSection);
            ChoreographySection first = TestFactory.Section("animation", 1d, null, clock: externalClock);
            ChoreographySection second = TestFactory.Section("events", 1d, null, new[] { new ChoreographyEvent("enter-tail", 0d) });
            TestChoreographyAsset asset = new TestChoreographyAsset("complete-section", first, second);

            FakeExternalClockSource source = new FakeExternalClockSource
            {
                Sample = ChoreographyExternalClockSample.FromLocalTime(0.5d, completed: true, tickIndex: 7, tickRate: 30d)
            };
            ExternalSectionClockDriver driver = new ExternalSectionClockDriver(source, ChoreographyClockKind.Animation);
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(new RecordingProvider()));
            List<ChoreographyEventInvocation> events = new List<ChoreographyEventInvocation>();
            sink.EventRaised += invocation => events.Add(invocation);

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            driver.Reset(player.ClockState);
            player.Play();

            player.Tick(driver, ChoreographyTimelineStep.FromDelta(0.1d));

            Assert.AreEqual(1d, player.Time, 0.000000001d);
            Assert.AreEqual(1, player.CurrentSectionIndex);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("enter-tail", events[0].Event.EventId);
            Assert.AreEqual(ChoreographyClockKind.Animation, events[0].ClockKind);
        }

        private static ChoreographyPlayer CreatePlayer(
            TestChoreographyAsset asset,
            List<ChoreographyEventStateSignal> stateSignals)
        {
            RecordingProvider provider = new RecordingProvider();
            DirectProviderSink sink = new DirectProviderSink(new RecordingProviderSet(provider));
            sink.EventStateRaised += signal => stateSignals.Add(signal);

            ChoreographyPlayer player = new ChoreographyPlayer();
            player.Load(asset, ChoreographyPlaybackContext.Default, sink);
            player.Play();
            return player;
        }

        private sealed class FakeExternalClockSource : IChoreographyExternalClockSource
        {
            public ChoreographyExternalClockSample Sample;

            public bool TryGetSample(in ChoreographyClockState state, out ChoreographyExternalClockSample sample)
            {
                sample = Sample;
                return sample.HasTime;
            }
        }
    }
}
