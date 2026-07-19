using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    [CreateAssetMenu(menuName = "CycloneGames/Networking/Bootstrap Preset", fileName = "NetworkBootstrapPreset")]
    public sealed class NetworkBootstrapPreset : ScriptableObject
    {
        [SerializeField] private NetworkBackendFeatures _requiredFeatures = NetworkBackendFeatures.RealtimeTransport;
        [SerializeField] private bool _requireCycloneTransport = true;
        [SerializeField] private bool _requireSingleMessageEndpoint = true;
        [SerializeField] private bool _requireRuntimeContextForMessageEndpoints = true;
        [SerializeField] private bool _checkOptionalSdkPackages = true;

        public NetworkBackendFeatures RequiredFeatures => _requiredFeatures;
        public bool RequireCycloneTransport => _requireCycloneTransport;
        public bool RequireSingleMessageEndpoint => _requireSingleMessageEndpoint;
        public bool RequireRuntimeContextForMessageEndpoints => _requireRuntimeContextForMessageEndpoints;
        public bool CheckOptionalSdkPackages => _checkOptionalSdkPackages;
    }
}
