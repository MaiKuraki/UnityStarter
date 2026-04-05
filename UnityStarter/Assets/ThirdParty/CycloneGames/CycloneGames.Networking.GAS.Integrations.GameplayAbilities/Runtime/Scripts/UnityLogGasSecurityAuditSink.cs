using UnityEngine;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Default audit sink that logs events to Unity console.
    /// </summary>
    public sealed class UnityLogGasSecurityAuditSink : IGasSecurityAuditSink
    {
        public static readonly UnityLogGasSecurityAuditSink Instance = new UnityLogGasSecurityAuditSink();

        private UnityLogGasSecurityAuditSink() { }

        public void Record(string eventName, int senderConnectionId, uint targetNetworkId, string reason)
        {
            Debug.LogWarning($"[GAS Security] event={eventName}, sender={senderConnectionId}, target={targetNetworkId}, reason={reason}");
        }
    }
}