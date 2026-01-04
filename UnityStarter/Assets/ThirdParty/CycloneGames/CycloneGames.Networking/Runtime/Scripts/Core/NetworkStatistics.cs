namespace CycloneGames.Networking
{
    /// <summary>
    /// Readonly struct containing transport-level statistics for diagnostics.
    /// </summary>
    public readonly struct NetworkStatistics
    {
        public readonly long BytesSent;
        public readonly long BytesReceived;
        public readonly int PacketsSent;
        public readonly int PacketsReceived;
        public readonly int ConnectionCount;

        public NetworkStatistics(long bytesSent, long bytesReceived, int packetsSent, int packetsReceived, int connectionCount)
        {
            BytesSent = bytesSent;
            BytesReceived = bytesReceived;
            PacketsSent = packetsSent;
            PacketsReceived = packetsReceived;
            ConnectionCount = connectionCount;
        }
    }
}
