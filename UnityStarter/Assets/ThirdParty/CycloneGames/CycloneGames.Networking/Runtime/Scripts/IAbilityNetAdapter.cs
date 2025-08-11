using UnityEngine;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Adapter between GameplayAbilities and the underlying networking.
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
    }
}