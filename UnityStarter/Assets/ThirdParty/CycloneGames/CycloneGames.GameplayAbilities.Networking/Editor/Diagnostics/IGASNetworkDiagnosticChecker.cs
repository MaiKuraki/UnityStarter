namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    internal interface IGASNetworkDiagnosticChecker
    {
        void Run(GASNetworkDiagnosticsContext context, GASNetworkDiagnosticReport report);
    }
}
