#if MIRROR
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Mirror
{
    public sealed class MirrorNetTransport : MonoBehaviour, INetTransport, IAbilityNetAdapter
    {
        public static MirrorNetTransport Instance { get; private set; }

        [Tooltip("If true, enforce singleton and persist across scene loads. Strongly recommended in production to avoid duplicate registration and state divergence.")]
        [SerializeField] private bool _singleton = true;

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

        public event System.Action<INetConnection, int, Vector3, Vector3> AbilityRequestReceived;

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
            for (int i = 0; i < connections.Count; i++)
            {
                Send(connections[i], payload, channelId);
            }
        }

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
                if (Instance == null) Instance = this;
            }

            NetworkServer.RegisterHandler<AbilityRequestMsg>(OnServerAbilityRequest, false);

            NetServices.Transport = this;
            NetServices.Ability = this;
        }

        void OnDestroy()
        {
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
            // server -> clients (observers)
            var msg = new AbilityMulticastMsg { abilityId = abilityId, pos = worldPos, dir = direction };
            foreach (NetworkConnectionToClient observer in NetworkServer.connections.Values)
            {
                observer.Send(msg, UnreliableChannel);
            }
        }
    }

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

    public struct RawBytesMessage : NetworkMessage
    {
        public ArraySegment<byte> data;
    }
}
#endif