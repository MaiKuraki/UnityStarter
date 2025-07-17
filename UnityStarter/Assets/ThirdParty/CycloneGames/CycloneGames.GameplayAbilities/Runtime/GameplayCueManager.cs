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

        // Registry for asset-based cues, discovered at startup. Key is the tag (from the address).
        private readonly Dictionary<GameplayTag, string> staticCueAddressRegistry = new Dictionary<GameplayTag, string>();
        // Cache for loaded cue assets to prevent redundant loading.
        private readonly Dictionary<string, GameplayCueSO> loadedStaticCues = new Dictionary<string, GameplayCueSO>();

        // Registry for dynamically added cue handlers at runtime.
        private readonly Dictionary<GameplayTag, List<IGameplayCueHandler>> runtimeCueHandlers = new Dictionary<GameplayTag, List<IGameplayCueHandler>>();

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
                var locationsHandle = Addressables.LoadResourceLocationsAsync(label, typeof(GameplayCueSO));
                IList<IResourceLocation> locations = await locationsHandle.Task;
                foreach (var loc in locations)
                {
                    if (GameplayTagManager.TryRequestTag(loc.PrimaryKey, out var tag))
                    {
                        staticCueAddressRegistry[tag] = loc.PrimaryKey;
                    }
                }
                Addressables.Release(locationsHandle);
            }

            isInitialized = true;
            CLogger.LogInfo($"[GameplayCueManager] Initialized. Discovered {staticCueAddressRegistry.Count} static GameplayCues.");
        }

        /// <summary>
        /// Registers a handler for a dynamic GameplayCue at runtime.
        /// </summary>
        public void RegisterRuntimeHandler(GameplayTag cueTag, IGameplayCueHandler handler)
        {
            if (cueTag == GameplayTag.None || handler == null) return;
            if (!runtimeCueHandlers.TryGetValue(cueTag, out var handlers))
            {
                handlers = new List<IGameplayCueHandler>();
                runtimeCueHandlers[cueTag] = handlers;
            }
            handlers.Add(handler);
        }

        /// <summary>
        /// Unregisters a dynamic GameplayCue handler.
        /// </summary>
        public void UnregisterRuntimeHandler(GameplayTag cueTag, IGameplayCueHandler handler)
        {
            if (cueTag == GameplayTag.None || handler == null) return;
            if (runtimeCueHandlers.TryGetValue(cueTag, out var handlers))
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// The main entry point to trigger a GameplayCue event.
        /// </summary>
        public async UniTaskVoid HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec)
        {
            if (!isInitialized || cueTag == GameplayTag.None) return;

            var parameters = new GameplayCueParameters(spec);

            // Handle static, asset-based cues.
            if (staticCueAddressRegistry.ContainsKey(cueTag))
            {
                var cueSO = await GetCueSOAsync(cueTag);
                if (cueSO != null)
                {
                    await DispatchToCueSO(cueSO, cueTag, eventType, parameters);
                }
            }

            // Handle dynamic, code-based cues.
            if (runtimeCueHandlers.TryGetValue(cueTag, out var handlers))
            {
                foreach (var handler in new List<IGameplayCueHandler>(handlers))
                {
                    handler.HandleCue(cueTag, eventType, parameters);
                }
            }
        }

        private async UniTask DispatchToCueSO(GameplayCueSO cueSO, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueParameters parameters)
        {
            switch (eventType)
            {
                case EGameplayCueEvent.Executed:
                    await cueSO.OnExecutedAsync(parameters, poolManager);
                    break;
                case EGameplayCueEvent.OnActive:
                case EGameplayCueEvent.WhileActive:
                    if (cueSO is IPersistentGameplayCue persistentCue)
                    {
                        GameObject instance = await persistentCue.OnActiveAsync(parameters, poolManager);
                        if (instance != null) AddInstanceToTracker(parameters.Target, cueTag, instance);
                    }
                    else
                    {
                        await cueSO.OnActiveAsync(parameters, poolManager);
                    }
                    break;
                case EGameplayCueEvent.Removed:
                    if (cueSO is IPersistentGameplayCue persistentCueToRemove)
                    {
                        await RemoveInstancesFromTrackerAsync(parameters.Target, cueTag, persistentCueToRemove, parameters);
                    }
                    else
                    {
                        await cueSO.OnRemovedAsync(parameters, poolManager);
                    }
                    break;
            }
        }

        private void AddInstanceToTracker(AbilitySystemComponent target, GameplayTag tag, GameObject instance)
        {
            if (target == null || instance == null) return;
            if (!activeInstances.TryGetValue(target, out var instanceList))
            {
                instanceList = new List<ActiveCueInstance>();
                activeInstances[target] = instanceList;
            }
            instanceList.Add(new ActiveCueInstance { CueTag = tag, Instance = instance });
        }

        private async UniTask RemoveInstancesFromTrackerAsync(AbilitySystemComponent target, GameplayTag tag, IPersistentGameplayCue persistentCue, GameplayCueParameters parameters)
        {
            if (target == null || !activeInstances.TryGetValue(target, out var instanceList)) return;

            var toRemove = new List<ActiveCueInstance>();
            foreach (var activeCue in instanceList)
            {
                if (activeCue.CueTag == tag) toRemove.Add(activeCue);
            }

            foreach (var itemToRemove in toRemove)
            {
                await persistentCue.OnRemovedAsync(itemToRemove.Instance, parameters);
                poolManager.Release(itemToRemove.Instance);
                instanceList.Remove(itemToRemove);
            }
        }

        private async UniTask<GameplayCueSO> GetCueSOAsync(GameplayTag cueTag)
        {
            if (!staticCueAddressRegistry.TryGetValue(cueTag, out var address)) return null;

            if (loadedStaticCues.TryGetValue(address, out var cue)) return cue;

            var loadedAsset = await resourceLocator.LoadAssetAsync<GameplayCueSO>(address);
            if (loadedAsset) loadedStaticCues[address] = loadedAsset;
            return loadedAsset;
        }

        /// <summary>
        /// Shuts down all systems, clearing pools and releasing assets. Call on application quit.
        /// </summary>
        public void Shutdown()
        {
            poolManager?.Shutdown();
            resourceLocator?.ReleaseAll();
            staticCueAddressRegistry.Clear();
            loadedStaticCues.Clear();
            runtimeCueHandlers.Clear();
            activeInstances.Clear();
            isInitialized = false;
        }
    }
}