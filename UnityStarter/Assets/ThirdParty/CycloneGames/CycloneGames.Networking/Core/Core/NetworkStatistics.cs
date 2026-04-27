namespace CycloneGames.Networking
{
    public readonly struct NetworkStatistics
    {
        public readonly long BytesSent;
        public readonly long BytesReceived;
        public readonly int PacketsSent;
        public readonly int PacketsReceived;
        public readonly int ConnectionCount;
        public readonly int DroppedPackets;
        public readonly float AverageRttMs;

        public float SendBandwidthKBps => BytesSent / 1024f;
        public float ReceiveBandwidthKBps => BytesReceived / 1024f;

        public NetworkStatistics(long bytesSent, long bytesReceived, int packetsSent, int packetsReceived,
            int connectionCount, int droppedPackets = 0, float averageRttMs = 0f)
        {
            BytesSent = bytesSent;
            BytesReceived = bytesReceived;
            PacketsSent = packetsSent;
            PacketsReceived = packetsReceived;
            ConnectionCount = connectionCount;
            DroppedPackets = droppedPackets;
            AverageRttMs = averageRttMs;
        }
    }
}
