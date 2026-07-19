using NUnit.Framework;

namespace CycloneGames.Foundation2D.Runtime.Tests
{
    public sealed class SpriteSequencePlaybackStateTests
    {
        private const double Epsilon = 0.0000001d;

        [Test]
        public void Once_DisplaysEveryFrameForOneDuration_ThenCompletesOnTerminalFrame()
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Once,
                frameCount: 3,
                frameRate: 10d);

            SpriteSequenceAdvanceResult first = state.Advance(0.1d, 8);
            Assert.That(first.FrameChanged, Is.True);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(1));
            Assert.That(state.IsPlaying, Is.True);

            state.Advance(0.1d, 8);
            SpriteSequenceAdvanceResult completed = state.Advance(0.1d, 8);

            Assert.That(state.CurrentFrameIndex, Is.EqualTo(2));
            Assert.That(state.IsPlaying, Is.False);
            Assert.That(completed.PlaybackCompleted, Is.True);
        }

        [Test]
        public void SingleFrameOnce_CompletesAfterFrameDuration_NotDuringInitialize()
        {
            SpriteSequencePlaybackState state = default;
            SpriteSequenceAdvanceResult initialized = state.Initialize(
                SpriteSequencePlaybackDirection.Forward,
                10d,
                SpriteSequencePlaybackMode.Once,
                1,
                1d,
                0,
                0d,
                SpriteSequenceIntervalHoldMode.Last);

            Assert.That(initialized.PlaybackCompleted, Is.False);
            Assert.That(state.IsPlaying, Is.True);
            Assert.That(state.Advance(0.099d, 8).PlaybackCompleted, Is.False);

            SpriteSequenceAdvanceResult completed = state.Advance(0.001d + Epsilon, 8);
            Assert.That(completed.PlaybackCompleted, Is.True);
            Assert.That(state.IsPlaying, Is.False);
            Assert.That(state.CurrentFrameIndex, Is.Zero);
        }

        [Test]
        public void FiniteLoop_ReportsEveryCrossedCycle_AndStopsOnTerminalFrame()
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Loop,
                frameCount: 3,
                frameRate: 10d,
                maxLoopCount: 2);

            SpriteSequenceAdvanceResult result = state.Advance(0.6d + Epsilon, 32);

            Assert.That(result.CompletedLoopCount, Is.EqualTo(2));
            Assert.That(result.PlaybackCompleted, Is.True);
            Assert.That(state.CurrentLoopCount, Is.EqualTo(2));
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(2));
            Assert.That(state.IsPlaying, Is.False);
        }

        [TestCase((int)SpriteSequencePlaybackDirection.Forward, 0)]
        [TestCase((int)SpriteSequencePlaybackDirection.Reverse, 2)]
        public void PingPong_CountsACompleteRoundTripAsOneCycle(
            int directionValue,
            int expectedTerminalFrame)
        {
            SpriteSequencePlaybackDirection direction = (SpriteSequencePlaybackDirection)directionValue;
            SpriteSequencePlaybackState state = CreateState(
                direction,
                SpriteSequencePlaybackMode.PingPong,
                frameCount: 3,
                frameRate: 10d,
                maxLoopCount: 1);

            SpriteSequenceAdvanceResult result = state.Advance(0.4d + Epsilon, 16);

            Assert.That(result.CompletedLoopCount, Is.EqualTo(1));
            Assert.That(result.PlaybackCompleted, Is.True);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(expectedTerminalFrame));
            Assert.That(state.IsPlaying, Is.False);
        }

        [Test]
        public void LoopInterval_ConsumesOverflowAndRetainsFollowingFramePhase()
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Loop,
                frameCount: 2,
                frameRate: 10d,
                loopInterval: 0.15d);

            SpriteSequenceAdvanceResult result = state.Advance(0.36d, 16);

            Assert.That(result.CompletedLoopCount, Is.EqualTo(1));
            Assert.That(state.IsInInterval, Is.False);
            Assert.That(state.CurrentFrameIndex, Is.Zero);
            Assert.That(state.CurrentFrameElapsed, Is.EqualTo(0.01d).Within(Epsilon));
        }

        [Test]
        public void CatchUpBudget_DropsWholeBacklogAndRetainsFractionalPhase()
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Loop,
                frameCount: 4,
                frameRate: 10d);

            SpriteSequenceAdvanceResult limited = state.Advance(0.255d, 2);

            Assert.That(limited.CatchUpLimited, Is.True);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(2));
            Assert.That(state.CurrentFrameElapsed, Is.EqualTo(0.055d).Within(Epsilon));

            state.Advance(0.045d + Epsilon, 2);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(3));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-0.01d)]
        public void InvalidDelta_IsRejectedWithoutMutatingPlayback(double deltaTime)
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Loop,
                frameCount: 4,
                frameRate: 12d);
            int frame = state.CurrentFrameIndex;
            double elapsed = state.CurrentFrameElapsed;

            SpriteSequenceAdvanceResult result = state.Advance(deltaTime, 8);

            Assert.That(result.InvalidInput, Is.True);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(frame));
            Assert.That(state.CurrentFrameElapsed, Is.EqualTo(elapsed));
            Assert.That(state.IsPlaying, Is.True);
        }

        [Test]
        public void PauseAndResume_PreserveFramePhase()
        {
            SpriteSequencePlaybackState state = CreateState(
                SpriteSequencePlaybackDirection.Forward,
                SpriteSequencePlaybackMode.Loop,
                frameCount: 4,
                frameRate: 10d);
            state.Advance(0.04d, 8);

            state.Pause();
            state.Advance(1d, 8);
            Assert.That(state.CurrentFrameElapsed, Is.EqualTo(0.04d).Within(Epsilon));

            state.Resume();
            state.Advance(0.06d + Epsilon, 8);
            Assert.That(state.CurrentFrameIndex, Is.EqualTo(1));
        }

        private static SpriteSequencePlaybackState CreateState(
            SpriteSequencePlaybackDirection direction,
            SpriteSequencePlaybackMode mode,
            int frameCount,
            double frameRate,
            int maxLoopCount = 0,
            double loopInterval = 0d)
        {
            SpriteSequencePlaybackState state = default;
            SpriteSequenceAdvanceResult result = state.Initialize(
                direction,
                frameRate,
                mode,
                frameCount,
                1d,
                maxLoopCount,
                loopInterval,
                SpriteSequenceIntervalHoldMode.Last);
            Assert.That(result.InvalidInput, Is.False);
            return state;
        }
    }
}
