using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Networking.Platform
{
    /// <summary>
    /// Returns a fixed readiness result for platforms where this helper cannot launch an automated
    /// request (the user must configure the system manually, or the platform cannot host at all).
    /// </summary>
    public sealed class StaticNetworkHostPermissionService : INetworkHostPermissionService
    {
        private readonly NetworkHostPermissionCheckResult _result;
        private readonly string _requestMessage;

        public StaticNetworkHostPermissionService(NetworkHostPermissionCheckResult result, string requestMessage)
        {
            _result = result;
            _requestMessage = requestMessage ?? string.Empty;
        }

        public NetworkHostPermissionCheckResult GetStatus(int port, NetworkTransportProtocol protocol)
        {
            if (!NetworkPortUtility.IsValidPort(port))
            {
                return NetworkPortUtility.CreateInvalidPortResult(port, _result.PlatformName);
            }

            return _result;
        }

        public NetworkHostPermissionRequestResult RequestSystemConfiguration(int port, NetworkTransportProtocol protocol)
        {
            if (!NetworkPortUtility.IsValidPort(port))
            {
                return NetworkPortUtility.CreateInvalidPortRequestResult(port);
            }

            return new NetworkHostPermissionRequestResult(
                NetworkHostPermissionRequestOutcome.NotApplicable,
                _requestMessage);
        }

        public UniTask<NetworkHostPermissionCheckResult> RefreshStatusAsync(
            int port,
            NetworkTransportProtocol protocol,
            CancellationToken cancellationToken = default)
        {
            // Platforms without an automated check cannot verify live OS state; report the static assessment.
            return UniTask.FromResult(GetStatus(port, protocol));
        }
    }
}
