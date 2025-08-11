using System;
using System.Collections.Generic;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Default stub implementation to keep gameplay code compiling without any network package present.
    /// </summary>
    public sealed class NoopNetTransport : INetTransport
    {
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsEncrypted => false;
        public int ReliableChannel => 0;
        public int UnreliableChannel => 1;
        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId) { /* no-op */ }
        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId) { /* no-op */ }
    }
}