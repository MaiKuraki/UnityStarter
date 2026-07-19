using System.Threading;

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
        public virtual UniTask OnExecutedAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <summary>
        /// Handles the witnessed activation transition for a non-persistent Gameplay Cue.
        /// Persistent cue assets implement <see cref="IPersistentGameplayCue"/> so creation and event callbacks
        /// share one generation-stamped instance lease.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        public virtual UniTask OnActiveAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <summary>
        /// Handles presentation first observed while the cue is active.
        /// This is invoked after OnActive for a witnessed activation and by itself for join-in-progress state.
        /// </summary>
        public virtual UniTask OnWhileActiveAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <summary>
        /// Handles the removal of a persistent Gameplay Cue.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        public virtual UniTask OnRemovedAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default) => UniTask.CompletedTask;
    }
}
