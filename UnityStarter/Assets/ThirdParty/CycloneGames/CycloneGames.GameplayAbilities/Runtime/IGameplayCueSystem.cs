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
}