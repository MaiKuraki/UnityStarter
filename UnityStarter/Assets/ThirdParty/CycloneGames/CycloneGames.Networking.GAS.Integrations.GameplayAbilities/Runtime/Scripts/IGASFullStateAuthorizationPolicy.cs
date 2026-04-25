using CycloneGames.Networking;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Policy for authorizing full-state requests.
    /// </summary>
    public interface IGASFullStateAuthorizationPolicy
    {
        bool IsAuthorized(in GASFullStateAuthorizationContext context);
    }

    public readonly struct GASFullStateAuthorizationContext
    {
        public readonly INetConnection Sender;
        public readonly uint TargetNetworkId;
        public readonly int OwnerConnectionId;
        public readonly bool IsObserver;

        public GASFullStateAuthorizationContext(INetConnection sender, uint targetNetworkId, int ownerConnectionId, bool isObserver)
        {
            Sender = sender;
            TargetNetworkId = targetNetworkId;
            OwnerConnectionId = ownerConnectionId;
            IsObserver = isObserver;
        }
    }
}
