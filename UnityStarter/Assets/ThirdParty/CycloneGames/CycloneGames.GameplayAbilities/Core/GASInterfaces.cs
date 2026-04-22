using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;

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
        ///  Generic overload — avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
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
        ///  Generic overload — avoids struct enumerator boxing when T is a concrete IGameplayTagContainer.
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
        PredictionKey PredictionKey { get; set; }
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

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object effectContext,
            object sourceObject,
            object targetObject,
            int effectLevel,
            float effectDuration)
        {
            Source = source;
            Target = target;
            EffectDefinition = effectDefinition;
            EffectContext = effectContext;
            SourceObject = sourceObject;
            TargetObject = targetObject;
            EffectLevel = effectLevel;
            EffectDuration = effectDuration;
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
    /// All value types — safe to copy across network message boundaries.
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
        public PredictionKey PredictionKey;
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

        public GASCueNetParams(int sourceAscNetId, int targetAscNetId, float magnitude, float normalizedMagnitude)
        {
            SourceAscNetId = sourceAscNetId;
            TargetAscNetId = targetAscNetId;
            Magnitude = magnitude;
            NormalizedMagnitude = normalizedMagnitude;
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
    /// Implement this interface with your chosen networking library (Netcode for GameObjects, Mirror,
    /// Photon Fusion, Fish-Net, etc.) and register it via <see cref="GASServices.NetworkBridge"/>.
    /// 
    /// The default implementation (<see cref="GASNullNetworkBridge"/>) routes all calls
    /// locally — safe for single-player and listen-server topologies.
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
    ///     public void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, PredictionKey key)
    ///         => ActivateAbilityServerRpc(GetNetId(asc), specHandle, key.Key);
    ///
    ///     [ServerRpc] private void ActivateAbilityServerRpc(ulong netId, int specHandle, int keyValue)
    ///     {
    ///         var asc = FindAscByNetId(netId);
    ///         asc?.ServerReceiveTryActivateAbility(specHandle, new PredictionKey(keyValue));
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

        // ---- Client → Server ----

        /// <summary>
        /// Called by the client when a LocalPredicted ability activates.
        /// Implementations should send an RPC to the server, which will call
        /// <see cref="IGASNetworkTarget.ServerReceiveTryActivateAbility"/> on the server-side ASC.
        /// </summary>
        void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, PredictionKey predictionKey);

        // ---- Server → Client ----

        /// <summary>
        /// Called by the server to confirm a client's predicted activation.
        /// Implementations should send an RPC to the owning client, which will call
        /// <see cref="IGASNetworkTarget.ClientReceiveActivationSucceeded"/>.
        /// </summary>
        void ServerConfirmActivation(IGASNetworkTarget targetAsc, int specHandle, PredictionKey predictionKey);

        /// <summary>
        /// Called by the server to reject a client's predicted activation.
        /// Implementations should send an RPC to the owning client, which will call
        /// <see cref="IGASNetworkTarget.ClientReceiveActivationFailed"/>.
        /// </summary>
        void ServerRejectActivation(IGASNetworkTarget targetAsc, int specHandle, PredictionKey predictionKey);

        // ---- Effect Replication (Server → All Relevant Clients) ----

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

        // ---- GameplayCue Replication (Server → All Clients) ----

        /// <summary>
        /// Called on the server when a GameplayCue fires.
        /// Implementations broadcast this to all relevant clients.
        /// The receiving client should trigger its local <see cref="IGameplayCueManager"/>.
        /// </summary>
        void ServerBroadcastGameplayCue(IGASNetworkTarget sourceAsc, GameplayTag cueTag,
            EGameplayCueEvent eventType, in GASCueNetParams cueParams);

        // ---- Attribute Replication (Server → Relevant Clients) ----

        /// <summary>
        /// Called on the server after one or more attributes changed due to effect execution or
        /// base-value modification during a tick. Delivers a delta snapshot of only the affected attributes.
        /// The receiving client should call <see cref="IGASNetworkTarget.ClientReceiveAttributeSnapshot"/>.
        /// </summary>
        void ServerReplicateAttributeSnapshot(IGASNetworkTarget targetAsc, GameplayAttributeStateSnapshot[] snapshot);

        // ---- Full-State Resync (Server → Single Client) ----

        /// <summary>
        /// Forces a complete state resync to a single client.
        /// Used for reconnect, late-join, and server-authoritative cheat correction.
        /// The client receiving this should call <see cref="IGASNetworkTarget.ClientReceiveFullSync"/>.
        /// </summary>
        void ServerForceFullSync(IGASNetworkTarget targetAsc, in AbilitySystemStateSnapshot snapshot);

        /// <summary>
        /// Sends a pending delta snapshot to a single client.
        /// The client receiving this should call <see cref="IGASNetworkTarget.ClientReceiveDeltaSnapshot"/>.
        /// </summary>
        void ServerSendDeltaSnapshot(IGASNetworkTarget targetAsc, in AbilitySystemStateDeltaSnapshot delta);
    }

    /// <summary>
    /// Exposes the server→client and client-receive entry points on an AbilitySystemComponent.
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
        void ServerReceiveTryActivateAbility(int specHandle, PredictionKey predictionKey);

        /// <summary>
        /// Client entry point: called when the client receives server confirmation of a predicted activation.
        /// </summary>
        void ClientReceiveActivationSucceeded(int specHandle, PredictionKey predictionKey);

        /// <summary>
        /// Client entry point: called when the client receives server rejection of a predicted activation.
        /// </summary>
        void ClientReceiveActivationFailed(int specHandle, PredictionKey predictionKey);

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
        /// Client entry point: server replicated a delta snapshot of changed attribute values.
        /// </summary>
        void ClientReceiveAttributeSnapshot(GameplayAttributeStateSnapshot[] snapshot);

        /// <summary>
        /// Client entry point: server forced a full ASC state resync (reconnect, late-join, cheat rollback).
        /// Replaces all local state with the authoritative snapshot.
        /// </summary>
        void ClientReceiveFullSync(in AbilitySystemStateSnapshot snapshot);

        /// <summary>
        /// Client entry point: server sent an incremental delta snapshot.
        /// Only sections present in <see cref="AbilitySystemStateDeltaSnapshot.ChangeMask"/> are applied.
        /// </summary>
        void ClientReceiveDeltaSnapshot(in AbilitySystemStateDeltaSnapshot delta);
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
        public void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, PredictionKey predictionKey)
        {
            asc.ServerReceiveTryActivateAbility(specHandle, predictionKey);
        }

        /// <summary>Directly calls the client receive method on the same ASC instance.</summary>
        public void ServerConfirmActivation(IGASNetworkTarget targetAsc, int specHandle, PredictionKey predictionKey)
        {
            targetAsc.ClientReceiveActivationSucceeded(specHandle, predictionKey);
        }

        /// <summary>Directly calls the client receive method on the same ASC instance.</summary>
        public void ServerRejectActivation(IGASNetworkTarget targetAsc, int specHandle, PredictionKey predictionKey)
        {
            targetAsc.ClientReceiveActivationFailed(specHandle, predictionKey);
        }

        // Effect application is already local — no replication needed.
        public void ServerReplicateEffectApplied(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectUpdated(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectRemoved(IGASNetworkTarget targetAsc, int effectNetId) { }

        // Cues are already dispatched locally in DispatchGameplayCues — no broadcast needed.
        public void ServerBroadcastGameplayCue(IGASNetworkTarget sourceAsc, GameplayTag cueTag,
            EGameplayCueEvent eventType, in GASCueNetParams cueParams) { }

        // Attributes already updated locally on the single process — no replication needed.
        public void ServerReplicateAttributeSnapshot(IGASNetworkTarget targetAsc, GameplayAttributeStateSnapshot[] snapshot) { }

        // No separate client in local mode.
        public void ServerForceFullSync(IGASNetworkTarget targetAsc, in AbilitySystemStateSnapshot snapshot) { }
        public void ServerSendDeltaSnapshot(IGASNetworkTarget targetAsc, in AbilitySystemStateDeltaSnapshot delta) { }
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

    #region Prediction Key

    /// <summary>
    /// Represents a unique key for client-side prediction events.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    public struct PredictionKey : IEquatable<PredictionKey>
    {
        public int Key { get; private set; }
        private static int s_NextKey = 1;

        public bool IsValid() => Key != 0;

        public static PredictionKey NewKey()
        {
            int key = System.Threading.Interlocked.Increment(ref s_NextKey);
            if (key >= int.MaxValue - 1)
            {
                System.Threading.Interlocked.Exchange(ref s_NextKey, 1);
            }
            return new PredictionKey { Key = key };
        }

        public bool Equals(PredictionKey other) => Key == other.Key;
        public override bool Equals(object obj) => obj is PredictionKey other && Equals(other);
        public override int GetHashCode() => Key;
        public static bool operator ==(PredictionKey left, PredictionKey right) => left.Equals(right);
        public static bool operator !=(PredictionKey left, PredictionKey right) => !left.Equals(right);
    }

    #endregion

    #region State Snapshots

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
    public readonly struct GrantedAbilityStateSnapshot
    {
        public readonly IGASAbilityDefinition AbilityDefinition;
        public readonly int Level;
        public readonly bool IsActive;

        public GrantedAbilityStateSnapshot(IGASAbilityDefinition abilityDefinition, int level, bool isActive)
        {
            AbilityDefinition = abilityDefinition;
            Level = level;
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Pure C# snapshot of a single SetByCaller magnitude addressed by GameplayTag.
    /// </summary>
    public readonly struct SetByCallerTagStateSnapshot
    {
        public readonly GameplayTag Tag;
        public readonly float Value;

        public SetByCallerTagStateSnapshot(GameplayTag tag, float value)
        {
            Tag = tag;
            Value = value;
        }
    }

    /// <summary>
    /// Pure C# snapshot of an active gameplay effect.
    /// </summary>
    public readonly struct ActiveGameplayEffectStateSnapshot
    {
        public readonly int InstanceId;
        public readonly object EffectDefinition;
        public readonly object SourceComponent;
        public readonly int Level;
        public readonly int StackCount;
        public readonly float Duration;
        public readonly float TimeRemaining;
        public readonly float PeriodTimeRemaining;
        public readonly PredictionKey PredictionKey;
        public readonly SetByCallerTagStateSnapshot[] SetByCallerTagMagnitudes;

        public ActiveGameplayEffectStateSnapshot(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            PredictionKey predictionKey,
            SetByCallerTagStateSnapshot[] setByCallerTagMagnitudes)
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
        }
    }

    /// <summary>
    /// Pure C# snapshot of an attribute value pair.
    /// </summary>
    public readonly struct GameplayAttributeStateSnapshot
    {
        public readonly string AttributeName;
        public readonly float BaseValue;
        public readonly float CurrentValue;

        public GameplayAttributeStateSnapshot(string attributeName, float baseValue, float currentValue)
        {
            AttributeName = attributeName;
            BaseValue = baseValue;
            CurrentValue = currentValue;
        }
    }

    /// <summary>
    /// Pure C# full-state snapshot of an ASC.
    /// Suitable for networking, replay, testing, and engine adapters.
    /// </summary>
    public readonly struct AbilitySystemStateSnapshot
    {
        public readonly GrantedAbilityStateSnapshot[] GrantedAbilities;
        public readonly ActiveGameplayEffectStateSnapshot[] ActiveEffects;
        public readonly GameplayAttributeStateSnapshot[] Attributes;
        public readonly GameplayTag[] Tags;

        public AbilitySystemStateSnapshot(
            GrantedAbilityStateSnapshot[] grantedAbilities,
            ActiveGameplayEffectStateSnapshot[] activeEffects,
            GameplayAttributeStateSnapshot[] attributes,
            GameplayTag[] tags)
        {
            GrantedAbilities = grantedAbilities;
            ActiveEffects = activeEffects;
            Attributes = attributes;
            Tags = tags;
        }
    }

    /// <summary>
    /// Pure C# delta snapshot of an ASC.
    /// Section arrays are populated only when the corresponding change bit is set.
    /// 
    /// <b>ActiveEffects semantics:</b> the array is an upsert list — each entry with a known NetworkId
    /// updates an existing effect; an unknown NetworkId creates a new one.
    /// Effects that should no longer exist are listed in <see cref="RemovedEffectNetIds"/>.
    /// 
    /// <b>GrantedAbilities semantics:</b> the array is a full replacement of the granted-ability list
    /// whenever <see cref="AbilitySystemStateChangeMask.GrantedAbilities"/> is set.
    /// Abilities in <see cref="RemovedAbilityDefinitions"/> are cleared individually when a partial
    /// remove is preferred over a full replacement.
    /// </summary>
    public readonly struct AbilitySystemStateDeltaSnapshot
    {
        public readonly ulong BaseVersion;
        public readonly ulong CurrentVersion;
        public readonly AbilitySystemStateChangeMask ChangeMask;
        public readonly GrantedAbilityStateSnapshot[] GrantedAbilities;
        /// <summary>Abilities that were removed since the last snapshot. Applied before GrantedAbilities upsert.</summary>
        public readonly IGASAbilityDefinition[] RemovedAbilityDefinitions;
        public readonly ActiveGameplayEffectStateSnapshot[] ActiveEffects;
        /// <summary>NetworkIds of effects that were removed since the last snapshot.</summary>
        public readonly int[] RemovedEffectNetIds;
        public readonly GameplayAttributeStateSnapshot[] Attributes;
        public readonly GameplayTag[] AddedTags;
        public readonly GameplayTag[] RemovedTags;

        public bool HasChanges => ChangeMask != AbilitySystemStateChangeMask.None;

        public AbilitySystemStateDeltaSnapshot(
            ulong baseVersion,
            ulong currentVersion,
            AbilitySystemStateChangeMask changeMask,
            GrantedAbilityStateSnapshot[] grantedAbilities,
            IGASAbilityDefinition[] removedAbilityDefinitions,
            ActiveGameplayEffectStateSnapshot[] activeEffects,
            int[] removedEffectNetIds,
            GameplayAttributeStateSnapshot[] attributes,
            GameplayTag[] addedTags,
            GameplayTag[] removedTags)
        {
            BaseVersion = baseVersion;
            CurrentVersion = currentVersion;
            ChangeMask = changeMask;
            GrantedAbilities = grantedAbilities;
            RemovedAbilityDefinitions = removedAbilityDefinitions;
            ActiveEffects = activeEffects;
            RemovedEffectNetIds = removedEffectNetIds;
            Attributes = attributes;
            AddedTags = addedTags;
            RemovedTags = removedTags;
        }
    }

    #endregion
}
