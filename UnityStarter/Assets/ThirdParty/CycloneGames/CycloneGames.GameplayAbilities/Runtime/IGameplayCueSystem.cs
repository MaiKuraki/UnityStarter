using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    internal interface IGameObjectLeaseAuthority
    {
        GameObject ResolveOutstandingInstance(
            object ownerIdentity,
            int instanceId,
            long generation,
            GameObject rawInstance);
    }

    /// <summary>
    /// An opaque, generation-stamped ownership token for one rented GameObject.
    /// Copies represent the same lease and must be returned at most once through the manager that created it.
    /// </summary>
    public readonly struct GameObjectLease
    {
        private readonly IGameObjectLeaseAuthority authority;
        private readonly object ownerIdentity;
        private readonly int instanceId;
        private readonly GameObject rawInstance;

        /// <summary>
        /// Resolves the instance only while this exact lease remains outstanding at its issuing manager.
        /// This validation cannot revoke a raw GameObject reference that a consumer cached earlier.
        /// </summary>
        public GameObject Instance
        {
            get
            {
                if (authority == null)
                {
                    throw new InvalidOperationException("The GameObject lease was not issued by a pool manager.");
                }

                return authority.ResolveOutstandingInstance(
                    ownerIdentity,
                    instanceId,
                    Generation,
                    rawInstance);
            }
        }

        public long Generation { get; }

        /// <summary>
        /// True when this value was issued by a pool manager. The issuing manager remains the authority for
        /// whether the lease is currently outstanding; copied or returned lease values remain structurally valid.
        /// </summary>
        public bool IsValid => ownerIdentity != null && instanceId != 0 && Generation > 0;

        internal object OwnerIdentity => ownerIdentity;
        internal int InstanceId => instanceId;
        internal GameObject RawInstance => rawInstance;

        internal GameObjectLease(
            IGameObjectLeaseAuthority authority,
            int instanceId,
            GameObject instance,
            long generation)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            ownerIdentity = authority;
            this.instanceId = instanceId;
            rawInstance = instance;
            Generation = generation;
        }
    }

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
    /// Optional reset contract for MonoBehaviours on pooled GameObjects.
    /// A callback failure quarantines and destroys the instance instead of returning it to the pool.
    /// Implementations must not release the owning lease recursively.
    /// </summary>
    public interface IGameObjectPoolLifecycle
    {
        void OnRentFromPool();
        void OnReturnToPool();
    }

    /// <summary>
    /// Defines the main-thread-owned, bounded pool used by Gameplay Cue implementations.
    /// </summary>
    public interface IGameObjectPoolManager
    {
        IResourceLocator ResourceLocator { get; }

        UniTask<GameObjectLease> GetAsync(
            string assetRef,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default);

        UniTask PrewarmPoolAsync(
            string assetRef,
            int count,
            string bucket = null,
            string cacheTag = null,
            string cacheOwner = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns exactly one outstanding lease. Foreign, duplicate, or stale-generation returns are rejected.
        /// </summary>
        void Release(GameObjectLease lease);

        /// <summary>
        /// Checks whether a lease is currently outstanding without transferring or releasing ownership.
        /// </summary>
        bool IsLeaseOutstanding(GameObjectLease lease);

        /// <summary>
        /// Cancels pending loads, destroys retained and outstanding instances, and invalidates further operations.
        /// </summary>
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
        public readonly AbilitySystemComponent Source;
        public readonly AbilitySystemComponent Target;
        public readonly GameObject SourceObject;
        public readonly GameObject TargetObject;
        public readonly int EffectLevel;
        public readonly long EffectDurationRaw;
        public GASFixedValue EffectDuration => GASFixedValue.FromRaw(EffectDurationRaw);
        public readonly GASPredictionKey PredictionKey;

        public GameplayCueParameters(GameplayEffectSpec spec)
        {
            EffectDefinition = spec?.Def;
            Source = spec?.Source;
            Target = spec?.Target;
            SourceObject = Source?.AvatarGameObject;
            TargetObject = Target?.AvatarGameObject;
            EffectLevel = spec?.Level ?? 0;
            EffectDurationRaw = spec?.DurationRaw ?? 0L;
            PredictionKey = spec?.Context?.PredictionKey ?? default;
        }

        public GameplayCueParameters(GameplayCueEventParams parameters)
        {
            EffectDefinition = parameters.EffectDefinition as GameplayEffect;
            Source = parameters.Source as AbilitySystemComponent;
            Target = parameters.Target as AbilitySystemComponent;
            SourceObject = parameters.SourceObject as GameObject ?? Source?.AvatarGameObject;
            TargetObject = parameters.TargetObject as GameObject ?? Target?.AvatarGameObject;
            EffectLevel = parameters.EffectLevel;
            EffectDurationRaw = parameters.EffectDurationRaw;
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
        /// Creates the persistent presentation instance.
        /// It MUST return the pool lease so the manager can track and exclusively own its lifetime.
        /// Implementations that acquire a lease and then observe cancellation before returning must release it.
        /// </summary>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <param name="poolManager">The pool manager to request objects from.</param>
        /// <param name="cancellationToken">Cancels activation when the cue is removed, rejected, or shut down.</param>
        /// <returns>The created GameObject lease for lifetime tracking.</returns>
        UniTask<GameObjectLease> CreateInstanceAsync(
            GameplayCueParameters parameters,
            IGameObjectPoolManager poolManager,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles the witnessed activation transition for an already-created instance.
        /// </summary>
        UniTask OnActiveAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles presentation first observed while the cue is active, including join-in-progress state.
        /// </summary>
        UniTask OnWhileActiveAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles the removal of a persistent Gameplay Cue.
        /// </summary>
        /// <param name="instance">The GameObject instance that was created by CreateInstanceAsync.</param>
        /// <param name="parameters">Contextual information about the cue event.</param>
        /// <param name="cancellationToken">Cancels removal when the target or cue service shuts down.</param>
        /// <returns>A UniTask for async operations.</returns>
        UniTask OnRemovedAsync(
            GameObject instance,
            GameplayCueParameters parameters,
            CancellationToken cancellationToken = default);
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
