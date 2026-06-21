namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Result of a LAN-host readiness check for a specific port and protocol.
    /// </summary>
    /// <remarks>
    /// <see cref="DeveloperMessage"/> is developer-facing guidance for logs and editor tooling. It is not
    /// localized and must not be shown verbatim to players; surface project-localized copy instead.
    /// </remarks>
    public readonly struct NetworkHostPermissionCheckResult
    {
        public readonly NetworkHostPermissionStatus Status;

        /// <summary>True when the user or operating system likely still has to allow inbound traffic
        /// (firewall rule, OS privacy permission, or router/hotspot configuration) before peers can connect.</summary>
        public readonly bool RequiresSystemConfiguration;

        /// <summary>True only on platforms where this helper can launch an automated request
        /// (currently the Windows firewall rule flow). Otherwise the user must configure the system manually.</summary>
        public readonly bool CanRequestAutomatically;

        /// <summary>True when this result was produced by actually querying the operating system
        /// (for example <see cref="INetworkHostPermissionService.RefreshStatusAsync"/> reading the live firewall
        /// state), rather than a lightweight conservative assumption from <c>GetStatus</c>.</summary>
        public readonly bool IsVerified;

        public readonly string PlatformName;
        public readonly string DeveloperMessage;

        public NetworkHostPermissionCheckResult(
            NetworkHostPermissionStatus status,
            bool requiresSystemConfiguration,
            bool canRequestAutomatically,
            string platformName,
            string developerMessage)
            : this(status, requiresSystemConfiguration, canRequestAutomatically, false, platformName, developerMessage)
        {
        }

        public NetworkHostPermissionCheckResult(
            NetworkHostPermissionStatus status,
            bool requiresSystemConfiguration,
            bool canRequestAutomatically,
            bool isVerified,
            string platformName,
            string developerMessage)
        {
            Status = status;
            RequiresSystemConfiguration = requiresSystemConfiguration;
            CanRequestAutomatically = canRequestAutomatically;
            IsVerified = isVerified;
            PlatformName = platformName ?? string.Empty;
            DeveloperMessage = developerMessage ?? string.Empty;
        }

        public bool CanHostLan => Status == NetworkHostPermissionStatus.CanHost;
    }
}
