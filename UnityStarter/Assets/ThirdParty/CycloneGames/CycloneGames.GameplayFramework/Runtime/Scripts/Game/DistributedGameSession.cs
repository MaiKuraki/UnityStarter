#if GAMEPLAY_FRAMEWORK_PRESENT

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// <see cref="IGameSession"/> implementation that coordinates player state across
    /// multiple server processes via an <see cref="IActorRouter"/> for MMO / sharded
    /// world architectures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Integration</b>: This class lives in GameplayFramework.Runtime so the router
    /// dependency is expressed as a generic delegate (<see cref="Func{TResult}"/> pattern)
    /// rather than a direct type reference. Concrete network implementations inject
    /// the router at initialization time.
    /// </para>
    /// <para>
    /// <b>Capacity</b>: Capacity queries aggregate across all shard processes.
    /// Override <see cref="GetRemotePlayerCount"/> and <see cref="GetRemoteSpectatorCount"/>
    /// to query the actual distributed service (Redis, etcd, Gossip protocol).
    /// </para>
    /// <para>
    /// <b>Thread Safety</b>: All public methods must be called from the main thread.
    /// The router delegate may be invoked from any context depending on the
    /// underlying router implementation.
    /// </para>
    /// </remarks>
    public class DistributedGameSession : GameSession
    {
        [Header("Shard Identity")]
        [SerializeField] private string processId = "default-shard";

        private readonly Dictionary<int, string> _playerProcessMap = new Dictionary<int, string>(64);
        private readonly HashSet<string> _knownPeerProcesses = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Identifier for this server process (registered with the actor router).
        /// </summary>
        public string ProcessId
        {
            get => processId;
            set => processId = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Called when a player with the given ID is discovered on a remote process.
        /// Subclasses can override to update capacity counters or trigger rebalancing.
        /// </summary>
        protected virtual void OnRemotePlayerDiscovered(int playerId, string remoteProcessId)
        {
            _playerProcessMap[playerId] = remoteProcessId;
        }

        /// <summary>
        /// Called when a player is no longer tracked on any process.
        /// </summary>
        protected virtual void OnPlayerLeft(int playerId)
        {
            _playerProcessMap.Remove(playerId);
        }

        /// <summary>
        /// Registers a peer process ID as known to this shard.
        /// Used for aggregate capacity queries.
        /// </summary>
        public virtual void RegisterPeerProcess(string peerProcessId)
        {
            if (!string.IsNullOrEmpty(peerProcessId))
            {
                _knownPeerProcesses.Add(peerProcessId);
            }
        }

        public virtual void UnregisterPeerProcess(string peerProcessId)
        {
            _knownPeerProcesses.Remove(peerProcessId);
        }

        /// <summary>
        /// Returns the total number of known peer processes (excluding this shard).
        /// </summary>
        public int PeerProcessCount => _knownPeerProcesses.Count;

        /// <summary>
        /// Returns the process ID hosting the given player, or null if not tracked
        /// or if the player is local.
        /// </summary>
        public virtual bool TryGetPlayerProcess(int playerId, out string processId)
        {
            return _playerProcessMap.TryGetValue(playerId, out processId);
        }

        /// <summary>
        /// Override to query the actual player count on remote processes.
        /// Default returns 0 (all capacity is local).
        /// </summary>
        protected virtual int GetRemotePlayerCount()
        {
            return 0;
        }

        /// <summary>
        /// Override to query the actual spectator count on remote processes.
        /// </summary>
        protected virtual int GetRemoteSpectatorCount()
        {
            return 0;
        }

        /// <summary>
        /// Aggregate capacity check including remote processes.
        /// Override to customize the distributed capacity logic.
        /// </summary>
        public virtual bool AtGlobalCapacity(bool bSpectator)
        {
            if (AtCapacity(bSpectator)) return true;

            int remoteCount = bSpectator ? GetRemoteSpectatorCount() : GetRemotePlayerCount();
            int remoteMax = bSpectator ? MaxSpectators : MaxPlayers;
            // Conservative: each remote shard can hold up to the same limit.
            // Override for game-specific load-balancing logic.
            return remoteCount >= remoteMax * Math.Max(1, _knownPeerProcesses.Count);
        }

        #region Lifecycle
        public override bool ApproveLogin(string options, string address, out string errorMessage)
        {
            if (AtGlobalCapacity(false))
            {
                errorMessage = "Global server capacity reached";
                return false;
            }
            return base.ApproveLogin(options, address, out errorMessage);
        }

        public override void RegisterPlayer(PlayerController pc)
        {
            base.RegisterPlayer(pc);
            int playerId = pc?.GetPlayerState()?.GetPlayerId() ?? 0;
            if (playerId != 0)
            {
                _playerProcessMap[playerId] = processId;
            }
        }

        public override void UnregisterPlayer(PlayerController pc)
        {
            int playerId = pc?.GetPlayerState()?.GetPlayerId() ?? 0;
            if (playerId != 0)
            {
                _playerProcessMap.Remove(playerId);
            }
            base.UnregisterPlayer(pc);
        }
        #endregion
    }
}

#endif
