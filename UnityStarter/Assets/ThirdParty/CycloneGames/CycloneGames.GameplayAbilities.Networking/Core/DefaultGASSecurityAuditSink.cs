namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class DefaultGASSecurityAuditSink : IGASSecurityAuditSink
    {
        public static readonly DefaultGASSecurityAuditSink Instance = new DefaultGASSecurityAuditSink();

        private DefaultGASSecurityAuditSink() { }

        public void Record(string eventName, int senderConnectionId, uint targetNetworkId, string reason)
        {
            GASNetLogger.LogWarning($"[GAS Security] event={eventName}, sender={senderConnectionId}, target={targetNetworkId}, reason={reason}");
        }
    }
}
