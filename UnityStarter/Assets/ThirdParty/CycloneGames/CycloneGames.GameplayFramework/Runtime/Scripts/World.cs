namespace CycloneGames.GameplayFramework
{
    public interface IWorld
    {
        public void SetGameMode(GameMode inGameModeRef);
        public GameMode GetGameMode();
        public PlayerController GetPlayerController();      //  TODO: maybe a player index
        public Pawn GetPlayerPawn();                        //  TODO: maybe a player index
    }
    
    /// <summary>
    /// NOTE: This class is NOT similar with UWorld in UnrealEngine
    /// </summary>
    public class World : IWorld
    {
        private GameMode savedGameMode;
        
        public void Initialize()
        {
            
        }

        public void SetGameMode(GameMode inGameModeRef)
        {
            savedGameMode = inGameModeRef;
        }

        public GameMode GetGameMode()
        {
            return savedGameMode;
        }

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