using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Platform service that reports LAN listen-server host readiness and, where supported, launches an
    /// automated request to configure the operating system (for example a Windows firewall rule).
    /// </summary>
    /// <remarks>
    /// Implementations are injectable: a project can supply a custom service (for example a native iOS
    /// Local Network adapter) to <see cref="NetworkHostPermissionProbe.SetPermissionService"/> or use it
    /// directly, instead of the platform default from <see cref="NetworkHostPermissionServiceFactory"/>.
    /// <see cref="GetStatus"/> must stay lightweight and synchronous; <see cref="RefreshStatusAsync"/> may
    /// query the live operating system state (a short out-of-process call) and must honor the token.
    /// </remarks>
    public interface INetworkHostPermissionService
    {
        NetworkHostPermissionCheckResult GetStatus(int port, NetworkTransportProtocol protocol);

        NetworkHostPermissionRequestResult RequestSystemConfiguration(int port, NetworkTransportProtocol protocol);

        /// <summary>
        /// Verify readiness against the live operating system state (for example reading whether the inbound
        /// firewall rule is actually present and enabled) and return a result with
        /// <see cref="NetworkHostPermissionCheckResult.IsVerified"/> set. Platforms that cannot verify return the
        /// same value as <see cref="GetStatus"/>. Must not block the calling thread on the underlying I/O.
        /// </summary>
        UniTask<NetworkHostPermissionCheckResult> RefreshStatusAsync(
            int port,
            NetworkTransportProtocol protocol,
            CancellationToken cancellationToken = default);
    }
}
