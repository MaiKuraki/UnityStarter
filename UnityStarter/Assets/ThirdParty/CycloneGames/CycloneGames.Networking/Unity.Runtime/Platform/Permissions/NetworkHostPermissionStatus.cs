namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Whether the current platform can act as a LAN listen-server host.
    /// </summary>
    /// <remarks>
    /// This is a coarse capability classification, not a guarantee that a remote peer will connect:
    /// firewall, router, and OS privacy state cannot be read cheaply at runtime, so a positive
    /// <see cref="CanHost"/> is paired with <see cref="NetworkHostPermissionCheckResult.RequiresSystemConfiguration"/>
    /// to advise whether the user still needs to act. The enum intentionally has no "granted/ready" value,
    /// because this helper never verifies that inbound traffic actually reaches the process.
    /// </remarks>
    public enum NetworkHostPermissionStatus
    {
        Unknown = 0,
        CanHost = 1,
        Unsupported = 2,
        Failed = 3
    }
}
