using CycloneGames.Networking;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Policy for authorizing full-state requests.
    /// </summary>
    public interface IGasFullStateAuthorizationPolicy
    {
        bool IsAuthorized(in GasFullStateAuthorizationContext context);
    }

    public readonly struct GasFullStateAuthorizationContext
    {
        public readonly INetConnection Sender;
        public readonly uint TargetNetworkId;
        public readonly int OwnerConnectionId;
        public readonly bool IsObserver;

        public GasFullStateAuthorizationContext(INetConnection sender, uint targetNetworkId, int ownerConnectionId, bool isObserver)
        {
            Sender = sender;
            TargetNetworkId = targetNetworkId;
            OwnerConnectionId = ownerConnectionId;
            IsObserver = isObserver;
        }
    }
}