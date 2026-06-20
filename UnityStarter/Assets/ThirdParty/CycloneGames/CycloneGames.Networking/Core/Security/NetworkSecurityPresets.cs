using System;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Factory helpers that assemble hardened <see cref="NetworkSecurityPipelineOptions"/> baselines.
    /// Presets pick safe defaults (replay protection, authenticated connections, rejection reporting)
    /// while leaving deployment-specific dependencies (signer keys, crypto, rate budgets, telemetry)
    /// to the caller. Run <see cref="NetworkSecurityAudit.Evaluate"/> afterwards to confirm the final
    /// configuration matches the target environment.
    /// </summary>
    public static class NetworkSecurityPresets
    {
        /// <summary>
        /// Hardened server baseline: replay protection and authenticated connections are required on the
        /// default policy, rejected messages are reported, and rate limiting is enabled when a limiter is
        /// supplied. Pass an enabled signer and/or crypto provider to satisfy the production audit.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateHardenedServerOptions(
            INetworkMessageSigner messageSigner = null,
            INetworkCryptoProvider cryptoProvider = null,
            INetworkAntiCheatSignalSink antiCheatSink = null,
            RateLimiter rateLimiter = null)
        {
            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithAuthenticatedConnectionRequired(true)
                .WithReplayProtection(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayProtector = new NetworkReplayGuardProtector(),
                MessageSigner = messageSigner ?? NoopNetworkMessageSigner.Instance,
                CryptoProvider = cryptoProvider ?? NoopNetworkCryptoProvider.Instance,
                AntiCheatSignalSink = antiCheatSink ?? NoopNetworkAntiCheatSignalSink.Instance,
                RateLimiter = rateLimiter,
                EnableRateLimiting = rateLimiter != null,
                ReportRejectedMessages = true
            };
        }

        /// <summary>
        /// Hardened client baseline: replay protection on, rejection reporting on. Clients usually do not
        /// rate-limit their own single server connection, so rate limiting is left disabled by default.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateHardenedClientOptions(
            INetworkMessageSigner messageSigner = null,
            INetworkCryptoProvider cryptoProvider = null,
            INetworkAntiCheatSignalSink antiCheatSink = null)
        {
            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithReplayProtection(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayProtector = new NetworkReplayGuardProtector(),
                MessageSigner = messageSigner ?? NoopNetworkMessageSigner.Instance,
                CryptoProvider = cryptoProvider ?? NoopNetworkCryptoProvider.Instance,
                AntiCheatSignalSink = antiCheatSink ?? NoopNetworkAntiCheatSignalSink.Instance,
                EnableRateLimiting = false,
                ReportRejectedMessages = true
            };
        }
    }
}
