using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    [CreateAssetMenu(menuName = GASNetworkEditorMenuPaths.DiagnosticsPreset, fileName = "GASNetworkDiagnosticsPreset")]
    public sealed class GASNetworkDiagnosticsPreset : ScriptableObject
    {
        [SerializeField] private bool _requireBridgeType = true;
        [SerializeField] private bool _requireAbilitySystemRuntime = true;
        [SerializeField] private bool _requireCycloneNetworkRuntime = true;
        [SerializeField] private bool _warnWhenNoNetworkManagerInOpenScenes = true;
        [SerializeField] private bool _checkOptionalSdkPackages = true;

        public bool RequireBridgeType => _requireBridgeType;
        public bool RequireAbilitySystemRuntime => _requireAbilitySystemRuntime;
        public bool RequireCycloneNetworkRuntime => _requireCycloneNetworkRuntime;
        public bool WarnWhenNoNetworkManagerInOpenScenes => _warnWhenNoNetworkManagerInOpenScenes;
        public bool CheckOptionalSdkPackages => _checkOptionalSdkPackages;
    }
}
