using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    #region Network Data

    /// <summary>
    /// Serializable snapshot of an ActiveGameplayEffect for network replication.
    /// All value types -- safe to copy across network message boundaries.
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

    #endregion

    #region Network Interfaces

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
    /// locally -- safe for single-player and listen-server topologies.
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

        // ---- Client -> Server ----

        /// <summary>
        /// Called by the client when a LocalPredicted ability activates.
        /// Implementations should send an RPC to the server, which will call
        /// <see cref="IGASNetworkTarget.ServerReceiveTryActivateAbility"/> on the server-side ASC.
        /// </summary>
        void ClientRequestActivateAbility(IGASNetworkTarget asc, int specHandle, GASPredictionKey predictionKey);

        // ---- Server -> Client ----

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

        // ---- Effect Replication (Server -> All Relevant Clients) ----

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

        // ---- GameplayCue Replication (Server -> All Clients) ----

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
    /// Exposes the server->Client and client-receive entry points on an AbilitySystemComponent.
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
}
