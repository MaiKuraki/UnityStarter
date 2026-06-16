using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    public sealed class GASNetworkDiagnosticIssue
    {
        public GASNetworkDiagnosticIssue(
            GASNetworkDiagnosticSeverity severity,
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
                ? string.Concat("[", Code, "] ", Message)
                : string.Concat("[", Code, "] ", Message, "\n", Action);
        }

        public GASNetworkDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Action { get; }
        public UnityEngine.Object Context { get; }
        public string DisplayText { get; }
    }
}
