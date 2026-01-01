using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A manager for GameplayCues. It handles on-demand async loading,
    /// execution, and robust lifetime management of Cue instances.
    /// Implements IGameplayCueManager for DI compatibility.
    /// </summary>
    public sealed class GameplayCueManager : IGameplayCueManager
    {
        private static GameplayCueManager s_DefaultInstance;
        private static IGameplayCueManager s_CustomInstance;
        
        /// <summary>
        /// Gets the default concrete GameplayCueManager instance.
        /// Use this for internal Runtime assembly code that needs the full API.
        /// </summary>
        public static GameplayCueManager Default => s_DefaultInstance ??= new GameplayCueManager();
        
        /// <summary>
        /// Gets the current GameplayCue manager instance (interface).
        /// Returns custom DI instance if set, otherwise returns Default.
        /// </summary>
        public static IGameplayCueManager Instance => s_CustomInstance ?? Default;
        
        /// <summary>
        /// Sets the GameplayCue manager instance. Call this during DI container setup.
        /// Also updates the GASServices.CueManager for cross-assembly access.
        /// </summary>
        public static void SetInstance(IGameplayCueManager instance)
        {
            s_CustomInstance = instance;
            GASServices.CueManager = instance;
        }
        
        /// <summary>
        /// Resets the instance to null. Call during test teardown or game shutdown.
        /// </summary>
        public static void ResetInstance()
        {
            s_CustomInstance = null;
            s_DefaultInstance = null;
            GASServices.Reset();
        }

        public IResourceLocator ResourceLocator => resourceLocator;
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
        
        public GameplayCueManager() { }

        public void Initialize(IAssetPackage assetPackage)
        {
            if (isInitialized) return;

            resourceLocator = new AssetManagementResourceLocator(assetPackage);
            poolManager = new GameObjectPoolManager(resourceLocator);

            isInitialized = true;
            GASLog.Info("GameplayCueManager initialized.");
        }
        
        // IGameplayCueManager.Initialize (generic object version for Core interface)
        void IGameplayCueManager.Initialize(object assetPackage)
        {
            if (assetPackage is IAssetPackage pkg)
            {
                Initialize(pkg);
            }
        }

        /// <summary>
        /// Registers a static, asset-based GameplayCue.
        /// </summary>
        public void RegisterStaticCue(GameplayTag cueTag, string assetAddress)
        {
            if (!cueTag.IsNone && !string.IsNullOrEmpty(assetAddress))
            {
                staticCueAddressRegistry[cueTag] = assetAddress;
            }
        }

        /// <summary>
        /// Registers a handler for a dynamic GameplayCue at runtime.
        /// </summary>
        public void RegisterRuntimeHandler(GameplayTag cueTag, IGameplayCueHandler handler)
        {
            if (cueTag.IsNone || handler == null) return;
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
            if (cueTag.IsNone || handler == null) return;
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
            if (!isInitialized || cueTag.IsNone) return;

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
                using (CycloneGames.GameplayTags.Runtime.Pools.ListPool<IGameplayCueHandler>.Get(out var safeHandlers))
                {
                    safeHandlers.AddRange(handlers);
                    for (int i = 0; i < safeHandlers.Count; i++)
                    {
                        safeHandlers[i].HandleCue(cueTag, eventType, parameters);
                    }
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

            // Use ListPool to avoid GC allocation
            using (CycloneGames.GameplayTags.Runtime.Pools.ListPool<ActiveCueInstance>.Get(out var toRemove))
            {
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
        
        #region IGameplayCueManager Interface Implementation
        
        /// <summary>
        /// Interface method - handles cue via Core interface (uses object types).
        /// </summary>
        void IGameplayCueManager.HandleCue(object asc, GameplayTag cueTag, Core.EGameplayCueEvent eventType, GameplayCueEventParams parameters)
        {
            if (asc is AbilitySystemComponent ascTyped && parameters.EffectSpec is GameplayEffectSpec spec)
            {
                // Convert Core event type to Runtime event type
                var runtimeEventType = (EGameplayCueEvent)(int)eventType;
                HandleCue(cueTag, runtimeEventType, spec).Forget();
            }
        }
        
        /// <summary>
        /// Interface method - removes all cues for a specific ASC.
        /// </summary>
        void IGameplayCueManager.RemoveAllCuesFor(object asc)
        {
            if (asc is AbilitySystemComponent ascTyped && activeInstances.TryGetValue(ascTyped, out var instances))
            {
                foreach (var instance in instances)
                {
                    if (instance.Instance != null)
                    {
                        poolManager?.Release(instance.Instance);
                    }
                }
                instances.Clear();
            }
        }
        
        #endregion
    }
}