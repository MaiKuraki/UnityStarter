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
            DisplayText = string.IsNullOrEmpty(Action)
                ? $"[{Code}] {Message}"
                : $"[{Code}] {Message}\n{Action}";
        }

        public NetworkBootstrapIssueSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Action { get; }
        public string DisplayText { get; }
        public UnityEngine.Object Context { get; }
    }
}
