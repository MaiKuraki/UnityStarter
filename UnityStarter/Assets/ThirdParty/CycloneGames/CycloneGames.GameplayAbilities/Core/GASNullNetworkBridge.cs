using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
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

        // Effect application is already local -- no replication needed.
        public void ServerReplicateEffectApplied(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectUpdated(IGASNetworkTarget targetAsc, in GASEffectReplicationData data) { }
        public void ServerReplicateEffectRemoved(IGASNetworkTarget targetAsc, int effectNetId) { }

        // Cues are already dispatched locally in DispatchGameplayCues -- no broadcast needed.
        public void ServerBroadcastGameplayCue(IGASNetworkTarget sourceAsc, GameplayTag cueTag,
            EGameplayCueEvent eventType, in GASCueNetParams cueParams)
        { }

        public void ServerSendStateDelta(IGASNetworkTarget targetAsc, GASAbilitySystemStateDeltaBuffer delta) { }
    }
}
