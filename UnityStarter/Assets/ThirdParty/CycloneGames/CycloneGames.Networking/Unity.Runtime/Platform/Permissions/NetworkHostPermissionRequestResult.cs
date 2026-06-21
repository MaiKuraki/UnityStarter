namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Result of <see cref="INetworkHostPermissionService.RequestSystemConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="DeveloperMessage"/> is developer-facing guidance for logs and editor tooling; it is not
    /// localized and must not be shown verbatim to players.
    /// </remarks>
    public readonly struct NetworkHostPermissionRequestResult
    {
        public readonly NetworkHostPermissionRequestOutcome Outcome;
        public readonly string DeveloperMessage;

        public NetworkHostPermissionRequestResult(
            NetworkHostPermissionRequestOutcome outcome,
            string developerMessage)
        {
            Outcome = outcome;
            DeveloperMessage = developerMessage ?? string.Empty;
        }

        public bool Launched => Outcome == NetworkHostPermissionRequestOutcome.Launched;
    }
}
