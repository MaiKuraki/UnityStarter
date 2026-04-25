using System;
using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    public static class GASBridgeSecurityExtensions
    {
        /// <summary>
        /// Apply a generic authorization policy to the bridge's FullStateRequestAuthorizer.
        /// </summary>
        public static void ConfigureFullStateAuthorization(
            this NetworkedAbilityBridge bridge,
            IGASFullStateAuthorizationPolicy policy,
            Func<uint, int> getOwnerConnectionId,
            Func<uint, IReadOnlyList<INetConnection>> getObservers)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (getOwnerConnectionId == null) throw new ArgumentNullException(nameof(getOwnerConnectionId));

            bridge.FullStateRequestAuthorizer = (sender, targetNetworkId) =>
            {
                int ownerConnectionId = getOwnerConnectionId(targetNetworkId);
                bool isObserver = false;

                if (getObservers != null)
                {
                    var observers = getObservers(targetNetworkId);
                    if (observers != null)
                    {
                        int senderId = sender?.ConnectionId ?? -1;
                        for (int i = 0; i < observers.Count; i++)
                        {
                            if (observers[i].ConnectionId == senderId)
                            {
                                isObserver = true;
                                break;
                            }
                        }
                    }
                }

                var context = new GASFullStateAuthorizationContext(
                    sender,
                    targetNetworkId,
                    ownerConnectionId,
                    isObserver);

                return policy.IsAuthorized(context);
            };
        }
    }
}
