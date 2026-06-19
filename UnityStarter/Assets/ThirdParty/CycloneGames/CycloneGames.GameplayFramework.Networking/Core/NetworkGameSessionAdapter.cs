using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Adapter that bridges CycloneGames.Networking connection lifecycle into IGameSession.
    ///
    /// This class keeps GameplayFramework network-agnostic while providing a ready-to-use
    /// implementation for projects that use CycloneGames.Networking.
    /// </summary>
    public class NetworkGameSessionAdapter : GameSession
    {
        [Header("Login Validation")]
        [SerializeField] private bool RejectUnknownAddresses = false;
        [SerializeField] private bool RejectDisconnectedConnections = true;
        [SerializeField] private bool RejectUnauthenticatedConnections = true;

        private INetworkManager _networkManager;
        private readonly Dictionary<PlayerController, INetConnection> _playerConnections = new Dictionary<PlayerController, INetConnection>(16);
        private readonly Dictionary<int, INetConnection> _playerIdConnections = new Dictionary<int, INetConnection>(16);
        private readonly HashSet<string> _bannedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public INetworkManager NetworkManager => _networkManager;

        public virtual void SetNetworkManager(INetworkManager manager)
        {
            _networkManager = manager;
            if (manager != null)
            {
                GameplayFrameworkNetworkProtocol.TryRegisterMessageCatalog(manager);
            }
        }

        /// <summary>
        /// Associates a PlayerController with its network connection.
        /// Call this from your net adapter once PlayerController is created.
        /// </summary>
        public virtual void BindConnection(PlayerController player, INetConnection connection)
        {
            if (player == null || connection == null)
            {
                return;
            }

            _playerConnections[player] = connection;
            int playerId = player.GetPlayerState()?.GetPlayerId() ?? 0;
            if (playerId != 0)
            {
                _playerIdConnections[playerId] = connection;
            }
        }

        public virtual bool TryGetConnection(PlayerController player, out INetConnection connection)
        {
            connection = null;
            return player != null && _playerConnections.TryGetValue(player, out connection) && connection != null;
        }

        public virtual bool TryGetConnectionByPlayerId(int playerId, out INetConnection connection)
        {
            connection = null;
            return playerId != 0 && _playerIdConnections.TryGetValue(playerId, out connection) && connection != null;
        }

        public virtual void UnbindConnection(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (_playerConnections.TryGetValue(player, out INetConnection connection) && connection != null)
            {
                int currentPlayerId = player.GetPlayerState()?.GetPlayerId() ?? 0;
                if (currentPlayerId != 0)
                {
                    _playerIdConnections.Remove(currentPlayerId);
                }
            }

            _playerConnections.Remove(player);
        }

        public override bool ApproveLogin(string options, string address, out string errorMessage)
        {
            if (!base.ApproveLogin(options, address, out errorMessage))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(address) && _bannedAddresses.Contains(address))
            {
                errorMessage = "Address is banned";
                return false;
            }

            if (TryGetConnectionByAddress(address, out INetConnection connection))
            {
                if (RejectDisconnectedConnections && !connection.IsConnected)
                {
                    errorMessage = "Connection is not active";
                    return false;
                }

                if (RejectUnauthenticatedConnections && !connection.IsAuthenticated)
                {
                    errorMessage = "Connection is not authenticated";
                    return false;
                }
            }
            else if (RejectUnknownAddresses)
            {
                errorMessage = "Unknown connection";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public override void RegisterPlayer(PlayerController pc)
        {
            base.RegisterPlayer(pc);

            if (pc == null)
            {
                return;
            }

            if (!TryGetConnection(pc, out INetConnection connection) || connection == null)
            {
                return;
            }

            // If the connection already has a stable platform/backend player id,
            // project it into framework PlayerState when possible.
            PlayerState state = pc.GetPlayerState();
            if (state != null && state.GetPlayerId() == 0)
            {
                ulong connectionPlayerId = connection.PlayerId;
                if (connectionPlayerId > 0 && connectionPlayerId <= int.MaxValue)
                {
                    state.SetPlayerId((int)connectionPlayerId);
                    _playerIdConnections[state.GetPlayerId()] = connection;
                }
            }
        }

        public override void UnregisterPlayer(PlayerController pc)
        {
            base.UnregisterPlayer(pc);
            UnbindConnection(pc);
        }

        public override void KickPlayer(PlayerController pc, string reason)
        {
            if (pc == null)
            {
                return;
            }

            if (_networkManager != null && TryGetConnection(pc, out INetConnection connection) && connection != null)
            {
                _networkManager.DisconnectClient(connection);
            }

            UnregisterPlayer(pc);
        }

        public override bool BanPlayer(PlayerController pc, string reason)
        {
            if (pc == null)
            {
                return false;
            }

            if (TryGetConnection(pc, out INetConnection connection) && connection != null)
            {
                if (!string.IsNullOrWhiteSpace(connection.RemoteAddress))
                {
                    _bannedAddresses.Add(connection.RemoteAddress);
                }
            }

            KickPlayer(pc, reason);
            return true;
        }

        public virtual void BanAddress(string address)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                _bannedAddresses.Add(address);
            }
        }

        public virtual void UnbanAddress(string address)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                _bannedAddresses.Remove(address);
            }
        }

        public virtual bool IsAddressBanned(string address)
        {
            return !string.IsNullOrWhiteSpace(address) && _bannedAddresses.Contains(address);
        }

        private bool TryGetConnectionByAddress(string address, out INetConnection connection)
        {
            connection = null;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            foreach (KeyValuePair<PlayerController, INetConnection> pair in _playerConnections)
            {
                if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Value.RemoteAddress))
                {
                    continue;
                }

                if (string.Equals(pair.Value.RemoteAddress, address, StringComparison.OrdinalIgnoreCase))
                {
                    connection = pair.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
