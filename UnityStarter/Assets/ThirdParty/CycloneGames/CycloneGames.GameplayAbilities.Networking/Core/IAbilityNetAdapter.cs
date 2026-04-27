using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    public interface IAbilityNetAdapter
    {
        void RequestActivateAbility(INetConnection self, int abilityId, NetworkVector3 worldPos, NetworkVector3 direction);
        void MulticastAbilityExecuted(INetConnection source, int abilityId, NetworkVector3 worldPos, NetworkVector3 direction);
        void ConfirmAbilityActivation(INetConnection owner, int abilityId, int predictionKey);
        void RejectAbilityActivation(INetConnection owner, int abilityId, int predictionKey);
        void ReplicateEffectApplied(IReadOnlyList<INetConnection> observers, int effectDefinitionId,
            uint targetNetworkId, uint sourceNetworkId, int stackCount, float duration, int predictionKey);
        void ReplicateEffectRemoved(IReadOnlyList<INetConnection> observers,
            uint targetNetworkId, int effectInstanceId);
    }
}
