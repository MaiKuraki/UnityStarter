using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using PlayerLoginRequest = CycloneGames.GameplayFramework.Core.PlayerLoginRequest;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Authoritative session adapter for CycloneGames.Networking. Network callbacks must marshal
    /// to the owning World thread before invoking this object.
    /// </summary>
    public sealed class NetworkGameSessionAdapter : IGameSession
    {
        public const int MaximumSupportedBannedAddressCount = 65_536;
        public const int DefaultMaximumBannedAddressCount = 4_096;

        private readonly struct ConnectionBinding
        {
            public ConnectionBinding(
                int playerId,
                int connectionId,
                string remoteAddress,
                INetConnection connection)
            {
                PlayerId = playerId;
                Connection = connection;
                ConnectionId = connectionId;
                RemoteAddress = remoteAddress;
            }

            public int PlayerId { get; }
            public INetConnection Connection { get; }
            public int ConnectionId { get; }
            public string RemoteAddress { get; }
        }

        private readonly struct StagedConnection
        {
            public StagedConnection(INetConnection connection, int connectionId, string remoteAddress)
            {
                Connection = connection;
                ConnectionId = connectionId;
                RemoteAddress = remoteAddress;
            }

            public INetConnection Connection { get; }
            public int ConnectionId { get; }
            public string RemoteAddress { get; }
        }

        private INetworkMessageEndpoint messageEndpoint;
        private readonly IGameSession gameSession;
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
        private readonly int maximumBannedAddressCount;
        private readonly int ownerThreadId;
        private readonly int maxPlayers;
        private readonly int maxSpectators;
        private PlayerController registrationRollbackPlayer;

        public NetworkGameSessionAdapter(
            int maxPlayers = 16,
            int maxSpectators = 4,
            int maximumBannedAddressCount = DefaultMaximumBannedAddressCount)
            : this(new GameSession(maxPlayers, maxSpectators), maximumBannedAddressCount)
        {
        }

        public NetworkGameSessionAdapter(
            IGameSession gameSession,
            int maximumBannedAddressCount = DefaultMaximumBannedAddressCount)
        {
            this.gameSession = gameSession ?? throw new ArgumentNullException(nameof(gameSession));
            if (maximumBannedAddressCount < 0 ||
                maximumBannedAddressCount > MaximumSupportedBannedAddressCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBannedAddressCount));
            }

            this.maximumBannedAddressCount = maximumBannedAddressCount;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            maxPlayers = gameSession.MaxPlayers;
            maxSpectators = gameSession.MaxSpectators;
            long combinedCapacity = (long)maxPlayers + maxSpectators;
            if (maxPlayers < 0 ||
                maxSpectators < 0 ||
                combinedCapacity > ParticipantRoster.MaximumSupportedParticipants)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gameSession),
                    $"Combined session capacity cannot exceed {ParticipantRoster.MaximumSupportedParticipants}.");
            }

            maxStagedConnections = (int)combinedCapacity;
        }

        private bool rejectUnknownAddresses = true;
        private bool rejectDisconnectedConnections = true;
        private bool rejectUnauthenticatedConnections = true;

        public bool RejectUnknownAddresses
        {
            get { AssertOwnerThread(); return rejectUnknownAddresses; }
            set { AssertOwnerThread(); rejectUnknownAddresses = value; }
        }
        public bool RejectDisconnectedConnections
        {
            get { AssertOwnerThread(); return rejectDisconnectedConnections; }
            set { AssertOwnerThread(); rejectDisconnectedConnections = value; }
        }
        public bool RejectUnauthenticatedConnections
        {
            get { AssertOwnerThread(); return rejectUnauthenticatedConnections; }
            set { AssertOwnerThread(); rejectUnauthenticatedConnections = value; }
        }
        public INetworkMessageEndpoint MessageEndpoint
        {
            get
            {
                AssertOwnerThread();
                return messageEndpoint;
            }
        }
        public int StagedConnectionCount
        {
            get
            {
                AssertOwnerThread();
                return stagedConnections.Count;
            }
        }
        public int MaxStagedConnections => maxStagedConnections;
        public int MaximumBannedAddressCount => maximumBannedAddressCount;
        public int BoundConnectionCount
        {
            get
            {
                AssertOwnerThread();
                return playerConnections.Count;
            }
        }
        public int BannedAddressCount
        {
            get
            {
                AssertOwnerThread();
                return bannedAddresses.Count;
            }
        }
        /// <summary>
        /// Gets whether a composed-session registration could not be rolled back. Further
        /// registrations are rejected until <see cref="TryRecoverRegistrationRollback"/>
        /// completes, preserving a single bounded recovery owner instead of admitting more
        /// participants into an inconsistent roster.
        /// </summary>
        public bool HasRegistrationRollbackFault
        {
            get
            {
                AssertOwnerThread();
                return !ReferenceEquals(registrationRollbackPlayer, null);
            }
        }
        public int MaxPlayers => maxPlayers;
        public int MaxSpectators => maxSpectators;
        public int PlayerCount
        {
            get { AssertOwnerThread(); return gameSession.PlayerCount; }
        }
        public int SpectatorCount
        {
            get { AssertOwnerThread(); return gameSession.SpectatorCount; }
        }

        public void SetMessageEndpoint(INetworkMessageEndpoint endpoint)
        {
            AssertOwnerThread();
            if (ReferenceEquals(messageEndpoint, endpoint))
            {
                return;
            }

            if (stagedConnections.Count > 0 || playerConnections.Count > 0)
            {
                throw new InvalidOperationException(
                    "MessageEndpoint cannot change while staged or active connections exist.");
            }

            if (endpoint != null)
            {
                GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(endpoint);
            }

            messageEndpoint = endpoint;
        }

        /// <summary>
        /// Stages an authenticated transport connection before GameMode.LoginAsync. The staged
        /// identity is consumed when the resulting PlayerController enters the session roster.
        /// </summary>
        public bool TryStageConnection(
            int playerId,
            INetConnection connection,
            out string errorMessage)
        {
            AssertOwnerThread();
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

            int connectionId = connection.ConnectionId;
            if (connectionId <= 0)
            {
                errorMessage = "A positive ConnectionId is required.";
                return false;
            }

            string remoteAddress = connection.RemoteAddress;
            if (remoteAddress != null &&
                remoteAddress.Length > PlayerLoginRequest.MaxRemoteAddressLength)
            {
                errorMessage = $"Connection address exceeds {PlayerLoginRequest.MaxRemoteAddressLength} characters.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(remoteAddress) &&
                bannedAddresses.Contains(remoteAddress))
            {
                errorMessage = "Address is banned.";
                return false;
            }

            if (playerIdConnections.ContainsKey(playerId))
            {
                errorMessage = $"PlayerId {playerId} already has an active connection.";
                return false;
            }

            if (connectionIdPlayerIds.TryGetValue(connectionId, out int existingPlayerId) &&
                existingPlayerId != playerId)
            {
                errorMessage = $"Connection is already assigned to PlayerId {existingPlayerId}.";
                return false;
            }

            if (stagedConnections.TryGetValue(playerId, out StagedConnection existing))
            {
                if (existing.ConnectionId == connectionId &&
                    ReferenceEquals(existing.Connection, connection))
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

            stagedConnections.EnsureCapacity(checked(stagedConnections.Count + 1));
            connectionIdPlayerIds.EnsureCapacity(connectionIdPlayerIds.ContainsKey(connectionId)
                ? connectionIdPlayerIds.Count
                : checked(connectionIdPlayerIds.Count + 1));
            stagedConnections.Add(
                playerId,
                new StagedConnection(connection, connectionId, remoteAddress));
            connectionIdPlayerIds[connectionId] = playerId;
            errorMessage = null;
            return true;
        }

        public bool RemoveStagedConnection(int playerId, INetConnection expectedConnection = null)
        {
            AssertOwnerThread();
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
                connectionIdPlayerIds.TryGetValue(staged.ConnectionId, out int indexedPlayerId) &&
                indexedPlayerId == playerId)
            {
                connectionIdPlayerIds.Remove(staged.ConnectionId);
            }

            return removed;
        }

        public void BindConnection(PlayerController player, INetConnection connection)
        {
            AssertOwnerThread();
            if (!TryBindConnection(player, connection, out string errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        public bool TryBindConnection(
            PlayerController player,
            INetConnection connection,
            out string errorMessage)
        {
            AssertOwnerThread();
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

            int connectionId = connection.ConnectionId;
            if (connectionId <= 0)
            {
                errorMessage = "A positive ConnectionId is required.";
                return false;
            }

            if (!gameSession.ContainsPlayer(player))
            {
                errorMessage = "PlayerController must be registered in this session before binding.";
                return false;
            }

            PlayerState playerState = player.GetPlayerState();
            int playerId = playerState?.GetPlayerId() ?? 0;
            if (playerId <= 0)
            {
                errorMessage = "A positive PlayerId is required before binding a network connection.";
                return false;
            }

            int expectedConnectionId = connectionId;
            string expectedRemoteAddress = connection.RemoteAddress;
            if (stagedConnections.TryGetValue(playerId, out StagedConnection staged))
            {
                if (staged.ConnectionId != connectionId ||
                    !ConnectionsEqual(staged.Connection, connection))
                {
                    errorMessage = $"PlayerId {playerId} has a different staged connection.";
                    return false;
                }

                expectedConnectionId = staged.ConnectionId;
                expectedRemoteAddress = staged.RemoteAddress;
            }

            if (!ValidateConnectionState(
                    connection,
                    expectedConnectionId,
                    expectedRemoteAddress,
                    out errorMessage))
            {
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

            if (connectionIdPlayerIds.TryGetValue(connectionId, out int indexedPlayerId) &&
                indexedPlayerId != playerId)
            {
                errorMessage = $"Connection is already assigned to PlayerId {indexedPlayerId}.";
                return false;
            }

            if (hasPreviousBinding)
            {
                if (previousBinding.PlayerId == playerId &&
                    previousBinding.ConnectionId == connectionId &&
                    ReferenceEquals(previousBinding.Connection, connection))
                {
                    RemoveStagedConnection(playerId, connection);
                    errorMessage = null;
                    return true;
                }
            }

            playerConnections.EnsureCapacity(hasPreviousBinding
                ? playerConnections.Count
                : checked(playerConnections.Count + 1));
            playerIdConnections.EnsureCapacity(playerIdConnections.ContainsKey(playerId)
                ? playerIdConnections.Count
                : checked(playerIdConnections.Count + 1));
            connectionIdPlayerIds.EnsureCapacity(connectionIdPlayerIds.ContainsKey(connectionId)
                ? connectionIdPlayerIds.Count
                : checked(connectionIdPlayerIds.Count + 1));

            RemoveStagedConnection(playerId, connection);
            playerConnections[player] = new ConnectionBinding(
                playerId,
                connectionId,
                expectedRemoteAddress,
                connection);
            playerIdConnections[playerId] = connection;
            connectionIdPlayerIds[connectionId] = playerId;

            if (hasPreviousBinding)
            {
                if (previousBinding.PlayerId != playerId &&
                    playerIdConnections.TryGetValue(previousBinding.PlayerId, out INetConnection previousIndexed) &&
                    ConnectionsEqual(previousIndexed, previousBinding.Connection))
                {
                    playerIdConnections.Remove(previousBinding.PlayerId);
                }

                if (previousBinding.ConnectionId != connectionId &&
                    connectionIdPlayerIds.TryGetValue(previousBinding.ConnectionId, out int previousPlayerId) &&
                    previousPlayerId == previousBinding.PlayerId)
                {
                    connectionIdPlayerIds.Remove(previousBinding.ConnectionId);
                }
            }

            errorMessage = null;
            return true;
        }

        public bool TryGetConnection(PlayerController player, out INetConnection connection)
        {
            AssertOwnerThread();
            connection = null;
            if (ReferenceEquals(player, null) ||
                !playerConnections.TryGetValue(player, out ConnectionBinding binding))
            {
                return false;
            }

            connection = binding.Connection;
            return connection != null;
        }

        public bool TryGetConnectionByPlayerId(int playerId, out INetConnection connection)
        {
            AssertOwnerThread();
            connection = null;
            return playerId > 0 &&
                   playerIdConnections.TryGetValue(playerId, out connection) &&
                   connection != null;
        }

        public bool UnbindConnection(PlayerController player)
        {
            AssertOwnerThread();
            if (ReferenceEquals(player, null) ||
                !TryGetConsistentBinding(player, out ConnectionBinding binding))
            {
                return false;
            }

            RemoveBindingCore(player, in binding);
            return true;
        }

        public bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage)
        {
            AssertOwnerThread();
            if (!gameSession.ApproveLogin(in request, out errorMessage))
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
                if (!ValidateConnectionState(
                        connection,
                        staged.ConnectionId,
                        staged.RemoteAddress,
                        out errorMessage))
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
            else if (rejectUnknownAddresses)
            {
                errorMessage = "Connection is unknown.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool TryRegisterPlayer(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            AssertOwnerThread();
            if (!ReferenceEquals(registrationRollbackPlayer, null))
            {
                errorMessage = "A previous network registration rollback must be recovered before admitting another participant.";
                return false;
            }

            if (!gameSession.TryRegisterPlayer(playerController, spectator, out errorMessage))
            {
                return false;
            }

            try
            {
                int playerId = playerController.GetPlayerState()?.GetPlayerId() ?? 0;
                if (stagedConnections.TryGetValue(playerId, out StagedConnection staged))
                {
                    if (playerController.IsLocalController)
                    {
                        if (!TryRollbackSessionRegistration(playerController))
                        {
                            errorMessage = "Network registration rollback did not complete.";
                            return false;
                        }

                        errorMessage = "Local PlayerId conflicts with a staged remote connection.";
                        return false;
                    }

                    if (!ValidateConnectionState(
                            staged.Connection,
                            staged.ConnectionId,
                            staged.RemoteAddress,
                            out errorMessage) ||
                        !TryBindConnection(playerController, staged.Connection, out errorMessage))
                    {
                        if (!TryRollbackSessionRegistration(playerController))
                        {
                            errorMessage = "Network registration rollback did not complete.";
                        }

                        return false;
                    }
                }
                else if (rejectUnknownAddresses && !playerController.IsLocalController)
                {
                    if (!TryRollbackSessionRegistration(playerController))
                    {
                        errorMessage = "Network registration rollback did not complete.";
                        return false;
                    }

                    errorMessage = "No staged connection exists for this PlayerId.";
                    return false;
                }
            }
            catch
            {
                TryRollbackSessionRegistration(playerController);
                throw;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Retries the single retained composed-session rollback after its owner has corrected
        /// the underlying session failure. No additional participant can be registered while
        /// this recovery owner is present.
        /// </summary>
        public bool TryRecoverRegistrationRollback()
        {
            AssertOwnerThread();
            PlayerController playerController = registrationRollbackPlayer;
            if (ReferenceEquals(playerController, null))
            {
                return true;
            }

            return TryRollbackSessionRegistration(playerController);
        }

        public bool UnregisterPlayer(PlayerController playerController)
        {
            AssertOwnerThread();
            if (!gameSession.ContainsPlayer(playerController))
            {
                return false;
            }

            bool hasBinding = TryGetConsistentBinding(
                playerController,
                out ConnectionBinding binding);

            if (!gameSession.UnregisterPlayer(playerController))
            {
                throw new InvalidOperationException(
                    "The composed GameSession changed during an owner-thread unregister operation.");
            }

            if (hasBinding)
            {
                RemoveBindingCore(playerController, in binding);
            }

            if (ReferenceEquals(registrationRollbackPlayer, playerController))
            {
                registrationRollbackPlayer = null;
            }

            return true;
        }

        /// <summary>
        /// Removes every active network binding and unregisters each bound participant from the
        /// composed session. Unbound participants owned directly by the composed session are
        /// left untouched. Safe to call when no bindings exist.
        /// </summary>
        public void UnregisterAllConnections()
        {
            AssertOwnerThread();
            if (playerConnections.Count == 0)
            {
                return;
            }

            // Snapshot the keys first: UnregisterPlayer mutates the dictionaries in place.
            PlayerController[] players = new PlayerController[playerConnections.Count];
            playerConnections.Keys.CopyTo(players, 0);
            for (int index = 0; index < players.Length; index++)
            {
                PlayerController player = players[index];
                if (ReferenceEquals(player, null) || player == null)
                {
                    continue;
                }

                UnregisterPlayer(player);
            }
        }

        /// <summary>
        /// Terminal, idempotent teardown of all adapter-owned state: drains network-bound
        /// participants, clears staged connections, bans, the rollback owner, and detaches the
        /// message endpoint. The composed IGameSession keeps any participant it was given
        /// directly by its owner. Message-catalog registration is owned by the shared protocol
        /// registry and is not removed here.
        /// </summary>
        public void Shutdown()
        {
            AssertOwnerThread();
            UnregisterAllConnections();
            stagedConnections.Clear();
            connectionIdPlayerIds.Clear();
            bannedAddresses.Clear();
            registrationRollbackPlayer = null;
            messageEndpoint = null;
        }

        public bool ContainsPlayer(PlayerController playerController)
        {
            AssertOwnerThread();
            return gameSession.ContainsPlayer(playerController);
        }

        public bool AtCapacity(bool spectator)
        {
            AssertOwnerThread();
            return gameSession.AtCapacity(spectator);
        }

        public bool TrySetSpectatorStatus(
            PlayerController playerController,
            bool spectator,
            out string errorMessage)
        {
            AssertOwnerThread();
            return gameSession.TrySetSpectatorStatus(playerController, spectator, out errorMessage);
        }

        public void HandleMatchHasStarted()
        {
            AssertOwnerThread();
            gameSession.HandleMatchHasStarted();
        }

        public void HandleMatchHasEnded()
        {
            AssertOwnerThread();
            gameSession.HandleMatchHasEnded();
        }

        /// <summary>
        /// Requests transport disconnection and removes the participant from gameplay state.
        /// Returns true when either operation was committed.
        /// </summary>
        public bool KickPlayer(PlayerController player)
        {
            AssertOwnerThread();
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

        /// <summary>
        /// Adds the participant's validated remote address to the bounded ban set, then kicks
        /// the participant. Returns false when no valid connection address can be resolved.
        /// </summary>
        public bool BanPlayer(PlayerController player)
        {
            AssertOwnerThread();
            if (ReferenceEquals(player, null))
            {
                return false;
            }

            player.World?.AssertOwnerThread();

            if (!TryGetConsistentBinding(player, out ConnectionBinding binding))
            {
                return false;
            }

            string remoteAddress = binding.RemoteAddress;
            if (string.IsNullOrWhiteSpace(remoteAddress) ||
                remoteAddress.Length > PlayerLoginRequest.MaxRemoteAddressLength)
            {
                return false;
            }

            if (!bannedAddresses.Contains(remoteAddress) &&
                bannedAddresses.Count >= maximumBannedAddressCount)
            {
                return false;
            }

            bannedAddresses.Add(remoteAddress);
            return KickPlayer(player);
        }

        public bool BanAddress(string address)
        {
            AssertOwnerThread();
            if (string.IsNullOrWhiteSpace(address) ||
                address.Length > PlayerLoginRequest.MaxRemoteAddressLength ||
                (!bannedAddresses.Contains(address) && bannedAddresses.Count >= maximumBannedAddressCount))
            {
                return false;
            }

            return bannedAddresses.Add(address);
        }

        public bool UnbanAddress(string address)
        {
            AssertOwnerThread();
            return !string.IsNullOrWhiteSpace(address) && bannedAddresses.Remove(address);
        }

        public bool IsAddressBanned(string address)
        {
            AssertOwnerThread();
            return !string.IsNullOrWhiteSpace(address) && bannedAddresses.Contains(address);
        }

        /// <summary>
        /// Logical connection identity (by ConnectionId) used for validation. A transport that
        /// reuses a ConnectionId for a new object is still considered logically equal here, so
        /// idempotent no-op paths that must update a stored reference use ReferenceEquals.
        /// </summary>
        private static bool ConnectionsEqual(INetConnection left, INetConnection right)
        {
            return ReferenceEquals(left, right) ||
                   left != null &&
                   right != null &&
                   left.ConnectionId > 0 &&
                   left.ConnectionId == right.ConnectionId;
        }

        private bool TryGetConsistentBinding(
            PlayerController player,
            out ConnectionBinding binding)
        {
            if (!playerConnections.TryGetValue(player, out binding))
            {
                return false;
            }

            if (!playerIdConnections.TryGetValue(binding.PlayerId, out INetConnection playerIdConnection) ||
                !ReferenceEquals(playerIdConnection, binding.Connection) ||
                !connectionIdPlayerIds.TryGetValue(binding.ConnectionId, out int indexedPlayerId) ||
                indexedPlayerId != binding.PlayerId)
            {
                throw new InvalidOperationException(
                    "Network session connection indexes are inconsistent.");
            }

            return true;
        }

        private void RemoveBindingCore(PlayerController player, in ConnectionBinding binding)
        {
            playerIdConnections.Remove(binding.PlayerId);
            connectionIdPlayerIds.Remove(binding.ConnectionId);
            playerConnections.Remove(player);
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "NetworkGameSessionAdapter must be accessed on its owning thread. " +
                    "Marshal transport callbacks through a bounded owner-thread queue.");
            }
        }

        private bool TryRollbackSessionRegistration(PlayerController playerController)
        {
            try
            {
                if (!gameSession.UnregisterPlayer(playerController))
                {
                    registrationRollbackPlayer = playerController;
                    return false;
                }

                if (ReferenceEquals(registrationRollbackPlayer, playerController))
                {
                    registrationRollbackPlayer = null;
                }

                return true;
            }
            catch (OutOfMemoryException)
            {
                registrationRollbackPlayer = playerController;
                throw;
            }
            catch
            {
                registrationRollbackPlayer = playerController;
                return false;
            }
        }

        private bool ValidateConnectionState(
            INetConnection connection,
            int expectedConnectionId,
            string expectedRemoteAddress,
            out string errorMessage)
        {
            if (connection == null)
            {
                errorMessage = "Connection is required.";
                return false;
            }

            int currentConnectionId = connection.ConnectionId;
            if (currentConnectionId <= 0)
            {
                errorMessage = "A positive ConnectionId is required.";
                return false;
            }

            if (currentConnectionId != expectedConnectionId)
            {
                errorMessage = "ConnectionId changed after identity validation.";
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

            if (rejectDisconnectedConnections && !connection.IsConnected)
            {
                errorMessage = "Connection is not active.";
                return false;
            }

            if (rejectUnauthenticatedConnections && !connection.IsAuthenticated)
            {
                errorMessage = "Connection is not authenticated.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
