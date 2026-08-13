using CycloneGames.GameplayFramework.Core;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Authoritative Unity participant boundary. Calls are serialized by the owning World.
    /// </summary>
    public interface IGameSession
    {
        int MaxPlayers { get; }
        int MaxSpectators { get; }
        int PlayerCount { get; }
        int SpectatorCount { get; }

        bool AtCapacity(bool spectator);
        bool ApproveLogin(in PlayerLoginRequest request, out string errorMessage);
        bool TryRegisterPlayer(PlayerController playerController, bool spectator, out string errorMessage);
        bool ContainsPlayer(PlayerController playerController);
        bool UnregisterPlayer(PlayerController playerController);
        bool TrySetSpectatorStatus(PlayerController playerController, bool spectator, out string errorMessage);
        void HandleMatchHasStarted();
        void HandleMatchHasEnded();
    }
}
