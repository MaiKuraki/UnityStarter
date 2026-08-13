using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Core.Tests
{
    public sealed class ParticipantRosterTests
    {
        [Test]
        public void RegistrationAndRemoval_MaintainBoundedCategoryCounts()
        {
            var roster = new ParticipantRoster(maximumPlayers: 1, maximumSpectators: 1);

            Assert.AreEqual(
                ParticipantRegistrationResult.Success,
                roster.Register(10, ParticipantCategory.Player));
            Assert.AreEqual(
                ParticipantRegistrationResult.PlayerCapacityReached,
                roster.Register(11, ParticipantCategory.Player));
            Assert.AreEqual(
                ParticipantRegistrationResult.Success,
                roster.Register(12, ParticipantCategory.Spectator));
            Assert.AreEqual(1, roster.PlayerCount);
            Assert.AreEqual(1, roster.SpectatorCount);
            Assert.AreEqual(2, roster.Count);

            Assert.AreEqual(ParticipantRemovalResult.Success, roster.Remove(10));
            Assert.AreEqual(ParticipantRemovalResult.NotRegistered, roster.Remove(10));
            Assert.AreEqual(0, roster.PlayerCount);
            Assert.AreEqual(1, roster.SpectatorCount);
        }

        [Test]
        public void CategoryChange_IsAtomicWhenTargetCategoryIsFull()
        {
            var roster = new ParticipantRoster(maximumPlayers: 1, maximumSpectators: 1);
            Assert.AreEqual(
                ParticipantRegistrationResult.Success,
                roster.Register(1, ParticipantCategory.Player));
            Assert.AreEqual(
                ParticipantRegistrationResult.Success,
                roster.Register(2, ParticipantCategory.Spectator));

            Assert.AreEqual(
                ParticipantCategoryChangeResult.SpectatorCapacityReached,
                roster.ChangeCategory(1, ParticipantCategory.Spectator));
            Assert.IsTrue(roster.TryGetCategory(1, out ParticipantCategory category));
            Assert.AreEqual(ParticipantCategory.Player, category);
            Assert.AreEqual(1, roster.PlayerCount);
            Assert.AreEqual(1, roster.SpectatorCount);
        }

        [Test]
        public void AccessFromNonOwnerThread_IsRejected()
        {
            var roster = new ParticipantRoster();
            Exception containsException = null;
            Exception countException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    roster.Contains(1);
                }
                catch (Exception exception)
                {
                    containsException = exception;
                }

                try
                {
                    _ = roster.Count;
                }
                catch (Exception exception)
                {
                    countException = exception;
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");

            Assert.IsInstanceOf<InvalidOperationException>(containsException);
            Assert.IsInstanceOf<InvalidOperationException>(countException);
        }

        [Test]
        public void Constructor_RejectsCombinedCapacityAboveSafetyLimit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ParticipantRoster(
                    ParticipantRoster.MaximumSupportedParticipants,
                    maximumSpectators: 1));
        }

        [Test]
        public void InvalidCategory_IsRejectedBeforeMembershipOrCapacityEvaluation()
        {
            var roster = new ParticipantRoster(maximumPlayers: 0, maximumSpectators: 0);
            var invalidCategory = (ParticipantCategory)byte.MaxValue;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                roster.EvaluateRegistration(-1, invalidCategory));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                roster.ChangeCategory(999, invalidCategory));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                roster.AtCapacity(invalidCategory));
        }

        [Test]
        public void RegisterRemove_WarmedEditorMonoSteadyStateAllocatesNoManagedBytes()
        {
            var roster = new ParticipantRoster(maximumPlayers: 1, maximumSpectators: 0);

            // Warm dictionary storage and all runtime call paths before measuring Editor Mono.
            Assert.AreEqual(
                ParticipantRegistrationResult.Success,
                roster.Register(1, ParticipantCategory.Player));
            Assert.AreEqual(ParticipantRemovalResult.Success, roster.Remove(1));
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 10_000; i++)
            {
                ParticipantRegistrationResult registration = roster.Register(
                    1,
                    ParticipantCategory.Player);
                ParticipantRemovalResult removal = roster.Remove(1);
                if (registration != ParticipantRegistrationResult.Success ||
                    removal != ParticipantRemovalResult.Success)
                {
                    Assert.Fail("Participant roster register/remove invariant failed.");
                }
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(
                0,
                allocatedBytes,
                "This assertion covers warmed managed allocations in Editor Mono only; it does not represent IL2CPP or target-platform profiling.");
        }
    }
}
