using System;

namespace CycloneGames.Networking.Rpc
{
    public enum RpcTarget : byte
    {
        Server,         // Client -> Server
        Owner,          // Server -> owning client
        AllClients,     // Server -> all clients (broadcast)
        Observers,      // Server -> all observing clients (via interest management)
        AllExceptOwner, // Server -> all clients except the owner
        AllExceptSender // Server -> all clients except the one who sent the message
    }

    /// <summary>
    /// Attribute for marking methods as RPCs. Used by code generators or runtime reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NetworkRpcAttribute : Attribute
    {
        public RpcTarget Target { get; }
        public NetworkChannel Channel { get; }
        public bool RequiresAuthority { get; set; }

        public NetworkRpcAttribute(RpcTarget target = RpcTarget.Server, NetworkChannel channel = NetworkChannel.Reliable)
        {
            Target = target;
            Channel = channel;
            RequiresAuthority = target == RpcTarget.Server;
        }
    }

    /// <summary>
    /// Attribute for Server RPCs (client -> server).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ServerRpcAttribute : Attribute
    {
        public NetworkChannel Channel { get; set; } = NetworkChannel.Reliable;
        public bool RequiresOwnership { get; set; } = true;
    }

    /// <summary>
    /// Attribute for Client RPCs (server -> client(s)).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ClientRpcAttribute : Attribute
    {
        public RpcTarget Target { get; set; } = RpcTarget.AllClients;
        public NetworkChannel Channel { get; set; } = NetworkChannel.Reliable;
    }
}
