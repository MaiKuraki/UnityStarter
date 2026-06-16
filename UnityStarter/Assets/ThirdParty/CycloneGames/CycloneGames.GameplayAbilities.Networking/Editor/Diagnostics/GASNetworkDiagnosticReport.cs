using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    public sealed class GASNetworkDiagnosticReport
    {
        private readonly List<GASNetworkDiagnosticIssue> _issues = new List<GASNetworkDiagnosticIssue>(32);

        public IReadOnlyList<GASNetworkDiagnosticIssue> Issues => _issues;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public int InfoCount { get; private set; }

        public void Add(GASNetworkDiagnosticIssue issue)
        {
            if (issue == null)
                throw new ArgumentNullException(nameof(issue));

            _issues.Add(issue);
            switch (issue.Severity)
            {
                case GASNetworkDiagnosticSeverity.Error:
                    ErrorCount++;
                    break;
                case GASNetworkDiagnosticSeverity.Warning:
                    WarningCount++;
                    break;
                default:
                    InfoCount++;
                    break;
            }
        }
    }
}
