using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Scriptable Object configuration for framework-level GameMode rules.
    /// Only contains fields that the base GameMode class itself reads.
    ///
    /// For game-specific rules (match duration, friendly fire, score limits, etc.)
    /// create a derived class and override <see cref="GameMode.SetGameModeConfig"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "GameModeConfig", menuName = "CycloneGames/GameplayFramework/GameModeConfig")]
    public class GameModeConfig : ScriptableObject
    {
        [Header("Player Spawn")]
        [Tooltip("When true, all players entering the game start in spectator mode.")]
        [SerializeField] private bool bDefaultToSpectator;

        public bool DefaultToSpectator => bDefaultToSpectator;

        /// <summary>
        /// Apply this configuration to a GameMode instance.
        /// Call this during GameMode initialization.
        /// </summary>
        public virtual void ApplyTo(GameMode gameMode)
        {
            if (gameMode == null) return;

            gameMode.StartPlayersAsSpectators = bDefaultToSpectator;
        }
    }
}
