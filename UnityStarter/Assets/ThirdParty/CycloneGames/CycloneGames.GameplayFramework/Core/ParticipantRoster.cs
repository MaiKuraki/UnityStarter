using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.GameplayFramework.Core
{
    public enum ParticipantCategory : byte
    {
        Player = 0,
        Spectator = 1,
    }

    public enum ParticipantRegistrationResult : byte
    {
        Success = 0,
        InvalidParticipantId = 1,
        AlreadyRegistered = 2,
        PlayerCapacityReached = 3,
        SpectatorCapacityReached = 4,
    }

    public enum ParticipantRemovalResult : byte
    {
        Success = 0,
        NotRegistered = 1,
    }

    public enum ParticipantCategoryChangeResult : byte
    {
        Success = 0,
        Unchanged = 1,
        NotRegistered = 2,
        PlayerCapacityReached = 3,
        SpectatorCapacityReached = 4,
    }

    /// <summary>
    /// Bounded participant membership owned by the thread that constructs it. The roster does
    /// not lock because gameplay mutation is serialized by its World owner.
    /// </summary>
    public sealed class ParticipantRoster
    {
        public const int MaximumSupportedParticipants = 100_000;

        private readonly Dictionary<int, ParticipantCategory> participants;
        private readonly int ownerThreadId;
        private int playerCount;
        private int spectatorCount;

        public ParticipantRoster(int maximumPlayers = 16, int maximumSpectators = 4)
        {
            if (maximumPlayers < 0 || maximumPlayers > MaximumSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPlayers));
            }

            if (maximumSpectators < 0 || maximumSpectators > MaximumSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSpectators));
            }

            if ((long)maximumPlayers + maximumSpectators > MaximumSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSpectators),
                    "Combined player and spectator capacity exceeds the supported limit.");
            }

            MaximumPlayers = maximumPlayers;
            MaximumSpectators = maximumSpectators;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            participants = new Dictionary<int, ParticipantCategory>(
                Math.Min(maximumPlayers + maximumSpectators, 64));
        }

        public int MaximumPlayers { get; }
        public int MaximumSpectators { get; }
        public int PlayerCount
        {
            get
            {
                EnsureOwnerThread();
                return playerCount;
            }
        }

        public int SpectatorCount
        {
            get
            {
                EnsureOwnerThread();
                return spectatorCount;
            }
        }

        public int Count
        {
            get
            {
                EnsureOwnerThread();
                return playerCount + spectatorCount;
            }
        }

        public void AssertOwnerThread()
        {
            EnsureOwnerThread();
        }

        public ParticipantRegistrationResult EvaluateRegistration(
            int participantId,
            ParticipantCategory category)
        {
            EnsureOwnerThread();
            ValidateCategory(category);
            if (participantId < 0)
            {
                return ParticipantRegistrationResult.InvalidParticipantId;
            }

            if (participants.ContainsKey(participantId))
            {
                return ParticipantRegistrationResult.AlreadyRegistered;
            }

            return HasCapacity(category)
                ? ParticipantRegistrationResult.Success
                : category == ParticipantCategory.Spectator
                    ? ParticipantRegistrationResult.SpectatorCapacityReached
                    : ParticipantRegistrationResult.PlayerCapacityReached;
        }

        public ParticipantRegistrationResult Register(
            int participantId,
            ParticipantCategory category)
        {
            ParticipantRegistrationResult result = EvaluateRegistration(participantId, category);
            if (result != ParticipantRegistrationResult.Success)
            {
                return result;
            }

            participants.Add(participantId, category);
            Increment(category);
            return ParticipantRegistrationResult.Success;
        }

        public ParticipantRemovalResult Remove(int participantId)
        {
            EnsureOwnerThread();
            if (!participants.TryGetValue(participantId, out ParticipantCategory category))
            {
                return ParticipantRemovalResult.NotRegistered;
            }

            participants.Remove(participantId);
            Decrement(category);
            return ParticipantRemovalResult.Success;
        }

        public ParticipantCategoryChangeResult ChangeCategory(
            int participantId,
            ParticipantCategory category)
        {
            EnsureOwnerThread();
            ValidateCategory(category);
            if (!participants.TryGetValue(participantId, out ParticipantCategory previousCategory))
            {
                return ParticipantCategoryChangeResult.NotRegistered;
            }

            if (previousCategory == category)
            {
                return ParticipantCategoryChangeResult.Unchanged;
            }

            if (!HasCapacity(category))
            {
                return category == ParticipantCategory.Spectator
                    ? ParticipantCategoryChangeResult.SpectatorCapacityReached
                    : ParticipantCategoryChangeResult.PlayerCapacityReached;
            }

            participants[participantId] = category;
            Decrement(previousCategory);
            Increment(category);
            return ParticipantCategoryChangeResult.Success;
        }

        public bool Contains(int participantId)
        {
            EnsureOwnerThread();
            return participants.ContainsKey(participantId);
        }

        public bool TryGetCategory(int participantId, out ParticipantCategory category)
        {
            EnsureOwnerThread();
            return participants.TryGetValue(participantId, out category);
        }

        public bool AtCapacity(ParticipantCategory category)
        {
            EnsureOwnerThread();
            return !HasCapacity(category);
        }

        private bool HasCapacity(ParticipantCategory category)
        {
            ValidateCategory(category);
            return category == ParticipantCategory.Spectator
                ? spectatorCount < MaximumSpectators
                : playerCount < MaximumPlayers;
        }

        private void Increment(ParticipantCategory category)
        {
            if (category == ParticipantCategory.Spectator)
            {
                spectatorCount++;
            }
            else
            {
                playerCount++;
            }
        }

        private void Decrement(ParticipantCategory category)
        {
            if (category == ParticipantCategory.Spectator)
            {
                spectatorCount--;
            }
            else
            {
                playerCount--;
            }
        }

        private static void ValidateCategory(ParticipantCategory category)
        {
            if (category != ParticipantCategory.Player && category != ParticipantCategory.Spectator)
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "ParticipantRoster may only be accessed by its owner thread.");
            }
        }
    }
}
