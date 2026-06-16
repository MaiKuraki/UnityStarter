using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    public static class GASNetworkDiagnostics
    {
        private static readonly IGASNetworkDiagnosticChecker[] Checkers =
        {
            new GASNetworkRuntimeChecker(),
            new GASNetworkOptionalSdkChecker()
        };

        public static GASNetworkDiagnosticReport Run(GASNetworkDiagnosticsPreset preset = null)
        {
            var context = new GASNetworkDiagnosticsContext(preset);
            var report = new GASNetworkDiagnosticReport();

            for (int i = 0; i < Checkers.Length; i++)
            {
                Checkers[i].Run(context, report);
            }

            return report;
        }

        public static void LogReport(GASNetworkDiagnosticReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            IReadOnlyList<GASNetworkDiagnosticIssue> issues = report.Issues;
            if (issues.Count == 0)
            {
                Debug.Log("[gas.networking.diagnostics.ready] GAS networking diagnostics completed with no issues.");
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                GASNetworkDiagnosticIssue issue = issues[i];
                string message = string.IsNullOrEmpty(issue.Action)
                    ? $"[{issue.Code}] {issue.Message}"
                    : $"[{issue.Code}] {issue.Message} {issue.Action}";

                switch (issue.Severity)
                {
                    case GASNetworkDiagnosticSeverity.Error:
                        Debug.LogError(message, issue.Context);
                        break;
                    case GASNetworkDiagnosticSeverity.Warning:
                        Debug.LogWarning(message, issue.Context);
                        break;
                    default:
                        Debug.Log(message, issue.Context);
                        break;
                }
            }
        }

        internal static void Add(
            GASNetworkDiagnosticReport report,
            GASNetworkDiagnosticSeverity severity,
            string code,
            string message,
            string action = null,
            UnityEngine.Object context = null)
        {
            report.Add(new GASNetworkDiagnosticIssue(severity, code, message, action, context));
        }

        internal static bool IsTypeLoaded(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return false;

            Type type = Type.GetType(fullName, false);
            if (type != null)
                return true;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetType(fullName, false) != null)
                    return true;
            }

            return false;
        }
    }
}
