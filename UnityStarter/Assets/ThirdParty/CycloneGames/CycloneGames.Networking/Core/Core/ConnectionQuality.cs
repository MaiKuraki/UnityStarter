namespace CycloneGames.Networking
{
    /// <summary>
    /// Categorizes connection quality based on latency and stability.
    /// </summary>
    public enum ConnectionQuality
    {
        Excellent,      // < 50ms RTT, stable
        Good,           // 50-100ms RTT
        Fair,           // 100-200ms RTT
        Poor,           // > 200ms RTT or unstable
        Disconnected
    }
}