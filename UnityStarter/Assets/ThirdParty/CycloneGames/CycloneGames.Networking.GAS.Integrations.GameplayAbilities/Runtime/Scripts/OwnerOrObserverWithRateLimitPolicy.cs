namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Generic production policy: allow owner/observer requests with optional per-connection rate limiting.
    /// </summary>
    public sealed class OwnerOrObserverWithRateLimitPolicy : IGasFullStateAuthorizationPolicy
    {
        private readonly IConnectionRateLimiter _rateLimiter;
        private readonly IGasSecurityAuditSink _auditSink;

        public OwnerOrObserverWithRateLimitPolicy(
            IConnectionRateLimiter rateLimiter = null,
            IGasSecurityAuditSink auditSink = null)
        {
            _rateLimiter = rateLimiter;
            _auditSink = auditSink;
        }

        public bool IsAuthorized(in GasFullStateAuthorizationContext context)
        {
            int senderId = context.Sender?.ConnectionId ?? -1;

            if (_rateLimiter != null && !_rateLimiter.TryConsume(senderId, 1))
            {
                _auditSink?.Record("GAS.FullState.RateLimited", senderId, context.TargetNetworkId, "rate-limit");
                return false;
            }

            bool isOwner = senderId == context.OwnerConnectionId;
            bool allowed = isOwner || context.IsObserver;

            if (!allowed)
            {
                _auditSink?.Record("GAS.FullState.Unauthorized", senderId, context.TargetNetworkId, "not-owner-or-observer");
            }

            return allowed;
        }
    }
}