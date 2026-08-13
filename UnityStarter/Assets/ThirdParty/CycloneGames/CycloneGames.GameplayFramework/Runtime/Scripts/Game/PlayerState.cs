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

        public event Action<PlayerState, Pawn, Pawn> OnPawnSetEvent;

        public Pawn GetPawn() => pawnPrivate;
        public T GetPawn<T>() where T : Pawn => pawnPrivate as T;

        internal Pawn SetPawnSilently(Pawn newPawn)
        {
            Pawn previousPawn = pawnPrivate;
            pawnPrivate = newPawn;
            return previousPawn;
        }

        internal void PublishPawnChanged(Pawn newPawn, Pawn oldPawn)
        {
            if (!ReferenceEquals(newPawn, oldPawn))
            {
                OnPawnSetEvent?.Invoke(this, newPawn, oldPawn);
            }
        }

        public string GetPlayerName() => playerName;

        public void SetPlayerName(string newName)
        {
            World?.AssertOwnerThread();
            if (newName != null && newName.Length > PlayerLoginRequest.MaxPlayerNameLength)
            {
                throw new ArgumentException(
                    $"Player name exceeds {PlayerLoginRequest.MaxPlayerNameLength} characters.",
                    nameof(newName));
            }

            playerName = newName;
        }

        public int GetPlayerId() => playerId;
        public bool IsIdentityLocked => identityLockOwner != null;

        public void SetPlayerId(int newId)
        {
            World?.AssertOwnerThread();
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
            World?.AssertOwnerThread();
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
            World?.AssertOwnerThread();
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

        public bool IsSpectator() => bIsSpectator;

        protected internal void SetIsSpectator(bool spectator)
        {
            World?.AssertOwnerThread();
            if (identityLockOwner != null && spectator != bIsSpectator)
            {
                throw new InvalidOperationException(
                    "Spectator status must be changed through the registered GameSession.");
            }

            bIsSpectator = spectator;
        }

        internal void SetRegisteredSpectatorStatus(object owner, bool spectator)
        {
            World?.AssertOwnerThread();
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
            World?.AssertOwnerThread();
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            other.World?.AssertOwnerThread();
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
            World?.AssertOwnerThread();
            return new PlayerStateSnapshot(playerName, playerId, bIsSpectator);
        }

        public bool TryRestoreSnapshot(PlayerStateSnapshot snapshot, out string error)
        {
            World?.AssertOwnerThread();
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
            OnPawnSetEvent = null;
            base.OnDestroy();
            pawnPrivate = null;
            identityLockOwner = null;
        }
    }
}
