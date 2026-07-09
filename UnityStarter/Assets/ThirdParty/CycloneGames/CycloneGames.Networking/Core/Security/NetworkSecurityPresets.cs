using System;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Factory helpers that assemble <see cref="NetworkSecurityPipelineOptions"/> security baselines.
    /// </summary>
    /// <remarks>
    /// These presets harden the <b>policy</b> only (replay protection, authenticated connections, rejection
    /// reporting). They are deliberately <b>not</b> production-complete: when no signer or crypto provider is
    /// supplied they fall back to the <c>Noop*</c> implementations, so a baseline returned with default arguments
    /// performs no message signing or encryption. A secure deployment must either supply a real
    /// <see cref="INetworkMessageSigner"/> / <see cref="INetworkCryptoProvider"/>, or terminate on an
    /// authenticated, encrypted transport (TLS/DTLS) where application-layer crypto is intentionally a no-op.
    /// Always run <see cref="NetworkSecurityAudit.Evaluate"/> on the final options to confirm the configuration
    /// matches the target environment before shipping.
    /// </remarks>
    public static class NetworkSecurityPresets
    {
        /// <summary>
        /// Server security baseline: the default policy requires replay protection and authenticated connections,
        /// rejected messages are reported, and rate limiting is enabled when a limiter is supplied. Signing and
        /// crypto default to <c>Noop</c>; supply a real signer and/or crypto provider (or rely on an authenticated,
        /// encrypted transport) and run <see cref="NetworkSecurityAudit.Evaluate"/> before treating the result as
        /// production-ready.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateServerSecurityBaseline(
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
        /// Strict server preset for release-oriented composition roots. Unlike
        /// <see cref="CreateServerSecurityBaseline"/>, this method rejects no-op telemetry,
        /// disabled signers, and missing rate limiters at construction time.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateProductionServerSecurityBaseline(
            INetworkMessageSigner messageSigner,
            INetworkAntiCheatSignalSink antiCheatSink,
            RateLimiter rateLimiter,
            INetworkCryptoProvider cryptoProvider = null,
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

            if (cryptoProvider != null && !cryptoProvider.IsEnabled)
            {
                throw new ArgumentException("A supplied production crypto provider must be enabled.", nameof(cryptoProvider));
            }

            MessageSecurityPolicy defaultPolicy = MessageSecurityPolicy.Default
                .WithAuthenticatedConnectionRequired(true)
                .WithEncryptedTransportRequired(requireEncryptedTransport)
                .WithReplayProtection(true)
                .WithSignatureRequired(true);

            return new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(defaultPolicy),
                ReplayProtector = new NetworkReplayGuardProtector(),
                MessageSigner = messageSigner,
                CryptoProvider = cryptoProvider ?? NoopNetworkCryptoProvider.Instance,
                AntiCheatSignalSink = antiCheatSink,
                RateLimiter = rateLimiter,
                EnableRateLimiting = true,
                ReportRejectedMessages = true
            };
        }

        /// <summary>
        /// Client security baseline: replay protection on, rejection reporting on. Clients usually do not
        /// rate-limit their own single server connection, so rate limiting is left disabled by default. Signing
        /// and crypto default to <c>Noop</c>; supply real implementations (or rely on an authenticated, encrypted
        /// transport) and run <see cref="NetworkSecurityAudit.Evaluate"/> before treating the result as
        /// production-ready.
        /// </summary>
        public static NetworkSecurityPipelineOptions CreateClientSecurityBaseline(
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
