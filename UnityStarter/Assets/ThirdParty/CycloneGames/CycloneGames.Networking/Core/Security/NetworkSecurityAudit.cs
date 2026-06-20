using System;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Severity of a configuration finding produced by <see cref="NetworkSecurityAudit"/>.
    /// </summary>
    public enum NetworkSecurityAuditSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>
    /// Stable identifiers for security audit findings. Use these for telemetry, suppression lists
    /// and deterministic test assertions instead of matching on the human-readable message text.
    /// </summary>
    public static class NetworkSecurityAuditIds
    {
        public const string UnencryptedReleaseTraffic = "security.audit.unencrypted_release_traffic";
        public const string MissingMessageIntegrity = "security.audit.missing_message_integrity";
        public const string SignerRequiredButDisabled = "security.audit.signer_required_but_disabled";
        public const string EncryptionRequiredButUnavailable = "security.audit.encryption_required_but_unavailable";
        public const string RateLimitingDisabled = "security.audit.rate_limiting_disabled";
        public const string ReplayProtectionMissing = "security.audit.replay_protection_missing";
        public const string AntiCheatSinkNoop = "security.audit.anti_cheat_sink_noop";
        public const string UnauthenticatedConnections = "security.audit.unauthenticated_connections";
    }

    public readonly struct NetworkSecurityAuditIssue
    {
        public readonly string Id;
        public readonly NetworkSecurityAuditSeverity Severity;
        public readonly string Message;
        public readonly string Recommendation;

        public NetworkSecurityAuditIssue(
            string id,
            NetworkSecurityAuditSeverity severity,
            string message,
            string recommendation)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Audit issue id must not be null or empty.", nameof(id));
            }

            Id = id;
            Severity = severity;
            Message = message ?? string.Empty;
            Recommendation = recommendation ?? string.Empty;
        }
    }

    /// <summary>
    /// Deployment facts the audit needs but cannot infer from the pipeline options alone.
    /// Supplied by the composition root (build flags, transport capabilities, role).
    /// </summary>
    public readonly struct NetworkSecurityEnvironment
    {
        /// <summary>True when the underlying transport already provides confidentiality (TLS/DTLS/WSS).</summary>
        public readonly bool TransportEncrypted;

        /// <summary>True for non-development production builds. Findings are escalated when true.</summary>
        public readonly bool IsReleaseBuild;

        /// <summary>True when auditing a server/host process. Server-only risks are skipped on pure clients.</summary>
        public readonly bool IsServer;

        public NetworkSecurityEnvironment(bool transportEncrypted, bool isReleaseBuild, bool isServer)
        {
            TransportEncrypted = transportEncrypted;
            IsReleaseBuild = isReleaseBuild;
            IsServer = isServer;
        }
    }

    public sealed class NetworkSecurityAuditReport
    {
        private static readonly NetworkSecurityAuditIssue[] EmptyIssues = Array.Empty<NetworkSecurityAuditIssue>();

        private readonly NetworkSecurityAuditIssue[] _issues;

        internal NetworkSecurityAuditReport(List<NetworkSecurityAuditIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                _issues = EmptyIssues;
                return;
            }

            _issues = issues.ToArray();
            for (int i = 0; i < _issues.Length; i++)
            {
                switch (_issues[i].Severity)
                {
                    case NetworkSecurityAuditSeverity.Critical:
                        CriticalCount++;
                        break;
                    case NetworkSecurityAuditSeverity.Warning:
                        WarningCount++;
                        break;
                    case NetworkSecurityAuditSeverity.Info:
                        InfoCount++;
                        break;
                }
            }
        }

        public IReadOnlyList<NetworkSecurityAuditIssue> Issues => _issues;
        public int CriticalCount { get; }
        public int WarningCount { get; }
        public int InfoCount { get; }
        public bool HasCritical => CriticalCount > 0;
        public bool HasFindings => _issues.Length > 0;

        /// <summary>
        /// Builds a multi-line, human-readable summary. Allocates; intended for startup logging
        /// and Editor tooling, not for runtime hot paths.
        /// </summary>
        public string BuildSummary()
        {
            if (_issues.Length == 0)
            {
                return "Network security audit: no findings.";
            }

            var builder = new StringBuilder(256);
            builder.Append("Network security audit: ")
                .Append(CriticalCount).Append(" critical, ")
                .Append(WarningCount).Append(" warning, ")
                .Append(InfoCount).Append(" info.");

            for (int i = 0; i < _issues.Length; i++)
            {
                NetworkSecurityAuditIssue issue = _issues[i];
                builder.Append('\n')
                    .Append('[').Append(issue.Severity).Append("] ")
                    .Append(issue.Id).Append(": ")
                    .Append(issue.Message);
                if (issue.Recommendation.Length > 0)
                {
                    builder.Append(" -> ").Append(issue.Recommendation);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Inspects a <see cref="NetworkSecurityPipelineOptions"/> configuration against deployment facts
    /// and reports misconfigurations that would leave production traffic unprotected or break message
    /// delivery. This is a configuration-time audit, not a runtime hot path; allocations are acceptable.
    /// </summary>
    /// <remarks>
    /// The audit deliberately does not bundle a concrete cipher. Transport-level encryption (TLS/DTLS/WSS)
    /// is the primary confidentiality mechanism across all target platforms, including WebGL where managed
    /// AEAD ciphers are unavailable. Configure <see cref="INetworkCryptoProvider"/> only when an
    /// application-layer cipher is genuinely required and verified on every shipping platform.
    /// </remarks>
    public static class NetworkSecurityAudit
    {
        public static NetworkSecurityAuditReport Evaluate(
            NetworkSecurityPipelineOptions options,
            in NetworkSecurityEnvironment environment)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var issues = new List<NetworkSecurityAuditIssue>(8);

            bool signerEnabled = options.MessageSigner != null && options.MessageSigner.IsEnabled;
            bool cryptoEnabled = options.CryptoProvider != null && options.CryptoProvider.IsEnabled;
            bool confidentiality = environment.TransportEncrypted || cryptoEnabled;

            MessageSecurityPolicy defaultPolicy = (options.MessagePolicies ?? new MessageSecurityPolicyRegistry()).DefaultPolicy;

            if (environment.IsReleaseBuild && !confidentiality)
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.UnencryptedReleaseTraffic,
                    NetworkSecurityAuditSeverity.Critical,
                    "Release build transmits network payloads without transport-level or application-level encryption.",
                    "Enable an encrypted transport (TLS/DTLS/WSS) or configure an INetworkCryptoProvider verified on every shipping platform."));
            }

            if (environment.IsReleaseBuild && !signerEnabled)
            {
                if (!environment.TransportEncrypted)
                {
                    issues.Add(new NetworkSecurityAuditIssue(
                        NetworkSecurityAuditIds.MissingMessageIntegrity,
                        NetworkSecurityAuditSeverity.Critical,
                        "Release build neither signs messages nor uses an authenticated transport; tampering is undetectable.",
                        "Configure HmacSha256NetworkMessageSigner or an authenticated transport so message integrity can be verified."));
                }
                else
                {
                    issues.Add(new NetworkSecurityAuditIssue(
                        NetworkSecurityAuditIds.MissingMessageIntegrity,
                        NetworkSecurityAuditSeverity.Warning,
                        "Release build relies solely on transport encryption for integrity; no application-layer message signing is configured.",
                        "Add an INetworkMessageSigner if per-message authentication beyond the transport channel is required."));
                }
            }

            if (defaultPolicy.RequireSignature && !signerEnabled)
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.SignerRequiredButDisabled,
                    NetworkSecurityAuditSeverity.Critical,
                    "Default message policy requires signatures but no message signer is enabled; matching messages will be rejected.",
                    "Provide an enabled INetworkMessageSigner or relax MessageSecurityPolicy.RequireSignature."));
            }

            if (defaultPolicy.RequireEncryptedTransport && !confidentiality)
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.EncryptionRequiredButUnavailable,
                    NetworkSecurityAuditSeverity.Critical,
                    "Default message policy requires encrypted transport but neither transport encryption nor a crypto provider is available; matching messages will be rejected.",
                    "Enable an encrypted transport or an INetworkCryptoProvider, or relax MessageSecurityPolicy.RequireEncryptedTransport."));
            }

            if (environment.IsServer)
            {
                bool rateLimitingActive = options.EnableRateLimiting && options.RateLimiter != null;
                if (!rateLimitingActive)
                {
                    issues.Add(new NetworkSecurityAuditIssue(
                        NetworkSecurityAuditIds.RateLimitingDisabled,
                        NetworkSecurityAuditSeverity.Warning,
                        "Server has no active inbound rate limiter; a single connection can exhaust message processing budget.",
                        "Assign a configured RateLimiter and set EnableRateLimiting = true with a per-connection byte budget."));
                }
            }

            if (options.ReplayProtector == null)
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.ReplayProtectionMissing,
                    NetworkSecurityAuditSeverity.Warning,
                    "No replay protector is configured; replayed sequences cannot be rejected.",
                    "Assign a NetworkReplayGuardProtector (or equivalent) to ReplayProtector."));
            }

            if (environment.IsServer
                && environment.IsReleaseBuild
                && ReferenceEquals(options.AntiCheatSignalSink, NoopNetworkAntiCheatSignalSink.Instance))
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.AntiCheatSinkNoop,
                    NetworkSecurityAuditSeverity.Warning,
                    "Server release build discards anti-cheat signals through the no-op sink; rejected messages produce no telemetry.",
                    "Route AntiCheatSignalSink to a real telemetry sink so security rejections are observable."));
            }

            if (environment.IsServer
                && environment.IsReleaseBuild
                && !defaultPolicy.RequireAuthenticatedConnection)
            {
                issues.Add(new NetworkSecurityAuditIssue(
                    NetworkSecurityAuditIds.UnauthenticatedConnections,
                    NetworkSecurityAuditSeverity.Info,
                    "Default message policy does not require authenticated connections; unauthenticated peers can exchange default-policy messages.",
                    "Require authenticated connections on the default policy unless pre-authentication handshakes intentionally need it."));
            }

            return new NetworkSecurityAuditReport(issues);
        }

        /// <summary>
        /// Throws when the report contains any <see cref="NetworkSecurityAuditSeverity.Critical"/> finding.
        /// Call from a server composition root to fail fast on insecure release configurations.
        /// </summary>
        public static void ThrowIfCritical(NetworkSecurityAuditReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (report.HasCritical)
            {
                throw new InvalidOperationException(report.BuildSummary());
            }
        }
    }
}
