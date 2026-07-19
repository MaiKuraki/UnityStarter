using System;
using System.Runtime.Serialization;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Versioned, bounded DTO for persistence and network adapters. Restore accepts only the
    /// current schema so adapters cannot silently interpret an incompatible payload.
    /// </summary>
    [DataContract]
    public sealed class PlayerStateSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [DataMember(Order = 2)] public string PlayerName { get; set; }
        [DataMember(Order = 3)] public int PlayerId { get; set; }
        [DataMember(Order = 4)] public bool IsSpectator { get; set; }
    }

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
            if (identityLockOwner != null && spectator != bIsSpectator)
            {
                throw new InvalidOperationException(
                    "Spectator status must be changed through the registered GameSession.");
            }

            bIsSpectator = spectator;
        }

        internal void SetRegisteredSpectatorStatus(object owner, bool spectator)
        {
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

        public virtual void CopyProperties(PlayerState other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (identityLockOwner != null && playerId != other.playerId)
            {
                throw new InvalidOperationException(
                    "PlayerId cannot change while the PlayerState is registered in a GameSession.");
            }

            if (identityLockOwner != null && bIsSpectator != other.bIsSpectator)
            {
                throw new InvalidOperationException(
                    "Spectator status must be changed through the registered GameSession.");
            }

            playerName = other.playerName;
            playerId = other.playerId;
            bIsSpectator = other.bIsSpectator;
        }

        public virtual PlayerStateSnapshot CaptureSnapshot()
        {
            return new PlayerStateSnapshot
            {
                SchemaVersion = PlayerStateSnapshot.CurrentSchemaVersion,
                PlayerName = playerName,
                PlayerId = playerId,
                IsSpectator = bIsSpectator,
            };
        }

        public virtual bool TryRestoreSnapshot(PlayerStateSnapshot snapshot, out string error)
        {
            if (snapshot == null)
            {
                error = "PlayerState snapshot is required.";
                return false;
            }

            if (snapshot.SchemaVersion != PlayerStateSnapshot.CurrentSchemaVersion)
            {
                error = $"Unsupported PlayerState schema version {snapshot.SchemaVersion}.";
                return false;
            }

            if (snapshot.PlayerId < 0)
            {
                error = "PlayerId cannot be negative.";
                return false;
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

            if (snapshot.PlayerName != null &&
                snapshot.PlayerName.Length > PlayerLoginRequest.MaxPlayerNameLength)
            {
                error = $"PlayerName exceeds {PlayerLoginRequest.MaxPlayerNameLength} characters.";
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
