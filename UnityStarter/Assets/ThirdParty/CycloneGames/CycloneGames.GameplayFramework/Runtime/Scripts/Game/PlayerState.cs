using System;
using CycloneGames.GameplayFramework.Core;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Stable participant identity and shared state. It survives Pawn replacement but remains
    /// scoped to one World unless an explicit travel adapter copies a snapshot.
    /// </summary>
    public class PlayerState : Actor
    {
        [SerializeField] private string playerName;

        private int playerId;
        private bool bIsSpectator;
        private Pawn pawnPrivate;
        private object identityLockOwner;
        private Action<PlayerState, Pawn, Pawn> pawnSetObservers;

        public event Action<PlayerState, Pawn, Pawn> OnPawnSetEvent
        {
            add
            {
                AssertActorOwnerThread();
                pawnSetObservers += value;
            }
            remove
            {
                AssertActorOwnerThread();
                pawnSetObservers -= value;
            }
        }

        public Pawn GetPawn()
        {
            AssertActorOwnerThread();
            return pawnPrivate;
        }

        public T GetPawn<T>() where T : Pawn
        {
            AssertActorOwnerThread();
            return pawnPrivate as T;
        }

        internal Pawn SetPawnSilently(Pawn newPawn)
        {
            AssertActorOwnerThread();
            Pawn previousPawn = pawnPrivate;
            pawnPrivate = newPawn;
            return previousPawn;
        }

        internal void PublishPawnChanged(Pawn newPawn, Pawn oldPawn)
        {
            AssertActorOwnerThread();
            if (!ReferenceEquals(newPawn, oldPawn))
            {
                pawnSetObservers?.Invoke(this, newPawn, oldPawn);
            }
        }

        public string GetPlayerName()
        {
            AssertActorOwnerThread();
            return playerName;
        }

        public void SetPlayerName(string newName)
        {
            AssertActorOwnerThread();
            if (newName != null && newName.Length > PlayerLoginRequest.MaxPlayerNameLength)
            {
                throw new ArgumentException(
                    $"Player name exceeds {PlayerLoginRequest.MaxPlayerNameLength} characters.",
                    nameof(newName));
            }

            playerName = newName;
        }

        public int GetPlayerId()
        {
            AssertActorOwnerThread();
            return playerId;
        }

        public bool IsIdentityLocked
        {
            get
            {
                AssertActorOwnerThread();
                return identityLockOwner != null;
            }
        }

        public void SetPlayerId(int newId)
        {
            AssertActorOwnerThread();
            if (newId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newId));
            }

            if (identityLockOwner != null && newId != playerId)
            {
                throw new InvalidOperationException(
                    "PlayerId cannot change while the PlayerState is registered in a GameSession.");
            }

            playerId = newId;
        }

        internal void LockIdentity(object owner, int expectedPlayerId)
        {
            AssertActorOwnerThread();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (identityLockOwner != null)
            {
                throw new InvalidOperationException(
                    "PlayerState is already registered in a GameSession.");
            }

            if (playerId != expectedPlayerId)
            {
                throw new InvalidOperationException("PlayerState identity changed during session registration.");
            }

            identityLockOwner = owner;
        }

        internal void UnlockIdentity(object owner)
        {
            AssertActorOwnerThread();
            if (identityLockOwner == null)
            {
                return;
            }

            if (!ReferenceEquals(identityLockOwner, owner))
            {
                throw new InvalidOperationException(
                    "Only the owning GameSession can unlock PlayerState identity.");
            }

            identityLockOwner = null;
        }

        public bool IsSpectator()
        {
            AssertActorOwnerThread();
            return bIsSpectator;
        }

        protected internal void SetIsSpectator(bool spectator)
        {
            AssertActorOwnerThread();
            if (identityLockOwner != null && spectator != bIsSpectator)
            {
                throw new InvalidOperationException(
                    "Spectator status must be changed through the registered GameSession.");
            }

            bIsSpectator = spectator;
        }

        internal void SetRegisteredSpectatorStatus(object owner, bool spectator)
        {
            AssertActorOwnerThread();
            if (identityLockOwner == null)
            {
                throw new InvalidOperationException("PlayerState is not registered in a GameSession.");
            }

            if (!ReferenceEquals(identityLockOwner, owner))
            {
                throw new InvalidOperationException(
                    "Only the owning GameSession can change registered spectator status.");
            }

            bIsSpectator = spectator;
        }

        public void CopyProperties(PlayerState other)
        {
            AssertActorOwnerThread();
            if (ReferenceEquals(other, null) || other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            other.AssertActorOwnerThread();

            PlayerStateSnapshot snapshot = other.CaptureSnapshot();
            if (!snapshot.TryValidate(out PlayerStateSnapshotValidationResult validationResult))
            {
                throw new InvalidOperationException(
                    $"Source PlayerState contains invalid snapshot data: {validationResult}.");
            }

            if (identityLockOwner != null && playerId != snapshot.PlayerId)
            {
                throw new InvalidOperationException(
                    "PlayerId cannot change while the PlayerState is registered in a GameSession.");
            }

            if (identityLockOwner != null && bIsSpectator != snapshot.IsSpectator)
            {
                throw new InvalidOperationException(
                    "Spectator status must be changed through the registered GameSession.");
            }

            playerName = snapshot.PlayerName;
            playerId = snapshot.PlayerId;
            bIsSpectator = snapshot.IsSpectator;
        }

        public PlayerStateSnapshot CaptureSnapshot()
        {
            AssertActorOwnerThread();
            return new PlayerStateSnapshot(playerName, playerId, bIsSpectator);
        }

        public bool TryRestoreSnapshot(PlayerStateSnapshot snapshot, out string error)
        {
            AssertActorOwnerThread();
            if (!snapshot.TryValidate(out PlayerStateSnapshotValidationResult validationResult))
            {
                switch (validationResult)
                {
                    case PlayerStateSnapshotValidationResult.InvalidPlayerId:
                        error = "PlayerId cannot be negative.";
                        return false;
                    case PlayerStateSnapshotValidationResult.PlayerNameTooLong:
                        error = $"PlayerName exceeds {PlayerLoginRequest.MaxPlayerNameLength} characters.";
                        return false;
                    default:
                        error = "PlayerState snapshot validation failed.";
                        return false;
                }
            }

            if (identityLockOwner != null && snapshot.PlayerId != playerId)
            {
                error = "PlayerId cannot change while the PlayerState is registered in a GameSession.";
                return false;
            }

            if (identityLockOwner != null && snapshot.IsSpectator != bIsSpectator)
            {
                error = "Spectator status must be changed through the registered GameSession.";
                return false;
            }

            playerName = snapshot.PlayerName;
            playerId = snapshot.PlayerId;
            bIsSpectator = snapshot.IsSpectator;
            error = null;
            return true;
        }

        protected override void OnDestroy()
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            pawnSetObservers = null;
            try
            {
                base.OnDestroy();
            }
            catch (Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "PlayerState base Actor cleanup failed during destruction.");
            }

            pawnPrivate = null;
            terminalExceptions.ThrowIfCaptured();
        }
    }
}
