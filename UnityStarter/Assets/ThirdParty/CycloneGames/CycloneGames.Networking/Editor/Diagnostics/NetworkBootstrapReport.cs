using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    public sealed class NetworkBootstrapReport
    {
        private readonly List<NetworkBootstrapIssue> _issues = new List<NetworkBootstrapIssue>(32);

        public IReadOnlyList<NetworkBootstrapIssue> Issues => _issues;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public int InfoCount { get; private set; }

        public void Add(NetworkBootstrapIssue issue)
        {
            if (issue == null)
                throw new ArgumentNullException(nameof(issue));

            _issues.Add(issue);
            switch (issue.Severity)
            {
                case NetworkBootstrapIssueSeverity.Error:
                    ErrorCount++;
                    break;
                case NetworkBootstrapIssueSeverity.Warning:
                    WarningCount++;
                    break;
                default:
                    InfoCount++;
                    break;
            }
        }
    }
}
