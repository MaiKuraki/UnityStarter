using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    [CreateAssetMenu(menuName = "CycloneGames/Networking/Bootstrap Preset", fileName = "NetworkBootstrapPreset")]
    public sealed class NetworkBootstrapPreset : ScriptableObject
    {
        [SerializeField] private NetworkBackendFeatures _requiredFeatures = NetworkBackendFeatures.RealtimeTransport;
        [SerializeField] private bool _requireCycloneTransport = true;
        [SerializeField] private bool _requireSingleNetworkManager = true;
        [SerializeField] private bool _requireRuntimeContextForCycloneManagers = true;
        [SerializeField] private bool _checkOptionalSdkPackages = true;

        public NetworkBackendFeatures RequiredFeatures => _requiredFeatures;
        public bool RequireCycloneTransport => _requireCycloneTransport;
        public bool RequireSingleNetworkManager => _requireSingleNetworkManager;
        public bool RequireRuntimeContextForCycloneManagers => _requireRuntimeContextForCycloneManagers;
        public bool CheckOptionalSdkPackages => _checkOptionalSdkPackages;
    }
}
