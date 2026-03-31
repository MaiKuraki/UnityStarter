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

        /// <summary>
        /// Validates that all required references are assigned.
        /// Call this explicitly when the WorldSettings is about to be used (e.g., at game startup).
        /// </summary>
        public bool Validate(bool logWarnings = true)
        {
            bool valid = true;
            if (gameModeClass == null)
            {
                if (logWarnings) Debug.LogWarning($"[WorldSettings] '{name}': GameModeClass is not assigned.");
                valid = false;
            }
            if (playerControllerClass == null)
            {
                if (logWarnings) Debug.LogWarning($"[WorldSettings] '{name}': PlayerControllerClass is not assigned.");
                valid = false;
            }
            if (pawnClass == null)
            {
                if (logWarnings) Debug.LogWarning($"[WorldSettings] '{name}': PawnClass is not assigned.");
                valid = false;
            }
            return valid;
        }
    }
}