using UnityEngine;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Default ability adapter that does nothing. Keeps gameplay compiling with no network stack.
    /// </summary>
    public sealed class NoopAbilityNetAdapter : IAbilityNetAdapter
    {
        public void RequestActivateAbility(INetConnection self, int abilityId, Vector3 worldPos, Vector3 direction) { }
        public void MulticastAbilityExecuted(INetConnection source, int abilityId, Vector3 worldPos, Vector3 direction) { }
    }
}