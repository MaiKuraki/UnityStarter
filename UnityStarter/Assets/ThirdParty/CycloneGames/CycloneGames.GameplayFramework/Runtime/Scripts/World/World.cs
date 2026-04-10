using System.Collections.Generic;

namespace CycloneGames.GameplayFramework.Runtime
{
    public interface IWorld
    {
        void SetGameMode(IGameMode inGameModeRef);
        GameMode GetGameMode();
        GameState GetGameState();
        PlayerController GetPlayerController();
        Pawn GetPlayerPawn();
    }

    /// <summary>
    /// Non-MonoBehaviour world context. Acts as a service locator for GameMode, GameState,
    /// and player queries. Instantiated per-session (not tied to a scene lifecycle).
    /// </summary>
    public class World : IWorld
    {
        private GameMode savedGameMode;
        private GameState gameState;

        public void Initialize() { }

        public void SetGameMode(IGameMode inGameModeRef)
        {
            savedGameMode = (GameMode)inGameModeRef;
        }

        public GameMode GetGameMode() => savedGameMode;

        public void SetGameState(GameState inGameState) => gameState = inGameState;
        public GameState GetGameState() => gameState;

        public PlayerController GetPlayerController()
        {
            return savedGameMode?.GetPlayerController();
        }

        public Pawn GetPlayerPawn()
        {
            return savedGameMode?.GetPlayerController()?.GetPawn();
        }
    }
}