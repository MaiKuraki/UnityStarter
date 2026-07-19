using CycloneGames.Networking;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal sealed class NetworkBootstrapContext
    {
        public readonly NetworkBootstrapPreset Preset;
        public readonly NetworkBackendFeatures RequiredFeatures;
        public readonly bool RequireCycloneTransport;
        public readonly bool RequireSingleMessageEndpoint;
        public readonly bool RequireRuntimeContextForMessageEndpoints;
        public readonly bool CheckOptionalSdkPackages;

        public NetworkBootstrapContext(NetworkBootstrapPreset preset)
        {
            Preset = preset;
            RequiredFeatures = preset != null ? preset.RequiredFeatures : NetworkBackendFeatures.RealtimeTransport;
            RequireCycloneTransport = preset == null || preset.RequireCycloneTransport;
            RequireSingleMessageEndpoint = preset == null || preset.RequireSingleMessageEndpoint;
            RequireRuntimeContextForMessageEndpoints = preset == null || preset.RequireRuntimeContextForMessageEndpoints;
            CheckOptionalSdkPackages = preset == null || preset.CheckOptionalSdkPackages;
        }
    }
}
