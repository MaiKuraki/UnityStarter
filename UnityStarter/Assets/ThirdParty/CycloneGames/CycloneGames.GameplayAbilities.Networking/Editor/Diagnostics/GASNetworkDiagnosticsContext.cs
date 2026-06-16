namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    internal sealed class GASNetworkDiagnosticsContext
    {
        public readonly GASNetworkDiagnosticsPreset Preset;
        public readonly bool RequireBridgeType;
        public readonly bool RequireAbilitySystemRuntime;
        public readonly bool RequireCycloneNetworkRuntime;
        public readonly bool WarnWhenNoNetworkManagerInOpenScenes;
        public readonly bool CheckOptionalSdkPackages;

        public GASNetworkDiagnosticsContext(GASNetworkDiagnosticsPreset preset)
        {
            Preset = preset;
            RequireBridgeType = preset == null || preset.RequireBridgeType;
            RequireAbilitySystemRuntime = preset == null || preset.RequireAbilitySystemRuntime;
            RequireCycloneNetworkRuntime = preset == null || preset.RequireCycloneNetworkRuntime;
            WarnWhenNoNetworkManagerInOpenScenes = preset == null || preset.WarnWhenNoNetworkManagerInOpenScenes;
            CheckOptionalSdkPackages = preset == null || preset.CheckOptionalSdkPackages;
        }
    }
}
