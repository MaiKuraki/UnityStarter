using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.Diagnostics
{
    /// <summary>
    /// Simulates adverse network conditions for testing.
    /// Wrap an INetTransport with this to inject latency, jitter, packet loss, etc.
    /// Essential for QA of all multiplayer game types.
    /// </summary>
    public sealed class NetworkConditionSimulator : INetTransport
    {
        private readonly INetTransport _inner;
        private readonly System.Random _rng;
        private readonly List<DelayedPacket> _delayedPackets = new List<DelayedPacket>(64);
        private const int MaxDelayedPackets = 4096;

        // Simulation parameters
        public float LatencyMs { get; set; }            // Base one-way latency
        public float JitterMs { get; set; }             // Random variance added to latency
        public float PacketLossPercent { get; set; }    // 0-100
        public float DuplicatePercent { get; set; }     // 0-100
        public float ReorderPercent { get; set; }       // 0-100
        public bool Enabled { get; set; } = true;

        // Presets
        public static readonly NetworkConditionPreset LAN = new(0, 0, 0);
        public static readonly NetworkConditionPreset Broadband = new(20, 5, 0.1f);
        public static readonly NetworkConditionPreset WiFi = new(40, 15, 1f);
        public static readonly NetworkConditionPreset Mobile4G = new(80, 30, 2f);
        public static readonly NetworkConditionPreset Mobile3G = new(200, 80, 5f);
        public static readonly NetworkConditionPreset Satellite = new(600, 100, 3f);
        public static readonly NetworkConditionPreset Terrible = new(300, 150, 10f);

        private struct DelayedPacket
        {
            public float DeliverAt;
            public INetConnection Connection;
            public byte[] Data;
            public int Length;
            public int ChannelId;
        }

        public NetworkConditionSimulator(INetTransport inner, int seed = 0)
        {
            _inner = inner;
            _rng = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        public void ApplyPreset(NetworkConditionPreset preset)
        {
            LatencyMs = preset.LatencyMs;
            JitterMs = preset.JitterMs;
            PacketLossPercent = preset.PacketLossPercent;
        }

        // INetTransport pass-through
        public bool IsServer => _inner.IsServer;
        public bool IsClient => _inner.IsClient;
        public bool IsRunning => _inner.IsRunning;
        public bool IsEncrypted => _inner.IsEncrypted;
        public bool Available => _inner.Available;
        public NetworkTransportCapabilities Capabilities => _inner.Capabilities;

        public int GetChannelId(NetworkChannel channel) => _inner.GetChannelId(channel);
        public int GetMaxPacketSize(int channelId) => _inner.GetMaxPacketSize(channelId);
        public NetworkStatistics GetStatistics() => _inner.GetStatistics();

        public event Action<INetConnection> OnClientConnected
        {
            add => _inner.OnClientConnected += value;
            remove => _inner.OnClientConnected -= value;
        }
        public event Action<INetConnection> OnClientDisconnected
        {
            add => _inner.OnClientDisconnected += value;
            remove => _inner.OnClientDisconnected -= value;
        }
        public event Action OnConnectedToServer
        {
            add => _inner.OnConnectedToServer += value;
            remove => _inner.OnConnectedToServer -= value;
        }
        public event Action OnDisconnectedFromServer
        {
            add => _inner.OnDisconnectedFromServer += value;
            remove => _inner.OnDisconnectedFromServer -= value;
        }
        public event Action<INetConnection, TransportError, string> OnError
        {
            add => _inner.OnError += value;
            remove => _inner.OnError -= value;
        }
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived
        {
            add => _inner.OnDataReceived += value;
            remove => _inner.OnDataReceived -= value;
        }

        public void StartServer() => _inner.StartServer();
        public void StartClient(string address) => _inner.StartClient(address);
        public void Stop() => _inner.Stop();
        public void Disconnect(INetConnection connection) => _inner.Disconnect(connection);

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!Enabled)
            {
                return _inner.Send(connection, payload, channelId);
            }

            // Packet loss
            if (PacketLossPercent > 0 && _rng.NextDouble() * 100 < PacketLossPercent)
                return NetworkSendResult.Fail(NetworkSendStatus.Dropped, channelId, connection, "Packet dropped by network condition simulator.");

            float delay = LatencyMs + (float)(_rng.NextDouble() * 2 - 1) * JitterMs;
            if (delay <= 0)
            {
                NetworkSendResult result = _inner.Send(connection, payload, channelId);
                if (DuplicatePercent > 0 && _rng.NextDouble() * 100 < DuplicatePercent)
                    _inner.Send(connection, payload, channelId);
                return result;
            }
            else
            {
                // Cap delayed packets to prevent unbounded memory growth
                if (_delayedPackets.Count >= MaxDelayedPackets)
                {
                    return NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, connection, "Network condition simulator delay queue is full.");
                }

                if (payload.Array == null || payload.Offset < 0 || payload.Count < 0 || payload.Offset > payload.Array.Length - payload.Count)
                    return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, "Invalid payload segment.");

                byte[] copy = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array!, payload.Offset, copy, 0, payload.Count);

                _delayedPackets.Add(new DelayedPacket
                {
                    DeliverAt = Time.unscaledTime + delay / 1000f,
                    Connection = connection,
                    Data = copy,
                    Length = payload.Count,
                    ChannelId = channelId
                });

                return NetworkSendResult.Queued(payload.Count, channelId, connection);
            }
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));

            int acceptedBytes = 0;
            int acceptedRecipients = 0;
            NetworkSendResult lastFailure = default;

            for (int i = 0; i < connections.Count; i++)
            {
                NetworkSendResult result = Send(connections[i], payload, channelId);
                if (result.Succeeded)
                {
                    acceptedBytes += result.BytesAccepted;
                    acceptedRecipients++;
                }
                else
                {
                    lastFailure = result;
                }
            }

            if (acceptedRecipients > 0)
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, acceptedBytes, acceptedRecipients, channelId);

            return connections.Count == 0
                ? NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, channelId)
                : lastFailure;
        }

        /// <summary>
        /// Must be called each frame to deliver delayed packets.
        /// </summary>
        public void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _delayedPackets.Count - 1; i >= 0; i--)
            {
                if (now >= _delayedPackets[i].DeliverAt)
                {
                    var p = _delayedPackets[i];
                    _inner.Send(p.Connection, new ArraySegment<byte>(p.Data, 0, p.Length), p.ChannelId);
                    _delayedPackets.RemoveAt(i);
                }
            }
        }

        public void ClearDelayedPackets() => _delayedPackets.Clear();
    }

    public readonly struct NetworkConditionPreset
    {
        public readonly float LatencyMs;
        public readonly float JitterMs;
        public readonly float PacketLossPercent;

        public NetworkConditionPreset(float latencyMs, float jitterMs, float packetLossPercent)
        {
            LatencyMs = latencyMs;
            JitterMs = jitterMs;
            PacketLossPercent = packetLossPercent;
        }
    }
}
