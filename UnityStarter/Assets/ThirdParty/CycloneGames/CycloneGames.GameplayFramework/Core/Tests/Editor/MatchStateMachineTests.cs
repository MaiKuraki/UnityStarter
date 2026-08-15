using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Core.Tests
{
    public sealed class MatchStateMachineTests
    {
        private static readonly Guid ClockEpoch =
            new Guid("5ca89b82-9574-42d8-b4e7-4b18a3557fb1");

        [Test]
        public void LegalTransitions_AccumulateOnlyInProgressTime()
        {
            var stateMachine = CreateStateMachine(10d);

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(11d)));
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.InProgress, Timestamp(20d)));
            Assert.AreEqual(5d, stateMachine.GetElapsedSeconds(Timestamp(25d)));
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(27d)));
            Assert.AreEqual(7d, stateMachine.GetElapsedSeconds(Timestamp(100d)));
        }

        [Test]
        public void RematchTransition_ResetsElapsedClock()
        {
            var stateMachine = CreateStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(1d));
            stateMachine.TryTransition(MatchState.InProgress, Timestamp(2d));
            stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(5d));

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(6d)));
            Assert.AreEqual(0d, stateMachine.GetElapsedSeconds(Timestamp(6d)));
        }

        [Test]
        public void IllegalOrBackwardTransitions_DoNotMutateState()
        {
            var stateMachine = CreateStateMachine();

            Assert.AreEqual(
                MatchStateTransitionResult.IllegalTransition,
                stateMachine.TryTransition(MatchState.InProgress, Timestamp(1d)));
            Assert.AreEqual(MatchState.EnteringMap, stateMachine.State);

            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(2d));
            stateMachine.TryTransition(MatchState.InProgress, Timestamp(3d));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(2d)));
            Assert.AreEqual(MatchState.InProgress, stateMachine.State);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => stateMachine.GetElapsedSeconds(Timestamp(2d)));
        }

        [Test]
        public void TransitionTimestamp_CannotMoveBackwardsWhileClockIsStopped()
        {
            var stateMachine = CreateStateMachine(10d);
            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(20d)));

            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.InProgress, Timestamp(19d)));
            Assert.AreEqual(MatchState.WaitingToStart, stateMachine.State);
        }

        [Test]
        public void ElapsedRead_AdvancesMonotonicHighWater()
        {
            var stateMachine = CreateStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(1d));
            stateMachine.TryTransition(MatchState.InProgress, Timestamp(2d));

            Assert.AreEqual(8d, stateMachine.GetElapsedSeconds(Timestamp(10d)));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(9d)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => stateMachine.GetElapsedSeconds(Timestamp(9d)));
            Assert.AreEqual(MatchState.InProgress, stateMachine.State);

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(12d)));
            Assert.AreEqual(10d, stateMachine.GetElapsedSeconds(Timestamp(20d)));
        }

        [Test]
        public void UnchangedTransition_AdvancesMonotonicHighWaterWithoutChangingClock()
        {
            var stateMachine = CreateStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(1d));

            Assert.AreEqual(
                MatchStateTransitionResult.Unchanged,
                stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(10d)));
            Assert.AreEqual(MatchState.WaitingToStart, stateMachine.State);
            Assert.AreEqual(0d, stateMachine.GetElapsedSeconds(Timestamp(10d)));
            Assert.AreEqual(
                MatchStateTransitionResult.InvalidTimestamp,
                stateMachine.TryTransition(MatchState.InProgress, Timestamp(9d)));

            Assert.AreEqual(
                MatchStateTransitionResult.Success,
                stateMachine.TryTransition(MatchState.InProgress, Timestamp(10d)));
            Assert.AreEqual(5d, stateMachine.GetElapsedSeconds(Timestamp(15d)));
        }

        [Test]
        public void ClockEpochMismatch_IsRejectedWithoutMutation()
        {
            var stateMachine = CreateStateMachine();
            var foreignTimestamp = new MatchTimestamp(Guid.NewGuid(), 1d);

            Assert.AreEqual(
                MatchStateTransitionResult.ClockEpochMismatch,
                stateMachine.TryTransition(MatchState.WaitingToStart, foreignTimestamp));
            Assert.Throws<ArgumentException>(
                () => stateMachine.GetElapsedSeconds(foreignTimestamp));
            Assert.AreEqual(MatchState.EnteringMap, stateMachine.State);
        }

        [Test]
        public void RunningSnapshot_RestorePreservesContinuousElapsedTime()
        {
            var stateMachine = CreateStateMachine(10d);
            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(11d));
            stateMachine.TryTransition(MatchState.InProgress, Timestamp(20d));
            MatchStateSnapshot snapshot = stateMachine.CaptureSnapshot(Timestamp(25d));

            MatchStateRestoreResult result = MatchStateMachine.TryRestore(
                snapshot,
                Timestamp(30d),
                out MatchStateMachine restored);

            Assert.AreEqual(MatchStateRestoreResult.Success, result);
            Assert.AreEqual(MatchState.InProgress, restored.State);
            Assert.AreEqual(10d, restored.GetElapsedSeconds(Timestamp(30d)));
            Assert.AreEqual(15d, restored.GetElapsedSeconds(Timestamp(35d)));
        }

        [Test]
        public void StoppedSnapshot_RestoreDoesNotCountSuspendedTime()
        {
            var stateMachine = CreateStateMachine();
            stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(1d));
            stateMachine.TryTransition(MatchState.InProgress, Timestamp(2d));
            stateMachine.TryTransition(MatchState.WaitingPostMatch, Timestamp(7d));
            MatchStateSnapshot snapshot = stateMachine.CaptureSnapshot(Timestamp(10d));

            MatchStateRestoreResult result = MatchStateMachine.TryRestore(
                snapshot,
                Timestamp(100d),
                out MatchStateMachine restored);

            Assert.AreEqual(MatchStateRestoreResult.Success, result);
            Assert.AreEqual(5d, restored.GetElapsedSeconds(Timestamp(200d)));
        }

        [Test]
        public void Restore_RejectsInvalidEpochAndBackwardTimestamp()
        {
            var snapshot = new MatchStateSnapshot(
                MatchState.InProgress,
                10d,
                20d,
                ClockEpoch);

            Assert.AreEqual(
                MatchStateRestoreResult.ClockEpochMismatch,
                MatchStateMachine.TryRestore(
                    snapshot,
                    new MatchTimestamp(Guid.NewGuid(), 21d),
                    out MatchStateMachine wrongEpoch));
            Assert.IsNull(wrongEpoch);

            Assert.AreEqual(
                MatchStateRestoreResult.RestoreTimestampPrecedesSnapshot,
                MatchStateMachine.TryRestore(
                    snapshot,
                    Timestamp(19d),
                    out MatchStateMachine backwards));
            Assert.IsNull(backwards);

            Assert.AreEqual(
                MatchStateRestoreResult.InvalidSnapshot,
                MatchStateMachine.TryRestore(
                    default,
                    Timestamp(21d),
                    out MatchStateMachine invalid));
            Assert.IsNull(invalid);
        }

        [Test]
        public void Restore_CapturesCallingThreadAsNewOwner()
        {
            MatchStateSnapshot snapshot = CreateStateMachine()
                .CaptureSnapshot(Timestamp(1d));
            MatchStateMachine restored = null;
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    Assert.AreEqual(
                        MatchStateRestoreResult.Success,
                        MatchStateMachine.TryRestore(
                            snapshot,
                            Timestamp(2d),
                            out restored));
                    Assert.AreEqual(MatchState.EnteringMap, restored.State);
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
            Assert.IsNull(workerException);
            Assert.Throws<InvalidOperationException>(() => _ = restored.State);
        }

        [Test]
        public void ReadAndMutation_FromNonOwnerThreadAreRejected()
        {
            var stateMachine = CreateStateMachine();
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
                    stateMachine.TryTransition(MatchState.WaitingToStart, Timestamp(1d));
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

        [Test]
        public void TimestampAndSnapshot_ConstructorsRejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(() => new MatchTimestamp(Guid.Empty, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchTimestamp(ClockEpoch, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchStateSnapshot(
                    MatchState.EnteringMap,
                    double.NaN,
                    0d,
                    ClockEpoch));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MatchStateSnapshot(
                    MatchState.InProgress,
                    2d,
                    1d,
                    ClockEpoch));
        }

        [Test]
        public void LongRunningClock_PreservesSubSecondElapsedPrecision()
        {
            const double startTimestamp = 10_000_000d;
            var stateMachine = CreateStateMachine(startTimestamp);
            stateMachine.TryTransition(
                MatchState.WaitingToStart,
                Timestamp(startTimestamp + 1d));
            stateMachine.TryTransition(
                MatchState.InProgress,
                Timestamp(startTimestamp + 2d));

            double elapsed = stateMachine.GetElapsedSeconds(
                Timestamp(startTimestamp + 2.125d));

            Assert.AreEqual(0.125d, elapsed, 1e-9d);
        }

        private static MatchStateMachine CreateStateMachine(double seconds = 0d)
        {
            return new MatchStateMachine(MatchState.EnteringMap, Timestamp(seconds));
        }

        private static MatchTimestamp Timestamp(double seconds)
        {
            return new MatchTimestamp(ClockEpoch, seconds);
        }
    }
}
