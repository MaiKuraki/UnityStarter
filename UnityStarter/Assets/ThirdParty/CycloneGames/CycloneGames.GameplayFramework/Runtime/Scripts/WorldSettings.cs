using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework
{
    public interface IWorldSettings
    {
        PlayerController PlayerControllerClass { get; }
        Pawn PawnClass { get; }
        PlayerState PlayerStateClass { get; }
        CameraManager CameraManagerClass { get; }
        SpectatorPawn SpectatorPawnClass { get; }
    }

    [CreateAssetMenu(fileName = "WorldSettings", menuName = "CycloneGames/GameplayFramework/WorldSettings")]
    public class WorldSettings : ScriptableObject, IWorldSettings
    {
        [SerializeField] private GameMode gameModeClass;
        [SerializeField] private PlayerController playerControllerClass;
        [SerializeField] private Pawn pawnClass;
        [SerializeField] private PlayerState playerStateClass;
        [SerializeField] private CameraManager cameraManagerClass;
        [SerializeField] private SpectatorPawn spectatorPawnClass;

        public GameMode GameModeClass => gameModeClass;
        public PlayerController PlayerControllerClass => playerControllerClass;
        public Pawn PawnClass => pawnClass;
        public PlayerState PlayerStateClass => playerStateClass;
        public CameraManager CameraManagerClass => cameraManagerClass;
        public SpectatorPawn SpectatorPawnClass => spectatorPawnClass;

        void OnEnable()
        {
            CLogger.Instance.AddLoggerUnique(new UnityLogger());
        }
    }
}