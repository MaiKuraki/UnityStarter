using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Networking.Platform
{
    public sealed class NetworkHostPermissionProbe : MonoBehaviour
    {
        [SerializeField] private int Port = 7777;
        [SerializeField] private NetworkTransportProtocol Protocol = NetworkTransportProtocol.Udp;
        [SerializeField] private string RuleDisplayNamePrefix = NetworkHostPermissionServiceFactory.DEFAULT_RULE_DISPLAY_NAME_PREFIX;
        [SerializeField] private bool LogOnStart = true;
        [SerializeField] private bool RequireGatewayForLocalAddresses = true;

        private readonly List<string> _localAddresses = new List<string>(4);
        private INetworkHostPermissionService _permissionService;

        public int HostPort => Port;

        public NetworkTransportProtocol HostProtocol => Protocol;

        public IReadOnlyList<string> LocalAddresses => _localAddresses;

        private void Awake()
        {
            EnsurePermissionService();
            RefreshLocalAddresses();
        }

        private void Start()
        {
            if (!LogOnStart)
            {
                return;
            }

            NetworkHostPermissionCheckResult status = GetStatus();
            Debug.Log($"LAN host permission status: {status.Status}. {status.DeveloperMessage}");

            if (_localAddresses.Count == 0)
            {
                Debug.LogWarning("No LAN IPv4 address was detected. Players may need to enter the host IP manually, or the machine may be isolated from the LAN.");
                return;
            }

            for (int i = 0; i < _localAddresses.Count; i++)
            {
                Debug.Log($"LAN host address candidate: {_localAddresses[i]}:{Port}/{Protocol}");
            }
        }

        /// <summary>
        /// Inject a custom permission service (for example a native iOS/Android adapter). Pass null to fall
        /// back to the platform default on the next access. Safe to call before or after Awake.
        /// </summary>
        public void SetPermissionService(INetworkHostPermissionService service)
        {
            _permissionService = service;
        }

        public NetworkHostPermissionCheckResult GetStatus()
        {
            EnsurePermissionService();
            return _permissionService.GetStatus(Port, Protocol);
        }

        public NetworkHostPermissionRequestResult RequestSystemConfiguration()
        {
            EnsurePermissionService();
            return _permissionService.RequestSystemConfiguration(Port, Protocol);
        }

        /// <summary>
        /// Verify readiness against live OS state (on Windows this reads the actual firewall rule). The returned
        /// result has <see cref="NetworkHostPermissionCheckResult.IsVerified"/> set on platforms that can verify.
        /// </summary>
        public UniTask<NetworkHostPermissionCheckResult> RefreshStatusAsync(CancellationToken cancellationToken = default)
        {
            EnsurePermissionService();
            return _permissionService.RefreshStatusAsync(Port, Protocol, cancellationToken);
        }

        public int RefreshLocalAddresses()
        {
            return NetworkLocalAddressUtility.GetLanIPv4Addresses(_localAddresses, RequireGatewayForLocalAddresses);
        }

        private void EnsurePermissionService()
        {
            if (_permissionService != null)
            {
                return;
            }

            _permissionService = NetworkHostPermissionServiceFactory.CreateDefault(RuleDisplayNamePrefix);
        }
    }
}
