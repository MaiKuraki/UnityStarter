using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{

    /// <summary>
    /// The base ScriptableObject for a self-contained Gameplay Cue.
    /// It defines the visual/audio effects and the logic to execute them.
    /// A derived class can optionally implement IPersistentGameplayCue if it needs instance tracking.
    /// </summary>
    public abstract class GameplayCueSO : ScriptableObject
    {
        /// <summary>
        /// Handles the execution of a one-shot, instant Gameplay Cue.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        public virtual UniTask OnExecutedAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager) => UniTask.CompletedTask;

        /// <summary>
        /// Handles the activation of a persistent Gameplay Cue.
        /// Should return the GameObject instance it creates so the manager can track its lifetime.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        public virtual UniTask OnActiveAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager) => UniTask.CompletedTask;

        /// <summary>
        /// Handles the removal of a persistent Gameplay Cue.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        public virtual UniTask OnRemovedAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager) => UniTask.CompletedTask;
    }
}
