using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Network-agnostic game session interface. Provides game-level session management
    /// independent of the underlying networking solution.
    ///
    /// Integration patterns:
    /// 1. Mirror: Create MirrorGameSession implementing IGameSession, wrapping NetworkManager.
    /// 2. CycloneGames.Networking: Create a session adapter bridging INetworkManager to IGameSession.
    /// 3. Custom: Implement IGameSession with any networking stack (Photon, Netcode, etc.)
    /// 4. Standalone: Use the default GameSession class for local/offline play.
    ///
    /// Without an IGameSession implementation, GameMode operates in standalone mode
    /// and all login/capacity checks are bypassed.
    /// </summary>
    public interface IGameSession
    {
        int MaxPlayers { get; }
        int MaxSpectators { get; }

        /// <summary>
        /// Validate whether a player should be allowed to join.
        /// Called by GameMode.PreLogin before creating the PlayerController.
        /// </summary>
        /// <param name="options">Game-specific options string (e.g., team preference, password).</param>
        /// <param name="address">Network address or unique identifier of the connecting player.</param>
        /// <param name="errorMessage">Error message to return if login is denied.</param>
        /// <returns>True if the login is approved.</returns>
        bool ApproveLogin(string options, string address, out string errorMessage);

        /// <summary>
        /// Register a player with the session after their PlayerController is fully set up.
        /// </summary>
        void RegisterPlayer(PlayerController pc);

        /// <summary>
        /// Unregister a player from the session (disconnect, logout).
        /// </summary>
        void UnregisterPlayer(PlayerController pc);

        /// <summary>
        /// Check if the game is at capacity.
        /// </summary>
        bool AtCapacity(bool bSpectator);

        /// <summary>
        /// Kick a player from the session.
        /// The actual network disconnect is the responsibility of the networking adapter.
        /// </summary>
        void KickPlayer(PlayerController pc, string reason);

        /// <summary>
        /// Ban a player from the session.
        /// The actual enforcement (IP ban, account ban) depends on the networking adapter.
        /// </summary>
        bool BanPlayer(PlayerController pc, string reason);

        /// <summary>
        /// Called when the match transitions to started state.
        /// </summary>
        void HandleMatchHasStarted();

        /// <summary>
        /// Called when the match transitions to ended state.
        /// </summary>
        void HandleMatchHasEnded();
    }

    /// <summary>
    /// Default game session implementation providing local/standalone session management.
    /// Subclass for game-specific session logic, or implement IGameSession directly
    /// for full control over session behavior with your networking adapter.
    /// </summary>
    public class GameSession : Actor, IGameSession
    {
        [SerializeField] private int maxPlayers = 16;
        [SerializeField] private int maxSpectators = 4;

        private int currentPlayerCount;
        private int currentSpectatorCount;

        public int MaxPlayers => maxPlayers;
        public int MaxSpectators => maxSpectators;

        public virtual bool ApproveLogin(string options, string address, out string errorMessage)
        {
            if (AtCapacity(false))
            {
                errorMessage = "Server is full";
                return false;
            }
            errorMessage = null;
            return true;
        }

        public virtual void RegisterPlayer(PlayerController pc)
        {
            if (pc == null) return;
            if (pc.GetPlayerState()?.IsSpectator() == true)
            {
                currentSpectatorCount++;
            }
            else
            {
                currentPlayerCount++;
            }
        }

        public virtual void UnregisterPlayer(PlayerController pc)
        {
            if (pc == null) return;
            if (pc.GetPlayerState()?.IsSpectator() == true)
            {
                currentSpectatorCount = Mathf.Max(0, currentSpectatorCount - 1);
            }
            else
            {
                currentPlayerCount = Mathf.Max(0, currentPlayerCount - 1);
            }
        }

        public virtual bool AtCapacity(bool bSpectator)
        {
            return bSpectator
                ? currentSpectatorCount >= maxSpectators
                : currentPlayerCount >= maxPlayers;
        }

        public virtual void KickPlayer(PlayerController pc, string reason) { }

        public virtual bool BanPlayer(PlayerController pc, string reason) => false;

        public virtual void HandleMatchHasStarted() { }

        public virtual void HandleMatchHasEnded() { }
    }
}
