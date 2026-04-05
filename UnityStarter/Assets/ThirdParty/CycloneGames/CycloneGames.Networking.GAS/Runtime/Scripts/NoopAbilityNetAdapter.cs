using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.GAS
{
    /// <summary>
    /// Default ability adapter that does nothing. Keeps gameplay compiling with no network stack.
    /// </summary>
    public sealed class NoopAbilityNetAdapter : IAbilityNetAdapter
    {
        public void RequestActivateAbility(INetConnection self, int abilityId, Vector3 worldPos, Vector3 direction) { }
        public void MulticastAbilityExecuted(INetConnection source, int abilityId, Vector3 worldPos, Vector3 direction) { }
        public void ConfirmAbilityActivation(INetConnection owner, int abilityId, int predictionKey) { }
        public void RejectAbilityActivation(INetConnection owner, int abilityId, int predictionKey) { }
        public void ReplicateEffectApplied(IReadOnlyList<INetConnection> observers, int effectDefinitionId,
            uint targetNetworkId, uint sourceNetworkId, int stackCount, float duration, int predictionKey) { }
        public void ReplicateEffectRemoved(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId) { }
    }
}