using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    public sealed class NetworkBootstrapIssue
    {
        public NetworkBootstrapIssue(
            NetworkBootstrapIssueSeverity severity,
            string code,
            string message,
            string action,
            UnityEngine.Object context)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Action = action ?? string.Empty;
            Context = context;
        }

        public NetworkBootstrapIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Action { get; }
        public UnityEngine.Object Context { get; }
    }
}
