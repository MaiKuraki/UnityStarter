using System;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Factory helpers that assemble <see cref="NetworkSecurityPipelineOptions"/> security baselines.
    /// </summary>
    /// <remarks>
    /// These presets harden the <b>policy</b> only (replay protection, authenticated connections, rejection
    /// reporting). A baseline returned without a signer performs no message signing. Deployments that require
    /// confidentiality must terminate on an authenticated, encrypted transport such as TLS, DTLS, or WSS.
    /// Always run <see cref="NetworkSecurityAudit.Evaluate"/> on the final options to confirm the configuration
    /// matches the target environment before shipping.
    /// </remarks>
    public static class NetworkSecurityPresets
    {
        /// <summary>
        /// Server security baseline: the default policy requires replay protection and authenticated connections,
        /// rejected messages are reported, and rate limiting is enabled when a limiter is supplied. Signing
        /// defaults to <c>Noop</c>; supply a real signer and use an authenticated, encrypted transport
        /// before treating the result as production-ready.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateServerSecurityBaseline(
            INetworkMessageSigner messageSigner = null,
            INetworkAntiCheatSignalSink antiCheatSink = null,
            RateLimiter rateLimiter = null)
        {
            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithAuthenticatedConnectionRequired(true)
                .WithReplayProtection(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayGuard = new NetworkReplayGuard(),
                MessageSigner = messageSigner ?? NoopNetworkMessageSigner.Instance,
                AntiCheatSignalSink = antiCheatSink ?? NoopNetworkAntiCheatSignalSink.Instance,
                RateLimiter = rateLimiter,
                ReportRejectedMessages = true
            };
        }

        /// <summary>
        /// Strict server preset for release-oriented composition roots. Unlike
        /// <see cref="CreateServerSecurityBaseline"/>, this method rejects no-op telemetry,
        /// disabled signers, and missing rate limiters at construction time.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateProductionServerSecurityBaseline(
            INetworkMessageSigner messageSigner,
            INetworkAntiCheatSignalSink antiCheatSink,
            RateLimiter rateLimiter,
            bool requireEncryptedTransport = true)
        {
            if (messageSigner == null)
            {
                throw new ArgumentNullException(nameof(messageSigner));
            }

            if (!messageSigner.IsEnabled)
            {
                throw new ArgumentException("Production server security requires an enabled message signer.", nameof(messageSigner));
            }

            if (antiCheatSink == null)
            {
                throw new ArgumentNullException(nameof(antiCheatSink));
            }

            if (ReferenceEquals(antiCheatSink, NoopNetworkAntiCheatSignalSink.Instance))
            {
                throw new ArgumentException("Production server security requires a non-noop anti-cheat signal sink.", nameof(antiCheatSink));
            }

            if (rateLimiter == null)
            {
                throw new ArgumentNullException(nameof(rateLimiter));
            }

            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithAuthenticatedConnectionRequired(true)
                .WithEncryptedTransportRequired(requireEncryptedTransport)
                .WithReplayProtection(true)
                .WithSignatureRequired(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayGuard = new NetworkReplayGuard(),
                MessageSigner = messageSigner,
                AntiCheatSignalSink = antiCheatSink,
                RateLimiter = rateLimiter,
                ReportRejectedMessages = true
            };
        }

        /// <summary>
        /// Client security baseline: replay protection on, rejection reporting on. Clients usually do not
        /// rate-limit their own single server connection, so rate limiting is left disabled by default. Signing
        /// defaults to <c>Noop</c>; supply a real signer when message authentication is required.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateClientSecurityBaseline(
            INetworkMessageSigner messageSigner = null,
            INetworkAntiCheatSignalSink antiCheatSink = null)
        {
            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithReplayProtection(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayGuard = new NetworkReplayGuard(),
                MessageSigner = messageSigner ?? NoopNetworkMessageSigner.Instance,
                AntiCheatSignalSink = antiCheatSink ?? NoopNetworkAntiCheatSignalSink.Instance,
                ReportRejectedMessages = true
            };
        }
    }
}
