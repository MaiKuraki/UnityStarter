using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Authoritative session adapter for CycloneGames.Networking. Network callbacks must marshal
    /// to the owning World thread before invoking this object.
    /// </summary>
    public class NetworkGameSessionAdapter : GameSession
    {
        public const int MaxBannedAddresses = 4096;

        private readonly struct ConnectionBinding
        {
            public ConnectionBinding(int playerId, INetConnection connection)
            {
                PlayerId = playerId;
                Connection = connection;
            }

            public int PlayerId { get; }
            public INetConnection Connection { get; }
        }

        private readonly struct StagedConnection
        {
            public StagedConnection(INetConnection connection, string remoteAddress)
            {
                Connection = connection;
                RemoteAddress = remoteAddress;
            }

            public INetConnection Connection { get; }
            public string RemoteAddress { get; }
        }

        private INetworkMessageEndpoint messageEndpoint;
        private readonly Dictionary<PlayerController, ConnectionBinding> playerConnections =
            new Dictionary<PlayerController, ConnectionBinding>(16);
        private readonly Dictionary<int, INetConnection> playerIdConnections =
            new Dictionary<int, INetConnection>(16);
        private readonly Dictionary<int, StagedConnection> stagedConnections =
            new Dictionary<int, StagedConnection>(16);
        private readonly Dictionary<int, int> connectionIdPlayerIds =
            new Dictionary<int, int>(16);
        private readonly HashSet<string> bannedAddresses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly int maxStagedConnections;

        public NetworkGameSessionAdapter(int maxPlayers = 16, int maxSpectators = 4)
            : base(maxPlayers, maxSpectators)
        {
            maxStagedConnections = maxPlayers + maxSpectators;
        }

        public bool RejectUnknownAddresses { get; set; } = true;
        public bool RejectDisconnectedConnections { get; set; } = true;
        public bool RejectUnauthenticatedConnections { get; set; } = true;
        public INetworkMessageEndpoint MessageEndpoint => messageEndpoint;
        public int StagedConnectionCount => stagedConnections.Count;
        public int MaxStagedConnections => maxStagedConnections;

        public virtual void SetMessageEndpoint(INetworkMessageEndpoint endpoint)
        {
            if (ReferenceEquals(messageEndpoint, endpoint))
            {
                return;
            }

            if (stagedConnections.Count > 0 || playerConnections.Count > 0)
            {
                throw new InvalidOperationException(
                    "MessageEndpoint cannot change while staged or active connections exist.");
            }

            messageEndpoint = endpoint;
            if (endpoint != null)
            {
                GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(endpoint);
            }
        }

        /// <summary>
        /// Stages an authenticated transport connection before GameMode.LoginAsync. The staged
        /// identity is consumed when the resulting PlayerController enters the session roster.
        /// </summary>
        public virtual bool TryStageConnection(
            int playerId,
            INetConnection connection,
            out string errorMessage)
        {
            if (playerId <= 0)
            {
                errorMessage = "A positive PlayerId is required for a network login.";
                return false;
            }

            if (connection == null)
            {
                errorMessage = "Connection is required.";
                return false;
            }

            if (connection.RemoteAddress != null &&
                connection.RemoteAddress.Length > PlayerLoginRequest.MaxRemoteAddressLength)
            {
                errorMessage = $"Connection address exceeds {PlayerLoginRequest.MaxRemoteAddressLength} characters.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(connection.RemoteAddress) &&
                bannedAddresses.Contains(connection.RemoteAddress))
            {
                errorMessage = "Address is banned.";
                return false;
            }

            if (playerIdConnections.ContainsKey(playerId))
            {
                errorMessage = $"PlayerId {playerId} already has an active connection.";
                return false;
            }

            if (connectionIdPlayerIds.TryGetValue(connection.ConnectionId, out int existingPlayerId) &&
                existingPlayerId != playerId)
            {
                errorMessage = $"Connection is already assigned to PlayerId {existingPlayerId}.";
                return false;
            }

            if (stagedConnections.TryGetValue(playerId, out StagedConnection existing))
            {
                if (ConnectionsEqual(existing.Connection, connection))
                {
                    errorMessage = null;
                    return true;
                }

                errorMessage = $"PlayerId {playerId} already has a different staged connection.";
                return false;
            }

            if (stagedConnections.Count >= maxStagedConnections)
            {
                errorMessage = "Staged connection capacity reached.";
                return false;
            }

            stagedConnections.Add(
                playerId,
                new StagedConnection(connection, connection.RemoteAddress));
            connectionIdPlayerIds[connection.ConnectionId] = playerId;
            errorMessage = null;
            return true;
        }

        public virtual bool RemoveStagedConnection(int playerId, INetConnection expectedConnection = null)
        {
            if (!stagedConnections.TryGetValue(playerId, out StagedConnection staged))
            {
                return false;
            }

            if (expectedConnection != null && !ConnectionsEqual(staged.Connection, expectedConnection))
            {
                return false;
            }

            bool removed = stagedConnections.Remove(playerId);
            if (removed &&
                connectionIdPlayerIds.TryGetValue(staged.Connection.ConnectionId, out int indexedPlayerId) &&
                indexedPlayerId == playerId)
            {
                connectionIdPlayerIds.Remove(staged.Connection.ConnectionId);
            }

            return removed;
        }

        public virtual void BindConnection(PlayerController player, INetConnection connection)
        {
            if (!TryBindConnection(player, connection, out string errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        public virtual bool TryBindConnection(
            PlayerController player,
            INetConnection connection,
            out string errorMessage)
        {
            if (ReferenceEquals(player, null))
            {
                errorMessage = "PlayerController is required.";
                return false;
            }

            if (connection == null)
            {
                errorMessage = "Connection is required.";
                return false;
            }

            if (!ContainsPlayer(player))
            {
                errorMessage = "PlayerController must be registered in this session before binding.";
                return false;
            }

            if (!ValidateConnectionState(connection, connection.RemoteAddress, out errorMessage))
            {
                return false;
            }

            PlayerState playerState = player.GetPlayerState();
            int playerId = playerState?.GetPlayerId() ?? 0;
            if (playerId <= 0)
            {
                errorMessage = "A positive PlayerId is required before binding a network connection.";
                return false;
            }

            bool hasPreviousBinding = playerConnections.TryGetValue(
                player,
                out ConnectionBinding previousBinding);
            if (playerIdConnections.TryGetValue(playerId, out INetConnection indexedConnection) &&
                !ConnectionsEqual(indexedConnection, connection) &&
                (!hasPreviousBinding ||
                 previousBinding.PlayerId != playerId ||
                 !ConnectionsEqual(indexedConnection, previousBinding.Connection)))
            {
                errorMessage = $"PlayerId {playerId} is already bound to another connection.";
                return false;
            }

            if (connectionIdPlayerIds.TryGetValue(connection.ConnectionId, out int indexedPlayerId) &&
                indexedPlayerId != playerId)
            {
                errorMessage = $"Connection is already assigned to PlayerId {indexedPlayerId}.";
                return false;
            }

            if (hasPreviousBinding)
            {
                if (previousBinding.PlayerId == playerId &&
                    ConnectionsEqual(previousBinding.Connection, connection))
                {
                    RemoveStagedConnection(playerId, connection);
                    errorMessage = null;
                    return true;
                }

                playerConnections.Remove(player);
                if (playerIdConnections.TryGetValue(previousBinding.PlayerId, out INetConnection previousIndexed) &&
                    ConnectionsEqual(previousIndexed, previousBinding.Connection))
                {
                    playerIdConnections.Remove(previousBinding.PlayerId);
                }


                if (connectionIdPlayerIds.TryGetValue(previousBinding.Connection.ConnectionId, out int previousPlayerId) &&
                    previousPlayerId == previousBinding.PlayerId)
                {
                    connectionIdPlayerIds.Remove(previousBinding.Connection.ConnectionId);
                }
            }

            playerConnections[player] = new ConnectionBinding(playerId, connection);
            playerIdConnections[playerId] = connection;
            RemoveStagedConnection(playerId, connection);
            connectionIdPlayerIds[connection.ConnectionId] = playerId;
            errorMessage = null;
            return true;
        }

        public virtual bool TryGetConnection(PlayerController player, out INetConnection connection)
        {
            connection = null;
            if (ReferenceEquals(player, null) ||
                !playerConnections.TryGetValue(player, out ConnectionBinding binding))
            {
                return false;
            }

            connection = binding.Connection;
            return connection != null;
        }

        public virtual bool TryGetConnectionByPlayerId(int playerId, out INetConnection connection)
        {
            connection = null;
            return playerId > 0 &&
                   playerIdConnections.TryGetValue(playerId, out connection) &&
                   connection != null;
        }

        public virtual bool UnbindConnection(PlayerController player)
        {
            if (ReferenceEquals(player, null) ||
                !playerConnections.TryGetValue(player, out ConnectionBinding binding))
            {
                return false;
            }

            if (playerIdConnections.TryGetValue(binding.PlayerId, out INetConnection indexed) &&
                ConnectionsEqual(indexed, binding.Connection))
            {
                playerIdConnections.Remove(binding.PlayerId);
            }

            if (connectionIdPlayerIds.TryGetValue(binding.Connection.ConnectionId, out int playerId) &&
                playerId == binding.PlayerId)
            {
                connectionIdPlayerIds.Remove(binding.Connection.ConnectionId);
            }

            return playerConnections.Remove(player);
        }

        public override bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
        {
            if (!base.ApproveLogin(in request, out errorMessage))
            {
                return false;
            }

            if (request.IsLocal)
            {
                if (stagedConnections.ContainsKey(request.PlayerId))
                {
                    errorMessage = "Local PlayerId conflicts with a staged remote connection.";
                    return false;
                }

                errorMessage = null;
                return true;
            }

            string address = request.RemoteAddress;
            if (!string.IsNullOrWhiteSpace(address) && bannedAddresses.Contains(address))
            {
                errorMessage = "Address is banned.";
                return false;
            }

            if (stagedConnections.TryGetValue(request.PlayerId, out StagedConnection staged))
            {
                INetConnection connection = staged.Connection;
                if (!ValidateConnectionState(connection, staged.RemoteAddress, out errorMessage))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(address) &&
                    !string.Equals(address, staged.RemoteAddress, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Login address does not match the staged connection.";
                    return false;
                }
            }
            else if (RejectUnknownAddresses)
            {
                errorMessage = "Connection is unknown.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public override bool TryRegisterPlayer(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            if (!base.TryRegisterPlayer(playerController, spectator, out errorMessage))
            {
                return false;
            }

            int playerId = playerController.GetPlayerState()?.GetPlayerId() ?? 0;
            if (stagedConnections.TryGetValue(playerId, out StagedConnection staged))
            {
                if (playerController.IsLocalController)
                {
                    base.UnregisterPlayer(playerController);
                    errorMessage = "Local PlayerId conflicts with a staged remote connection.";
                    return false;
                }

                if (!ValidateConnectionState(staged.Connection, staged.RemoteAddress, out errorMessage) ||
                    !TryBindConnection(playerController, staged.Connection, out errorMessage))
                {
                    base.UnregisterPlayer(playerController);
                    return false;
                }
            }
            else if (RejectUnknownAddresses && !playerController.IsLocalController)
            {
                base.UnregisterPlayer(playerController);
                errorMessage = "No staged connection exists for this PlayerId.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public override bool UnregisterPlayer(PlayerController playerController)
        {
            bool removed = base.UnregisterPlayer(playerController);
            UnbindConnection(playerController);
            return removed;
        }

        public virtual bool KickPlayer(PlayerController player, string reason)
        {
            _ = reason;
            if (ReferenceEquals(player, null))
            {
                return false;
            }

            player.World?.AssertOwnerThread();

            bool disconnectRequested = false;
            bool gameplayRemoved = false;
            try
            {
                if (messageEndpoint != null && TryGetConnection(player, out INetConnection connection))
                {
                    messageEndpoint.Disconnect(connection);
                    disconnectRequested = true;
                }
            }
            finally
            {
                GameMode gameMode = player.World?.GameMode;
                gameplayRemoved = gameMode != null
                    ? gameMode.Logout(player)
                    : UnregisterPlayer(player);
            }

            return disconnectRequested || gameplayRemoved;
        }

        public virtual bool BanPlayer(PlayerController player, string reason)
        {
            if (ReferenceEquals(player, null))
            {
                return false;
            }

            player.World?.AssertOwnerThread();

            if (!TryGetConnection(player, out INetConnection connection) ||
                string.IsNullOrWhiteSpace(connection.RemoteAddress))
            {
                return false;
            }

            if (!bannedAddresses.Contains(connection.RemoteAddress) &&
                bannedAddresses.Count >= MaxBannedAddresses)
            {
                return false;
            }

            bannedAddresses.Add(connection.RemoteAddress);
            return KickPlayer(player, reason);
        }

        public virtual bool BanAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address) ||
                address.Length > PlayerLoginRequest.MaxRemoteAddressLength ||
                (!bannedAddresses.Contains(address) && bannedAddresses.Count >= MaxBannedAddresses))
            {
                return false;
            }

            return bannedAddresses.Add(address);
        }

        public virtual bool UnbanAddress(string address)
        {
            return !string.IsNullOrWhiteSpace(address) && bannedAddresses.Remove(address);
        }

        public virtual bool IsAddressBanned(string address)
        {
            return !string.IsNullOrWhiteSpace(address) && bannedAddresses.Contains(address);
        }

        private static bool ConnectionsEqual(INetConnection left, INetConnection right)
        {
            return ReferenceEquals(left, right) ||
                   left != null && right != null && left.ConnectionId == right.ConnectionId;
        }

        private bool ValidateConnectionState(
            INetConnection connection,
            string expectedRemoteAddress,
            out string errorMessage)
        {
            if (connection == null)
            {
                errorMessage = "Connection is required.";
                return false;
            }

            string currentAddress = connection.RemoteAddress;
            if (currentAddress != null && currentAddress.Length > PlayerLoginRequest.MaxRemoteAddressLength)
            {
                errorMessage = $"Connection address exceeds {PlayerLoginRequest.MaxRemoteAddressLength} characters.";
                return false;
            }

            if (!string.Equals(
                    currentAddress ?? string.Empty,
                    expectedRemoteAddress ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Connection address changed after staging.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(currentAddress) && bannedAddresses.Contains(currentAddress))
            {
                errorMessage = "Address is banned.";
                return false;
            }

            if (RejectDisconnectedConnections && !connection.IsConnected)
            {
                errorMessage = "Connection is not active.";
                return false;
            }

            if (RejectUnauthenticatedConnections && !connection.IsAuthenticated)
            {
                errorMessage = "Connection is not authenticated.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
