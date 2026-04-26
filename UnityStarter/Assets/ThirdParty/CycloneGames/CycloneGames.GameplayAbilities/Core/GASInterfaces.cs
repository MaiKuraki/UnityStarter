using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    #region Simulation Interfaces

    /// <summary>
    /// Core interface for AbilitySystemComponent simulation.
    /// Enables pure C# simulation and unit testing without Unity dependencies.
    /// </summary>
    public interface IAbilitySystemComponent
    {
        /// <summary>
        /// An opaque reference to the owning actor (e.g., player controller).
        /// </summary>
        object OwnerActor { get; }

        /// <summary>
        /// An opaque reference to the physical avatar actor.
        /// </summary>
        object AvatarActor { get; }

        /// <summary>
        /// Gets the current tag count container.
        /// </summary>
        ITagCountContainer CombinedTags { get; }

        /// <summary>
        /// Gets an attribute by name.
        /// </summary>
        IGameplayAttribute GetAttribute(string name);

        /// <summary>
        /// Ticks the ASC simulation.
        /// </summary>
        void Tick(float deltaTime, bool isServer);
    }

    /// <summary>
    /// Interface abstraction for tag count containers.
    /// </summary>
    public interface ITagCountContainer
    {
        bool HasTag(GameplayTag tag);
        bool HasAny(IEnumerable<GameplayTag> tags);
        bool HasAll(IEnumerable<GameplayTag> tags);

        /// <summary>
        ///  Generic overload --avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
        /// Uses GetTags() struct enumerator path for zero-allocation iteration.
        /// C# 8 default interface method: callers with a concrete ITagCountContainer reference get this for free.
        /// </summary>
        bool HasAny<T>(in T tags) where T : IGameplayTagContainer
        {
            if (tags.IsEmpty) return false;
            var en = tags.GetTags();
            while (en.MoveNext())
                if (HasTag(en.Current)) return true;
            return false;
        }

        /// <summary>
        ///  Generic overload --avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
        /// </summary>
        bool HasAll<T>(in T tags) where T : IGameplayTagContainer
        {
            if (tags.IsEmpty) return true;
            var en = tags.GetTags();
            while (en.MoveNext())
                if (!HasTag(en.Current)) return false;
            return true;
        }

        void AddTag(GameplayTag tag);
        void RemoveTag(GameplayTag tag);
        void Clear();
    }

    /// <summary>
    /// Interface for gameplay attributes.
    /// </summary>
    public interface IGameplayAttribute
    {
        string Name { get; }
        float BaseValue { get; }
        float CurrentValue { get; }
    }

    /// <summary>
    /// Interface for effect spec simulation.
    /// </summary>
    public interface IGameplayEffectSpec
    {
        int Level { get; }
        float Duration { get; }
        IGameplayEffectContext Context { get; }
    }

    /// <summary>
    /// Interface for effect context.
    /// </summary>
    public interface IGameplayEffectContext
    {
        GASPredictionKey PredictionKey { get; set; }
    }

    #endregion

    #region GameplayCue Interfaces (DI-friendly)

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
    /// Parameters passed to GameplayCue handlers. Uses object types to avoid Unity dependencies.
    /// </summary>
    public readonly struct GameplayCueEventParams
    {
        public readonly object Source;
        public readonly object Target;
        public readonly object EffectDefinition;
        public readonly object EffectContext;
        public readonly object SourceObject;
        public readonly object TargetObject;
        public readonly int EffectLevel;
        public readonly float EffectDuration;
        public readonly GASPredictionKey PredictionKey;

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object effectContext,
            object sourceObject,
            object targetObject,
            int effectLevel,
            float effectDuration)
            : this(source, target, effectDefinition, effectContext, sourceObject, targetObject, effectLevel, effectDuration, default)
        {
        }

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object effectContext,
            object sourceObject,
            object targetObject,
            int effectLevel,
            float effectDuration,
            GASPredictionKey predictionKey)
        {
            Source = source;
            Target = target;
            EffectDefinition = effectDefinition;
            EffectContext = effectContext;
            SourceObject = sourceObject;
            TargetObject = targetObject;
            EffectLevel = effectLevel;
            EffectDuration = effectDuration;
            PredictionKey = predictionKey;
        }
    }

    /// <summary>
    /// Interface for GameplayCue management. Allows DI injection and server-side mocking.
    /// </summary>
    public interface IGameplayCueManager
    {
        void RegisterStaticCue(GameplayTag cueTag, string assetAddress);
        void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters);
        void RemoveAllCuesFor(object asc);
        void Initialize(object assetPackage);
    }

    #endregion

    #region Network Bridge

    /// <summary>
    /// Serializable snapshot of an ActiveGameplayEffect for network replication.
    /// All value types --safe to copy across network message boundaries.
    /// </summary>
    public struct GASEffectReplicationData
    {
        /// <summary>Unique ID assigned by the server when this effect was applied. Used to match server/client instances.</summary>
        public int NetworkId;
        /// <summary>Stable ID the implementation uses to look up the GameplayEffect definition (e.g. SO instance ID, string hash).</summary>
        public int EffectDefId;
        /// <summary>Network ID of the source AbilitySystemComponent.</summary>
        public int SourceAscNetId;
        /// <summary>Network ID of the target AbilitySystemComponent.</summary>
        public int TargetAscNetId;
        public int Level;
        public int StackCount;
        public float Duration;
        public float TimeRemaining;
        public float PeriodTimeRemaining;
        public GASPredictionKey PredictionKey;
        /// <summary>Replicated SetByCaller entries addressed by GameplayTag.</summary>
        public GameplayTag[] SetByCallerTags;
        public float[] SetByCallerValues;
        public int SetByCallerCount;
    }

    /// <summary>
    /// Minimal parameters for replicating a GameplayCue event across the network.
    /// </summary>
    public readonly struct GASCueNetParams
    {
        public readonly int SourceAscNetId;
        public readonly int TargetAscNetId;
        /// <summary>Raw magnitude value (e.g. damage dealt). Implementation-defined meaning.</summary>
        public readonly float Magnitude;
        /// <summary>Magnitude normalized to [0..1] for visual scaling (e.g. hit sparks).</summary>
        public readonly float NormalizedMagnitude;
        public readonly GASPredictionKey PredictionKey;

        public GASCueNetParams(int sourceAscNetId, int targetAscNetId, float magnitude, float normalizedMagnitude)
            : this(sourceAscNetId, targetAscNetId, magnitude, normalizedMagnitude, default)
        {
        }

        public GASCueNetParams(int sourceAscNetId, int targetAscNetId, float magnitude, float normalizedMagnitude, GASPredictionKey predictionKey)
        {
            SourceAscNetId = sourceAscNetId;
            TargetAscNetId = targetAscNetId;
            Magnitude = magnitude;
            NormalizedMagnitude = normalizedMagnitude;
            PredictionKey = predictionKey;
        }
    }

    /// <summary>
    /// Resolves stable identifiers used by GAS replication and authoritative rollback.
    /// 
    /// This service is intentionally separate from <see cref="IGASNetworkBridge"/>:
    /// the bridge transports messages, while the resolver maps runtime objects to stable IDs
    /// and back again.
    /// </summary>
    public interface IGASReplicationResolver
    {
        /// <summary>Gets a stable network-visible ID for an ASC.</summary>
        int GetAbilitySystemNetworkId(IGASNetworkTarget asc);

        /// <summary>Resolves an ASC by its stable network-visible ID.</summary>
        bool TryResolveAbilitySystem(int networkId, out IGASNetworkTarget asc);

        /// <summary>Gets a stable replication ID for a gameplay effect definition object.</summary>
        int GetGameplayEffectDefinitionId(object effectDefinition);

        /// <summary>Resolves a gameplay effect definition object from its stable replication ID.</summary>
        object ResolveGameplayEffectDefinition(int effectDefinitionId);
    }

    /// <summary>
    /// Transport-agnostic network bridge for the Gameplay Ability System.
    /// 
    /// Implement this interface with your chosen networking library (Netcode for GameObjects,
    /// Photon Fusion, FishNet, custom transport, etc.) and register it via <see cref="GASServices.NetworkBridge"/>.
    /// 
    /// The default implementation (<see cref="GASNullNetworkBridge"/>) routes all calls
    /// locally --safe for single-player and listen-server topologies.
    /// 
    /// <b>Calling convention:</b>
    /// - <c>Client*</c> methods are called by the local client to request server actions.
    /// - <c>Server*</c> methods are called by the server to notify clients.
    /// - The ASC checks <see cref="IsServer"/> to decide which path to follow.
    /// 
    /// <b>Usage example (Netcode for GameObjects):</b>
    /// <code>
    /// public class NetcodeGASBridge : NetworkBehaviour, IGASNetworkBridge
    /// {
    ///     public bool IsServer => NetworkManager.Singleton.IsServer;
    ///     public bool IsLocallyOwned(IGASNetworkTarget asc)
    ///         => asc is MyPlayerASC p && p.OwnerClientId == NetworkManager.LocalClientId;
    ///
    ///     public void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, GASPredictionKey key)
    ///         => ActivateAbilityServerRpc(GetNetId(asc), specHandle, key.Key);
    ///
    ///     [ServerRpc] private void ActivateAbilityServerRpc(ulong netId, int specHandle, int keyValue)
    ///     {
    ///         var asc = FindAscByNetId(netId);
    ///         asc?.ServerReceiveTryActivateAbility(specHandle, new GASPredictionKey(keyValue));
    ///     }
    ///     // ... and so on for other methods
    /// }
    /// </code>
    /// </summary>
    public interface IGASNetworkBridge
    {
        /// <summary>Returns true if this process has server authority over GAS state.</summary>
        bool IsServer { get; }

        /// <summary>Returns true if the given ASC is locally owned (eligible for client-side prediction).</summary>
        bool IsLocallyOwned(IGASNetworkTarget asc);

        // ---- Client ->Server ----

        /// <summary>
        /// Called by the client when a LocalPredicted ability activates.
        /// Implementations should send an RPC to the server, which will call
        /// <see cref="IGASNetworkTarget.ServerReceiveTryActivateAbility"/> on the server-side ASC.
        /// </summary>
        void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, GASPredictionKey predictionKey);

        // ---- Server ->Client ----

        /// <summary>
        /// Called by the server to confirm a client's predicted activation.
        /// Implementations should send an RPC to the owning client, which will call
        /// <see cref="IGASNetworkTarget.ClientReceiveActivationSucceeded"/>.
        /// </summary>
        void ServerConfirmActivation(IGASNetworkTarget targetAsc, int specHandle, GASPredictionKey predictionKey);

        /// <summary>
        /// Called by the server to reject a client's predicted activation.
        /// Implementations should send an RPC to the owning client, which will call
        /// <see cref="IGASNetworkTarget.ClientReceiveActivationFailed"/>.
        /// </summary>
        void ServerRejectActivation(IGASNetworkTarget targetAsc, int specHandle, GASPredictionKey predictionKey);

        // ---- Effect Replication (Server ->All Relevant Clients) ----

        /// <summary>
        /// Called on the server when a new ActiveGameplayEffect is applied.
        /// Implementations replicate this to all clients that need it.
        /// The client receiving this should call <see cref="IGASNetworkTarget.ClientReceiveEffectApplied"/>.
        /// </summary>
        void ServerReplicateEffectApplied(IGASNetworkTarget targetAsc, in GASEffectReplicationData data);

        /// <summary>
        /// Called on the server when an existing ActiveGameplayEffect changes authoritative state
        /// (for example: stacking, refreshed duration, or server-side reconciliation).
        /// </summary>
        void ServerReplicateEffectUpdated(IGASNetworkTarget targetAsc, in GASEffectReplicationData data);

        /// <summary>
        /// Called on the server when an ActiveGameplayEffect is removed.
        /// </summary>
        void ServerReplicateEffectRemoved(IGASNetworkTarget targetAsc, int effectNetId);

        // ---- GameplayCue Replication (Server ->All Clients) ----

        /// <summary>
        /// Called on the server when a GameplayCue fires.
        /// Implementations broadcast this to all relevant clients.
        /// The receiving client should trigger its local <see cref="IGameplayCueManager"/>.
        /// </summary>
        void ServerBroadcastGameplayCue(IGASNetworkTarget sourceAsc, GameplayTag cueTag,
            EGameplayCueEvent eventType, in GASCueNetParams cueParams);

        /// <summary>
        /// Sends a count-based, caller-owned delta buffer to a single client.
        /// Network serializers should write only [0, Count) for each buffer section.
        /// </summary>
        void ServerSendStateDelta(IGASNetworkTarget targetAsc, GASAbilitySystemStateDeltaBuffer delta);
    }

    /// <summary>
    /// Exposes the server-> Client and client-receive entry points on an AbilitySystemComponent.
    /// 
    /// Network bridge implementations cast <see cref="IAbilitySystemComponent"/> to this interface
    /// to deliver incoming RPCs without depending on the concrete Runtime type.
    /// 
    /// AbilitySystemComponent implements both IAbilitySystemComponent and IGASNetworkTarget independently.
    /// </summary>
    public interface IGASNetworkTarget
    {
        /// <summary>
        /// Server entry point: called when the server receives a client's activation RPC.
        /// </summary>
        void ServerReceiveTryActivateAbility(int specHandle, GASPredictionKey predictionKey);

        /// <summary>
        /// Client entry point: called when the client receives server confirmation of a predicted activation.
        /// </summary>
        void ClientReceiveActivationSucceeded(int specHandle, GASPredictionKey predictionKey);

        /// <summary>
        /// Client entry point: called when the client receives server rejection of a predicted activation.
        /// </summary>
        void ClientReceiveActivationFailed(int specHandle, GASPredictionKey predictionKey);

        /// <summary>
        /// Client entry point: called when the client receives a replicated effect application from the server.
        /// </summary>
        void ClientReceiveEffectApplied(in GASEffectReplicationData data);

        /// <summary>
        /// Client entry point: called when the client receives an authoritative update for an already-known effect.
        /// </summary>
        void ClientReceiveEffectUpdated(in GASEffectReplicationData data);

        /// <summary>
        /// Client entry point: called when the client receives a replicated effect removal from the server.
        /// </summary>
        void ClientReceiveEffectRemoved(int effectNetId);

        /// <summary>
        /// Client entry point: called when the client receives a replicated GameplayCue event.
        /// </summary>
        void ClientReceiveGameplayCue(GameplayTag cueTag, EGameplayCueEvent eventType, in GASCueNetParams cueParams);

        /// <summary>
        /// Client entry point: server sent a count-based incremental delta buffer.
        /// Only [0, Count) entries in each section are meaningful.
        /// </summary>
        void ClientReceiveStateDelta(GASAbilitySystemStateDeltaBuffer delta);
    }

    #endregion

    #region Service Locator

    /// <summary>
    /// Service locator for GAS services. Provides default implementations while allowing DI override.
    /// Thread-safe with volatile read/write for lock-free access after initialization.
    /// </summary>
    public static class GASServices
    {
        private static volatile IGameplayCueManager s_CueManager;
        private static volatile ISimulationTimeProvider s_TimeProvider;
        private static volatile ISimulationRandomProvider s_RandomProvider;
        private static volatile IGASNetworkBridge s_NetworkBridge;
        private static volatile IGASReplicationResolver s_ReplicationResolver;
        private static volatile IGASDefinitionRegistry s_DefinitionRegistry;
        private static volatile IGASAttributeRegistry s_AttributeRegistry;

        /// <summary>
        /// Gets or sets the GameplayCue manager. Returns NullGameplayCueManager if not set.
        /// </summary>
        public static IGameplayCueManager CueManager
        {
            get => s_CueManager ?? NullGameplayCueManager.Instance;
            set => s_CueManager = value;
        }

        /// <summary>
        /// Gets or sets the time provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationTimeProvider TimeProvider
        {
            get => s_TimeProvider ?? DefaultTimeProvider.Instance;
            set => s_TimeProvider = value;
        }

        /// <summary>
        /// Gets or sets the random provider for simulation. Allows deterministic replay.
        /// </summary>
        public static ISimulationRandomProvider RandomProvider
        {
            get => s_RandomProvider ?? DefaultRandomProvider.Instance;
            set => s_RandomProvider = value;
        }

        /// <summary>
        /// Gets or sets the network bridge.
        /// Defaults to <see cref="GASNullNetworkBridge.Instance"/> (local single-player routing).
        /// Replace with your networking-library implementation before any ability activation occurs.
        /// <example>
        /// // In your NetworkManager bootstrap:
        /// GASServices.NetworkBridge = new MyNetcodeGASBridge();
        /// </example>
        /// </summary>
        public static IGASNetworkBridge NetworkBridge
        {
            get => s_NetworkBridge ?? GASNullNetworkBridge.Instance;
            set => s_NetworkBridge = value;
        }

        /// <summary>
        /// Gets or sets the replication resolver.
        /// Defaults to <see cref="GASLocalReplicationResolver.Instance"/> (in-process stable IDs).
        /// </summary>
        public static IGASReplicationResolver ReplicationResolver
        {
            get => s_ReplicationResolver ?? GASLocalReplicationResolver.Instance;
            set => s_ReplicationResolver = value;
        }

        public static IGASDefinitionRegistry DefinitionRegistry
        {
            get => s_DefinitionRegistry ?? GASDefaultDefinitionRegistry.Instance;
            set => s_DefinitionRegistry = value;
        }

        public static IGASAttributeRegistry AttributeRegistry
        {
            get => s_AttributeRegistry ?? GASDefaultAttributeRegistry.Instance;
            set => s_AttributeRegistry = value;
        }

        /// <summary>
        /// Resets all services to null. Call during game shutdown or test teardown.
        /// </summary>
        public static void Reset()
        {
            s_CueManager = null;
            s_TimeProvider = null;
            s_RandomProvider = null;
            s_NetworkBridge = null;
            s_ReplicationResolver = null;
            s_DefinitionRegistry = null;
            s_AttributeRegistry = null;
        }
    }

    #endregion

    #region Null Object Implementations

    /// <summary>
    /// Null object pattern for server-side or headless environments.
    /// </summary>
    public sealed class NullGameplayCueManager : IGameplayCueManager
    {
        public static readonly NullGameplayCueManager Instance = new NullGameplayCueManager();
        private NullGameplayCueManager() { }

        public void RegisterStaticCue(GameplayTag cueTag, string assetAddress) { }
        public void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters) { }
        public void RemoveAllCuesFor(object asc) { }
        public void Initialize(object assetPackage) { }
    }

    /// <summary>
    /// Default network bridge: routes all calls locally within the same process.
    /// 
    /// - <see cref="IsServer"/> always returns true (this process is authoritative).
    /// - <c>ClientRequest*</c> calls are forwarded directly to the server-side ASC methods.
    /// - <c>ServerConfirm/Reject</c> calls are forwarded directly to the client-side ASC methods.
    /// - Effect replication and cue broadcasting are no-ops (same process = already applied).
    /// 
    /// Suitable for: single-player, listen-server, offline testing, headless servers.
    /// </summary>
    public sealed class GASNullNetworkBridge : IGASNetworkBridge
    {
        public static readonly GASNullNetworkBridge Instance = new GASNullNetworkBridge();
        private GASNullNetworkBridge() { }

        /// <summary>Always true: this process IS the server in local mode.</summary>
        public bool IsServer => true;

        /// <summary>Always true: every ASC is locally owned in single-player mode.</summary>
        public bool IsLocallyOwned(IGASNetworkTarget asc) => true;

        /// <summary>
        /// In local mode the client IS the server, so we dispatch directly to the server entry point.
        /// </summary>
        public void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, GASPredictionKey predictionKey)
        {
            asc.ServerReceiveTryActivateAbility(specHandle, predictionKey);
        }

        /// <summary>Directly calls the client receive method on the same ASC instance.</summary>
        public void ServerConfirmActivation(IGASNetworkTarget targetAsc, int specHandle, GASPredictionKey predictionKey)
        {
            targetAsc.ClientReceiveActivationSucceeded(specHandle, predictionKey);
        }

        /// <summary>Directly calls the client receive method on the same ASC instance.</summary>
        public void ServerRejectActivation(IGASNetworkTarget targetAsc, int specHandle, GASPredictionKey predictionKey)
        {
            targetAsc.ClientReceiveActivationFailed(specHandle, predictionKey);
        }

        // Effect application is already local --no replication needed.
        public void ServerReplicateEffectApplied(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectUpdated(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectRemoved(IGASNetworkTarget targetAsc, int effectNetId) { }

        // Cues are already dispatched locally in DispatchGameplayCues --no broadcast needed.
        public void ServerBroadcastGameplayCue(IGASNetworkTarget sourceAsc, GameplayTag cueTag,
            EGameplayCueEvent eventType, in GASCueNetParams cueParams)
        { }

        public void ServerSendStateDelta(IGASNetworkTarget targetAsc, GASAbilitySystemStateDeltaBuffer delta) { }
    }

    /// <summary>
    /// Null replication resolver. All lookups fail; all IDs return 0.
    /// Suitable only when replication is entirely disabled.
    /// For single-player or listen-server use, prefer <see cref="GASLocalReplicationResolver"/>.
    /// </summary>
    public sealed class GASNullReplicationResolver : IGASReplicationResolver
    {
        public static readonly GASNullReplicationResolver Instance = new GASNullReplicationResolver();
        private GASNullReplicationResolver() { }

        public int GetAbilitySystemNetworkId(IGASNetworkTarget asc) => 0;
        public bool TryResolveAbilitySystem(int networkId, out IGASNetworkTarget asc) { asc = null; return false; }
        public int GetGameplayEffectDefinitionId(object effectDefinition) => 0;
        public object ResolveGameplayEffectDefinition(int effectDefinitionId) => null;
    }

    /// <summary>
    /// Bidirectional in-process replication resolver.
    /// Assigns stable monotonically-increasing integer IDs to ASC instances and effect definitions
    /// and resolves them back. Thread-safe via lock.
    /// 
    /// Use this as the default for single-player, listen-server, or test contexts where all objects
    /// live in the same process. For dedicated-server / client-server topologies, implement
    /// <see cref="IGASReplicationResolver"/> using your transport's network object IDs.
    /// </summary>
    public sealed class GASLocalReplicationResolver : IGASReplicationResolver
    {
        public static readonly GASLocalReplicationResolver Instance = new GASLocalReplicationResolver();

        private readonly object _lock = new object();
        private int _nextId = 1;

        // ASC registry
        private readonly Dictionary<IGASNetworkTarget, int> _ascToId = new Dictionary<IGASNetworkTarget, int>(16);
        private readonly Dictionary<int, IGASNetworkTarget> _idToAsc = new Dictionary<int, IGASNetworkTarget>(16);

        // Effect definition registry
        private readonly Dictionary<object, int> _defToId = new Dictionary<object, int>(64);
        private readonly Dictionary<int, object> _idToDef = new Dictionary<int, object>(64);

        // ----- ASC -----

        /// <summary>Registers an ASC and returns its stable ID. Idempotent: re-registering the same instance returns the same ID.</summary>
        public int Register(IGASNetworkTarget asc)
        {
            if (asc == null) return 0;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int existing)) return existing;
                int id = _nextId++;
                _ascToId[asc] = id;
                _idToAsc[id] = asc;
                return id;
            }
        }

        /// <summary>Removes an ASC from the registry (call when the ASC is destroyed).</summary>
        public void Unregister(IGASNetworkTarget asc)
        {
            if (asc == null) return;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int id))
                {
                    _ascToId.Remove(asc);
                    _idToAsc.Remove(id);
                }
            }
        }

        public int GetAbilitySystemNetworkId(IGASNetworkTarget asc)
        {
            if (asc == null) return 0;
            lock (_lock)
            {
                if (_ascToId.TryGetValue(asc, out int id)) return id;
                // Auto-register on first encounter so callers don't have to pre-register.
                int newId = _nextId++;
                _ascToId[asc] = newId;
                _idToAsc[newId] = asc;
                return newId;
            }
        }

        public bool TryResolveAbilitySystem(int networkId, out IGASNetworkTarget asc)
        {
            lock (_lock) { return _idToAsc.TryGetValue(networkId, out asc); }
        }

        // ----- Effect Definitions -----

        /// <summary>Registers an effect definition and returns its stable ID. Idempotent.</summary>
        public int RegisterDefinition(object effectDefinition)
        {
            if (effectDefinition == null) return 0;
            lock (_lock)
            {
                if (_defToId.TryGetValue(effectDefinition, out int existing)) return existing;
                int id = _nextId++;
                _defToId[effectDefinition] = id;
                _idToDef[id] = effectDefinition;
                return id;
            }
        }

        public int GetGameplayEffectDefinitionId(object effectDefinition)
        {
            if (effectDefinition == null) return 0;
            lock (_lock)
            {
                if (_defToId.TryGetValue(effectDefinition, out int id)) return id;
                int newId = _nextId++;
                _defToId[effectDefinition] = newId;
                _idToDef[newId] = effectDefinition;
                return newId;
            }
        }

        public object ResolveGameplayEffectDefinition(int effectDefinitionId)
        {
            lock (_lock)
            {
                _idToDef.TryGetValue(effectDefinitionId, out var def);
                return def;
            }
        }

        /// <summary>Clears all registrations. Use when transitioning between scenes / sessions.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _ascToId.Clear();
                _idToAsc.Clear();
                _defToId.Clear();
                _idToDef.Clear();
                _nextId = 1;
            }
        }
    }

    #endregion

    #region Simulation Providers

    /// <summary>
    /// Provides time for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationTimeProvider
    {
        float DeltaTime { get; }
        float TotalTime { get; }
        int FrameCount { get; }
    }

    /// <summary>
    /// Provides random values for simulation. Override for deterministic replay.
    /// </summary>
    public interface ISimulationRandomProvider
    {
        float NextFloat();
        float NextFloat(float min, float max);
        int NextInt(int min, int max);
    }

    /// <summary>
    /// Default time provider using system time.
    /// </summary>
    public sealed class DefaultTimeProvider : ISimulationTimeProvider
    {
        public static readonly DefaultTimeProvider Instance = new DefaultTimeProvider();
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private double _lastTime;
        private double _deltaTime;
        private int _frameCount;

        private DefaultTimeProvider() { }

        /// <summary>
        ///  Advances the time provider by sampling the stopwatch. Call exactly once per frame.
        /// Separating the advance step from the read step eliminates the data race where two callers
        /// reading DeltaTime would each modify _lastTime, producing incorrect deltas.
        /// </summary>
        public void Advance()
        {
            double current = _stopwatch.Elapsed.TotalSeconds;
            _deltaTime = current - _lastTime;
            _lastTime = current;
            _frameCount++;
        }

        /// <summary>Read-only. Call Advance() once per frame before reading this value.</summary>
        public float DeltaTime => (float)_deltaTime;
        public float TotalTime => (float)_stopwatch.Elapsed.TotalSeconds;
        public int FrameCount => _frameCount;
    }

    /// <summary>
    /// Default random provider using System.Random.
    /// </summary>
    public sealed class DefaultRandomProvider : ISimulationRandomProvider
    {
        public static readonly DefaultRandomProvider Instance = new DefaultRandomProvider();

        //  [ThreadStatic] per-thread Random instances eliminate thread-safety concerns with System.Random.
        // Each thread gets a uniquely seeded instance on first access, preventing determinism issues.
        [System.ThreadStatic]
        private static Random s_ThreadRandom;

        private static Random ThreadRandom
            => s_ThreadRandom ??= new Random(
                System.Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId);

        private DefaultRandomProvider() { }

        public float NextFloat() => (float)ThreadRandom.NextDouble();
        public float NextFloat(float min, float max) => min + (float)ThreadRandom.NextDouble() * (max - min);
        public int NextInt(int min, int max) => ThreadRandom.Next(min, max);
    }

    /// <summary>
    /// Deterministic time provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicTimeProvider : ISimulationTimeProvider
    {
        private double _totalTime;
        private double _deltaTime;
        private int _frameCount;

        public float DeltaTime => (float)_deltaTime;
        public float TotalTime => (float)_totalTime;
        public int FrameCount => _frameCount;

        /// <summary>
        /// Advances time by the specified delta.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _deltaTime = deltaTime;
            _totalTime += deltaTime;
            _frameCount++;
        }

        /// <summary>
        /// Resets time to zero.
        /// </summary>
        public void Reset()
        {
            _totalTime = 0d;
            _deltaTime = 0d;
            _frameCount = 0;
        }
    }

    /// <summary>
    /// Deterministic random provider for unit tests and replays.
    /// </summary>
    public sealed class DeterministicRandomProvider : ISimulationRandomProvider
    {
        private readonly Random _random;

        public DeterministicRandomProvider(int seed)
        {
            _random = new Random(seed);
        }

        public float NextFloat() => (float)_random.NextDouble();
        public float NextFloat(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        public int NextInt(int min, int max) => _random.Next(min, max);
    }

    #endregion
    #region State Data

    /// <summary>
    /// Marker interface for ability definition types.
    /// Constraining snapshot fields to this interface prevents accidental boxing of value types
    /// and makes the API intent explicit: only reference-type definitions are valid.
    /// </summary>
    public interface IGASAbilityDefinition { }

    [Flags]
    public enum AbilitySystemStateChangeMask
    {
        None = 0,
        GrantedAbilities = 1 << 0,
        ActiveEffects = 1 << 1,
        Attributes = 1 << 2,
        Tags = 1 << 3
    }

    /// <summary>
    /// Pure C# snapshot of a granted ability entry.
    /// The definition reference is opaque so adapters can map it to engine- or network-specific IDs.
    /// </summary>
    public readonly struct GASGrantedAbilityStateData
    {
        public readonly int SpecHandle;
        public readonly IGASAbilityDefinition AbilityDefinition;
        public readonly int Level;
        public readonly bool IsActive;

        public GASGrantedAbilityStateData(IGASAbilityDefinition abilityDefinition, int level, bool isActive)
            : this(0, abilityDefinition, level, isActive)
        {
        }

        public GASGrantedAbilityStateData(int specHandle, IGASAbilityDefinition abilityDefinition, int level, bool isActive)
        {
            SpecHandle = specHandle;
            AbilityDefinition = abilityDefinition;
            Level = level;
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Pure C# snapshot of a single SetByCaller magnitude addressed by GameplayTag.
    /// </summary>
    public readonly struct GASSetByCallerTagStateData
    {
        public readonly GameplayTag Tag;
        public readonly float Value;

        public GASSetByCallerTagStateData(GameplayTag tag, float value)
        {
            Tag = tag;
            Value = value;
        }
    }

    /// <summary>
    /// Pure C# snapshot of an active gameplay effect.
    /// </summary>
    public readonly struct GASActiveEffectStateData
    {
        public readonly int InstanceId;
        public readonly object EffectDefinition;
        public readonly object SourceComponent;
        public readonly int Level;
        public readonly int StackCount;
        public readonly float Duration;
        public readonly float TimeRemaining;
        public readonly float PeriodTimeRemaining;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASSetByCallerTagStateData[] SetByCallerTagMagnitudes;
        public readonly int SetByCallerTagMagnitudeCount;

        public GASActiveEffectStateData(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes)
            : this(
                instanceId,
                effectDefinition,
                sourceComponent,
                level,
                stackCount,
                duration,
                timeRemaining,
                periodTimeRemaining,
                predictionKey,
                setByCallerTagMagnitudes,
                setByCallerTagMagnitudes != null ? setByCallerTagMagnitudes.Length : 0)
        {
        }

        public GASActiveEffectStateData(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount)
        {
            InstanceId = instanceId;
            EffectDefinition = effectDefinition;
            SourceComponent = sourceComponent;
            Level = level;
            StackCount = stackCount;
            Duration = duration;
            TimeRemaining = timeRemaining;
            PeriodTimeRemaining = periodTimeRemaining;
            PredictionKey = predictionKey;
            SetByCallerTagMagnitudes = setByCallerTagMagnitudes;
            SetByCallerTagMagnitudeCount = setByCallerTagMagnitudeCount < 0 ? 0 : setByCallerTagMagnitudeCount;
        }
    }

    /// <summary>
    /// Pure C# snapshot of an attribute value pair.
    /// </summary>
    public readonly struct GASAttributeStateData
    {
        public readonly string AttributeName;
        public readonly float BaseValue;
        public readonly float CurrentValue;

        public GASAttributeStateData(string attributeName, float baseValue, float currentValue)
        {
            AttributeName = attributeName;
            BaseValue = baseValue;
            CurrentValue = currentValue;
        }
    }

    public sealed class GASAbilitySystemStateDeltaBuffer
    {
        public uint Sequence;
        public uint StateChecksum;
        public ulong BaseVersion;
        public ulong CurrentVersion;
        public AbilitySystemStateChangeMask ChangeMask;

        public GASGrantedAbilityStateData[] GrantedAbilities = Array.Empty<GASGrantedAbilityStateData>();
        public int GrantedAbilityCount;

        public IGASAbilityDefinition[] RemovedAbilityDefinitions = Array.Empty<IGASAbilityDefinition>();
        public int RemovedAbilityDefinitionCount;

        public GASActiveEffectStateData[] ActiveEffects = Array.Empty<GASActiveEffectStateData>();
        public int ActiveEffectCount;
        public GASSetByCallerTagStateData[][] ActiveEffectSetByCallerMagnitudes = Array.Empty<GASSetByCallerTagStateData[]>();

        public int[] RemovedEffectNetIds = Array.Empty<int>();
        public int RemovedEffectNetIdCount;

        public GASAttributeStateData[] Attributes = Array.Empty<GASAttributeStateData>();
        public int AttributeCount;

        public GameplayTag[] AddedTags = Array.Empty<GameplayTag>();
        public int AddedTagCount;

        public GameplayTag[] RemovedTags = Array.Empty<GameplayTag>();
        public int RemovedTagCount;

        public bool HasChanges => ChangeMask != AbilitySystemStateChangeMask.None;

        public void Reserve(
            int grantedAbilityCapacity,
            int removedAbilityDefinitionCapacity,
            int activeEffectCapacity,
            int removedEffectCapacity,
            int attributeCapacity,
            int addedTagCapacity,
            int removedTagCapacity,
            int maxSetByCallerPerEffect = 0)
        {
            EnsureGrantedAbilityCapacity(grantedAbilityCapacity);
            EnsureRemovedAbilityDefinitionCapacity(removedAbilityDefinitionCapacity);
            EnsureActiveEffectCapacity(activeEffectCapacity);
            EnsureRemovedEffectNetIdCapacity(removedEffectCapacity);
            EnsureAttributeCapacity(attributeCapacity);
            EnsureAddedTagCapacity(addedTagCapacity);
            EnsureRemovedTagCapacity(removedTagCapacity);

            if (maxSetByCallerPerEffect > 0)
            {
                for (int i = 0; i < activeEffectCapacity; i++)
                {
                    EnsureActiveEffectSetByCallerCapacity(i, maxSetByCallerPerEffect);
                }
            }
        }

        public void ClearCounts()
        {
            Sequence = 0;
            StateChecksum = 0;
            BaseVersion = 0;
            CurrentVersion = 0;
            ChangeMask = AbilitySystemStateChangeMask.None;
            GrantedAbilityCount = 0;
            RemovedAbilityDefinitionCount = 0;
            ActiveEffectCount = 0;
            RemovedEffectNetIdCount = 0;
            AttributeCount = 0;
            AddedTagCount = 0;
            RemovedTagCount = 0;
        }

        public GASGrantedAbilityStateData[] EnsureGrantedAbilityCapacity(int capacity)
        {
            if (GrantedAbilities.Length < capacity)
            {
                GrantedAbilities = new GASGrantedAbilityStateData[capacity];
            }

            return GrantedAbilities;
        }

        public IGASAbilityDefinition[] EnsureRemovedAbilityDefinitionCapacity(int capacity)
        {
            if (RemovedAbilityDefinitions.Length < capacity)
            {
                RemovedAbilityDefinitions = new IGASAbilityDefinition[capacity];
            }

            return RemovedAbilityDefinitions;
        }

        public GASActiveEffectStateData[] EnsureActiveEffectCapacity(int capacity)
        {
            if (ActiveEffects.Length < capacity)
            {
                ActiveEffects = new GASActiveEffectStateData[capacity];
            }

            if (ActiveEffectSetByCallerMagnitudes.Length < capacity)
            {
                var existing = ActiveEffectSetByCallerMagnitudes;
                ActiveEffectSetByCallerMagnitudes = new GASSetByCallerTagStateData[capacity][];
                for (int i = 0; i < existing.Length; i++)
                {
                    ActiveEffectSetByCallerMagnitudes[i] = existing[i];
                }
            }

            return ActiveEffects;
        }

        public GASSetByCallerTagStateData[] EnsureActiveEffectSetByCallerCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0)
            {
                return Array.Empty<GASSetByCallerTagStateData>();
            }

            if (ActiveEffectSetByCallerMagnitudes.Length <= effectIndex)
            {
                EnsureActiveEffectCapacity(effectIndex + 1);
            }

            var entries = ActiveEffectSetByCallerMagnitudes[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                entries = new GASSetByCallerTagStateData[capacity];
                ActiveEffectSetByCallerMagnitudes[effectIndex] = entries;
            }

            return entries;
        }

        public int[] EnsureRemovedEffectNetIdCapacity(int capacity)
        {
            if (RemovedEffectNetIds.Length < capacity)
            {
                RemovedEffectNetIds = new int[capacity];
            }

            return RemovedEffectNetIds;
        }

        public GASAttributeStateData[] EnsureAttributeCapacity(int capacity)
        {
            if (Attributes.Length < capacity)
            {
                Attributes = new GASAttributeStateData[capacity];
            }

            return Attributes;
        }

        public GameplayTag[] EnsureAddedTagCapacity(int capacity)
        {
            if (AddedTags.Length < capacity)
            {
                AddedTags = new GameplayTag[capacity];
            }

            return AddedTags;
        }

        public GameplayTag[] EnsureRemovedTagCapacity(int capacity)
        {
            if (RemovedTags.Length < capacity)
            {
                RemovedTags = new GameplayTag[capacity];
            }

            return RemovedTags;
        }
    }

    #endregion

    #region GAS Core Contracts

    /// <summary>
    /// Stable network-visible entity id used by the GAS core runtime. It is deliberately not a Unity object reference.
    /// </summary>
    public readonly struct GASEntityId : IEquatable<GASEntityId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASEntityId(int value)
        {
            Value = value;
        }

        public bool Equals(GASEntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASEntityId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASEntityId left, GASEntityId right) => left.Equals(right);
        public static bool operator !=(GASEntityId left, GASEntityId right) => !left.Equals(right);
    }

    public readonly struct GASDefinitionId : IEquatable<GASDefinitionId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASDefinitionId(int value)
        {
            Value = value;
        }

        public bool Equals(GASDefinitionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASDefinitionId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASDefinitionId left, GASDefinitionId right) => left.Equals(right);
        public static bool operator !=(GASDefinitionId left, GASDefinitionId right) => !left.Equals(right);
    }

    public enum GASDefinitionKind : byte
    {
        Unknown,
        Ability,
        Effect,
        Cue
    }

    public readonly struct GASDefinitionVersion
    {
        public readonly GASDefinitionKind Kind;
        public readonly GASDefinitionId Id;
        public readonly uint ContentHash;

        public GASDefinitionVersion(GASDefinitionKind kind, GASDefinitionId id, uint contentHash)
        {
            Kind = kind;
            Id = id;
            ContentHash = contentHash;
        }
    }

    public readonly struct GASSpecHandle : IEquatable<GASSpecHandle>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASSpecHandle(int value)
        {
            Value = value;
        }

        public bool Equals(GASSpecHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASSpecHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASSpecHandle left, GASSpecHandle right) => left.Equals(right);
        public static bool operator !=(GASSpecHandle left, GASSpecHandle right) => !left.Equals(right);
    }

    public readonly struct GASActiveEffectHandle : IEquatable<GASActiveEffectHandle>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASActiveEffectHandle(int value)
        {
            Value = value;
        }

        public bool Equals(GASActiveEffectHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASActiveEffectHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASActiveEffectHandle left, GASActiveEffectHandle right) => left.Equals(right);
        public static bool operator !=(GASActiveEffectHandle left, GASActiveEffectHandle right) => !left.Equals(right);
    }

    public readonly struct GASAttributeId : IEquatable<GASAttributeId>
    {
        public readonly int Value;
        public bool IsValid => Value != 0;

        public GASAttributeId(int value)
        {
            Value = value;
        }

        public bool Equals(GASAttributeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GASAttributeId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(GASAttributeId left, GASAttributeId right) => left.Equals(right);
        public static bool operator !=(GASAttributeId left, GASAttributeId right) => !left.Equals(right);
    }

    public enum GASNetExecutionPolicy : byte
    {
        LocalOnly,
        LocalPredicted,
        ServerOnly,
        ServerInitiated
    }

    public enum GASInstancingPolicy : byte
    {
        NonInstanced,
        InstancedPerActor,
        InstancedPerExecution
    }

    public enum GASReplicationPolicy : byte
    {
        None,
        OwnerOnly,
        SimulatedOnly,
        Everyone
    }

    public enum GASModifierOp : byte
    {
        Add,
        Multiply,
        Division,
        Override
    }

    public enum GASEffectDurationPolicy : byte
    {
        Instant,
        Infinite,
        Duration
    }

    public enum GASAbilityActivationResultCode : byte
    {
        Accepted,
        Predicted,
        MissingSpec,
        InvalidPredictionKey,
        NetworkRejected
    }

    public readonly struct GASFixedTime
    {
        public readonly int Tick;
        public readonly int TickRate;

        public GASFixedTime(int tick, int tickRate)
        {
            Tick = tick;
            TickRate = tickRate > 0 ? tickRate : 1;
        }
    }

    public readonly struct GASPredictionKey : IEquatable<GASPredictionKey>
    {
        private static int s_NextKey = 1;

        public readonly int Value;
        public readonly GASEntityId Owner;
        public readonly int InputSequence;
        public int Key => Value;
        public bool IsValid => Value != 0;

        public GASPredictionKey(int value)
            : this(value, default, 0)
        {
        }

        public GASPredictionKey(int value, GASEntityId owner, int inputSequence)
        {
            Value = value;
            Owner = owner;
            InputSequence = inputSequence;
        }

        public static GASPredictionKey NewKey()
        {
            return NewKey(default, 0);
        }

        public static GASPredictionKey NewKey(GASEntityId owner, int inputSequence)
        {
            int key = System.Threading.Interlocked.Increment(ref s_NextKey);
            if (key >= int.MaxValue - 1)
            {
                System.Threading.Interlocked.Exchange(ref s_NextKey, 1);
            }

            return new GASPredictionKey(key, owner, inputSequence);
        }

        public bool Equals(GASPredictionKey other)
        {
            return Value == other.Value && Owner == other.Owner && InputSequence == other.InputSequence;
        }

        public override bool Equals(object obj) => obj is GASPredictionKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Value;
                hash = (hash * 397) ^ Owner.Value;
                hash = (hash * 397) ^ InputSequence;
                return hash;
            }
        }

        public static bool operator ==(GASPredictionKey left, GASPredictionKey right) => left.Equals(right);
        public static bool operator !=(GASPredictionKey left, GASPredictionKey right) => !left.Equals(right);
    }

    public enum GASPredictionWindowStatus : byte
    {
        Open,
        Confirmed,
        Rejected,
        TimedOut
    }

    [Flags]
    public enum GASPredictionRollbackFlags : ushort
    {
        None = 0,
        CorePrediction = 1 << 0,
        ActiveEffects = 1 << 1,
        AttributeSnapshots = 1 << 2,
        GameplayCues = 1 << 3,
        AbilityTasks = 1 << 4,
        AbilityCancelled = 1 << 5,
        DependentWindows = 1 << 6,
        StaleMessage = 1 << 7
    }

    public struct GASPredictionWindowData
    {
        public GASPredictionKey PredictionKey;
        public GASPredictionKey ParentPredictionKey;
        public GASSpecHandle SpecHandle;
        public int AbilitySpecHandle;
        public int OpenFrame;
        public int TimeoutFrame;
        public int PredictedEffectCount;
        public int PredictedAttributeSnapshotCount;
        public int PredictedGameplayCueCount;
        public int PredictedAbilityTaskCount;
        public GASPredictionWindowStatus Status;
        public int CloseFrame;
        public GASPredictionRollbackFlags RollbackFlags;

        public GASPredictionWindowData(
            GASPredictionKey predictionKey,
            GASPredictionKey parentPredictionKey,
            GASSpecHandle specHandle,
            int abilitySpecHandle,
            int openFrame,
            int timeoutFrame)
        {
            PredictionKey = predictionKey;
            ParentPredictionKey = parentPredictionKey;
            SpecHandle = specHandle;
            AbilitySpecHandle = abilitySpecHandle;
            OpenFrame = openFrame;
            TimeoutFrame = timeoutFrame;
            PredictedEffectCount = 0;
            PredictedAttributeSnapshotCount = 0;
            PredictedGameplayCueCount = 0;
            PredictedAbilityTaskCount = 0;
            Status = GASPredictionWindowStatus.Open;
            CloseFrame = 0;
            RollbackFlags = GASPredictionRollbackFlags.None;
        }
    }

    public readonly struct GASPredictionTransactionRecord
    {
        public readonly GASPredictionKey PredictionKey;
        public readonly GASPredictionKey ParentPredictionKey;
        public readonly GASSpecHandle SpecHandle;
        public readonly int AbilitySpecHandle;
        public readonly GASPredictionWindowStatus Status;
        public readonly GASPredictionRollbackFlags RollbackFlags;
        public readonly int OpenFrame;
        public readonly int CloseFrame;
        public readonly int TimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly int DurationFrames;

        public GASPredictionTransactionRecord(GASPredictionWindowData window, GASPredictionWindowStatus status, GASPredictionRollbackFlags rollbackFlags, int closeFrame)
        {
            PredictionKey = window.PredictionKey;
            ParentPredictionKey = window.ParentPredictionKey;
            SpecHandle = window.SpecHandle;
            AbilitySpecHandle = window.AbilitySpecHandle;
            Status = status;
            RollbackFlags = rollbackFlags;
            OpenFrame = window.OpenFrame;
            CloseFrame = closeFrame;
            TimeoutFrame = window.TimeoutFrame;
            PredictedEffectCount = window.PredictedEffectCount;
            PredictedAttributeSnapshotCount = window.PredictedAttributeSnapshotCount;
            PredictedGameplayCueCount = window.PredictedGameplayCueCount;
            PredictedAbilityTaskCount = window.PredictedAbilityTaskCount;
            DurationFrames = closeFrame > 0 && window.OpenFrame > 0 ? closeFrame - window.OpenFrame : 0;
        }
    }

    public readonly struct GASPredictionWindowStats
    {
        public readonly int OpenCount;
        public readonly int ParentLinkedCount;
        public readonly int ExpirableCount;
        public readonly int EarliestTimeoutFrame;
        public readonly int PredictedEffectCount;
        public readonly int PredictedAttributeSnapshotCount;
        public readonly int PredictedGameplayCueCount;
        public readonly int PredictedAbilityTaskCount;
        public readonly long TotalOpenedCount;
        public readonly long TotalConfirmedCount;
        public readonly long TotalRejectedCount;
        public readonly long TotalTimedOutCount;
        public readonly long StaleConfirmCount;
        public readonly long StaleRejectCount;
        public readonly int ClosedTransactionRecordCount;
        public readonly int ClosedTransactionRecordCapacity;

        public GASPredictionWindowStats(
            int openCount,
            int parentLinkedCount,
            int expirableCount,
            int earliestTimeoutFrame,
            int predictedEffectCount,
            int predictedAttributeSnapshotCount,
            int predictedGameplayCueCount,
            int predictedAbilityTaskCount,
            long totalOpenedCount,
            long totalConfirmedCount,
            long totalRejectedCount,
            long totalTimedOutCount,
            long staleConfirmCount,
            long staleRejectCount,
            int closedTransactionRecordCount,
            int closedTransactionRecordCapacity)
        {
            OpenCount = openCount;
            ParentLinkedCount = parentLinkedCount;
            ExpirableCount = expirableCount;
            EarliestTimeoutFrame = earliestTimeoutFrame;
            PredictedEffectCount = predictedEffectCount;
            PredictedAttributeSnapshotCount = predictedAttributeSnapshotCount;
            PredictedGameplayCueCount = predictedGameplayCueCount;
            PredictedAbilityTaskCount = predictedAbilityTaskCount;
            TotalOpenedCount = totalOpenedCount;
            TotalConfirmedCount = totalConfirmedCount;
            TotalRejectedCount = totalRejectedCount;
            TotalTimedOutCount = totalTimedOutCount;
            StaleConfirmCount = staleConfirmCount;
            StaleRejectCount = staleRejectCount;
            ClosedTransactionRecordCount = closedTransactionRecordCount;
            ClosedTransactionRecordCapacity = closedTransactionRecordCapacity;
        }
    }

    public readonly struct GASAbilitySpecData
    {
        public readonly GASSpecHandle Handle;
        public readonly GASDefinitionId AbilityDefinitionId;
        public readonly ushort Level;
        public readonly GASInstancingPolicy InstancingPolicy;
        public readonly GASNetExecutionPolicy NetExecutionPolicy;
        public readonly GASReplicationPolicy ReplicationPolicy;

        public GASAbilitySpecData(
            GASSpecHandle handle,
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            Handle = handle;
            AbilityDefinitionId = abilityDefinitionId;
            Level = level;
            InstancingPolicy = instancingPolicy;
            NetExecutionPolicy = netExecutionPolicy;
            ReplicationPolicy = replicationPolicy;
        }
    }

    public readonly struct GASAttributeValueData
    {
        public readonly GASAttributeId AttributeId;
        public readonly float BaseValue;
        public readonly float CurrentValue;
        public readonly uint AggregatorVersion;

        public GASAttributeValueData(GASAttributeId attributeId, float baseValue, float currentValue, uint aggregatorVersion)
        {
            AttributeId = attributeId;
            BaseValue = baseValue;
            CurrentValue = currentValue;
            AggregatorVersion = aggregatorVersion;
        }
    }

    public readonly struct GASModifierData
    {
        public readonly GASAttributeId AttributeId;
        public readonly GASModifierOp Op;
        public readonly float Magnitude;

        public GASModifierData(GASAttributeId attributeId, GASModifierOp op, float magnitude)
        {
            AttributeId = attributeId;
            Op = op;
            Magnitude = magnitude;
        }
    }

    public readonly struct GASAbilityGrantRequest
    {
        public readonly GASDefinitionId AbilityDefinitionId;
        public readonly ushort Level;
        public readonly GASInstancingPolicy InstancingPolicy;
        public readonly GASNetExecutionPolicy NetExecutionPolicy;
        public readonly GASReplicationPolicy ReplicationPolicy;

        public GASAbilityGrantRequest(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            AbilityDefinitionId = abilityDefinitionId;
            Level = level;
            InstancingPolicy = instancingPolicy;
            NetExecutionPolicy = netExecutionPolicy;
            ReplicationPolicy = replicationPolicy;
        }
    }

    public readonly struct GASGameplayEffectSpecData
    {
        public readonly GASDefinitionId EffectDefinitionId;
        public readonly GASEntityId Source;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASEffectDurationPolicy DurationPolicy;
        public readonly ushort Level;
        public readonly ushort StackCount;
        public readonly int StartTick;
        public readonly int DurationTicks;
        public readonly GASModifierData[] Modifiers;
        public readonly int ModifierStart;
        public readonly int ModifierCount;

        public GASGameplayEffectSpecData(
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            GASModifierData[] modifiers,
            int modifierStart,
            int modifierCount)
        {
            EffectDefinitionId = effectDefinitionId;
            Source = source;
            PredictionKey = predictionKey;
            DurationPolicy = durationPolicy;
            Level = level;
            StackCount = stackCount;
            StartTick = startTick;
            DurationTicks = durationTicks;
            Modifiers = modifiers;
            ModifierStart = modifierStart;
            ModifierCount = modifierCount;
        }
    }

    public readonly struct GASAbilityActivationResult
    {
        public readonly GASAbilityActivationResultCode Code;
        public readonly GASSpecHandle SpecHandle;
        public readonly GASPredictionKey PredictionKey;

        public bool Succeeded => Code == GASAbilityActivationResultCode.Accepted || Code == GASAbilityActivationResultCode.Predicted;

        public GASAbilityActivationResult(GASAbilityActivationResultCode code, GASSpecHandle specHandle, GASPredictionKey predictionKey)
        {
            Code = code;
            SpecHandle = specHandle;
            PredictionKey = predictionKey;
        }
    }

    internal readonly struct GASPredictedAttributeChange
    {
        public readonly GASPredictionKey PredictionKey;
        public readonly GASAttributeId AttributeId;
        public readonly float OldBaseValue;

        public GASPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId, float oldBaseValue)
        {
            PredictionKey = predictionKey;
            AttributeId = attributeId;
            OldBaseValue = oldBaseValue;
        }
    }

    public readonly struct GASActiveEffectData
    {
        public readonly GASActiveEffectHandle Handle;
        public readonly GASDefinitionId EffectDefinitionId;
        public readonly GASEntityId Source;
        public readonly GASEntityId Target;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASEffectDurationPolicy DurationPolicy;
        public readonly ushort Level;
        public readonly ushort StackCount;
        public readonly int StartTick;
        public readonly int DurationTicks;
        public readonly uint ModifierStartIndex;
        public readonly ushort ModifierCount;

        public GASActiveEffectData(
            GASActiveEffectHandle handle,
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASEntityId target,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            uint modifierStartIndex,
            ushort modifierCount)
        {
            Handle = handle;
            EffectDefinitionId = effectDefinitionId;
            Source = source;
            Target = target;
            PredictionKey = predictionKey;
            DurationPolicy = durationPolicy;
            Level = level;
            StackCount = stackCount;
            StartTick = startTick;
            DurationTicks = durationTicks;
            ModifierStartIndex = modifierStartIndex;
            ModifierCount = modifierCount;
        }
    }

    public readonly struct GASStateChecksum
    {
        public readonly uint Abilities;
        public readonly uint Attributes;
        public readonly uint Effects;
        public readonly uint Tags;

        public uint Combined
        {
            get
            {
                unchecked
                {
                    uint hash = 2166136261u;
                    hash = (hash ^ Abilities) * 16777619u;
                    hash = (hash ^ Attributes) * 16777619u;
                    hash = (hash ^ Effects) * 16777619u;
                    hash = (hash ^ Tags) * 16777619u;
                    return hash;
                }
            }
        }

        public GASStateChecksum(uint abilities, uint attributes, uint effects, uint tags)
        {
            Abilities = abilities;
            Attributes = attributes;
            Effects = effects;
            Tags = tags;
        }
    }

    public sealed class GASAbilitySystemStateBuffer
    {
        public GASEntityId Entity;
        public ulong Version;
        public GASStateChecksum Checksum;

        public GASAbilitySpecData[] AbilitySpecs = Array.Empty<GASAbilitySpecData>();
        public int AbilitySpecCount;

        public GASAttributeValueData[] Attributes = Array.Empty<GASAttributeValueData>();
        public int AttributeCount;

        public GASActiveEffectData[] ActiveEffects = Array.Empty<GASActiveEffectData>();
        public int ActiveEffectCount;

        public GASModifierData[] Modifiers = Array.Empty<GASModifierData>();
        public int ModifierCount;

        public void ClearCounts()
        {
            Entity = default;
            Version = 0;
            Checksum = default;
            AbilitySpecCount = 0;
            AttributeCount = 0;
            ActiveEffectCount = 0;
            ModifierCount = 0;
        }

        public GASAbilitySpecData[] EnsureAbilitySpecCapacity(int capacity)
        {
            if (AbilitySpecs.Length < capacity)
            {
                AbilitySpecs = new GASAbilitySpecData[capacity];
            }

            return AbilitySpecs;
        }

        public GASAttributeValueData[] EnsureAttributeCapacity(int capacity)
        {
            if (Attributes.Length < capacity)
            {
                Attributes = new GASAttributeValueData[capacity];
            }

            return Attributes;
        }

        public GASActiveEffectData[] EnsureActiveEffectCapacity(int capacity)
        {
            if (ActiveEffects.Length < capacity)
            {
                ActiveEffects = new GASActiveEffectData[capacity];
            }

            return ActiveEffects;
        }

        public GASModifierData[] EnsureModifierCapacity(int capacity)
        {
            if (Modifiers.Length < capacity)
            {
                Modifiers = new GASModifierData[capacity];
            }

            return Modifiers;
        }
    }

    public interface IGASDefinitionRegistry
    {
        GASDefinitionId RegisterAbilityDefinition(object abilityDefinition, string stableName, uint contentHash = 0);
        GASDefinitionId RegisterEffectDefinition(object effectDefinition, string stableName, uint contentHash = 0);
        bool TryGetAbilityDefinitionId(object abilityDefinition, out GASDefinitionId id);
        bool TryGetEffectDefinitionId(object effectDefinition, out GASDefinitionId id);
        object ResolveAbilityDefinition(GASDefinitionId id);
        object ResolveEffectDefinition(GASDefinitionId id);
        bool TryGetDefinitionVersion(GASDefinitionId id, out GASDefinitionVersion version);
    }

    public sealed class GASDefaultDefinitionRegistry : IGASDefinitionRegistry
    {
        public static readonly GASDefaultDefinitionRegistry Instance = new GASDefaultDefinitionRegistry();

        private readonly object syncRoot = new object();
        private readonly Dictionary<object, GASDefinitionVersion> byObject = new Dictionary<object, GASDefinitionVersion>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, object> abilityById = new Dictionary<int, object>();
        private readonly Dictionary<int, object> effectById = new Dictionary<int, object>();
        private readonly Dictionary<int, GASDefinitionVersion> versionById = new Dictionary<int, GASDefinitionVersion>();
        private int nextId = 1;

        private GASDefaultDefinitionRegistry()
        {
        }

        public GASDefinitionId RegisterAbilityDefinition(object abilityDefinition, string stableName, uint contentHash = 0)
        {
            return RegisterDefinition(abilityDefinition, stableName, contentHash, GASDefinitionKind.Ability, abilityById);
        }

        public GASDefinitionId RegisterEffectDefinition(object effectDefinition, string stableName, uint contentHash = 0)
        {
            return RegisterDefinition(effectDefinition, stableName, contentHash, GASDefinitionKind.Effect, effectById);
        }

        public bool TryGetAbilityDefinitionId(object abilityDefinition, out GASDefinitionId id)
        {
            return TryGetDefinitionId(abilityDefinition, GASDefinitionKind.Ability, out id);
        }

        public bool TryGetEffectDefinitionId(object effectDefinition, out GASDefinitionId id)
        {
            return TryGetDefinitionId(effectDefinition, GASDefinitionKind.Effect, out id);
        }

        public object ResolveAbilityDefinition(GASDefinitionId id)
        {
            lock (syncRoot)
            {
                abilityById.TryGetValue(id.Value, out var definition);
                return definition;
            }
        }

        public object ResolveEffectDefinition(GASDefinitionId id)
        {
            lock (syncRoot)
            {
                effectById.TryGetValue(id.Value, out var definition);
                return definition;
            }
        }

        public bool TryGetDefinitionVersion(GASDefinitionId id, out GASDefinitionVersion version)
        {
            lock (syncRoot)
            {
                return versionById.TryGetValue(id.Value, out version);
            }
        }

        private GASDefinitionId RegisterDefinition(object definition, string stableName, uint contentHash, GASDefinitionKind kind, Dictionary<int, object> typedLookup)
        {
            if (definition == null)
            {
                return default;
            }

            lock (syncRoot)
            {
                if (byObject.TryGetValue(definition, out var existing))
                {
                    return existing.Id;
                }

                uint hash = contentHash != 0 ? contentHash : ComputeStableHash(stableName);
                var id = new GASDefinitionId(nextId++);
                var version = new GASDefinitionVersion(kind, id, hash);
                byObject.Add(definition, version);
                typedLookup.Add(id.Value, definition);
                versionById.Add(id.Value, version);
                return id;
            }
        }

        private bool TryGetDefinitionId(object definition, GASDefinitionKind expectedKind, out GASDefinitionId id)
        {
            lock (syncRoot)
            {
                if (definition != null && byObject.TryGetValue(definition, out var version) && version.Kind == expectedKind)
                {
                    id = version.Id;
                    return true;
                }
            }

            id = default;
            return false;
        }

        private static uint ComputeStableHash(string stableName)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(stableName))
                {
                    for (int i = 0; i < stableName.Length; i++)
                    {
                        hash = (hash ^ stableName[i]) * 16777619u;
                    }
                }

                return hash != 0 ? hash : 2166136261u;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }

    public readonly struct GASAttributeDefinition
    {
        public readonly GASAttributeId Id;
        public readonly string StableName;
        public readonly uint ContentHash;

        public GASAttributeDefinition(GASAttributeId id, string stableName, uint contentHash)
        {
            Id = id;
            StableName = stableName;
            ContentHash = contentHash;
        }
    }

    public interface IGASAttributeRegistry
    {
        GASAttributeId RegisterAttribute(string stableName, uint contentHash = 0);
        bool TryGetAttributeId(string stableName, out GASAttributeId id);
        bool TryGetAttributeDefinition(GASAttributeId id, out GASAttributeDefinition definition);
    }

    public sealed class GASDefaultAttributeRegistry : IGASAttributeRegistry
    {
        public static readonly GASDefaultAttributeRegistry Instance = new GASDefaultAttributeRegistry();

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, GASAttributeDefinition> byName = new Dictionary<string, GASAttributeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<int, GASAttributeDefinition> byId = new Dictionary<int, GASAttributeDefinition>();
        private int nextId = 1;

        private GASDefaultAttributeRegistry()
        {
        }

        public GASAttributeId RegisterAttribute(string stableName, uint contentHash = 0)
        {
            if (string.IsNullOrEmpty(stableName))
            {
                return default;
            }

            lock (syncRoot)
            {
                if (byName.TryGetValue(stableName, out var existing))
                {
                    return existing.Id;
                }

                uint hash = contentHash != 0 ? contentHash : ComputeStableHash(stableName);
                var id = new GASAttributeId(nextId++);
                var definition = new GASAttributeDefinition(id, stableName, hash);
                byName.Add(stableName, definition);
                byId.Add(id.Value, definition);
                return id;
            }
        }

        public bool TryGetAttributeId(string stableName, out GASAttributeId id)
        {
            lock (syncRoot)
            {
                if (!string.IsNullOrEmpty(stableName) && byName.TryGetValue(stableName, out var definition))
                {
                    id = definition.Id;
                    return true;
                }
            }

            id = default;
            return false;
        }

        public bool TryGetAttributeDefinition(GASAttributeId id, out GASAttributeDefinition definition)
        {
            lock (syncRoot)
            {
                return byId.TryGetValue(id.Value, out definition);
            }
        }

        private static uint ComputeStableHash(string stableName)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < stableName.Length; i++)
                {
                    hash = (hash ^ stableName[i]) * 16777619u;
                }

                return hash != 0 ? hash : 2166136261u;
            }
        }
    }

    public interface IGASCoreNetworkDriver
    {
        bool IsServer { get; }
        bool IsOwner(GASEntityId entity);
        void SendAbilityActivationRequest(GASEntityId entity, GASSpecHandle specHandle, GASPredictionKey predictionKey);
        void SendAbilityActivationResult(GASEntityId entity, GASSpecHandle specHandle, GASPredictionKey predictionKey, bool accepted);
        void SendStateDelta(GASEntityId entity, in GASStateChecksum checksum);
    }

    /// <summary>
    /// Pure ASC state. This is the authoritative storage model that Runtime adapters should wrap.
    /// It intentionally stores ids, handles and compact value data instead of Unity object references.
    /// </summary>
    public sealed class GASAbilitySystemState
    {
        private GASAbilitySpecData[] abilitySpecs;
        private GASAttributeValueData[] attributes;
        private GASActiveEffectData[] activeEffects;
        private GASModifierData[] modifiers;
        private GASPredictedAttributeChange[] predictedAttributeChanges;

        private int abilitySpecCount;
        private int attributeCount;
        private int activeEffectCount;
        private int modifierCount;
        private int predictedAttributeChangeCount;
        private int nextSpecHandle = 1;
        private int nextEffectHandle = 1;

        public GASEntityId Entity { get; private set; }
        public ulong Version { get; private set; }
        public int AbilitySpecCount => abilitySpecCount;
        public int AttributeCount => attributeCount;
        public int ActiveEffectCount => activeEffectCount;
        public int ModifierCount => modifierCount;

        public GASAbilitySystemState(
            GASEntityId entity,
            int abilityCapacity = 16,
            int attributeCapacity = 32,
            int activeEffectCapacity = 32,
            int modifierCapacity = 128,
            int predictionCapacity = 32)
        {
            Entity = entity;
            abilitySpecs = new GASAbilitySpecData[Math.Max(1, abilityCapacity)];
            attributes = new GASAttributeValueData[Math.Max(1, attributeCapacity)];
            activeEffects = new GASActiveEffectData[Math.Max(1, activeEffectCapacity)];
            modifiers = new GASModifierData[Math.Max(1, modifierCapacity)];
            predictedAttributeChanges = new GASPredictedAttributeChange[Math.Max(1, predictionCapacity)];
        }

        public void Reset(GASEntityId entity)
        {
            Entity = entity;
            abilitySpecCount = 0;
            attributeCount = 0;
            activeEffectCount = 0;
            modifierCount = 0;
            predictedAttributeChangeCount = 0;
            nextSpecHandle = 1;
            nextEffectHandle = 1;
            Version++;
        }

        public void Reserve(
            int abilityCapacity,
            int attributeCapacity,
            int activeEffectCapacity,
            int modifierCapacity,
            int predictionCapacity)
        {
            if (abilityCapacity > 0)
            {
                EnsureAbilityCapacity(abilityCapacity);
            }

            if (attributeCapacity > 0)
            {
                EnsureAttributeCapacity(attributeCapacity);
            }

            if (activeEffectCapacity > 0)
            {
                EnsureActiveEffectCapacity(activeEffectCapacity);
            }

            if (modifierCapacity > 0)
            {
                EnsureModifierCapacity(modifierCapacity);
            }

            if (predictionCapacity > 0)
            {
                EnsurePredictionCapacity(predictionCapacity);
            }
        }

        public bool TryGetAbilitySpecByIndex(int index, out GASAbilitySpecData spec)
        {
            if ((uint)index >= (uint)abilitySpecCount)
            {
                spec = default;
                return false;
            }

            spec = abilitySpecs[index];
            return true;
        }

        public bool TryGetAttributeByIndex(int index, out GASAttributeValueData attribute)
        {
            if ((uint)index >= (uint)attributeCount)
            {
                attribute = default;
                return false;
            }

            attribute = attributes[index];
            return true;
        }

        public bool TryGetActiveEffectByIndex(int index, out GASActiveEffectData effect)
        {
            if ((uint)index >= (uint)activeEffectCount)
            {
                effect = default;
                return false;
            }

            effect = activeEffects[index];
            return true;
        }

        public bool TryGetModifierByIndex(int index, out GASModifierData modifier)
        {
            if ((uint)index >= (uint)modifierCount)
            {
                modifier = default;
                return false;
            }

            modifier = modifiers[index];
            return true;
        }

        public void CaptureStateNonAlloc(GASAbilitySystemStateBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.ClearCounts();
            buffer.Entity = Entity;
            buffer.Version = Version;
            buffer.Checksum = ComputeChecksum();

            if (abilitySpecCount > 0)
            {
                Array.Copy(abilitySpecs, 0, buffer.EnsureAbilitySpecCapacity(abilitySpecCount), 0, abilitySpecCount);
                buffer.AbilitySpecCount = abilitySpecCount;
            }

            if (attributeCount > 0)
            {
                Array.Copy(attributes, 0, buffer.EnsureAttributeCapacity(attributeCount), 0, attributeCount);
                buffer.AttributeCount = attributeCount;
            }

            if (activeEffectCount > 0)
            {
                Array.Copy(activeEffects, 0, buffer.EnsureActiveEffectCapacity(activeEffectCount), 0, activeEffectCount);
                buffer.ActiveEffectCount = activeEffectCount;
            }

            if (modifierCount > 0)
            {
                Array.Copy(modifiers, 0, buffer.EnsureModifierCapacity(modifierCount), 0, modifierCount);
                buffer.ModifierCount = modifierCount;
            }
        }

        public bool TryGetAbilitySpec(GASSpecHandle handle, out GASAbilitySpecData spec)
        {
            int index = FindAbilitySpecIndex(handle);
            if (index < 0)
            {
                spec = default;
                return false;
            }

            spec = abilitySpecs[index];
            return true;
        }

        public bool TryGetAttribute(GASAttributeId attributeId, out GASAttributeValueData attribute)
        {
            int index = FindAttributeIndex(attributeId);
            if (index < 0)
            {
                attribute = default;
                return false;
            }

            attribute = attributes[index];
            return true;
        }

        public GASSpecHandle GrantAbility(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            if (!abilityDefinitionId.IsValid)
            {
                return default;
            }

            EnsureAbilityCapacity(abilitySpecCount + 1);
            var handle = new GASSpecHandle(nextSpecHandle++);
            abilitySpecs[abilitySpecCount++] = new GASAbilitySpecData(
                handle,
                abilityDefinitionId,
                level,
                instancingPolicy,
                netExecutionPolicy,
                replicationPolicy);
            Version++;
            return handle;
        }

        public bool TryGrantAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            handle = GrantAbility(
                request.AbilityDefinitionId,
                request.Level,
                request.InstancingPolicy,
                request.NetExecutionPolicy,
                request.ReplicationPolicy);
            return handle.IsValid;
        }

        public bool RemoveAbility(GASSpecHandle handle)
        {
            int index = FindAbilitySpecIndex(handle);
            if (index < 0)
            {
                return false;
            }

            int lastIndex = abilitySpecCount - 1;
            if (index != lastIndex)
            {
                abilitySpecs[index] = abilitySpecs[lastIndex];
            }

            abilitySpecs[lastIndex] = default;
            abilitySpecCount--;
            Version++;
            return true;
        }

        public void SetAttributeBase(GASAttributeId attributeId, float baseValue)
        {
            int index = EnsureAttribute(attributeId);
            attributes[index] = new GASAttributeValueData(
                attributeId,
                baseValue,
                EvaluateCurrentValue(attributeId, baseValue),
                attributes[index].AggregatorVersion + 1u);
            Version++;
        }

        public bool ApplyInstantModifier(GASModifierData modifier)
        {
            return ApplyInstantModifier(modifier, default);
        }

        public bool ApplyInstantModifier(GASModifierData modifier, GASPredictionKey predictionKey)
        {
            int index = EnsureAttribute(modifier.AttributeId);
            var attribute = attributes[index];
            if (predictionKey.IsValid)
            {
                RecordPredictedAttributeChange(predictionKey, modifier.AttributeId, attribute.BaseValue);
            }

            float baseValue = ApplyModifierToBase(attribute.BaseValue, modifier);
            attributes[index] = new GASAttributeValueData(
                modifier.AttributeId,
                baseValue,
                EvaluateCurrentValue(modifier.AttributeId, baseValue),
                attribute.AggregatorVersion + 1u);
            Version++;
            return true;
        }

        public GASActiveEffectHandle AddActiveEffect(
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            GASModifierData[] effectModifiers,
            int effectModifierStart,
            int effectModifierCount)
        {
            if (!effectDefinitionId.IsValid)
            {
                return default;
            }

            if (effectModifierCount < 0 || effectModifierStart < 0 || effectModifiers == null && effectModifierCount > 0)
            {
                return default;
            }

            if (effectModifiers != null && effectModifierStart + effectModifierCount > effectModifiers.Length)
            {
                return default;
            }

            EnsureActiveEffectCapacity(activeEffectCount + 1);
            EnsureModifierCapacity(modifierCount + effectModifierCount);

            int modifierStart = modifierCount;
            for (int i = 0; i < effectModifierCount; i++)
            {
                modifiers[modifierCount++] = effectModifiers[effectModifierStart + i];
            }

            var handle = new GASActiveEffectHandle(nextEffectHandle++);
            activeEffects[activeEffectCount++] = new GASActiveEffectData(
                handle,
                effectDefinitionId,
                source,
                Entity,

                predictionKey,
                durationPolicy,
                level,
                stackCount == 0 ? (ushort)1 : stackCount,
                startTick,
                durationTicks,
                (uint)modifierStart,
                (ushort)effectModifierCount);

            RecalculateModifiedAttributes(modifierStart, effectModifierCount);
            Version++;
            return handle;
        }

        public GASActiveEffectHandle ApplyGameplayEffectSpecToSelf(in GASGameplayEffectSpecData spec)
        {
            if (spec.DurationPolicy == GASEffectDurationPolicy.Instant)
            {
                if (spec.Modifiers == null && spec.ModifierCount > 0)
                {
                    return default;
                }

                if (spec.ModifierStart < 0 || spec.ModifierCount < 0)
                {
                    return default;
                }

                if (spec.Modifiers != null && spec.ModifierStart + spec.ModifierCount > spec.Modifiers.Length)
                {
                    return default;
                }

                for (int i = 0; i < spec.ModifierCount; i++)
                {
                    ApplyInstantModifier(spec.Modifiers[spec.ModifierStart + i], spec.PredictionKey);
                }

                return default;
            }

            return AddActiveEffect(
                spec.EffectDefinitionId,
                spec.Source,
                spec.PredictionKey,
                spec.DurationPolicy,
                spec.Level,
                spec.StackCount,
                spec.StartTick,
                spec.DurationTicks,
                spec.Modifiers,
                spec.ModifierStart,
                spec.ModifierCount);
        }

        public bool RemoveActiveEffect(GASActiveEffectHandle handle)
        {
            int index = FindActiveEffectIndex(handle);
            if (index < 0)
            {
                return false;
            }

            var removed = activeEffects[index];
            RemoveModifierRange((int)removed.ModifierStartIndex, removed.ModifierCount);
            int lastIndex = activeEffectCount - 1;
            if (index != lastIndex)
            {
                activeEffects[index] = activeEffects[lastIndex];
            }

            activeEffects[lastIndex] = default;
            activeEffectCount--;
            RecalculateAllAttributes();
            Version++;
            return true;
        }

        public int RemoveExpiredEffects(int currentTick)
        {
            int removedCount = 0;
            for (int i = activeEffectCount - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.DurationPolicy != GASEffectDurationPolicy.Duration)
                {
                    continue;
                }

                if (currentTick - effect.StartTick >= effect.DurationTicks)
                {
                    RemoveActiveEffect(effect.Handle);
                    removedCount++;
                }
            }

            return removedCount;
        }

        public void AcceptPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            RemovePredictedAttributeChanges(predictionKey, restore: false);
        }

        public void RejectPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            for (int i = activeEffectCount - 1; i >= 0; i--)
            {
                if (activeEffects[i].PredictionKey.Equals(predictionKey))
                {
                    RemoveActiveEffect(activeEffects[i].Handle);
                }
            }

            RemovePredictedAttributeChanges(predictionKey, restore: true);
            Version++;
        }

        public GASStateChecksum ComputeChecksum()
        {
            uint abilities = 2166136261u;
            for (int i = 0; i < abilitySpecCount; i++)
            {
                var spec = abilitySpecs[i];
                abilities = HashInt(abilities, spec.Handle.Value);
                abilities = HashInt(abilities, spec.AbilityDefinitionId.Value);
                abilities = HashInt(abilities, spec.Level);
                abilities = HashInt(abilities, (int)spec.InstancingPolicy);
                abilities = HashInt(abilities, (int)spec.NetExecutionPolicy);
            }

            uint attrs = 2166136261u;
            for (int i = 0; i < attributeCount; i++)
            {
                var attr = attributes[i];
                attrs = HashInt(attrs, attr.AttributeId.Value);
                attrs = HashFloat(attrs, attr.BaseValue);
                attrs = HashFloat(attrs, attr.CurrentValue);
                attrs = HashInt(attrs, (int)attr.AggregatorVersion);
            }

            uint effects = 2166136261u;
            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                effects = HashInt(effects, effect.Handle.Value);
                effects = HashInt(effects, effect.EffectDefinitionId.Value);
                effects = HashInt(effects, effect.Source.Value);
                effects = HashInt(effects, effect.Target.Value);
                effects = HashInt(effects, effect.PredictionKey.Value);
                effects = HashInt(effects, effect.Level);
                effects = HashInt(effects, effect.StackCount);
                effects = HashInt(effects, effect.StartTick);
                effects = HashInt(effects, effect.DurationTicks);
            }

            return new GASStateChecksum(abilities, attrs, effects, 2166136261u);
        }

        private int EnsureAttribute(GASAttributeId attributeId)
        {
            int index = FindAttributeIndex(attributeId);
            if (index >= 0)
            {
                return index;
            }

            EnsureAttributeCapacity(attributeCount + 1);
            attributes[attributeCount] = new GASAttributeValueData(attributeId, 0f, 0f, 1u);
            return attributeCount++;
        }

        private void RecordPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId, float oldBaseValue)
        {
            for (int i = 0; i < predictedAttributeChangeCount; i++)
            {
                var change = predictedAttributeChanges[i];
                if (change.PredictionKey.Equals(predictionKey) && change.AttributeId == attributeId)
                {
                    return;
                }
            }

            EnsurePredictionCapacity(predictedAttributeChangeCount + 1);
            predictedAttributeChanges[predictedAttributeChangeCount++] = new GASPredictedAttributeChange(predictionKey, attributeId, oldBaseValue);
        }

        private void RemovePredictedAttributeChanges(GASPredictionKey predictionKey, bool restore)
        {
            for (int i = predictedAttributeChangeCount - 1; i >= 0; i--)
            {
                var change = predictedAttributeChanges[i];
                if (!change.PredictionKey.Equals(predictionKey))
                {
                    continue;
                }

                if (restore)
                {
                    int attrIndex = EnsureAttribute(change.AttributeId);
                    attributes[attrIndex] = new GASAttributeValueData(
                        change.AttributeId,
                        change.OldBaseValue,
                        EvaluateCurrentValue(change.AttributeId, change.OldBaseValue),
                        attributes[attrIndex].AggregatorVersion + 1u);
                }

                int lastIndex = predictedAttributeChangeCount - 1;
                if (i != lastIndex)
                {
                    predictedAttributeChanges[i] = predictedAttributeChanges[lastIndex];
                }

                predictedAttributeChanges[lastIndex] = default;
                predictedAttributeChangeCount--;
            }
        }

        private float EvaluateCurrentValue(GASAttributeId attributeId, float baseValue)
        {
            float add = 0f;
            float multiply = 1f;
            float overrideValue = 0f;
            bool hasOverride = false;

            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                int start = (int)effect.ModifierStartIndex;
                int end = start + effect.ModifierCount;
                for (int m = start; m < end; m++)
                {
                    var modifier = modifiers[m];
                    if (modifier.AttributeId != attributeId)
                    {
                        continue;
                    }

                    float magnitude = modifier.Magnitude * effect.StackCount;
                    switch (modifier.Op)
                    {
                        case GASModifierOp.Add:
                            add += magnitude;
                            break;
                        case GASModifierOp.Multiply:
                            multiply *= modifier.Magnitude;
                            break;
                        case GASModifierOp.Division:
                            if (Math.Abs(modifier.Magnitude) > float.Epsilon)
                            {
                                multiply /= modifier.Magnitude;
                            }
                            break;
                        case GASModifierOp.Override:
                            hasOverride = true;
                            overrideValue = modifier.Magnitude;
                            break;
                    }
                }
            }

            return hasOverride ? overrideValue : (baseValue + add) * multiply;
        }

        private static float ApplyModifierToBase(float baseValue, GASModifierData modifier)
        {
            switch (modifier.Op)
            {
                case GASModifierOp.Add:
                    return baseValue + modifier.Magnitude;
                case GASModifierOp.Multiply:
                    return baseValue * modifier.Magnitude;
                case GASModifierOp.Division:
                    return Math.Abs(modifier.Magnitude) > float.Epsilon ? baseValue / modifier.Magnitude : baseValue;
                case GASModifierOp.Override:
                    return modifier.Magnitude;
                default:
                    return baseValue;
            }
        }

        private void RecalculateModifiedAttributes(int modifierStart, int modifierLength)
        {
            int end = modifierStart + modifierLength;
            for (int i = modifierStart; i < end; i++)
            {
                RecalculateAttribute(modifiers[i].AttributeId);
            }
        }

        private void RecalculateAllAttributes()
        {
            for (int i = 0; i < attributeCount; i++)
            {
                RecalculateAttribute(attributes[i].AttributeId);
            }
        }

        private void RecalculateAttribute(GASAttributeId attributeId)
        {
            int index = FindAttributeIndex(attributeId);
            if (index < 0)
            {
                return;
            }

            var attribute = attributes[index];
            attributes[index] = new GASAttributeValueData(
                attribute.AttributeId,
                attribute.BaseValue,
                EvaluateCurrentValue(attribute.AttributeId, attribute.BaseValue),
                attribute.AggregatorVersion + 1u);
        }

        private void RemoveModifierRange(int start, int length)
        {
            if (length <= 0)
            {
                return;
            }

            int end = start + length;
            int tailLength = modifierCount - end;
            if (tailLength > 0)
            {
                Array.Copy(modifiers, end, modifiers, start, tailLength);
            }

            for (int i = modifierCount - length; i < modifierCount; i++)
            {
                modifiers[i] = default;
            }

            modifierCount -= length;
            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                if (effect.ModifierStartIndex > start)
                {
                    activeEffects[i] = new GASActiveEffectData(
                        effect.Handle,
                        effect.EffectDefinitionId,
                        effect.Source,
                        effect.Target,
                        effect.PredictionKey,
                        effect.DurationPolicy,
                        effect.Level,
                        effect.StackCount,
                        effect.StartTick,
                        effect.DurationTicks,
                        effect.ModifierStartIndex - (uint)length,
                        effect.ModifierCount);
                }
            }
        }

        private int FindAbilitySpecIndex(GASSpecHandle handle)
        {
            for (int i = 0; i < abilitySpecCount; i++)
            {
                if (abilitySpecs[i].Handle == handle)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindAttributeIndex(GASAttributeId attributeId)
        {
            for (int i = 0; i < attributeCount; i++)
            {
                if (attributes[i].AttributeId == attributeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindActiveEffectIndex(GASActiveEffectHandle handle)
        {
            for (int i = 0; i < activeEffectCount; i++)
            {
                if (activeEffects[i].Handle == handle)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureAbilityCapacity(int capacity)
        {
            if (abilitySpecs.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref abilitySpecs, abilitySpecs.Length * 2 >= capacity ? abilitySpecs.Length * 2 : capacity);
        }

        private void EnsureAttributeCapacity(int capacity)
        {
            if (attributes.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref attributes, attributes.Length * 2 >= capacity ? attributes.Length * 2 : capacity);
        }

        private void EnsureActiveEffectCapacity(int capacity)
        {
            if (activeEffects.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref activeEffects, activeEffects.Length * 2 >= capacity ? activeEffects.Length * 2 : capacity);
        }

        private void EnsureModifierCapacity(int capacity)
        {
            if (modifiers.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref modifiers, modifiers.Length * 2 >= capacity ? modifiers.Length * 2 : capacity);
        }

        private void EnsurePredictionCapacity(int capacity)
        {
            if (predictedAttributeChanges.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref predictedAttributeChanges, predictedAttributeChanges.Length * 2 >= capacity ? predictedAttributeChanges.Length * 2 : capacity);
        }

        private static uint HashInt(uint hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint)value) * 16777619u;
            }
        }

        private static uint HashFloat(uint hash, float value)
        {
            return HashInt(hash, BitConverter.SingleToInt32Bits(value));
        }
    }

    /// <summary>
    /// UE GAS-style facade over the core state container.
    /// Runtime adapters should expose familiar ASC methods while delegating state mutation here.
    /// </summary>
    public sealed class GASAbilitySystemFacade
    {
        private readonly GASAbilitySystemState state;
        private readonly IGASCoreNetworkDriver network;

        public GASAbilitySystemState State => state;
        public GASEntityId Entity => state.Entity;

        public GASAbilitySystemFacade(GASAbilitySystemState state, IGASCoreNetworkDriver network = null)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.network = network;
        }

        public GASSpecHandle GiveAbility(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy = GASInstancingPolicy.InstancedPerActor,
            GASNetExecutionPolicy netExecutionPolicy = GASNetExecutionPolicy.LocalPredicted,
            GASReplicationPolicy replicationPolicy = GASReplicationPolicy.OwnerOnly)
        {
            return state.GrantAbility(
                abilityDefinitionId,
                level,
                instancingPolicy,
                netExecutionPolicy,
                replicationPolicy);
        }

        public bool GiveAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            return state.TryGrantAbility(in request, out handle);
        }

        public bool ClearAbility(GASSpecHandle handle)
        {
            return state.RemoveAbility(handle);
        }

        public GASAbilityActivationResult TryActivateAbility(GASSpecHandle handle, GASPredictionKey predictionKey)
        {
            if (!state.TryGetAbilitySpec(handle, out var spec))
            {
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, handle, predictionKey);
            }

            if (spec.NetExecutionPolicy == GASNetExecutionPolicy.LocalPredicted)
            {
                if (!predictionKey.IsValid)
                {
                    return new GASAbilityActivationResult(GASAbilityActivationResultCode.InvalidPredictionKey, handle, predictionKey);
                }

                if (network != null && !network.IsServer && network.IsOwner(Entity))
                {
                    network.SendAbilityActivationRequest(Entity, handle, predictionKey);
                    return new GASAbilityActivationResult(GASAbilityActivationResultCode.Predicted, handle, predictionKey);
                }
            }

            if (spec.NetExecutionPolicy == GASNetExecutionPolicy.ServerOnly && network != null && !network.IsServer)
            {
                network.SendAbilityActivationRequest(Entity, handle, predictionKey);
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.Predicted, handle, predictionKey);
            }

            return new GASAbilityActivationResult(GASAbilityActivationResultCode.Accepted, handle, predictionKey);
        }

        public GASAbilityActivationResult ServerReceiveTryActivateAbility(GASSpecHandle handle, GASPredictionKey predictionKey)
        {
            var result = state.TryGetAbilitySpec(handle, out _)
                ? new GASAbilityActivationResult(GASAbilityActivationResultCode.Accepted, handle, predictionKey)
                : new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, handle, predictionKey);

            network?.SendAbilityActivationResult(Entity, handle, predictionKey, result.Succeeded);
            return result;
        }

        public GASActiveEffectHandle ApplyGameplayEffectSpecToSelf(in GASGameplayEffectSpecData spec)
        {
            var handle = state.ApplyGameplayEffectSpecToSelf(in spec);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
            return handle;
        }

        public bool RemoveActiveGameplayEffect(GASActiveEffectHandle handle)
        {
            bool removed = state.RemoveActiveEffect(handle);
            if (removed)
            {
                if (network != null)
                {
                    var checksum = state.ComputeChecksum();
                    network.SendStateDelta(Entity, in checksum);
                }
            }

            return removed;
        }

        public void AcceptPrediction(GASPredictionKey predictionKey)
        {
            state.AcceptPrediction(predictionKey);
        }

        public void RejectPrediction(GASPredictionKey predictionKey)
        {
            state.RejectPrediction(predictionKey);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
        }

        public void SetNumericAttributeBase(GASAttributeId attributeId, float value)
        {
            state.SetAttributeBase(attributeId, value);
            if (network != null)
            {
                var checksum = state.ComputeChecksum();
                network.SendStateDelta(Entity, in checksum);
            }
        }

        public bool GetGameplayAttributeValue(GASAttributeId attributeId, out float currentValue)
        {
            if (state.TryGetAttribute(attributeId, out var attribute))
            {
                currentValue = attribute.CurrentValue;
                return true;
            }

            currentValue = default;
            return false;
        }
    }

    #endregion
}
