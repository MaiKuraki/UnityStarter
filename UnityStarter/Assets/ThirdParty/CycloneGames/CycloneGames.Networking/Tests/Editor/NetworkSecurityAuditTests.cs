using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkSecurityAuditTests
    {
        private static byte[] SampleKey => new byte[] { 1, 7, 9, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59 };

        [Test]
        public void Audit_Flags_Unencrypted_Release_Server_As_Critical()
        {
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions();
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: false,
                isReleaseBuild: true,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.IsTrue(report.HasCritical);
            Assert.IsTrue(ContainsId(report, NetworkSecurityAuditIds.UnencryptedReleaseTraffic));
            Assert.IsTrue(ContainsId(report, NetworkSecurityAuditIds.MissingMessageIntegrity));
        }

        [Test]
        public void Audit_Encrypted_Transport_Removes_Confidentiality_Critical()
        {
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions();
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: true,
                isReleaseBuild: true,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.IsFalse(ContainsId(report, NetworkSecurityAuditIds.UnencryptedReleaseTraffic));
            Assert.AreEqual(NetworkSecurityAuditSeverity.Warning, FindSeverity(report, NetworkSecurityAuditIds.MissingMessageIntegrity));
        }

        [Test]
        public void Audit_Hardened_Server_With_Signer_And_Encrypted_Transport_Has_No_Critical()
        {
            using var signer = new HmacSha256NetworkMessageSigner(SampleKey);
            var rateLimiter = new RateLimiter(maxMessagesPerSecond: 120, maxBytesPerSecond: 65536, burstLimit: 8);
            var sink = new RecordingNetworkAntiCheatSignalSink();
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions(
                messageSigner: signer,
                antiCheatSink: sink,
                rateLimiter: rateLimiter);
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: true,
                isReleaseBuild: true,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.IsFalse(report.HasCritical, report.BuildSummary());
        }

        [Test]
        public void Audit_Reports_Required_Signature_Without_Signer_As_Critical()
        {
            var options = new NetworkSecurityPipelineOptions
            {
                MessagePolicies = new MessageSecurityPolicyRegistry(
                    MessageSecurityPolicy.Default.WithSignatureRequired(true))
            };
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: true,
                isReleaseBuild: false,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.IsTrue(ContainsId(report, NetworkSecurityAuditIds.SignerRequiredButDisabled));
            Assert.IsTrue(report.HasCritical);
        }

        [Test]
        public void Audit_Development_Build_Does_Not_Emit_Confidentiality_Critical()
        {
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions();
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: false,
                isReleaseBuild: false,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.IsFalse(ContainsId(report, NetworkSecurityAuditIds.UnencryptedReleaseTraffic));
            Assert.IsFalse(ContainsId(report, NetworkSecurityAuditIds.MissingMessageIntegrity));
        }

        [Test]
        public void Audit_ThrowIfCritical_Throws_On_Critical_Report()
        {
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions();
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: false,
                isReleaseBuild: true,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.Throws<System.InvalidOperationException>(() => NetworkSecurityAudit.ThrowIfCritical(report));
        }

        [Test]
        public void Audit_Server_Without_Rate_Limiter_Warns()
        {
            NetworkSecurityPipelineOptions options = NetworkSecurityPresets.CreateHardenedServerOptions();
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted: true,
                isReleaseBuild: false,
                isServer: true);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);

            Assert.AreEqual(NetworkSecurityAuditSeverity.Warning, FindSeverity(report, NetworkSecurityAuditIds.RateLimitingDisabled));
        }

        private static bool ContainsId(NetworkSecurityAuditReport report, string id)
        {
            System.Collections.Generic.IReadOnlyList<NetworkSecurityAuditIssue> issues = report.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                if (string.Equals(issues[i].Id, id, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static NetworkSecurityAuditSeverity FindSeverity(NetworkSecurityAuditReport report, string id)
        {
            System.Collections.Generic.IReadOnlyList<NetworkSecurityAuditIssue> issues = report.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                if (string.Equals(issues[i].Id, id, System.StringComparison.Ordinal))
                {
                    return issues[i].Severity;
                }
            }

            Assert.Fail("Audit issue not found: " + id);
            return NetworkSecurityAuditSeverity.Info;
        }
    }
}
