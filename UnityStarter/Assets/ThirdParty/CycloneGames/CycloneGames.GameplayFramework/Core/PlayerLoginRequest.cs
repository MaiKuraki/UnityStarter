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

    public enum PlayerLoginRequestValidationResult : byte
    {
        Valid = 0,
        InvalidPlayerId = 1,
        PlayerNameTooLong = 2,
        RemoteAddressTooLong = 3,
        OptionsTooLong = 4,
        LocalRequestWithRemoteAddress = 5,
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
            PlayerLoginRequestValidationResult result = Validate();
            if (result == PlayerLoginRequestValidationResult.Valid)
            {
                error = null;
                return true;
            }

            error = GetValidationError(result);
            return false;
        }

        /// <summary>
        /// Allocation-free ingress validation. Transport adapters that only need a machine
        /// decision can use this instead of building an error string.
        /// </summary>
        public PlayerLoginRequestValidationResult Validate()
        {
            if (PlayerId < 0)
            {
                return PlayerLoginRequestValidationResult.InvalidPlayerId;
            }

            if (PlayerName != null && PlayerName.Length > MaxPlayerNameLength)
            {
                return PlayerLoginRequestValidationResult.PlayerNameTooLong;
            }

            if (RemoteAddress != null && RemoteAddress.Length > MaxRemoteAddressLength)
            {
                return PlayerLoginRequestValidationResult.RemoteAddressTooLong;
            }

            if (Options != null && Options.Length > MaxOptionsLength)
            {
                return PlayerLoginRequestValidationResult.OptionsTooLong;
            }

            if (IsLocal && !string.IsNullOrEmpty(RemoteAddress))
            {
                return PlayerLoginRequestValidationResult.LocalRequestWithRemoteAddress;
            }

            return PlayerLoginRequestValidationResult.Valid;
        }

        private static string GetValidationError(PlayerLoginRequestValidationResult result)
        {
            switch (result)
            {
                case PlayerLoginRequestValidationResult.InvalidPlayerId:
                    return "PlayerId cannot be negative.";
                case PlayerLoginRequestValidationResult.PlayerNameTooLong:
                    return $"PlayerName exceeds {MaxPlayerNameLength} characters.";
                case PlayerLoginRequestValidationResult.RemoteAddressTooLong:
                    return $"RemoteAddress exceeds {MaxRemoteAddressLength} characters.";
                case PlayerLoginRequestValidationResult.OptionsTooLong:
                    return $"Options exceeds {MaxOptionsLength} characters.";
                case PlayerLoginRequestValidationResult.LocalRequestWithRemoteAddress:
                    return "A local login request cannot include a remote address.";
                default:
                    return "Player login request is invalid.";
            }
        }
    }
}
