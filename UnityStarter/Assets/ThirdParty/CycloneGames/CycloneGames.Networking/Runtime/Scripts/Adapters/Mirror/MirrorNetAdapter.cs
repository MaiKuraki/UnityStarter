#if MIRROR
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Mirror
{
    /// <summary>
    /// Mirror-backed transport and ability adapter.
    /// Lives under Mirror folder so deleting Mirror removes this too without breaking CycloneGames.
    /// </summary>
    public sealed class MirrorNetTransport : MonoBehaviour, INetTransport, IAbilityNetAdapter
    {
        public static MirrorNetTransport Instance { get; private set; }

        [Tooltip("If true, enforce singleton and persist across scene loads. Strongly recommended in production to avoid duplicate registration and state divergence.")]
        [SerializeField] private bool _singleton = true;

        // cache common channels for clarity
        public int ReliableChannel => Channels.Reliable;
        public int UnreliableChannel => Channels.Unreliable;

        public bool IsServer => NetworkServer.active;
        public bool IsClient => NetworkClient.active;
        public bool IsEncrypted
        {
            get
            {
                var t = Transport.active;
                return t != null && t.IsEncrypted;
            }
        }

        // Optional hook for gameplay to validate/execute before multicast
        public event System.Action<INetConnection, int, Vector3, Vector3> AbilityRequestReceived;

        // zero-allocation send using pooled writer, caller provides serialized payload
        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (connection is MirrorNetConnection mc)
            {
                if (NetworkServer.connections.TryGetValue(mc.ConnectionId, out NetworkConnectionToClient conn))
                {
                    conn.Send(new RawBytesMessage { data = payload }, channelId);
                }
            }
        }

        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            // batch by iterating once; Mirror will coalesce per-conn internally via Batcher
            for (int i = 0; i < connections.Count; i++)
            {
                Send(connections[i], payload, channelId);
            }
        }

        // Ability adapter: define a compact struct message to minimize GC and bandwidth
        public struct AbilityRequestMsg : NetworkMessage
        {
            public int abilityId;
            public Vector3 pos;
            public Vector3 dir;
        }

        public struct AbilityMulticastMsg : NetworkMessage
        {
            public int abilityId;
            public Vector3 pos;
            public Vector3 dir;
        }

        void Awake()
        {
            // Singleton enforcement
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // not singleton mode: still set Instance if null for convenience
                if (Instance == null) Instance = this;
            }

            // Register server-side handler for ability requests (once)
            NetworkServer.RegisterHandler<AbilityRequestMsg>(OnServerAbilityRequest, false);

            // Register into NetServices so Cyclone gameplay can resolve adapter
            NetServices.Transport = this;
            NetServices.Ability = this;
        }

        void OnDestroy()
        {
            // unregister only if Mirror is still active
            NetworkServer.UnregisterHandler<AbilityRequestMsg>();

            if (_singleton && Instance == this)
            {
                Instance = null;
            }
        }

        void OnServerAbilityRequest(NetworkConnectionToClient conn, AbilityRequestMsg msg)
        {
            var wrapper = new MirrorNetConnection(conn);
            if (AbilityRequestReceived != null)
            {
                AbilityRequestReceived.Invoke(wrapper, msg.abilityId, msg.pos, msg.dir);
            }
            else
            {
                // Fallback: directly multicast
                MulticastAbilityExecuted(wrapper, msg.abilityId, msg.pos, msg.dir);
            }
        }

        public void RequestActivateAbility(INetConnection self, int abilityId, Vector3 worldPos, Vector3 direction)
        {
            // client -> server
            var msg = new AbilityRequestMsg { abilityId = abilityId, pos = worldPos, dir = direction };
            NetworkClient.Send(msg, ReliableChannel);
        }

        public void MulticastAbilityExecuted(INetConnection source, int abilityId, Vector3 worldPos, Vector3 direction)
        {
            // server -> clients (observers); unreliable is fine for FX
            var msg = new AbilityMulticastMsg { abilityId = abilityId, pos = worldPos, dir = direction };
            // Use Mirror's observers broadcast
            foreach (NetworkConnectionToClient observer in NetworkServer.connections.Values)
            {
                observer.Send(msg, UnreliableChannel);
            }
        }
    }

    // lightweight connection wrapper
    public readonly struct MirrorNetConnection : INetConnection
    {
        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsAuthenticated { get; }
        public bool IsConnected { get; }

        public MirrorNetConnection(NetworkConnectionToClient conn)
        {
            ConnectionId = conn.connectionId;
            RemoteAddress = conn.address;
            IsAuthenticated = conn.isAuthenticated;
            IsConnected = conn.isAuthenticated && conn.isReady;
        }
    }

    // raw bytes message to forward pre-serialized payloads without extra GC
    public struct RawBytesMessage : NetworkMessage
    {
        public ArraySegment<byte> data;
    }
}
#endif