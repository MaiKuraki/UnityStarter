namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    internal sealed class GASNetworkOptionalSdkChecker : IGASNetworkDiagnosticChecker
    {
        public void Run(GASNetworkDiagnosticsContext context, GASNetworkDiagnosticReport report)
        {
            if (!context.CheckOptionalSdkPackages)
                return;

            if (GASNetworkDiagnostics.IsTypeLoaded("Mirror.NetworkManager"))
            {
                GASNetworkDiagnostics.Add(
                    report,
                    GASNetworkDiagnosticSeverity.Info,
                    "mirror.package.detected",
                    "Mirror SDK is loaded. GAS networking should still be connected through Cyclone networking interfaces.",
                    null);
            }

            if (GASNetworkDiagnostics.IsTypeLoaded("Nakama.Client"))
            {
                GASNetworkDiagnostics.Add(
                    report,
                    GASNetworkDiagnosticSeverity.Info,
                    "nakama.package.detected",
                    "Nakama SDK is loaded. Use it for account/session/matchmaking flows and keep GAS replication behind Cyclone interfaces.",
                    null);
            }
        }
    }
}
