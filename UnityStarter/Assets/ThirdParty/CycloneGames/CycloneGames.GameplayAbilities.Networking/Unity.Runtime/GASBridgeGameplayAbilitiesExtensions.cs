using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Convenience helpers that make the GameplayAbilities integration usable as a mainline replication path
    /// without forcing projects to manually wire bridge registration and per-tick delta replication.
    /// </summary>
    public static class GASBridgeGameplayAbilitiesExtensions
    {
        public static GameplayAbilitiesNetworkedASCAdapter RegisterGameplayAbilitiesASC(
            this NetworkedAbilityBridge bridge,
            AbilitySystemComponent asc,
            uint networkId,
            int ownerConnectionId,
            IGASNetIdRegistry idRegistry = null)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));

            var adapter = new GameplayAbilitiesNetworkedASCAdapter(
                asc,
                networkId,
                ownerConnectionId,
                idRegistry);

            bridge.RegisterASC(networkId, ownerConnectionId, adapter);
            return adapter;
        }

        public static ReplicatedAbilitySystemStateDelta ReplicatePendingState(
            this NetworkedAbilityBridge bridge,
            GameplayAbilitiesNetworkedASCAdapter adapter,
            Func<uint, IReadOnlyList<INetConnection>> getObservers)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));

            var observers = getObservers?.Invoke(adapter.NetworkId);
            return adapter.CaptureAndReplicatePendingStateDelta(bridge, observers);
        }

        public static void SendGameplayAbilitiesFullState(
            this NetworkedAbilityBridge bridge,
            GameplayAbilitiesNetworkedASCAdapter adapter,
            INetConnection client)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (client == null) throw new ArgumentNullException(nameof(client));

            bridge.ServerSendFullState(client, adapter.CaptureFullState());
        }

        public static void SendGameplayAbilitiesFullState(
            this NetworkedAbilityBridge bridge,
            GameplayAbilitiesNetworkedASCAdapter adapter,
            IReadOnlyList<INetConnection> clients)
        {
            if (bridge == null) throw new ArgumentNullException(nameof(bridge));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (clients == null) throw new ArgumentNullException(nameof(clients));

            var data = adapter.CaptureFullState();
            for (int i = 0; i < clients.Count; i++)
            {
                var client = clients[i];
                if (client != null)
                {
                    bridge.ServerSendFullState(client, data);
                }
            }
        }
    }
}