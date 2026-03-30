using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
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
        [Header("Game Mode")]
        [SerializeField] private GameMode gameModeClass;

        [Header("Player")]
        [SerializeField] private PlayerController playerControllerClass;
        [SerializeField] private Pawn pawnClass;
        [SerializeField] private PlayerState playerStateClass;

        [Header("Camera")]
        [SerializeField] private CameraManager cameraManagerClass;

        [Header("Spectator")]
        [SerializeField] private SpectatorPawn spectatorPawnClass;

        public GameMode GameModeClass => gameModeClass;
        public PlayerController PlayerControllerClass => playerControllerClass;
        public Pawn PawnClass => pawnClass;
        public PlayerState PlayerStateClass => playerStateClass;
        public CameraManager CameraManagerClass => cameraManagerClass;
        public SpectatorPawn SpectatorPawnClass => spectatorPawnClass;

        void OnValidate()
        {
            // Validate critical references in editor
            if (gameModeClass == null)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': GameModeClass is not assigned.");
            }
            if (playerControllerClass == null)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': PlayerControllerClass is not assigned.");
            }
            if (pawnClass == null)
            {
                Debug.LogWarning($"[WorldSettings] '{name}': PawnClass is not assigned.");
            }
        }
    }
}