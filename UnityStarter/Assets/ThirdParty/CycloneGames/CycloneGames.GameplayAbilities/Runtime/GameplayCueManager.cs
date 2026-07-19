using System;
using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;
using CycloneGames.GameplayTags.Core;
using UnityEngine;
using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Main-thread-owned Gameplay Cue service with bounded GameObject pooling and cancellation-aware shutdown.
    /// </summary>
    public sealed class GameplayCueManager : IGameplayCueManager, IDisposable
    {
        private const int MaxAssetAddressLength = 1024;
        private const int MaxRetainedScratchListsPerType = 4;
        private readonly GameObjectPoolManager.PoolConfig poolConfig;
        private readonly CancellationTokenSource shutdownCancellation;
        private readonly CancellationToken shutdownToken;
        private IResourceLocator resourceLocator;
        private IGameObjectPoolManager poolManager;
        private bool isInitialized;
        private bool disposed;
        private int pendingActivationCount;
        private int activeCueReferenceCount;

        // Registry for asset-based cues, discovered at startup. Key is the tag (from the address).
        private readonly Dictionary<GameplayTag, string> staticCueAddressRegistry = new Dictionary<GameplayTag, string>();
        // Cache for loaded cue assets to prevent redundant loading.
        private readonly Dictionary<string, IResourceHandle<GameplayCueSO>> loadedStaticCues = new Dictionary<string, IResourceHandle<GameplayCueSO>>();
        private readonly Dictionary<string, UniTaskCompletionSource<IResourceHandle<GameplayCueSO>>> pendingStaticCueLoads = new Dictionary<string, UniTaskCompletionSource<IResourceHandle<GameplayCueSO>>>(StringComparer.Ordinal);

        // Registry for dynamically added cue handlers at runtime.
        private readonly Dictionary<GameplayTag, List<IGameplayCueHandler>> runtimeCueHandlers = new Dictionary<GameplayTag, List<IGameplayCueHandler>>();

        private readonly struct CueOccurrence
        {
            public readonly GASPredictionKey PredictionKey;
            public readonly bool PredictionCommitted;

            public CueOccurrence(GASPredictionKey predictionKey, bool predictionCommitted = false)
            {
                PredictionKey = predictionKey;
                PredictionCommitted = predictionCommitted;
            }

            public CueOccurrence CommitPrediction() => new CueOccurrence(PredictionKey, true);
        }

        private sealed class CueReferenceState
        {
            public readonly List<CueOccurrence> Occurrences = new List<CueOccurrence>(1);
        }

        private struct ActiveCueInstance
        {
            public GameplayTag CueTag;
            public GameObjectLease Lease;
            public IPersistentGameplayCue Handler;
        }

        private sealed class PendingCueActivation : IDisposable
        {
            private readonly CancellationTokenSource cancellation;
            private bool disposed;

            public readonly AbilitySystemComponent Target;
            public readonly GameplayTag CueTag;
            public readonly CueReferenceState ReferenceState;
            public bool IsCanceled { get; private set; }
            public CancellationToken Token => cancellation.Token;

            public PendingCueActivation(
                AbilitySystemComponent target,
                GameplayTag cueTag,
                CueReferenceState referenceState,
                CancellationToken shutdownToken)
            {
                Target = target;
                CueTag = cueTag;
                ReferenceState = referenceState;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            }

            public void Cancel()
            {
                if (IsCanceled) return;
                IsCanceled = true;
                try
                {
                    cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    GASLog.Error($"Persistent Gameplay Cue activation cancellation callback failed: {exception.Message}");
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                cancellation.Dispose();
            }
        }

        private sealed class CueReleaseOwner : IDisposable
        {
            private readonly CancellationTokenSource cancellation;
            private bool disposed;

            public readonly AbilitySystemComponent Target;
            public readonly GameObjectLease Lease;
            public bool IsCanceled { get; private set; }
            public CancellationToken Token => cancellation.Token;

            public CueReleaseOwner(
                AbilitySystemComponent target,
                GameObjectLease lease,
                CancellationToken parentToken)
            {
                Target = target;
                Lease = lease;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            }

            public void Cancel()
            {
                if (IsCanceled) return;
                IsCanceled = true;
                try
                {
                    cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    GASLog.Error($"Persistent Gameplay Cue removal cancellation callback failed: {exception.Message}");
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                cancellation.Dispose();
            }
        }

        private readonly Dictionary<AbilitySystemComponent, List<ActiveCueInstance>> activeInstances = new Dictionary<AbilitySystemComponent, List<ActiveCueInstance>>();
        private readonly Dictionary<AbilitySystemComponent, Dictionary<GameplayTag, CueReferenceState>> cueReferences = new Dictionary<AbilitySystemComponent, Dictionary<GameplayTag, CueReferenceState>>();
        private readonly Dictionary<AbilitySystemComponent, List<PendingCueActivation>> pendingActivations = new Dictionary<AbilitySystemComponent, List<PendingCueActivation>>();
        private readonly Dictionary<AbilitySystemComponent, List<CueReleaseOwner>> inFlightReleases = new Dictionary<AbilitySystemComponent, List<CueReleaseOwner>>();
        private readonly GameplayCueScratchListPool<IGameplayCueHandler> runtimeHandlerScratchPool;
        private readonly GameplayCueScratchListPool<GameplayTag> tagRemovalScratchPool;
        private readonly GameplayCueScratchListPool<ActiveCueInstance> activeInstanceScratchPool;
        private readonly GameplayCueScratchListPool<PendingCueActivation> pendingActivationScratchPool;
        private readonly GameplayCueScratchListPool<CueReleaseOwner> releaseOwnerScratchPool;

        public GameplayCueManager(GameObjectPoolManager.PoolConfig poolConfig)
        {
            AssertCueThread();
            poolConfig.Validate(nameof(poolConfig));
            this.poolConfig = poolConfig;
            int retainedScratchListCount = Math.Min(MaxRetainedScratchListsPerType, poolConfig.MaxActiveLeases);
            runtimeHandlerScratchPool = new GameplayCueScratchListPool<IGameplayCueHandler>(
                "RuntimeHandlers",
                poolConfig.MaxActiveLeases,
                retainedScratchListCount,
                poolConfig.MaxActiveLeasesPerPool);
            tagRemovalScratchPool = new GameplayCueScratchListPool<GameplayTag>(
                "TagRemoval",
                poolConfig.MaxActiveLeases,
                retainedScratchListCount,
                poolConfig.MaxActiveLeases);
            activeInstanceScratchPool = new GameplayCueScratchListPool<ActiveCueInstance>(
                "ActiveInstances",
                poolConfig.MaxActiveLeases,
                retainedScratchListCount,
                poolConfig.MaxActiveLeases);
            pendingActivationScratchPool = new GameplayCueScratchListPool<PendingCueActivation>(
                "PendingActivations",
                poolConfig.MaxActiveLeases,
                retainedScratchListCount,
                poolConfig.MaxActiveLeases);
            releaseOwnerScratchPool = new GameplayCueScratchListPool<CueReleaseOwner>(
                "ReleaseOwners",
                poolConfig.MaxActiveLeases,
                retainedScratchListCount,
                poolConfig.MaxActiveLeases);
            shutdownCancellation = new CancellationTokenSource();
            shutdownToken = shutdownCancellation.Token;
        }

        public void Initialize(IResourceLocator locator)
        {
            AssertCueThread();
            ThrowIfDisposed();
            if (isInitialized) return;

            if (locator == null)
            {
                throw new ArgumentNullException(nameof(locator));
            }

            resourceLocator = locator;
            poolManager = new GameObjectPoolManager(resourceLocator, poolConfig);

            isInitialized = true;
            GASLog.Info("GameplayCueManager initialized.");
        }

        /// <summary>
        /// Registers a static, asset-based GameplayCue.
        /// </summary>
        public void RegisterStaticCue(GameplayTag cueTag, string assetAddress)
        {
            AssertCueThread();
            ThrowIfDisposed();
            if (cueTag.IsNone || !cueTag.IsValid || string.IsNullOrWhiteSpace(assetAddress)) return;
            if (assetAddress.Length > MaxAssetAddressLength)
            {
                throw new ArgumentOutOfRangeException(nameof(assetAddress), assetAddress.Length, $"Gameplay Cue asset addresses cannot exceed {MaxAssetAddressLength} characters.");
            }
            if (!staticCueAddressRegistry.ContainsKey(cueTag) && staticCueAddressRegistry.Count >= poolConfig.MaxAssetPools)
            {
                throw new InvalidOperationException($"Gameplay Cue static registration capacity ({poolConfig.MaxAssetPools}) is exhausted.");
            }
            staticCueAddressRegistry.TryGetValue(cueTag, out string previousAddress);
            staticCueAddressRegistry[cueTag] = assetAddress;
            if (!string.IsNullOrEmpty(previousAddress) &&
                !string.Equals(previousAddress, assetAddress, StringComparison.Ordinal) &&
                !IsStaticAddressRegistered(previousAddress) &&
                loadedStaticCues.TryGetValue(previousAddress, out IResourceHandle<GameplayCueSO> previousHandle))
            {
                loadedStaticCues.Remove(previousAddress);
                try { previousHandle?.Dispose(); }
                catch (Exception exception)
                {
                    GASLog.Error($"Gameplay Cue asset handle cleanup failed for '{previousAddress}': {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Registers a handler for a dynamic GameplayCue at runtime.
        /// </summary>
        public void RegisterRuntimeHandler(GameplayTag cueTag, IGameplayCueHandler handler)
        {
            AssertCueThread();
            ThrowIfDisposed();
            if (cueTag.IsNone || !cueTag.IsValid || handler == null) return;
            if (!runtimeCueHandlers.TryGetValue(cueTag, out var handlers))
            {
                if (runtimeCueHandlers.Count >= poolConfig.MaxAssetPools)
                {
                    throw new InvalidOperationException($"Gameplay Cue runtime-handler tag capacity ({poolConfig.MaxAssetPools}) is exhausted.");
                }
                handlers = new List<IGameplayCueHandler>();
                runtimeCueHandlers[cueTag] = handlers;
            }
            for (int i = 0; i < handlers.Count; i++)
            {
                if (ReferenceEquals(handlers[i], handler)) return;
            }
            if (handlers.Count >= poolConfig.MaxActiveLeasesPerPool)
            {
                throw new InvalidOperationException($"Gameplay Cue handler capacity for '{cueTag}' ({poolConfig.MaxActiveLeasesPerPool}) is exhausted.");
            }
            handlers.Add(handler);
        }

        /// <summary>
        /// Unregisters a dynamic GameplayCue handler.
        /// </summary>
        public void UnregisterRuntimeHandler(GameplayTag cueTag, IGameplayCueHandler handler)
        {
            AssertCueThread();
            ThrowIfDisposed();
            if (cueTag.IsNone || handler == null) return;
            if (runtimeCueHandlers.TryGetValue(cueTag, out var handlers))
            {
                for (int i = handlers.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(handlers[i], handler)) continue;
                    int lastIndex = handlers.Count - 1;
                    handlers[i] = handlers[lastIndex];
                    handlers.RemoveAt(lastIndex);
                    break;
                }
                if (handlers.Count == 0)
                {
                    runtimeCueHandlers.Remove(cueTag);
                }
            }
        }

        /// <summary>
        /// The main entry point to trigger a GameplayCue event.
        /// </summary>
        public void HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec)
            => HandleCueAsync(cueTag, eventType, new GameplayCueParameters(spec)).Forget();

        /// <summary>
        /// Snapshot-based GameplayCue dispatch.
        /// The cue parameters are immutable so async loading cannot observe pooled runtime state.
        /// </summary>
        public void HandleCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueParameters parameters)
            => HandleCueAsync(cueTag, eventType, parameters).Forget();

        /// <summary>
        /// Awaitable cue dispatch used by tests, tools, and composition code that requires completion ordering.
        /// The Core-facing interface remains fire-and-forget and logs failures at this boundary.
        /// </summary>
        public async UniTask HandleCueAsync(
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GameplayCueParameters parameters)
        {
            AssertCueThread();
            if (disposed || !isInitialized || cueTag.IsNone) return;

            if (!PrepareCueEvent(parameters.Target, cueTag, eventType, parameters.PredictionKey, out CueReferenceState referenceState))
            {
                return;
            }

            PendingCueActivation pendingActivation = null;
            bool canceledPendingDuringRemoval = false;
            if ((eventType == EGameplayCueEvent.OnActive || eventType == EGameplayCueEvent.WhileActive) &&
                referenceState != null &&
                staticCueAddressRegistry.ContainsKey(cueTag))
            {
                pendingActivation = BeginPendingActivation(parameters.Target, cueTag, referenceState);
            }
            else if (eventType == EGameplayCueEvent.Removed && parameters.Target != null)
            {
                canceledPendingDuringRemoval = CancelPendingActivations(parameters.Target, cueTag);
            }

            try
            {
                bool hasTrackedInstance = parameters.Target != null && HasTrackedInstance(parameters.Target, cueTag);
                if (eventType == EGameplayCueEvent.Removed && hasTrackedInstance)
                {
                    try
                    {
                        await RemoveInstancesFromTrackerAsync(
                            parameters.Target,
                            cueTag,
                            parameters,
                            shutdownToken);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"Persistent Gameplay Cue '{cueTag}' removal failed: {exception.Message}");
                    }
                }

                bool shouldLoadStaticCue = staticCueAddressRegistry.ContainsKey(cueTag) &&
                                           !hasTrackedInstance &&
                                           !(eventType == EGameplayCueEvent.Removed && canceledPendingDuringRemoval && !hasTrackedInstance);
                if (shouldLoadStaticCue)
                {
                    CancellationToken dispatchToken = pendingActivation?.Token ?? shutdownToken;
                    GameplayCueSO cueSO = await GetCueSOAsync(cueTag, dispatchToken);
                    await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, dispatchToken);
                    if (CannotContinue(parameters, cueTag, referenceState, eventType)) return;

                    if (cueSO != null)
                    {
                        try
                        {
                            if (!await DispatchToCueSO(
                                    cueSO,
                                    cueTag,
                                    eventType,
                                    parameters,
                                    referenceState,
                                    pendingActivation,
                                    dispatchToken))
                            {
                                return;
                            }
                        }
                        catch (OperationCanceledException) when (dispatchToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            GASLog.Error($"Static Gameplay Cue '{cueTag}' handler failed: {exception.Message}");
                        }
                    }
                }

                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, shutdownToken);
                if (CannotContinue(parameters, cueTag, referenceState, eventType)) return;

                DispatchRuntimeHandlers(cueTag, eventType, parameters);
                if (eventType == EGameplayCueEvent.OnActive)
                {
                    DispatchRuntimeHandlers(cueTag, EGameplayCueEvent.WhileActive, parameters);
                }
            }
            catch (OperationCanceledException) when (
                shutdownToken.IsCancellationRequested ||
                (pendingActivation != null && pendingActivation.IsCanceled))
            {
                // Removal, prediction rollback, or shutdown owns cancellation of in-flight cue work.
            }
            catch (Exception exception)
            {
                GASLog.Error($"Gameplay Cue '{cueTag}' dispatch failed: {exception.Message}");
            }
            finally
            {
                if (pendingActivation != null)
                {
                    await UniTask.SwitchToMainThread();
                    EndPendingActivation(pendingActivation);
                }
            }
        }

        private async UniTask<bool> DispatchToCueSO(
            GameplayCueSO cueSO,
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GameplayCueParameters parameters,
            CueReferenceState referenceState,
            PendingCueActivation pendingActivation,
            CancellationToken cancellationToken)
        {
            switch (eventType)
            {
                case EGameplayCueEvent.Executed:
                    await cueSO.OnExecutedAsync(parameters, poolManager, cancellationToken);
                    break;
                case EGameplayCueEvent.OnActive:
                case EGameplayCueEvent.WhileActive:
                    if (cueSO is IPersistentGameplayCue persistentCue)
                    {
                        if (parameters.Target == null || pendingActivation == null) return false;

                        GameObjectLease lease = default;
                        bool transferredToTracker = false;
                        try
                        {
                            lease = await persistentCue.CreateInstanceAsync(parameters, poolManager, pendingActivation.Token);
                            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, pendingActivation.Token);
                            if (pendingActivation.IsCanceled ||
                                CannotContinue(parameters, cueTag, referenceState, eventType) ||
                                !poolManager.IsLeaseOutstanding(lease))
                            {
                                return false;
                            }

                            if (eventType == EGameplayCueEvent.OnActive)
                            {
                                await persistentCue.OnActiveAsync(lease.Instance, parameters, pendingActivation.Token);
                                await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, pendingActivation.Token);
                            }

                            await persistentCue.OnWhileActiveAsync(lease.Instance, parameters, pendingActivation.Token);
                            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, pendingActivation.Token);
                            if (pendingActivation.IsCanceled || CannotContinue(parameters, cueTag, referenceState, eventType))
                            {
                                return false;
                            }

                            transferredToTracker = TryAddInstanceToTracker(
                                parameters.Target,
                                cueTag,
                                lease,
                                persistentCue,
                                referenceState);
                            return transferredToTracker;
                        }
                        finally
                        {
                            await UniTask.SwitchToMainThread();
                            if (!transferredToTracker)
                            {
                                ReleaseLeaseIfOwned(lease);
                            }
                        }
                    }
                    else
                    {
                        if (eventType == EGameplayCueEvent.OnActive)
                        {
                            await cueSO.OnActiveAsync(parameters, poolManager, cancellationToken);
                        }
                        await cueSO.OnWhileActiveAsync(parameters, poolManager, cancellationToken);
                    }
                    break;
                case EGameplayCueEvent.Removed:
                    if (parameters.Target != null && HasTrackedInstance(parameters.Target, cueTag))
                    {
                        await RemoveInstancesFromTrackerAsync(
                            parameters.Target,
                            cueTag,
                            parameters,
                            cancellationToken);
                    }
                    else
                    {
                        await cueSO.OnRemovedAsync(parameters, poolManager, cancellationToken);
                    }
                    break;
            }

            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, cancellationToken);
            return true;
        }

        private bool PrepareCueEvent(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GASPredictionKey predictionKey,
            out CueReferenceState referenceState)
        {
            referenceState = null;
            switch (eventType)
            {
                case EGameplayCueEvent.OnActive:
                    if (target == null || target.IsDisposed) return false;
                    return TryAcquireCueReference(target, cueTag, predictionKey, out referenceState);

                case EGameplayCueEvent.WhileActive:
                    if (target == null || target.IsDisposed) return false;
                    if (TryGetCueReference(target, cueTag, out referenceState)) return false;
                    return TryAcquireCueReference(target, cueTag, predictionKey, out referenceState);

                case EGameplayCueEvent.Removed:
                    if (target == null) return false;
                    return TryReleaseCueReference(target, cueTag, predictionKey, out referenceState);

                default:
                    return true;
            }
        }

        private bool TryAcquireCueReference(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            GASPredictionKey predictionKey,
            out CueReferenceState referenceState)
        {
            if (activeCueReferenceCount >= poolConfig.MaxActiveLeases)
            {
                throw new InvalidOperationException($"Gameplay Cue reference capacity ({poolConfig.MaxActiveLeases}) is exhausted.");
            }

            if (!cueReferences.TryGetValue(target, out Dictionary<GameplayTag, CueReferenceState> targetReferences))
            {
                targetReferences = new Dictionary<GameplayTag, CueReferenceState>();
                cueReferences.Add(target, targetReferences);
            }

            if (!targetReferences.TryGetValue(cueTag, out referenceState))
            {
                referenceState = new CueReferenceState();
                targetReferences.Add(cueTag, referenceState);
            }

            if (referenceState.Occurrences.Count >= poolConfig.MaxActiveLeasesPerPool)
            {
                throw new InvalidOperationException($"Gameplay Cue reference capacity for '{cueTag}' ({poolConfig.MaxActiveLeasesPerPool}) is exhausted.");
            }

            bool isFirstReference = referenceState.Occurrences.Count == 0;
            referenceState.Occurrences.Add(new CueOccurrence(predictionKey));
            activeCueReferenceCount++;
            return isFirstReference;
        }

        private bool TryReleaseCueReference(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            GASPredictionKey predictionKey,
            out CueReferenceState referenceState)
        {
            if (!TryGetCueReference(target, cueTag, out referenceState)) return false;

            int occurrenceIndex = -1;
            for (int i = 0; i < referenceState.Occurrences.Count; i++)
            {
                GASPredictionKey candidate = referenceState.Occurrences[i].PredictionKey;
                if ((predictionKey.IsValid && candidate.Equals(predictionKey)) ||
                    (!predictionKey.IsValid && !candidate.IsValid))
                {
                    occurrenceIndex = i;
                    break;
                }
            }

            if (occurrenceIndex < 0)
            {
                GASLog.Warning($"Gameplay Cue '{cueTag}' removal did not match an active occurrence.");
                return false;
            }

            int lastIndex = referenceState.Occurrences.Count - 1;
            referenceState.Occurrences[occurrenceIndex] = referenceState.Occurrences[lastIndex];
            referenceState.Occurrences.RemoveAt(lastIndex);
            activeCueReferenceCount--;
            if (referenceState.Occurrences.Count > 0) return false;

            Dictionary<GameplayTag, CueReferenceState> targetReferences = cueReferences[target];
            targetReferences.Remove(cueTag);
            if (targetReferences.Count == 0)
            {
                cueReferences.Remove(target);
            }
            return true;
        }

        private bool TryGetCueReference(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            out CueReferenceState referenceState)
        {
            referenceState = null;
            return target != null &&
                   cueReferences.TryGetValue(target, out Dictionary<GameplayTag, CueReferenceState> targetReferences) &&
                   targetReferences.TryGetValue(cueTag, out referenceState);
        }

        private bool IsCueReferenceCurrent(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            CueReferenceState referenceState)
        {
            return referenceState != null &&
                   TryGetCueReference(target, cueTag, out CueReferenceState current) &&
                   ReferenceEquals(current, referenceState) &&
                   current.Occurrences.Count > 0;
        }

        private void ClearCueReferences(AbilitySystemComponent target)
        {
            if (!cueReferences.TryGetValue(target, out Dictionary<GameplayTag, CueReferenceState> targetReferences))
            {
                return;
            }

            foreach (CueReferenceState state in targetReferences.Values)
            {
                activeCueReferenceCount -= state.Occurrences.Count;
                state.Occurrences.Clear();
            }
            cueReferences.Remove(target);
            if (activeCueReferenceCount < 0) activeCueReferenceCount = 0;
        }

        private bool HasTrackedInstance(AbilitySystemComponent target, GameplayTag cueTag)
        {
            if (!activeInstances.TryGetValue(target, out List<ActiveCueInstance> instances)) return false;
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i].CueTag == cueTag) return true;
            }
            return false;
        }

        private void DispatchRuntimeHandlers(
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GameplayCueParameters parameters)
        {
            if (!runtimeCueHandlers.TryGetValue(cueTag, out List<IGameplayCueHandler> handlers)) return;

            GameplayCueScratchListLease<IGameplayCueHandler> scratchLease = runtimeHandlerScratchPool.Rent();
            List<IGameplayCueHandler> safeHandlers = scratchLease.Value;
            try
            {
                safeHandlers.AddRange(handlers);
                for (int i = 0; i < safeHandlers.Count; i++)
                {
                    try { safeHandlers[i].HandleCue(cueTag, eventType, parameters); }
                    catch (Exception exception)
                    {
                        GASLog.Error($"Runtime Gameplay Cue handler failed after cue dispatch: {exception.Message}");
                    }
                }
            }
            finally
            {
                runtimeHandlerScratchPool.Return(scratchLease);
            }
        }

        private bool TryAddInstanceToTracker(
            AbilitySystemComponent target,
            GameplayTag tag,
            GameObjectLease lease,
            IPersistentGameplayCue handler,
            CueReferenceState referenceState)
        {
            AssertCueThread();
            if (target == null ||
                target.IsDisposed ||
                handler == null ||
                !IsCueReferenceCurrent(target, tag, referenceState) ||
                !poolManager.IsLeaseOutstanding(lease))
            {
                return false;
            }
            if (!activeInstances.TryGetValue(target, out var instanceList))
            {
                instanceList = new List<ActiveCueInstance>();
                activeInstances[target] = instanceList;
            }

            for (int i = 0; i < instanceList.Count; i++)
            {
                if (instanceList[i].CueTag == tag) return false;
            }

            instanceList.Add(new ActiveCueInstance { CueTag = tag, Lease = lease, Handler = handler });
            return true;
        }

        public void CommitPredictedCues(AbilitySystemComponent target, GASPredictionKey predictionKey)
        {
            AssertCueThread();
            if (disposed) return;
            if (target == null || !predictionKey.IsValid) return;

            if (!cueReferences.TryGetValue(target, out Dictionary<GameplayTag, CueReferenceState> targetReferences))
            {
                return;
            }

            foreach (CueReferenceState state in targetReferences.Values)
            {
                for (int i = 0; i < state.Occurrences.Count; i++)
                {
                    CueOccurrence occurrence = state.Occurrences[i];
                    if (occurrence.PredictionKey.Equals(predictionKey))
                    {
                        state.Occurrences[i] = occurrence.CommitPrediction();
                    }
                }
            }
        }

        public void RollbackPredictedCues(AbilitySystemComponent target, GASPredictionKey predictionKey)
            => RollbackPredictedCuesAsync(target, predictionKey).Forget();

        public async UniTask RollbackPredictedCuesAsync(
            AbilitySystemComponent target,
            GASPredictionKey predictionKey)
        {
            AssertCueThread();
            if (disposed) return;
            if (target == null || !predictionKey.IsValid) return;

            try
            {
                GameplayCueScratchListLease<GameplayTag> scratchLease = tagRemovalScratchPool.Rent();
                List<GameplayTag> tagsToRemove = scratchLease.Value;
                try
                {
                    if (cueReferences.TryGetValue(target, out Dictionary<GameplayTag, CueReferenceState> targetReferences))
                    {
                        foreach (KeyValuePair<GameplayTag, CueReferenceState> pair in targetReferences)
                        {
                            List<CueOccurrence> occurrences = pair.Value.Occurrences;
                            for (int occurrenceIndex = occurrences.Count - 1; occurrenceIndex >= 0; occurrenceIndex--)
                            {
                                CueOccurrence occurrence = occurrences[occurrenceIndex];
                                if (!occurrence.PredictionCommitted && occurrence.PredictionKey.Equals(predictionKey))
                                {
                                    occurrences.RemoveAt(occurrenceIndex);
                                    activeCueReferenceCount--;
                                }
                            }

                            if (occurrences.Count == 0)
                            {
                                tagsToRemove.Add(pair.Key);
                            }
                        }

                        for (int i = 0; i < tagsToRemove.Count; i++)
                        {
                            targetReferences.Remove(tagsToRemove[i]);
                        }
                        if (targetReferences.Count == 0)
                        {
                            cueReferences.Remove(target);
                        }
                    }

                    Exception firstException = null;
                    for (int i = 0; i < tagsToRemove.Count; i++)
                    {
                        GameplayTag tagToRemove = tagsToRemove[i];
                        CancelPendingActivations(target, tagToRemove);
                        var parameters = new GameplayCueParameters(new GameplayCueEventParams(
                            null,
                            target,
                            null,
                            null,
                            target.AvatarGameObject,
                            0,
                            0L,
                            predictionKey));

                        try
                        {
                            await RemoveInstancesFromTrackerAsync(
                                target,
                                tagToRemove,
                                parameters,
                                shutdownToken);
                            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, shutdownToken);
                            if (disposed) return;
                        }
                        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            if (firstException == null)
                            {
                                firstException = exception;
                            }
                        }
                    }

                    if (firstException != null)
                    {
                        throw firstException;
                    }
                }
                finally
                {
                    tagRemovalScratchPool.Return(scratchLease);
                }
            }

            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                // Shutdown destroys all active pool leases.
            }
        }

        private async UniTask RemoveInstancesFromTrackerAsync(
            AbilitySystemComponent target,
            GameplayTag tag,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken)
        {
            if (target == null) return;
            CancelPendingActivations(target, tag);
            if (!activeInstances.TryGetValue(target, out var instanceList)) return;

            // Reuse the temporary removal snapshot after its capacity has been established.
            GameplayCueScratchListLease<ActiveCueInstance> scratchLease = activeInstanceScratchPool.Rent();
            List<ActiveCueInstance> toRemove = scratchLease.Value;
            try
            {
                for (int i = instanceList.Count - 1; i >= 0; i--)
                {
                    var activeCue = instanceList[i];
                    if (activeCue.CueTag == tag)
                    {
                        toRemove.Add(activeCue);
                        RemoveAtSwapBack(instanceList, i);
                    }
                }


                if (instanceList.Count == 0)
                {
                    activeInstances.Remove(target);
                }

                Exception firstException = null;
                for (int i = 0; i < toRemove.Count; i++)
                {
                    var itemToRemove = toRemove[i];
                    try
                    {
                        await ReleaseTrackedCueInstanceAsync(
                            itemToRemove,
                            itemToRemove.Handler,
                            parameters,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        if (firstException == null)
                        {
                            firstException = exception;
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (firstException != null)
                {
                    throw firstException;
                }
            }
            finally
            {
                activeInstanceScratchPool.Return(scratchLease);
            }
        }

        private static void RemoveAtSwapBack(List<ActiveCueInstance> instanceList, int index)
        {
            int lastIndex = instanceList.Count - 1;
            if (index != lastIndex)
            {
                instanceList[index] = instanceList[lastIndex];
            }

            instanceList.RemoveAt(lastIndex);
        }

        private PendingCueActivation BeginPendingActivation(
            AbilitySystemComponent target,
            GameplayTag cueTag,
            CueReferenceState referenceState)
        {
            AssertCueThread();
            if (pendingActivationCount >= poolConfig.MaxActiveLeases)
            {
                throw new InvalidOperationException($"Persistent Gameplay Cue pending activation capacity ({poolConfig.MaxActiveLeases}) is exhausted.");
            }
            var pending = new PendingCueActivation(target, cueTag, referenceState, shutdownToken);
            pendingActivationCount++;
            try
            {
                if (!pendingActivations.TryGetValue(target, out List<PendingCueActivation> activations))
                {
                    activations = new List<PendingCueActivation>();
                    pendingActivations.Add(target, activations);
                }

                activations.Add(pending);
                return pending;
            }
            catch
            {
                pendingActivationCount--;
                pending.Dispose();
                throw;
            }
        }

        private void EndPendingActivation(PendingCueActivation pending)
        {
            AssertCueThread();
            AbilitySystemComponent target = pending.Target;
            if (target != null && pendingActivations.TryGetValue(target, out List<PendingCueActivation> activations))
            {
                activations.Remove(pending);
                if (activations.Count == 0)
                {
                    pendingActivations.Remove(target);
                }
            }

            if (pendingActivationCount > 0) pendingActivationCount--;
            pending.Dispose();
        }

        private bool CancelPendingActivations(AbilitySystemComponent target, GameplayTag cueTag)
        {
            if (!pendingActivations.TryGetValue(target, out List<PendingCueActivation> activations)) return false;
            bool canceledAny = false;
            GameplayCueScratchListLease<PendingCueActivation> scratchLease = pendingActivationScratchPool.Rent();
            List<PendingCueActivation> snapshot = scratchLease.Value;
            try
            {
                snapshot.AddRange(activations);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    PendingCueActivation pending = snapshot[i];
                    if (pending.CueTag == cueTag)
                    {
                        pending.Cancel();
                        canceledAny = true;
                    }
                }
            }
            finally
            {
                pendingActivationScratchPool.Return(scratchLease);
            }
            return canceledAny;
        }

        private void CancelAllPendingActivations(AbilitySystemComponent target)
        {
            if (!pendingActivations.TryGetValue(target, out List<PendingCueActivation> activations)) return;
            GameplayCueScratchListLease<PendingCueActivation> scratchLease = pendingActivationScratchPool.Rent();
            List<PendingCueActivation> snapshot = scratchLease.Value;
            try
            {
                snapshot.AddRange(activations);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    snapshot[i].Cancel();
                }
            }
            finally
            {
                pendingActivationScratchPool.Return(scratchLease);
            }
        }

        private void CancelEveryPendingActivation()
        {
            GameplayCueScratchListLease<PendingCueActivation> scratchLease = pendingActivationScratchPool.Rent();
            List<PendingCueActivation> snapshot = scratchLease.Value;
            try
            {
                foreach (List<PendingCueActivation> activations in pendingActivations.Values)
                {
                    snapshot.AddRange(activations);
                }

                for (int i = 0; i < snapshot.Count; i++)
                {
                    snapshot[i].Cancel();
                }
            }
            finally
            {
                pendingActivationScratchPool.Return(scratchLease);
            }
        }

        private CueReleaseOwner BeginRelease(
            ActiveCueInstance activeCue,
            AbilitySystemComponent target,
            CancellationToken parentToken)
        {
            AssertCueThread();
            var owner = new CueReleaseOwner(target, activeCue.Lease, parentToken);
            if (target != null)
            {
                if (!inFlightReleases.TryGetValue(target, out List<CueReleaseOwner> releases))
                {
                    releases = new List<CueReleaseOwner>();
                    inFlightReleases.Add(target, releases);
                }

                releases.Add(owner);
                if (target.IsDisposed)
                {
                    owner.Cancel();
                }
            }

            return owner;
        }

        private void EndRelease(CueReleaseOwner owner)
        {
            AssertCueThread();
            AbilitySystemComponent target = owner.Target;
            if (target != null && inFlightReleases.TryGetValue(target, out List<CueReleaseOwner> releases))
            {
                releases.Remove(owner);
                if (releases.Count == 0)
                {
                    inFlightReleases.Remove(target);
                }
            }

            try
            {
                ReleaseLeaseIfOwned(owner.Lease);
            }
            finally
            {
                owner.Dispose();
            }
        }

        private void CancelInFlightReleases(AbilitySystemComponent target)
        {
            if (!inFlightReleases.TryGetValue(target, out List<CueReleaseOwner> releases)) return;
            GameplayCueScratchListLease<CueReleaseOwner> scratchLease = releaseOwnerScratchPool.Rent();
            List<CueReleaseOwner> snapshot = scratchLease.Value;
            try
            {
                snapshot.AddRange(releases);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    snapshot[i].Cancel();
                }
            }
            finally
            {
                releaseOwnerScratchPool.Return(scratchLease);
            }
        }

        private void CancelEveryInFlightRelease()
        {
            GameplayCueScratchListLease<CueReleaseOwner> scratchLease = releaseOwnerScratchPool.Rent();
            List<CueReleaseOwner> snapshot = scratchLease.Value;
            try
            {
                foreach (List<CueReleaseOwner> releases in inFlightReleases.Values)
                {
                    snapshot.AddRange(releases);
                }

                for (int i = 0; i < snapshot.Count; i++)
                {
                    snapshot[i].Cancel();
                }
            }
            finally
            {
                releaseOwnerScratchPool.Return(scratchLease);
            }
        }

        private void ReleaseLeaseIfOwned(GameObjectLease lease)
        {
            AssertCueThread();
            if (!lease.IsValid || disposed || poolManager == null) return;
            if (!poolManager.IsLeaseOutstanding(lease)) return;
            poolManager.Release(lease);
        }

        private async UniTask ReleaseTrackedCueInstanceAsync(
            ActiveCueInstance activeCue,
            IPersistentGameplayCue persistentCue,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken)
        {
            if (persistentCue == null) return;
            CueReleaseOwner releaseOwner = BeginRelease(activeCue, parameters.Target, cancellationToken);
            try
            {
                if (!releaseOwner.IsCanceled &&
                    poolManager != null &&
                    poolManager.IsLeaseOutstanding(activeCue.Lease) &&
                    activeCue.Lease.Instance != null)
                {
                    await persistentCue.OnRemovedAsync(activeCue.Lease.Instance, parameters, releaseOwner.Token);
                }
            }
            catch (OperationCanceledException) when (releaseOwner.IsCanceled || cancellationToken.IsCancellationRequested)
            {
                // The release owner still returns its lease in the finally block.
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                EndRelease(releaseOwner);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask<GameplayCueSO> GetCueSOAsync(GameplayTag cueTag, CancellationToken ct = default)
        {
            if (!staticCueAddressRegistry.TryGetValue(cueTag, out var address)) return null;

            if (loadedStaticCues.TryGetValue(address, out var cueHandle)) return cueHandle.Asset;

            if (!pendingStaticCueLoads.TryGetValue(
                    address,
                    out UniTaskCompletionSource<IResourceHandle<GameplayCueSO>> completion))
            {
                completion = new UniTaskCompletionSource<IResourceHandle<GameplayCueSO>>();
                pendingStaticCueLoads.Add(address, completion);
                LoadStaticCueHandleAsync(address, completion).Forget();
            }

            IResourceHandle<GameplayCueSO> handle = ct.CanBeCanceled
                ? await completion.Task.AttachExternalCancellation(ct)
                : await completion.Task;

            await UniTask.SwitchToMainThread(PlayerLoopTiming.Update, ct);
            if (disposed || shutdownToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(shutdownToken);
            }

            if (!staticCueAddressRegistry.TryGetValue(cueTag, out string currentAddress) ||
                !string.Equals(currentAddress, address, StringComparison.Ordinal))
            {
                return null;
            }

            return handle?.Asset;
        }

        private async UniTaskVoid LoadStaticCueHandleAsync(
            string address,
            UniTaskCompletionSource<IResourceHandle<GameplayCueSO>> completion)
        {
            IResourceHandle<GameplayCueSO> loadedHandle = null;
            try
            {
                loadedHandle = await resourceLocator.LoadAssetAsync<GameplayCueSO>(
                    address,
                    bucket: "GameplayCue",
                    cacheTag: "GameplayCue",
                    cacheOwner: address,
                    cancellationToken: shutdownToken);

                await UniTask.SwitchToMainThread();
                if (disposed || shutdownToken.IsCancellationRequested)
                {
                    DisposeCueHandleNoThrow(loadedHandle, address);
                    loadedHandle = null;
                    completion.TrySetCanceled(shutdownToken);
                    return;
                }

                if (loadedHandle == null || loadedHandle.Asset == null || !IsStaticAddressRegistered(address))
                {
                    DisposeCueHandleNoThrow(loadedHandle, address);
                    loadedHandle = null;
                    completion.TrySetResult(null);
                    return;
                }

                if (loadedStaticCues.TryGetValue(address, out IResourceHandle<GameplayCueSO> cachedHandle))
                {
                    DisposeCueHandleNoThrow(loadedHandle, address);
                    loadedHandle = null;
                    completion.TrySetResult(cachedHandle);
                    return;
                }

                loadedStaticCues.Add(address, loadedHandle);
                completion.TrySetResult(loadedHandle);
                loadedHandle = null;
            }
            catch (OperationCanceledException cancellation)
            {
                await UniTask.SwitchToMainThread();
                DisposeCueHandleNoThrow(loadedHandle, address);
                loadedHandle = null;
                completion.TrySetCanceled(cancellation.CancellationToken);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                DisposeCueHandleNoThrow(loadedHandle, address);
                loadedHandle = null;
                SetCueLoadExceptionAndMarkObserved(completion, exception);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                pendingStaticCueLoads.Remove(address);
                DisposeCueHandleNoThrow(loadedHandle, address);
            }
        }

        private static void DisposeCueHandleNoThrow(
            IResourceHandle<GameplayCueSO> handle,
            string address)
        {
            if (handle == null) return;
            try { handle.Dispose(); }
            catch (Exception exception)
            {
                GASLog.Error($"Gameplay Cue asset handle cleanup failed for '{address}': {exception.Message}");
            }
        }

        private static void SetCueLoadExceptionAndMarkObserved(
            UniTaskCompletionSource<IResourceHandle<GameplayCueSO>> completion,
            Exception exception)
        {
            if (!completion.TrySetException(exception)) return;
            try { completion.GetResult(0); }
            catch
            {
                // Preserve the faulted shared completion while preventing an abandoned view from reporting it again.
            }
        }

        private bool IsStaticAddressRegistered(string address)
        {
            foreach (string registeredAddress in staticCueAddressRegistry.Values)
            {
                if (string.Equals(registeredAddress, address, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Shuts down all systems, clearing pools and releasing assets. Call on application quit.
        /// </summary>
        public void Shutdown()
        {
            AssertCueThread();
            if (disposed) return;
            disposed = true;

            Exception cleanupFailure = null;
            try { CancelEveryPendingActivation(); }
            catch (Exception exception) { cleanupFailure = exception; }
            try { CancelEveryInFlightRelease(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            try { shutdownCancellation.Cancel(); }
            catch (Exception exception) { cleanupFailure ??= exception; }

            int outstandingScratchLeaseCount =
                runtimeHandlerScratchPool.OutstandingCount +
                tagRemovalScratchPool.OutstandingCount +
                activeInstanceScratchPool.OutstandingCount +
                pendingActivationScratchPool.OutstandingCount +
                releaseOwnerScratchPool.OutstandingCount;
            if (outstandingScratchLeaseCount > 0)
            {
                GASLog.Warning(
                    $"GameplayCueManager shutdown is discarding {outstandingScratchLeaseCount} outstanding scratch-list lease(s) when they return.");
            }
            TryDisposeScratchPool(runtimeHandlerScratchPool, ref cleanupFailure);
            TryDisposeScratchPool(tagRemovalScratchPool, ref cleanupFailure);
            TryDisposeScratchPool(activeInstanceScratchPool, ref cleanupFailure);
            TryDisposeScratchPool(pendingActivationScratchPool, ref cleanupFailure);
            TryDisposeScratchPool(releaseOwnerScratchPool, ref cleanupFailure);

            try { poolManager?.Shutdown(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            poolManager = null;
            resourceLocator = null;
            staticCueAddressRegistry.Clear();
            foreach (var kvp in loadedStaticCues)
            {
                try { kvp.Value?.Dispose(); }
                catch (Exception exception) { cleanupFailure ??= exception; }
            }
            loadedStaticCues.Clear();
            pendingStaticCueLoads.Clear();
            runtimeCueHandlers.Clear();
            activeInstances.Clear();
            cueReferences.Clear();
            activeCueReferenceCount = 0;
            pendingActivationCount = 0;
            pendingActivations.Clear();
            inFlightReleases.Clear();
            isInitialized = false;
            try { shutdownCancellation.Dispose(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (cleanupFailure != null)
            {
                GASLog.Error($"GameplayCueManager shutdown completed with cleanup failures: {cleanupFailure.Message}");
            }
        }

        public void Dispose() => Shutdown();

        #region IGameplayCueManager Interface Implementation

        /// <summary>
        /// Interface method - handles cue via Core interface (uses object types).
        /// </summary>
        void IGameplayCueManager.HandleCue(object asc, GameplayTag cueTag, Core.EGameplayCueEvent eventType, GameplayCueEventParams parameters)
        {
            if (asc is AbilitySystemComponent target)
            {
                var runtimeEventType = (EGameplayCueEvent)(int)eventType;
                var runtimeParameters = new GameplayCueParameters(parameters);
                if (runtimeParameters.Target != null && !ReferenceEquals(runtimeParameters.Target, target))
                {
                    GASLog.Error("Gameplay Cue owner does not match the target in its parameter snapshot.");
                    return;
                }
                HandleCueAsync(cueTag, runtimeEventType, runtimeParameters).Forget();
            }
        }

        /// <summary>
        /// Interface method - removes all cues for a specific ASC.
        /// </summary>
        void IGameplayCueManager.RemoveAllCuesFor(object asc)
        {
            AssertCueThread();
            if (disposed) return;
            if (!(asc is AbilitySystemComponent ascTyped)) return;

            CancelAllPendingActivations(ascTyped);
            CancelInFlightReleases(ascTyped);
            ClearCueReferences(ascTyped);
            if (activeInstances.TryGetValue(ascTyped, out var instances))
            {
                activeInstances.Remove(ascTyped);
                foreach (var instance in instances)
                {
                    try
                    {
                        ReleaseLeaseIfOwned(instance.Lease);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"Gameplay Cue lease cleanup failed: {exception.Message}");
                    }
                }
                instances.Clear();
            }
        }

        void IGameplayCueManager.CommitPredictedCues(object asc, GASPredictionKey predictionKey)
        {
            if (asc is AbilitySystemComponent target)
            {
                CommitPredictedCues(target, predictionKey);
            }
        }

        void IGameplayCueManager.RollbackPredictedCues(object asc, GASPredictionKey predictionKey)
        {
            if (asc is AbilitySystemComponent target)
            {
                RollbackPredictedCues(target, predictionKey);
            }
        }

        #endregion

        private bool CannotContinue(
            GameplayCueParameters parameters,
            GameplayTag cueTag,
            CueReferenceState referenceState,
            EGameplayCueEvent eventType)
        {
            if (disposed ||
                shutdownToken.IsCancellationRequested ||
                (parameters.Target != null && parameters.Target.IsDisposed))
            {
                return true;
            }

            return (eventType == EGameplayCueEvent.OnActive || eventType == EGameplayCueEvent.WhileActive) &&
                   !IsCueReferenceCurrent(parameters.Target, cueTag, referenceState);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GameplayCueManager));
            }
        }

        private static void TryDisposeScratchPool<T>(
            GameplayCueScratchListPool<T> scratchPool,
            ref Exception cleanupFailure)
        {
            try { scratchPool.Dispose(); }
            catch (Exception exception) { cleanupFailure ??= exception; }
        }

        private static void AssertCueThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("GameplayCueManager must be accessed from the Unity main thread.");
            }
        }
    }

    internal readonly struct GameplayCueScratchListLease<T>
    {
        internal readonly GameplayCueScratchListPool<T> Owner;
        internal readonly GameplayCueScratchListPool<T>.Entry Entry;
        internal readonly ulong Generation;

        internal GameplayCueScratchListLease(
            GameplayCueScratchListPool<T> owner,
            GameplayCueScratchListPool<T>.Entry entry,
            ulong generation)
        {
            Owner = owner;
            Entry = entry;
            Generation = generation;
        }

        internal List<T> Value
        {
            get
            {
                if (Owner == null)
                {
                    throw new InvalidOperationException("Gameplay Cue scratch-list lease is not initialized.");
                }

                return Owner.GetValue(this);
            }
        }
    }

    internal sealed class GameplayCueScratchListPool<T> : IDisposable
    {
        internal sealed class Entry
        {
            internal readonly GameplayCueScratchListPool<T> Owner;
            internal readonly List<T> List = new List<T>();
            internal ulong Generation;
            internal bool IsOutstanding;

            internal Entry(GameplayCueScratchListPool<T> owner)
            {
                Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }
        }

        private readonly string m_Name;
        private readonly int m_MaxOutstandingCount;
        private readonly int m_MaxRetainedListCount;
        private readonly int m_MaxRetainedElementCapacity;
        private readonly Stack<Entry> m_Inactive;

        private bool m_IsDisposed;
        private int m_OutstandingCount;
        private int m_PeakOutstandingCount;
        private int m_DiscardedCount;
        private int m_InvalidReturnCount;

        internal GameplayCueScratchListPool(
            string name,
            int maxOutstandingCount,
            int maxRetainedListCount,
            int maxRetainedElementCapacity)
        {
            AssertMainThread();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Scratch-list pool name cannot be empty.", nameof(name));
            }
            if (maxOutstandingCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingCount));
            }
            if (maxRetainedListCount <= 0 || maxRetainedListCount > maxOutstandingCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetainedListCount));
            }
            if (maxRetainedElementCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetainedElementCapacity));
            }

            m_Name = name;
            m_MaxOutstandingCount = maxOutstandingCount;
            m_MaxRetainedListCount = maxRetainedListCount;
            m_MaxRetainedElementCapacity = maxRetainedElementCapacity;
            m_Inactive = new Stack<Entry>(maxRetainedListCount);
            m_Inactive.Push(new Entry(this));
        }

        internal int OutstandingCount => m_OutstandingCount;
        internal int RetainedCount => m_Inactive.Count;
        internal int PeakOutstandingCount => m_PeakOutstandingCount;
        internal int DiscardedCount => m_DiscardedCount;
        internal int InvalidReturnCount => m_InvalidReturnCount;
        internal bool IsDisposed => m_IsDisposed;

        internal GameplayCueScratchListLease<T> Rent()
        {
            AssertMainThread();
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(m_Name);
            }
            if (m_OutstandingCount >= m_MaxOutstandingCount)
            {
                throw new InvalidOperationException(
                    $"Gameplay Cue scratch-list pool '{m_Name}' exhausted its outstanding lease capacity ({m_MaxOutstandingCount}).");
            }

            Entry entry = m_Inactive.Count > 0 ? m_Inactive.Pop() : new Entry(this);
            if (entry.IsOutstanding)
            {
                throw new InvalidOperationException($"Gameplay Cue scratch-list pool '{m_Name}' contains an outstanding entry.");
            }

            unchecked
            {
                entry.Generation++;
                if (entry.Generation == 0)
                {
                    entry.Generation = 1;
                }
            }
            entry.IsOutstanding = true;
            m_OutstandingCount++;
            if (m_OutstandingCount > m_PeakOutstandingCount)
            {
                m_PeakOutstandingCount = m_OutstandingCount;
            }

            return new GameplayCueScratchListLease<T>(this, entry, entry.Generation);
        }

        internal List<T> GetValue(in GameplayCueScratchListLease<T> lease)
        {
            AssertMainThread();
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(m_Name);
            }

            return ValidateLease(lease).List;
        }

        internal void Return(in GameplayCueScratchListLease<T> lease)
        {
            AssertMainThread();
            Entry entry = ValidateLease(lease);
            entry.List.Clear();
            entry.IsOutstanding = false;
            m_OutstandingCount--;

            if (m_IsDisposed ||
                entry.List.Capacity > m_MaxRetainedElementCapacity ||
                m_Inactive.Count >= m_MaxRetainedListCount)
            {
                m_DiscardedCount++;
                return;
            }

            m_Inactive.Push(entry);
        }

        public void Dispose()
        {
            AssertMainThread();
            if (m_IsDisposed)
            {
                return;
            }

            m_IsDisposed = true;
            while (m_Inactive.Count > 0)
            {
                m_Inactive.Pop().List.Clear();
            }
        }

        private Entry ValidateLease(in GameplayCueScratchListLease<T> lease)
        {
            if (!ReferenceEquals(lease.Owner, this) ||
                lease.Entry == null ||
                !ReferenceEquals(lease.Entry.Owner, this) ||
                !lease.Entry.IsOutstanding ||
                lease.Entry.Generation != lease.Generation)
            {
                m_InvalidReturnCount++;
                throw new InvalidOperationException(
                    $"Gameplay Cue scratch-list lease for '{m_Name}' is foreign, stale, or already returned.");
            }

            return lease.Entry;
        }

        private static void AssertMainThread()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("Gameplay Cue scratch-list pools must be accessed from the Unity main thread.");
            }
        }
    }
}
