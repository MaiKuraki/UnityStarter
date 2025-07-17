using CycloneGames.GameplayTags.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Defines the contract for a system that can asynchronously load assets.
    /// This decouples the rest of the system from a specific implementation like Addressables.
    /// </summary>
    public interface IResourceLocator
    {
        UniTask<T> LoadAssetAsync<T>(object key) where T : Object;
        void ReleaseAsset(object key);
        void ReleaseAll();
    }

    /// <summary>
    /// Defines the contract for a system that manages pools of GameObjects.
    /// </summary>
    public interface IGameObjectPoolManager
    {
        UniTask<GameObject> GetAsync(AssetReferenceGameObject assetRef, Vector3 position, Quaternion rotation, Transform parent = null);
        void Release(GameObject instance);
        void Shutdown();
    }

    /// <summary>
    /// Describes the type of event that triggered a GameplayCue.
    /// </summary>
    public enum EGameplayCueEvent
    {
        OnActive,
        WhileActive,
        Removed,
        Executed
    }

    /// <summary>
    /// A data structure passed to GameplayCues, providing context about the event.
    /// </summary>
    public readonly struct GameplayCueParameters
    {
        public readonly GameplayEffectSpec EffectSpec;
        public AbilitySystemComponent Source => EffectSpec.Source;
        public AbilitySystemComponent Target => EffectSpec.Target;
        public GameObject SourceObject => Source?.AvatarActor as GameObject;
        public GameObject TargetObject => Target?.AvatarActor as GameObject;

        public GameplayCueParameters(GameplayEffectSpec spec)
        {
            EffectSpec = spec;
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