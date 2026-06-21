namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Creates the platform-default <see cref="INetworkHostPermissionService"/> for the current build/editor target.
    /// </summary>
    /// <remarks>
    /// This factory is intentionally stateless: there is no global mutable override. To customize behavior
    /// (for example a native iOS Local Network adapter, or a Linux service that shells out to a firewall tool),
    /// construct your own <see cref="INetworkHostPermissionService"/> and inject it where it is used
    /// (see <see cref="NetworkHostPermissionProbe.SetPermissionService"/>) instead of mutating a global default.
    /// </remarks>
    public static class NetworkHostPermissionServiceFactory
    {
        public const string DEFAULT_RULE_DISPLAY_NAME_PREFIX = "CycloneGames LAN Host";

        public static INetworkHostPermissionService CreateDefault(string ruleDisplayNamePrefix = DEFAULT_RULE_DISPLAY_NAME_PREFIX)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return new WindowsNetworkHostPermissionService(ruleDisplayNamePrefix);
#elif UNITY_WEBGL
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.Unsupported,
                    false,
                    false,
                    "WebGL",
                    "WebGL builds cannot host a LAN listen server or receive UDP LAN discovery packets. Use a native host build, a relay, or a browser-compatible backend."),
                "WebGL cannot request operating system firewall permissions.");
#elif UNITY_IOS
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.CanHost,
                    true,
                    false,
                    "iOS",
                    "iOS can use local networking only when the app declares a local network usage description and the player grants the Local Network permission. The app cannot edit system firewall rules at runtime. Inject a native Local Network adapter via SetPermissionService to trigger or read that permission."),
                "iOS does not allow apps to edit system firewall rules at runtime.");
#elif UNITY_ANDROID
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.CanHost,
                    true,
                    false,
                    "Android",
                    "Android can join or host local network sessions when the build declares the required network permissions. The app cannot edit router, hotspot, or system firewall settings at runtime. Inject a native adapter via SetPermissionService for nearby/Wi-Fi permission flows."),
                "Android does not allow apps to edit router, hotspot, or system firewall settings at runtime.");
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.CanHost,
                    true,
                    false,
                    "macOS",
                    "macOS can host LAN sessions, but the app should rely on the system firewall prompt or user-managed firewall settings. Automatic firewall rule editing is not performed by this helper."),
                "macOS firewall permissions must be handled by the system prompt or user-managed settings.");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.CanHost,
                    true,
                    false,
                    "Linux",
                    "Linux can host LAN sessions, but firewall tools vary by distribution. Show the host IP and port, then let the user or event organizer manage firewall policy."),
                "Linux firewall permissions vary by distribution and are not edited by this helper.");
#else
            return new StaticNetworkHostPermissionService(
                new NetworkHostPermissionCheckResult(
                    NetworkHostPermissionStatus.Unsupported,
                    false,
                    false,
                    "Unknown",
                    "This platform is not known to support LAN listen-server hosting through this helper. Inject a platform-specific INetworkHostPermissionService if it does."),
                "This platform cannot request operating system firewall permissions through this helper.");
#endif
        }
    }
}
