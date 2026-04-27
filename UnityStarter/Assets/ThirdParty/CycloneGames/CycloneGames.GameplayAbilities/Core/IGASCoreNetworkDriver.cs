namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Lightweight network contract consumed by the pure-C# core state engine.
    /// 
    /// Distinct from <see cref="IGASNetworkBridge"/>: the bridge is a richer
    /// transport-level interface (RPCs, effect replication, cue broadcasting) used
    /// by the Runtime layer, while this driver provides the minimal surface the core
    /// state module needs — ownership queries, activation RPCs, and checksum deltas.
    /// Adapters should implement both interfaces when bridging the core state engine
    /// into a full networking stack.
    /// </summary>
    public interface IGASCoreNetworkDriver
    {
        bool IsServer { get; }
        bool IsOwner(GASEntityId entity);
        void SendAbilityActivationRequest(GASEntityId entity, GASSpecHandle specHandle, GASPredictionKey predictionKey);
        void SendAbilityActivationResult(GASEntityId entity, GASSpecHandle specHandle, GASPredictionKey predictionKey, bool accepted);
        void SendStateDelta(GASEntityId entity, in GASStateChecksum checksum);
    }
}
