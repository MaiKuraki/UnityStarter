namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Outcome of asking the platform to configure host networking (for example a Windows firewall rule).
    /// </summary>
    /// <remarks>
    /// <see cref="Launched"/> means the request was started (the OS prompt was shown). It does not assert that
    /// the configuration succeeded: this helper never reads back the resulting firewall/OS state, so callers
    /// confirm success by establishing an actual connection from another peer.
    /// </remarks>
    public enum NetworkHostPermissionRequestOutcome
    {
        Unknown = 0,
        Launched = 1,
        NotApplicable = 2,
        InvalidInput = 3,
        Failed = 4
    }
}
