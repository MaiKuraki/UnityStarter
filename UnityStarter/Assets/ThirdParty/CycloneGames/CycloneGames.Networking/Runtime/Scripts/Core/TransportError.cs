namespace CycloneGames.Networking
{
    /// <summary>
    /// Transport-level error types for network operations.
    /// </summary>
    public enum TransportError
    {
        None,
        DnsResolve,
        Refused,
        Timeout,
        Congestion,
        InvalidReceive,
        InvalidSend,
        ConnectionClosed,
        Unexpected
    }
}
