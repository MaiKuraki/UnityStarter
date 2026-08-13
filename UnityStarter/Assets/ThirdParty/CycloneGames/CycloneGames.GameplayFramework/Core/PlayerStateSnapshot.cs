namespace CycloneGames.GameplayFramework.Core
{
    public enum PlayerStateSnapshotValidationResult : byte
    {
        Valid = 0,
        InvalidPlayerId = 1,
        PlayerNameTooLong = 2,
    }

    /// <summary>
    /// Immutable runtime participant-state value. Persistence and transport schema versions
    /// belong to their owning adapter or codec rather than this in-memory snapshot.
    /// </summary>
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(string playerName, int playerId, bool isSpectator)
        {
            PlayerName = playerName;
            PlayerId = playerId;
            IsSpectator = isSpectator;
        }

        public string PlayerName { get; }
        public int PlayerId { get; }
        public bool IsSpectator { get; }

        public bool TryValidate(out PlayerStateSnapshotValidationResult result)
        {
            if (PlayerId < 0)
            {
                result = PlayerStateSnapshotValidationResult.InvalidPlayerId;
                return false;
            }

            if (PlayerName != null && PlayerName.Length > PlayerLoginRequest.MaxPlayerNameLength)
            {
                result = PlayerStateSnapshotValidationResult.PlayerNameTooLong;
                return false;
            }

            result = PlayerStateSnapshotValidationResult.Valid;
            return true;
        }
    }
}
