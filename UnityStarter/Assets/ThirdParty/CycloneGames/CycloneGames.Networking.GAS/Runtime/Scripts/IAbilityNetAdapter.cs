using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.GAS
{
    /// <summary>
    /// Adapter between GameplayAbilities and the underlying networking.
    /// Implement this to connect your AbilitySystemComponent to the network layer.
    /// 
    /// For a full-featured implementation, use NetworkedAbilityBridge (in GAS namespace)
    /// which provides attribute sync, effect replication, prediction, and reconnect support.
    /// This interface remains for lightweight / custom integration scenarios.
    /// </summary>
    public interface IAbilityNetAdapter
    {
        /// <summary>
        /// Called on client to request ability activation. Implementation must route to server reliably.
        /// </summary>
        void RequestActivateAbility(INetConnection self, int abilityId, Vector3 worldPos, Vector3 direction);

        /// <summary>
        /// Called on server to multicast ability executed state to observers. Use unreliable for FX-heavy events.
        /// </summary>
        void MulticastAbilityExecuted(INetConnection source, int abilityId, Vector3 worldPos, Vector3 direction);

        /// <summary>
        /// Called on server to confirm a predicted ability activation to the owning client.
        /// </summary>
        void ConfirmAbilityActivation(INetConnection owner, int abilityId, int predictionKey);

        /// <summary>
        /// Called on server to reject a predicted ability activation (triggers client rollback).
        /// </summary>
        void RejectAbilityActivation(INetConnection owner, int abilityId, int predictionKey);

        /// <summary>
        /// Called on server to replicate a gameplay effect application to observers.
        /// </summary>
        void ReplicateEffectApplied(IReadOnlyList<INetConnection> observers, int effectDefinitionId,
            uint targetNetworkId, uint sourceNetworkId, int stackCount, float duration, int predictionKey);

        /// <summary>
        /// Called on server to replicate a gameplay effect removal to observers.
        /// </summary>
        void ReplicateEffectRemoved(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId);
    }
}
