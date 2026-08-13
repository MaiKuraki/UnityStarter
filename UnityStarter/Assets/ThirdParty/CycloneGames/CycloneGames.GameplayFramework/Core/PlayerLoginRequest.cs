namespace CycloneGames.GameplayFramework.Core
{
    public enum PlayerLoginStatus : byte
    {
        Success = 0,
        InvalidRequest = 1,
        NotAuthoritative = 2,
        WorldNotAcceptingPlayers = 3,
        Rejected = 4,
        AtCapacity = 5,
        SpawnFailed = 6,
        Cancelled = 7,
    }

    /// <summary>
    /// Bounded login input. Transport adapters authenticate and rate-limit requests before
    /// constructing this framework-level value.
    /// </summary>
    public readonly struct PlayerLoginRequest
    {
        public const int MaxPlayerNameLength = 64;
        public const int MaxRemoteAddressLength = 256;
        public const int MaxOptionsLength = 1024;

        public PlayerLoginRequest(
            int playerId,
            string playerName,
            bool isSpectator = false,
            string remoteAddress = null,
            string options = null,
            bool isLocal = false)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            IsSpectator = isSpectator;
            RemoteAddress = remoteAddress;
            Options = options;
            IsLocal = isLocal;
        }

        public int PlayerId { get; }
        public string PlayerName { get; }
        public bool IsSpectator { get; }
        public string RemoteAddress { get; }
        public string Options { get; }
        public bool IsLocal { get; }

        public bool TryValidate(out string error)
        {
            if (PlayerId < 0)
            {
                error = "PlayerId cannot be negative.";
                return false;
            }

            if (PlayerName != null && PlayerName.Length > MaxPlayerNameLength)
            {
                error = $"PlayerName exceeds {MaxPlayerNameLength} characters.";
                return false;
            }

            if (RemoteAddress != null && RemoteAddress.Length > MaxRemoteAddressLength)
            {
                error = $"RemoteAddress exceeds {MaxRemoteAddressLength} characters.";
                return false;
            }

            if (Options != null && Options.Length > MaxOptionsLength)
            {
                error = $"Options exceeds {MaxOptionsLength} characters.";
                return false;
            }

            if (IsLocal && !string.IsNullOrEmpty(RemoteAddress))
            {
                error = "A local login request cannot include a remote address.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
