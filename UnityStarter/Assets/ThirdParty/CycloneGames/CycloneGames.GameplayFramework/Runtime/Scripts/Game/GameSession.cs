using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Unity-facing participant session. Stable membership and capacity rules are delegated to
    /// ParticipantRoster while controller and PlayerState ownership remains in this facade.
    /// </summary>
    public sealed class GameSession : IGameSession
    {
        private readonly struct RuntimeRosterEntry
        {
            public RuntimeRosterEntry(
                int participantId,
                ParticipantCategory category,
                PlayerState playerState)
            {
                ParticipantId = participantId;
                Category = category;
                PlayerState = playerState;
            }

            public int ParticipantId { get; }
            public ParticipantCategory Category { get; }
            public PlayerState PlayerState { get; }
        }

        private readonly ParticipantRoster participantRoster;
        private readonly Dictionary<PlayerController, RuntimeRosterEntry> runtimeRoster;
        private readonly int ownerThreadId;

        public GameSession(int maxPlayers = 16, int maxSpectators = 4)
        {
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            participantRoster = new ParticipantRoster(maxPlayers, maxSpectators);
            runtimeRoster = new Dictionary<PlayerController, RuntimeRosterEntry>(
                Math.Min(maxPlayers + maxSpectators, 64));
        }

        public int MaxPlayers => participantRoster.MaximumPlayers;
        public int MaxSpectators => participantRoster.MaximumSpectators;
        public int PlayerCount
        {
            get
            {
                AssertOwnerThread();
                return participantRoster.PlayerCount;
            }
        }

        public int SpectatorCount
        {
            get
            {
                AssertOwnerThread();
                return participantRoster.SpectatorCount;
            }
        }

        public bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
        {
            AssertOwnerThread();
            if (!request.TryValidate(out errorMessage))
            {
                return false;
            }

            ParticipantRegistrationResult result = participantRoster.EvaluateRegistration(
                request.PlayerId,
                ToCategory(request.IsSpectator));
            if (result == ParticipantRegistrationResult.Success)
            {
                errorMessage = null;
                return true;
            }

            errorMessage = GetRegistrationError(result, request.PlayerId);
            return false;
        }

        public bool TryRegisterPlayer(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            AssertOwnerThread();
            if (ReferenceEquals(playerController, null))
            {
                errorMessage = "PlayerController is required.";
                return false;
            }

            if (runtimeRoster.ContainsKey(playerController))
            {
                errorMessage = "PlayerController is already registered.";
                return false;
            }

            PlayerState playerState = playerController.GetPlayerState();
            if (playerState == null)
            {
                errorMessage = "PlayerController requires a PlayerState before session registration.";
                return false;
            }

            int participantId = playerState.GetPlayerId();
            if (playerState.IsIdentityLocked)
            {
                errorMessage = "PlayerState is already registered in a GameSession.";
                return false;
            }

            ParticipantCategory category = ToCategory(spectator);
            ParticipantRegistrationResult admission = participantRoster.EvaluateRegistration(
                participantId,
                category);
            if (admission != ParticipantRegistrationResult.Success)
            {
                errorMessage = GetRegistrationError(admission, participantId);
                return false;
            }

            bool previousSpectator = playerState.IsSpectator();
            bool identityLocked = false;
            bool coreRegistered = false;
            bool runtimeRegistered = false;
            try
            {
                playerState.SetIsSpectator(spectator);
                playerState.LockIdentity(this, participantId);
                identityLocked = true;

                ParticipantRegistrationResult registration = participantRoster.Register(
                    participantId,
                    category);
                if (registration != ParticipantRegistrationResult.Success)
                {
                    playerState.UnlockIdentity(this);
                    identityLocked = false;
                    playerState.SetIsSpectator(previousSpectator);
                    errorMessage = GetRegistrationError(registration, participantId);
                    return false;
                }

                coreRegistered = true;
                runtimeRoster.Add(
                    playerController,
                    new RuntimeRosterEntry(participantId, category, playerState));
                runtimeRegistered = true;
                errorMessage = null;
                return true;
            }
            catch
            {
                RollbackRegistration(
                    playerController,
                    playerState,
                    participantId,
                    previousSpectator,
                    identityLocked,
                    coreRegistered,
                    runtimeRegistered);
                throw;
            }
        }

        public bool UnregisterPlayer(PlayerController playerController)
        {
            AssertOwnerThread();
            if (ReferenceEquals(playerController, null) ||
                !runtimeRoster.TryGetValue(playerController, out RuntimeRosterEntry entry))
            {
                return false;
            }

            ParticipantRemovalResult removal = participantRoster.Remove(entry.ParticipantId);
            if (removal != ParticipantRemovalResult.Success)
            {
                throw new InvalidOperationException(
                    "Runtime and Core participant rosters are inconsistent.");
            }

            runtimeRoster.Remove(playerController);
            try
            {
                // Unity may already have destroyed the PlayerState while the managed
                // participant identity is still present in the runtime roster. Session
                // ownership must still commit, but there is no live identity lock to mutate.
                if (!ReferenceEquals(entry.PlayerState, null) && entry.PlayerState != null)
                {
                    entry.PlayerState.UnlockIdentity(this);
                }
                return true;
            }
            catch
            {
                ParticipantRegistrationResult registration = participantRoster.Register(
                    entry.ParticipantId,
                    entry.Category);
                if (registration != ParticipantRegistrationResult.Success)
                {
                    throw new InvalidOperationException(
                        "Participant roster rollback failed after identity unlock failure.");
                }

                runtimeRoster.Add(playerController, entry);
                throw;
            }
        }

        public bool TrySetSpectatorStatus(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            AssertOwnerThread();
            if (ReferenceEquals(playerController, null) ||
                !runtimeRoster.TryGetValue(playerController, out RuntimeRosterEntry entry))
            {
                errorMessage = "PlayerController is not registered.";
                return false;
            }

            ParticipantCategory nextCategory = ToCategory(spectator);
            ParticipantCategoryChangeResult result = participantRoster.ChangeCategory(
                entry.ParticipantId,
                nextCategory);
            if (result == ParticipantCategoryChangeResult.Unchanged)
            {
                errorMessage = null;
                return true;
            }

            if (result != ParticipantCategoryChangeResult.Success)
            {
                errorMessage = GetCategoryChangeError(result);
                return false;
            }

            try
            {
                entry.PlayerState.SetRegisteredSpectatorStatus(this, spectator);
                runtimeRoster[playerController] = new RuntimeRosterEntry(
                    entry.ParticipantId,
                    nextCategory,
                    entry.PlayerState);
                errorMessage = null;
                return true;
            }
            catch
            {
                ParticipantCategoryChangeResult rollback = participantRoster.ChangeCategory(
                    entry.ParticipantId,
                    entry.Category);
                if (rollback != ParticipantCategoryChangeResult.Success)
                {
                    throw new InvalidOperationException(
                        "Participant category rollback failed after Runtime state mutation failure.");
                }

                if (entry.PlayerState.IsSpectator() !=
                    (entry.Category == ParticipantCategory.Spectator))
                {
                    entry.PlayerState.SetRegisteredSpectatorStatus(
                        this,
                        entry.Category == ParticipantCategory.Spectator);
                }
                runtimeRoster[playerController] = entry;
                throw;
            }
        }

        public bool ContainsPlayer(PlayerController playerController)
        {
            AssertOwnerThread();
            return !ReferenceEquals(playerController, null) && runtimeRoster.ContainsKey(playerController);
        }

        public bool AtCapacity(bool spectator)
        {
            AssertOwnerThread();
            return participantRoster.AtCapacity(ToCategory(spectator));
        }

        public void HandleMatchHasStarted()
        {
            AssertOwnerThread();
        }

        public void HandleMatchHasEnded()
        {
            AssertOwnerThread();
        }

        private void RollbackRegistration(
            PlayerController playerController,
            PlayerState playerState,
            int participantId,
            bool previousSpectator,
            bool identityLocked,
            bool coreRegistered,
            bool runtimeRegistered)
        {
            if (runtimeRegistered)
            {
                runtimeRoster.Remove(playerController);
            }

            if (coreRegistered)
            {
                ParticipantRemovalResult removal = participantRoster.Remove(participantId);
                if (removal != ParticipantRemovalResult.Success)
                {
                    throw new InvalidOperationException("Participant registration rollback failed.");
                }
            }

            if (identityLocked)
            {
                playerState.UnlockIdentity(this);
            }

            playerState.SetIsSpectator(previousSpectator);
        }

        private static ParticipantCategory ToCategory(bool spectator)
        {
            return spectator ? ParticipantCategory.Spectator : ParticipantCategory.Player;
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "GameSession may only be accessed by its owner thread.");
            }
        }

        private static string GetRegistrationError(
            ParticipantRegistrationResult result,
            int participantId)
        {
            switch (result)
            {
                case ParticipantRegistrationResult.InvalidParticipantId:
                    return $"PlayerId {participantId} is invalid.";
                case ParticipantRegistrationResult.AlreadyRegistered:
                    return $"PlayerId {participantId} is already registered.";
                case ParticipantRegistrationResult.PlayerCapacityReached:
                    return "Player capacity reached.";
                case ParticipantRegistrationResult.SpectatorCapacityReached:
                    return "Spectator capacity reached.";
                default:
                    return "Participant registration failed.";
            }
        }

        private static string GetCategoryChangeError(ParticipantCategoryChangeResult result)
        {
            switch (result)
            {
                case ParticipantCategoryChangeResult.NotRegistered:
                    return "PlayerController is not registered.";
                case ParticipantCategoryChangeResult.PlayerCapacityReached:
                    return "Player capacity reached.";
                case ParticipantCategoryChangeResult.SpectatorCapacityReached:
                    return "Spectator capacity reached.";
                default:
                    return "Participant category change failed.";
            }
        }
    }
}
