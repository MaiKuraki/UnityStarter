using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
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
        public static GameplayCueManager Default
        {
            get
            {
                if (s_DefaultInstance != null) return s_DefaultInstance;
                //  Thread-safe lazy init --Interlocked.CompareExchange guarantees exactly one winner;
                // any concurrent new GameplayCueManager() that lost the race is discarded.
                var newInstance = new GameplayCueManager();
                return Interlocked.CompareExchange(ref s_DefaultInstance, newInstance, null) ?? newInstance;
            }
        }

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

        //  Cancellation token source tied to this manager's lifetime.
        // Cancelled on Shutdown() to interrupt any in-flight async cue loads.
        private CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        // Registry for asset-based cues, discovered at startup. Key is the tag (from the address).
        private readonly Dictionary<GameplayTag, string> staticCueAddressRegistry = new Dictionary<GameplayTag, string>();
        // Cache for loaded cue assets to prevent redundant loading.
        private readonly Dictionary<string, IResourceHandle<GameplayCueSO>> loadedStaticCues = new Dictionary<string, IResourceHandle<GameplayCueSO>>();

        // Registry for dynamically added cue handlers at runtime.
        private readonly Dictionary<GameplayTag, List<IGameplayCueHandler>> runtimeCueHandlers = new Dictionary<GameplayTag, List<IGameplayCueHandler>>();

        private struct ActiveCueInstance { public GameplayTag CueTag; public GameObject Instance; public GASPredictionKey PredictionKey; }
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
        public UniTaskVoid HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec)
            => HandleCue(cueTag, eventType, new GameplayCueParameters(spec));

        /// <summary>
        /// Snapshot-based GameplayCue dispatch.
        /// The cue parameters are immutable so async loading cannot observe pooled runtime state.
        /// </summary>
        public async UniTaskVoid HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueParameters parameters)
        {
            if (!isInitialized || cueTag.IsNone) return;

            var ct = _shutdownCts.Token;

            // Handle static, asset-based cues.
            if (staticCueAddressRegistry.ContainsKey(cueTag))
            {
                var cueSO = await GetCueSOAsync(cueTag, ct);
                //  If the manager was shut down or ASC was destroyed during the async load, bail out.
                if (ct.IsCancellationRequested) return;
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
                        if (instance != null) AddInstanceToTracker(parameters.Target, cueTag, instance, parameters.PredictionKey);
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

        private void AddInstanceToTracker(AbilitySystemComponent target, GameplayTag tag, GameObject instance, GASPredictionKey predictionKey)
        {
            if (target == null || instance == null) return;
            if (!activeInstances.TryGetValue(target, out var instanceList))
            {
                instanceList = new List<ActiveCueInstance>();
                activeInstances[target] = instanceList;
            }
            instanceList.Add(new ActiveCueInstance { CueTag = tag, Instance = instance, PredictionKey = predictionKey });
        }

        public void AcceptPredictedCues(AbilitySystemComponent target, GASPredictionKey predictionKey)
        {
            if (target == null || !predictionKey.IsValid || !activeInstances.TryGetValue(target, out var instanceList)) return;

            for (int i = 0; i < instanceList.Count; i++)
            {
                var activeCue = instanceList[i];
                if (activeCue.PredictionKey.Equals(predictionKey))
                {
                    activeCue.PredictionKey = default;
                    instanceList[i] = activeCue;
                }
            }
        }

        public async UniTaskVoid RemovePredictedCues(AbilitySystemComponent target, GASPredictionKey predictionKey)
        {
            if (target == null || !predictionKey.IsValid || !activeInstances.TryGetValue(target, out var instanceList)) return;

            using (CycloneGames.GameplayTags.Runtime.Pools.ListPool<ActiveCueInstance>.Get(out var toRemove))
            {
                for (int i = 0; i < instanceList.Count; i++)
                {
                    var activeCue = instanceList[i];
                    if (activeCue.PredictionKey.Equals(predictionKey))
                    {
                        toRemove.Add(activeCue);
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var itemToRemove = toRemove[i];
                    var parameters = new GameplayCueParameters(new GameplayCueEventParams(
                        null,
                        target,
                        null,
                        null,
                        null,
                        target.AvatarGameObject,
                        0,
                        0f,
                        predictionKey));

                    await ReleaseTrackedCueInstanceAsync(itemToRemove, parameters);
                    RemoveTrackedCueInstance(instanceList, itemToRemove);
                }
            }
        }

        private async UniTask RemoveInstancesFromTrackerAsync(AbilitySystemComponent target, GameplayTag tag, IPersistentGameplayCue persistentCue, GameplayCueParameters parameters)
        {
            if (target == null || !activeInstances.TryGetValue(target, out var instanceList)) return;

            // Use ListPool to avoid GC allocation
            using (CycloneGames.GameplayTags.Runtime.Pools.ListPool<ActiveCueInstance>.Get(out var toRemove))
            {
                for (int i = 0; i < instanceList.Count; i++)
                {
                    var activeCue = instanceList[i];
                    if (activeCue.CueTag == tag) toRemove.Add(activeCue);
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var itemToRemove = toRemove[i];
                    await persistentCue.OnRemovedAsync(itemToRemove.Instance, parameters);
                    poolManager.Release(itemToRemove.Instance);
                    RemoveTrackedCueInstance(instanceList, itemToRemove);
                }
            }
        }

        private static void RemoveTrackedCueInstance(List<ActiveCueInstance> instanceList, ActiveCueInstance itemToRemove)
        {
            for (int i = instanceList.Count - 1; i >= 0; i--)
            {
                if (!instanceList[i].Equals(itemToRemove))
                {
                    continue;
                }

                int lastIndex = instanceList.Count - 1;
                if (i != lastIndex)
                {
                    instanceList[i] = instanceList[lastIndex];
                }
                instanceList.RemoveAt(lastIndex);
                return;
            }
        }

        private async UniTask ReleaseTrackedCueInstanceAsync(ActiveCueInstance activeCue, GameplayCueParameters parameters)
        {
            if (activeCue.Instance == null) return;

            var cueSO = await GetCueSOAsync(activeCue.CueTag, _shutdownCts.Token);
            if (!_shutdownCts.IsCancellationRequested && cueSO is IPersistentGameplayCue persistentCue)
            {
                await persistentCue.OnRemovedAsync(activeCue.Instance, parameters);
            }

            poolManager.Release(activeCue.Instance);
        }

        private async UniTask<GameplayCueSO> GetCueSOAsync(GameplayTag cueTag, CancellationToken ct = default)
        {
            if (!staticCueAddressRegistry.TryGetValue(cueTag, out var address)) return null;

            if (loadedStaticCues.TryGetValue(address, out var cueHandle)) return cueHandle.Asset;

            var loadedHandle = await resourceLocator.LoadAssetAsync<GameplayCueSO>(address, bucket: "GameplayCue", cacheTag: "GameplayCue", cacheOwner: cueTag.ToString(), cancellationToken: ct);
            if (loadedHandle != null && loadedHandle.Asset != null)
            {
                loadedStaticCues[address] = loadedHandle;
                return loadedHandle.Asset;
            }
            return null;
        }

        /// <summary>
        /// Shuts down all systems, clearing pools and releasing assets. Call on application quit.
        /// </summary>
        public void Shutdown()
        {
            //  Cancel all in-flight async cue loads before clearing state.
            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            _shutdownCts = new CancellationTokenSource(); // Reset for potential re-use after re-init.

            poolManager?.Shutdown();
            staticCueAddressRegistry.Clear();
            foreach (var kvp in loadedStaticCues)
            {
                kvp.Value?.Dispose();
            }
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
            if (asc is AbilitySystemComponent)
            {
                var runtimeEventType = (EGameplayCueEvent)(int)eventType;
                HandleCue(cueTag, runtimeEventType, new GameplayCueParameters(parameters)).Forget();
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
