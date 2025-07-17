using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A manager for GameplayCues. It handles on-demand async loading,
    /// execution, and robust lifetime management of Cue instances.
    /// </summary>
    public sealed class GameplayCueManager
    {
        private static readonly GameplayCueManager instance = new GameplayCueManager();
        public static GameplayCueManager Instance => instance;

        private IResourceLocator resourceLocator;
        private IGameObjectPoolManager poolManager;
        private bool isInitialized = false;

        private readonly Dictionary<GameplayTag, string> tagToAddress = new Dictionary<GameplayTag, string>();
        private readonly Dictionary<string, GameplayCueSO> loadedCues = new Dictionary<string, GameplayCueSO>();

        private class ActiveCueInstance { public GameplayTag CueTag; public GameObject Instance; }
        private readonly Dictionary<AbilitySystemComponent, List<ActiveCueInstance>> activeInstances = new Dictionary<AbilitySystemComponent, List<ActiveCueInstance>>();

        private GameplayCueManager() { }

        /// <summary>
        /// Initializes all internal systems and discovers cue assets. Must be called once at game startup.
        /// </summary>
        public async UniTask InitializeAsync(List<string> labelsToDiscover)
        {
            if (isInitialized) return;

            resourceLocator = new AddressableResourceLocator();
            poolManager = new GameObjectPoolManager(resourceLocator);

            foreach (var label in labelsToDiscover)
            {
                AsyncOperationHandle<IList<IResourceLocation>> locationsHandle = Addressables.LoadResourceLocationsAsync(label, typeof(GameplayCueSO));

                IList<IResourceLocation> locations = await locationsHandle.Task;

                foreach (var loc in locations)
                {
                    if (GameplayTagManager.TryRequestTag(loc.PrimaryKey, out var tag))
                    {
                        tagToAddress[tag] = loc.PrimaryKey;
                    }
                }

                Addressables.Release(locationsHandle);
            }

            isInitialized = true;
            CLogger.LogInfo($"[GameplayCueManager] Initialized. Discovered {tagToAddress.Count} addressable GameplayCues.");
        }

        /// <summary>
        /// The main entry point to trigger a GameplayCue event.
        /// </summary>
        public async UniTaskVoid HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec)
        {
            if (!isInitialized || cueTag == GameplayTag.None) return;

            var cueSO = await GetCueSOAsync(cueTag);
            if (cueSO == null) return;

            var parameters = new GameplayCueParameters(spec);

            switch (eventType)
            {
                case EGameplayCueEvent.Executed:
                    await cueSO.OnExecutedAsync(parameters, poolManager);
                    break;
                case EGameplayCueEvent.OnActive:
                case EGameplayCueEvent.WhileActive:
                    await cueSO.OnActiveAsync(parameters, poolManager);
                    break;
                case EGameplayCueEvent.Removed:
                    await cueSO.OnRemovedAsync(parameters, poolManager);
                    break;
            }
        }

        private async UniTask<GameplayCueSO> GetCueSOAsync(GameplayTag cueTag)
        {
            if (loadedCues.TryGetValue(cueTag.Name, out var cue)) return cue;

            if (tagToAddress.TryGetValue(cueTag, out var address))
            {
                var loadedAsset = await resourceLocator.LoadAssetAsync<GameplayCueSO>(address);
                if (loadedAsset) loadedCues[address] = loadedAsset;
                return loadedAsset;
            }
            return null;
        }

        public void OnOwnerDestroyed(AbilitySystemComponent owner)
        {
            // Placeholder for future robust persistent Cue tracking and cleanup.
        }

        /// <summary>
        /// Shuts down all systems, clearing pools and releasing assets. Call on application quit.
        /// </summary>
        public void Shutdown()
        {
            poolManager?.Shutdown();
            resourceLocator?.ReleaseAll();
            loadedCues.Clear();
            tagToAddress.Clear();
            isInitialized = false;
        }
    }
}