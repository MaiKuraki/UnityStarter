using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayFramework.Runtime
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
    /// Bounded login input. Network adapters validate authentication and rate limits before
    /// creating this request; GameSession validates framework-level size and capacity limits.
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

    public readonly struct PlayerLoginResult
    {
        private PlayerLoginResult(PlayerLoginStatus status, PlayerController playerController, string error)
        {
            Status = status;
            PlayerController = playerController;
            Error = error;
        }

        public PlayerLoginStatus Status { get; }
        public PlayerController PlayerController { get; }
        public string Error { get; }
        public bool Succeeded => Status == PlayerLoginStatus.Success;

        public static PlayerLoginResult Success(PlayerController playerController)
        {
            return new PlayerLoginResult(PlayerLoginStatus.Success, playerController, null);
        }

        public static PlayerLoginResult Failure(PlayerLoginStatus status, string error)
        {
            if (status == PlayerLoginStatus.Success)
            {
                throw new ArgumentException("A failure result cannot use Success status.", nameof(status));
            }

            return new PlayerLoginResult(status, null, error);
        }
    }

    /// <summary>
    /// Authoritative admission and roster boundary. Calls are serialized on the World owner
    /// thread; network callbacks must marshal to that thread before invoking this contract.
    /// </summary>
    public interface IGameSession
    {
        int MaxPlayers { get; }
        int MaxSpectators { get; }
        int PlayerCount { get; }
        int SpectatorCount { get; }

        bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage);
        bool TryRegisterPlayer(PlayerController playerController, bool spectator, out string errorMessage);
        bool ContainsPlayer(PlayerController playerController);
        bool UnregisterPlayer(PlayerController playerController);
        bool TrySetSpectatorStatus(PlayerController playerController, bool spectator, out string errorMessage);
        void HandleMatchHasStarted();
        void HandleMatchHasEnded();
    }

    /// <summary>
    /// Identity-safe local roster implementation. It is intentionally not thread-safe because
    /// World mutation has a single main-thread owner.
    /// </summary>
    public class GameSession : IGameSession
    {
        public const int MaxSupportedParticipants = 100_000;

        private readonly struct RosterEntry
        {
            public RosterEntry(int playerId, bool spectator, PlayerState playerState)
            {
                PlayerId = playerId;
                Spectator = spectator;
                PlayerState = playerState;
            }

            public int PlayerId { get; }
            public bool Spectator { get; }
            public PlayerState PlayerState { get; }
        }

        private readonly Dictionary<PlayerController, RosterEntry> roster;
        private readonly Dictionary<int, PlayerController> playersById;
        private int playerCount;
        private int spectatorCount;

        public GameSession(int maxPlayers = 16, int maxSpectators = 4)
        {
            if (maxPlayers < 0 || maxPlayers > MaxSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            }

            if (maxSpectators < 0 || maxSpectators > MaxSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSpectators));
            }

            if ((long)maxPlayers + maxSpectators > MaxSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSpectators),
                    "Combined player and spectator capacity exceeds the supported limit.");
            }

            MaxPlayers = maxPlayers;
            MaxSpectators = maxSpectators;
            int initialCapacity = Math.Min(maxPlayers + maxSpectators, 64);
            roster = new Dictionary<PlayerController, RosterEntry>(initialCapacity);
            playersById = new Dictionary<int, PlayerController>(initialCapacity);
        }

        public int MaxPlayers { get; }
        public int MaxSpectators { get; }
        public int PlayerCount => playerCount;
        public int SpectatorCount => spectatorCount;

        public virtual bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
        {
            if (!request.TryValidate(out errorMessage))
            {
                return false;
            }

            if (AtCapacity(request.IsSpectator))
            {
                errorMessage = request.IsSpectator ? "Spectator capacity reached." : "Player capacity reached.";
                return false;
            }

            if (playersById.ContainsKey(request.PlayerId))
            {
                errorMessage = $"PlayerId {request.PlayerId} is already registered.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public virtual bool TryRegisterPlayer(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            if (ReferenceEquals(playerController, null))
            {
                errorMessage = "PlayerController is required.";
                return false;
            }

            if (roster.ContainsKey(playerController))
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

            int playerId = playerState.GetPlayerId();
            if (playerId < 0 || playersById.ContainsKey(playerId))
            {
                errorMessage = $"PlayerId {playerId} is invalid or already registered.";
                return false;
            }

            if (playerState.IsIdentityLocked)
            {
                errorMessage = "PlayerState is already registered in a GameSession.";
                return false;
            }

            if (AtCapacity(spectator))
            {
                errorMessage = spectator ? "Spectator capacity reached." : "Player capacity reached.";
                return false;
            }

            playerState.SetIsSpectator(spectator);
            playerState.LockIdentity(this, playerId);
            try
            {
                roster.Add(playerController, new RosterEntry(playerId, spectator, playerState));
                playersById.Add(playerId, playerController);
            }
            catch
            {
                roster.Remove(playerController);
                playersById.Remove(playerId);
                playerState.UnlockIdentity(this);
                throw;
            }
            if (spectator)
            {
                spectatorCount++;
            }
            else
            {
                playerCount++;
            }

            errorMessage = null;
            return true;
        }

        public virtual bool UnregisterPlayer(PlayerController playerController)
        {
            if (ReferenceEquals(playerController, null) ||
                !roster.TryGetValue(playerController, out RosterEntry entry))
            {
                return false;
            }

            roster.Remove(playerController);
            playersById.Remove(entry.PlayerId);
            entry.PlayerState?.UnlockIdentity(this);
            if (entry.Spectator)
            {
                spectatorCount--;
            }
            else
            {
                playerCount--;
            }

            return true;
        }

        public virtual bool TrySetSpectatorStatus(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            if (ReferenceEquals(playerController, null) ||
                !roster.TryGetValue(playerController, out RosterEntry entry))
            {
                errorMessage = "PlayerController is not registered.";
                return false;
            }

            if (entry.Spectator == spectator)
            {
                errorMessage = null;
                return true;
            }

            if (AtCapacity(spectator))
            {
                errorMessage = spectator ? "Spectator capacity reached." : "Player capacity reached.";
                return false;
            }

            entry.PlayerState.SetRegisteredSpectatorStatus(this, spectator);
            roster[playerController] = new RosterEntry(entry.PlayerId, spectator, entry.PlayerState);
            if (spectator)
            {
                playerCount--;
                spectatorCount++;
            }
            else
            {
                spectatorCount--;
                playerCount++;
            }

            errorMessage = null;
            return true;
        }

        public bool ContainsPlayer(PlayerController playerController)
        {
            return !ReferenceEquals(playerController, null) && roster.ContainsKey(playerController);
        }

        public bool AtCapacity(bool spectator)
        {
            return spectator ? spectatorCount >= MaxSpectators : playerCount >= MaxPlayers;
        }

        public virtual void HandleMatchHasStarted() { }
        public virtual void HandleMatchHasEnded() { }
    }
}
