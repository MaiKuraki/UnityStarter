using CycloneGames.Networking.Security;
using UnityEngine;

namespace CycloneGames.Networking.Unity.Runtime
{
    /// <summary>
    /// Unity bridge that runs <see cref="NetworkSecurityAudit"/> against a configured pipeline and
    /// surfaces findings through the Unity console. Call this from a server/client composition root
    /// after building <see cref="NetworkSecurityPipelineOptions"/> and before accepting traffic.
    /// </summary>
    /// <remarks>
    /// The release flag is derived from <see cref="Debug.isDebugBuild"/>: development builds and the
    /// Editor are treated as non-release so the audit does not block local iteration. The caller still
    /// supplies transport and role facts the engine cannot infer.
    /// </remarks>
    public static class NetworkSecurityStartupCheck
    {
        /// <summary>
        /// Evaluates the configuration and logs findings. Critical findings log as errors, warnings as
        /// warnings, and info as plain logs. When <paramref name="throwOnCritical"/> is true the call
        /// throws on any critical finding so a misconfigured server fails fast.
        /// </summary>
        public static NetworkSecurityAuditReport Run(
            NetworkSecurityPipelineOptions options,
            bool transportEncrypted,
            bool isServer,
            bool throwOnCritical = false)
        {
            var environment = new NetworkSecurityEnvironment(
                transportEncrypted,
                isReleaseBuild: !Debug.isDebugBuild,
                isServer);

            NetworkSecurityAuditReport report = NetworkSecurityAudit.Evaluate(options, environment);
            LogReport(report);

            if (throwOnCritical)
            {
                NetworkSecurityAudit.ThrowIfCritical(report);
            }

            return report;
        }

        private static void LogReport(NetworkSecurityAuditReport report)
        {
            if (!report.HasFindings)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<NetworkSecurityAuditIssue> issues = report.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                NetworkSecurityAuditIssue issue = issues[i];
                string line = issue.Recommendation.Length > 0
                    ? string.Concat("[NetworkSecurity] ", issue.Id, ": ", issue.Message, " -> ", issue.Recommendation)
                    : string.Concat("[NetworkSecurity] ", issue.Id, ": ", issue.Message);

                switch (issue.Severity)
                {
                    case NetworkSecurityAuditSeverity.Critical:
                        Debug.LogError(line);
                        break;
                    case NetworkSecurityAuditSeverity.Warning:
                        Debug.LogWarning(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
        }
    }
}
