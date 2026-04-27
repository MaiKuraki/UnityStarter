using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class NoopAbilityNetAdapter : IAbilityNetAdapter
    {
        public void RequestActivateAbility(INetConnection self, int abilityId, NetworkVector3 worldPos, NetworkVector3 direction) { }
        public void MulticastAbilityExecuted(INetConnection source, int abilityId, NetworkVector3 worldPos, NetworkVector3 direction) { }
        public void ConfirmAbilityActivation(INetConnection owner, int abilityId, int predictionKey) { }
        public void RejectAbilityActivation(INetConnection owner, int abilityId, int predictionKey) { }
        public void ReplicateEffectApplied(IReadOnlyList<INetConnection> observers, int effectDefinitionId,
            uint targetNetworkId, uint sourceNetworkId, int stackCount, float duration, int predictionKey)
        { }
        public void ReplicateEffectRemoved(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId)
        { }
    }
}