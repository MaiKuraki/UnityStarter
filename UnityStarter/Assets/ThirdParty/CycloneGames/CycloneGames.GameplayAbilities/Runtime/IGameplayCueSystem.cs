using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a loaded asset handle that must be disposed when no longer needed.
    /// This allows the underlying asset management system to properly track and evict unused assets.
    /// </summary>
    public interface IResourceHandle<T> : System.IDisposable where T : UnityEngine.Object
    {
        T Asset { get; }
    }

    /// <summary>
    /// Defines the contract for a system that can asynchronously load assets.
    /// This decouples the rest of the system from a specific implementation like Addressables.
    /// </summary>
    public interface IResourceLocator
    {
        UniTask<IResourceHandle<T>> LoadAssetAsync<T>(string key, string bucket = null, string cacheTag = null, string cacheOwner = null, CancellationToken cancellationToken = default) where T : UnityEngine.Object;
    }

    /// <summary>
    /// Defines the contract for a system that manages pools of GameObjects.
    /// </summary>
    public interface IGameObjectPoolManager
    {
        UniTask<GameObject> GetAsync(string assetRef, Vector3 position, Quaternion rotation, Transform parent = null, string bucket = null, string cacheTag = null, string cacheOwner = null);
        void Release(GameObject instance);
        void Shutdown();
    }

    // EGameplayCueEvent is canonically defined in Core/GASInterfaces.cs.
    // Re-export here so Runtime consumers can use it without importing Core namespace.

    /// <summary>
    /// A data structure passed to GameplayCues, providing context about the event.
    /// </summary>
    public readonly struct GameplayCueParameters
    {
        public readonly GameplayEffect EffectDefinition;
        public readonly IGameplayEffectContext EffectContext;
        public readonly AbilitySystemComponent Source;
        public readonly AbilitySystemComponent Target;
        public readonly GameObject SourceObject;
        public readonly GameObject TargetObject;
        public readonly int EffectLevel;
        public readonly float EffectDuration;
        public readonly GASPredictionKey PredictionKey;

        public GameplayCueParameters(GameplayEffectSpec spec)
        {
            EffectDefinition = spec?.Def;
            EffectContext = spec?.Context;
            Source = spec?.Source;
            Target = spec?.Target;
            SourceObject = Source?.AvatarGameObject;
            TargetObject = Target?.AvatarGameObject;
            EffectLevel = spec?.Level ?? 0;
            EffectDuration = spec?.Duration ?? 0f;
            PredictionKey = spec?.Context?.PredictionKey ?? default;
        }

        public GameplayCueParameters(GameplayCueEventParams parameters)
        {
            EffectDefinition = parameters.EffectDefinition as GameplayEffect;
            EffectContext = parameters.EffectContext as IGameplayEffectContext;
            Source = parameters.Source as AbilitySystemComponent;
            Target = parameters.Target as AbilitySystemComponent;
            SourceObject = parameters.SourceObject as GameObject ?? Source?.AvatarGameObject;
            TargetObject = parameters.TargetObject as GameObject ?? Target?.AvatarGameObject;
            EffectLevel = parameters.EffectLevel;
            EffectDuration = parameters.EffectDuration;
            PredictionKey = parameters.PredictionKey;
        }
    }

    /// <summary>
    /// An interface for GameplayCueSO assets that create persistent instances (e.g., looping VFX)
    /// which need to be tracked and explicitly removed by the GameplayCueManager.
    /// </summary>
    public interface IPersistentGameplayCue
    {
        /// <summary>
        /// Handles the activation of a persistent Gameplay Cue.
        /// It MUST return the instantiated GameObject so the manager can track it.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <param name="poolManager">The pool manager to request objects from.</param>
        /// <returns>The created GameObject instance for lifetime tracking.</returns>
        UniTask<GameObject> OnActiveAsync(GameplayCueParameters parameters, IGameObjectPoolManager poolManager);

        /// <summary>
        /// Handles the removal of a persistent Gameplay Cue.
        /// </summary>
        /// <param name="instance">The GameObject instance that was created by OnActiveAsync.</param>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <returns>A UniTask for async operations.</returns>
        UniTask OnRemovedAsync(GameObject instance, GameplayCueParameters parameters);
    }

    /// <summary>
    /// Defines a contract for a runtime object that can handle a GameplayCue event.
    /// Used for dynamically registered cue handlers.
    /// </summary>
    public interface IGameplayCueHandler
    {
        void HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueParameters parameters);
    }
}
