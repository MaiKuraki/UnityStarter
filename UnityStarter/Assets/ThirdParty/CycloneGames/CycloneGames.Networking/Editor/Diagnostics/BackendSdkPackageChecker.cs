namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal sealed class BackendSdkPackageChecker : INetworkBootstrapChecker
    {
        public void Run(NetworkBootstrapContext context, NetworkBootstrapReport report)
        {
            if (!context.CheckOptionalSdkPackages)
                return;

            if (NetworkBootstrapDiagnostics.IsTypeLoaded("Nakama.Client"))
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Info,
                    "nakama.package.detected",
                    "Nakama SDK is loaded. It should be connected through Cyclone backend service abstractions rather than hard dependencies in gameplay modules.",
                    null);
            }

            if (NetworkBootstrapDiagnostics.IsTypeLoaded("Best.HTTP.HTTPRequest"))
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Info,
                    "best_http.package.detected",
                    "Best HTTP is loaded. It is suitable for backend HTTP/RPC/download flows, not as the default realtime gameplay transport.",
                    null);
            }
        }
    }
}
