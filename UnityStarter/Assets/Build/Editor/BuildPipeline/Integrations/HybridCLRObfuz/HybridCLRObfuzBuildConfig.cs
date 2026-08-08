using UnityEngine;

namespace Build.Pipeline.Editor
{
    [HotUpdateProviderAuthoring(
        HybridCLRHotUpdateProviderIds.Obfuz,
        "HybridCLR + Obfuz",
        Description = "Compile HybridCLR assemblies and obfuscate hot-update DLLs through Obfuz4HybridCLR.",
        RequiredEditorTypeNames = new[]
        {
            "HybridCLR.Editor.Commands.PrebuildCommand",
            "Obfuz.Settings.ObfuzSettings",
            "Obfuz4HybridCLR.PrebuildCommandExt"
        },
        Order = 110)]
    [CreateAssetMenu(menuName = "CycloneGames/Build/Hot Update/HybridCLR + Obfuz")]
    public sealed class HybridCLRObfuzBuildConfig : HybridCLRBuildConfig
    {
        public override string ProviderId => HybridCLRHotUpdateProviderIds.Obfuz;

        internal override HybridCLRBuildVariant Variant =>
            HybridCLRBuildVariant.Obfuz;
    }
}
