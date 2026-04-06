namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Sink for security-related audit events emitted by networking policies.
    /// </summary>
    public interface IGasSecurityAuditSink
    {
        void Record(string eventName, int senderConnectionId, uint targetNetworkId, string reason);
    }
}