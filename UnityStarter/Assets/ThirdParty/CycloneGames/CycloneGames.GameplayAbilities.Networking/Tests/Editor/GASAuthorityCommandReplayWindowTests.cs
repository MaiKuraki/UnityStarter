using System;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASAuthorityCommandReplayWindowTests
    {
        [Test]
        public void CompletedCommand_IsExecutedOnceAndThenReturnsCachedResult()
        {
            var window = new GASAuthorityCommandReplayWindow(7u, capacity: 2);
            GASAbilityCommand command = CreateCommand(7u, 1u);

            Assert.That(window.Evaluate(in command, 101UL, out _), Is.EqualTo(GASCommandReplayDecision.Execute));
            GASCommandResult result = CreateResult(in command, GASCommandStatus.Accepted);
            window.Complete(in result);

            Assert.That(window.Evaluate(in command, 101UL, out GASCommandResult cached),
                Is.EqualTo(GASCommandReplayDecision.Duplicate));
            Assert.That(cached.Status, Is.EqualTo(GASCommandStatus.Accepted));
            Assert.That(window.HighestCompletedSequence, Is.EqualTo(1u));
        }

        [Test]
        public void ConflictingReplayAndSequenceGap_FailClosed()
        {
            var window = new GASAuthorityCommandReplayWindow(9u, capacity: 2);
            GASAbilityCommand first = CreateCommand(9u, 1u);
            Assert.That(window.Evaluate(in first, 501UL, out _), Is.EqualTo(GASCommandReplayDecision.Execute));
            GASCommandResult firstResult = CreateResult(in first, GASCommandStatus.Rejected);
            window.Complete(in firstResult);

            Assert.That(window.Evaluate(in first, 777UL, out _),
                Is.EqualTo(GASCommandReplayDecision.ConflictingReplay));

            GASAbilityCommand third = CreateCommand(9u, 3u);
            Assert.That(window.Evaluate(in third, 503UL, out _),
                Is.EqualTo(GASCommandReplayDecision.SequenceGap));
        }

        [Test]
        public void Capacity_EvictsOnlyOldCompletedResults()
        {
            var window = new GASAuthorityCommandReplayWindow(11u, capacity: 2);
            Complete(window, CreateCommand(11u, 1u), 1UL);
            Complete(window, CreateCommand(11u, 2u), 2UL);
            Complete(window, CreateCommand(11u, 3u), 3UL);

            GASAbilityCommand first = CreateCommand(11u, 1u);
            GASAbilityCommand second = CreateCommand(11u, 2u);
            Assert.That(window.Evaluate(in first, 1UL, out _), Is.EqualTo(GASCommandReplayDecision.TooOld));
            Assert.That(window.Evaluate(in second, 2UL, out _), Is.EqualTo(GASCommandReplayDecision.Duplicate));
        }

        [Test]
        public void Reset_RequiresANewEpochAndNoPendingExecution()
        {
            var window = new GASAuthorityCommandReplayWindow(3u);
            GASAbilityCommand command = CreateCommand(3u, 1u);
            Assert.That(window.Evaluate(in command, 1UL, out _), Is.EqualTo(GASCommandReplayDecision.Execute));
            Assert.Throws<InvalidOperationException>(() => window.Reset(4u));

            GASCommandResult result = CreateResult(in command, GASCommandStatus.AuthorityUnavailable);
            window.Complete(in result);
            window.Reset(4u);

            Assert.That(window.StreamEpoch, Is.EqualTo(4u));
            Assert.That(window.HighestCompletedSequence, Is.Zero);
            Assert.That(window.Evaluate(in command, 1UL, out _), Is.EqualTo(GASCommandReplayDecision.WrongEpoch));
        }

        private static void Complete(
            GASAuthorityCommandReplayWindow window,
            GASAbilityCommand command,
            ulong fingerprint)
        {
            Assert.That(window.Evaluate(in command, fingerprint, out _), Is.EqualTo(GASCommandReplayDecision.Execute));
            GASCommandResult result = CreateResult(in command, GASCommandStatus.Accepted);
            window.Complete(in result);
        }

        private static GASAbilityCommand CreateCommand(uint epoch, uint sequence)
        {
            return new GASAbilityCommand(
                epoch,
                sequence,
                new GASNetworkEntityId(100UL),
                new GASNetworkGrantId(200UL),
                GASAbilityCommandKind.Activate);
        }

        private static GASCommandResult CreateResult(in GASAbilityCommand command, GASCommandStatus status)
        {
            return new GASCommandResult(
                command.StreamEpoch,
                command.CommandSequence,
                command.Entity,
                command.Grant,
                command.Kind,
                status,
                10UL);
        }
    }
}
