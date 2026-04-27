namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Sink for security-related audit events emitted by networking policies.
    /// </summary>
    public interface IGASSecurityAuditSink
    {
        void Record(string eventName, int senderConnectionId, uint targetNetworkId, string reason);
    }
}