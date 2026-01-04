using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Stubs
{
    /// <summary>
    /// Default stub implementation to keep gameplay code compiling without any network package present.
    /// </summary>
    public sealed class NoopNetTransport : INetTransport
    {
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsRunning => false;
        public bool IsEncrypted => false;
        public bool Available => true;

        public int GetChannelId(NetworkChannel channel) => 0;
        public int GetMaxPacketSize(int channelId) => 65535;
        public NetworkStatistics GetStatistics() => default;

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;

        public void StartServer() { }
        public void StartClient(string address) { }
        public void Stop() { }
        public void Disconnect(INetConnection connection) { }

        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId) { }
        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId) { }

        // Suppress unused event warnings for the no-op stub
        private void SuppressUnusedWarnings()
        {
            OnClientConnected?.Invoke(null);
            OnClientDisconnected?.Invoke(null);
            OnConnectedToServer?.Invoke();
            OnDisconnectedFromServer?.Invoke();
            OnError?.Invoke(null, TransportError.None, null);
        }
    }
}