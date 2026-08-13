using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Core.Tests
{
    public sealed class MatchStateMachineTests
    {
        [Test]
        public void LegalTransitions_AccumulateOnlyInProgressTime()
        {
            var stateMachine = new MatchStateMachine(timestamp: 10d);

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, 11d));
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.InProgress, 20d));
            Assert.AreEqual(5d, stateMachine.GetElapsedSeconds(25d));
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, 27d));
            Assert.AreEqual(7d, stateMachine.GetElapsedSeconds(100d));
        }

        [Test]
        public void RematchTransition_ResetsElapsedClock()
        {
            var stateMachine = new MatchStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, 1d);
            stateMachine.TryTransition(MatchState.InProgress, 2d);
            stateMachine.TryTransition(MatchState.WaitingPostMatch, 5d);

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, 6d));
            Assert.AreEqual(0d, stateMachine.GetElapsedSeconds(6d));
        }

        [Test]
        public void IllegalOrBackwardTransitions_DoNotMutateState()
        {
            var stateMachine = new MatchStateMachine();

            Assert.AreEqual(
                MatchStateTransitionResult.IllegalTransition,
                stateMachine.TryTransition(MatchState.InProgress, 1d));
            Assert.AreEqual(MatchState.EnteringMap, stateMachine.State);

            stateMachine.TryTransition(MatchState.WaitingToStart, 2d);
            stateMachine.TryTransition(MatchState.InProgress, 3d);
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, 2d));
            Assert.AreEqual(MatchState.InProgress, stateMachine.State);
            Assert.Throws<ArgumentOutOfRangeException>(() => stateMachine.GetElapsedSeconds(2d));
        }

        [Test]
        public void TransitionTimestamp_CannotMoveBackwardsWhileClockIsStopped()
        {
            var stateMachine = new MatchStateMachine(timestamp: 10d);
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, 20d));

            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.InProgress, 19d));
            Assert.AreEqual(MatchState.WaitingToStart, stateMachine.State);
        }

        [Test]
        public void ElapsedRead_AdvancesMonotonicHighWater()
        {
            var stateMachine = new MatchStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, 1d);
            stateMachine.TryTransition(MatchState.InProgress, 2d);

            Assert.AreEqual(8d, stateMachine.GetElapsedSeconds(10d));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, 9d));
            Assert.Throws<ArgumentOutOfRangeException>(() => stateMachine.GetElapsedSeconds(9d));
            Assert.AreEqual(MatchState.InProgress, stateMachine.State);

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, 12d));
            Assert.AreEqual(10d, stateMachine.GetElapsedSeconds(20d));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.WaitingToStart, 19d));
            Assert.Throws<ArgumentOutOfRangeException>(() => stateMachine.GetElapsedSeconds(19d));
        }

        [Test]
        public void UnchangedTransition_AdvancesMonotonicHighWaterWithoutChangingClock()
        {
            var stateMachine = new MatchStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, 1d);

            Assert.AreEqual(
                MatchStateTransitionResult.Unchanged,
                stateMachine.TryTransition(MatchState.WaitingToStart, 10d));
            Assert.AreEqual(MatchState.WaitingToStart, stateMachine.State);
            Assert.AreEqual(0d, stateMachine.GetElapsedSeconds(10d));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.InProgress, 9d));
            Assert.Throws<ArgumentOutOfRangeException>(() => stateMachine.GetElapsedSeconds(9d));

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.InProgress, 10d));
            Assert.AreEqual(5d, stateMachine.GetElapsedSeconds(15d));
        }

        [Test]
        public void ReadAndMutation_FromNonOwnerThreadAreRejected()
        {
            var stateMachine = new MatchStateMachine();
            Exception readException = null;
            Exception mutationException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    _ = stateMachine.State;
                }
                catch (Exception exception)
                {
                    readException = exception;
                }

                try
                {
                    stateMachine.TryTransition(MatchState.WaitingToStart, 1d);
                }
                catch (Exception exception)
                {
                    mutationException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");

            Assert.IsInstanceOf<InvalidOperationException>(readException);
            Assert.IsInstanceOf<InvalidOperationException>(mutationException);
        }
    }
}
