using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Stubs
{
    /// <summary>
    /// Default stub implementation to keep gameplay code compiling without any network package present.
    /// </summary>
    public sealed class NoopNetTransport : INetTransport, INetworkLifecycleProvider, INetworkFeatureProvider
    {
        public bool IsServer => false;
        public bool IsClient => false;
        public bool IsRunning => false;
        public bool IsEncrypted => false;
        public bool Available => true;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.None;
        public NetworkTransportCapabilities Capabilities => NetworkTransportCapabilities.None;

        public int GetChannelId(NetworkChannel channel) => 0;
        public int GetMaxPacketSize(int channelId) => 65535;
        public NetworkStatistics GetStatistics() => default;
        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
            return new NetworkLifecycleSnapshot(
                NetworkLifecycleState.Stopped,
                Features,
                TransportError.None,
                string.Empty,
                Available,
                IsRunning,
                IsServer,
                IsClient,
                IsEncrypted);
        }

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        public void StartServer() { }
        public void StartClient(string address) { }
        public void Stop() { }
        public void Disconnect(INetConnection connection) { }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, channelId, connection, "No-op transport does not send data.");
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, channelId, reason: "No-op transport does not send data.");
        }

        // Suppress unused event warnings for the no-op stub
        private void SuppressUnusedWarnings()
        {
            OnClientConnected?.Invoke(null);
            OnClientDisconnected?.Invoke(null);
            OnConnectedToServer?.Invoke();
            OnDisconnectedFromServer?.Invoke();
            OnError?.Invoke(null, TransportError.None, null);
            OnDataReceived?.Invoke(null, default, 0);
        }
    }
}
