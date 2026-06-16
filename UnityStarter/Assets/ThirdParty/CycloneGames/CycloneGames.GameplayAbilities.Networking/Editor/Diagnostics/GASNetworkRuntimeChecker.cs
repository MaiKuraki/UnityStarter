using System.Collections.Generic;
using CycloneGames.Networking;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor.Diagnostics
{
    internal sealed class GASNetworkRuntimeChecker : IGASNetworkDiagnosticChecker
    {
        public void Run(GASNetworkDiagnosticsContext context, GASNetworkDiagnosticReport report)
        {
            if (context.RequireBridgeType
                && !GASNetworkDiagnostics.IsTypeLoaded("CycloneGames.GameplayAbilities.Networking.NetworkedAbilityBridge"))
            {
                GASNetworkDiagnostics.Add(
                    report,
                    GASNetworkDiagnosticSeverity.Error,
                    "gas.networking.bridge.missing",
                    "NetworkedAbilityBridge type was not found.",
                    "Ensure CycloneGames.GameplayAbilities.Networking.Core is enabled and compiled.");
            }

            if (context.RequireAbilitySystemRuntime
                && !GASNetworkDiagnostics.IsTypeLoaded("CycloneGames.GameplayAbilities.Runtime.AbilitySystemComponent"))
            {
                GASNetworkDiagnostics.Add(
                    report,
                    GASNetworkDiagnosticSeverity.Error,
                    "gas.runtime.asc.missing",
                    "AbilitySystemComponent type was not found.",
                    "Ensure CycloneGames.GameplayAbilities.Runtime is enabled and compiled.");
            }

            if (context.RequireCycloneNetworkRuntime
                && !GASNetworkDiagnostics.IsTypeLoaded("CycloneGames.Networking.INetworkManager"))
            {
                GASNetworkDiagnostics.Add(
                    report,
                    GASNetworkDiagnosticSeverity.Error,
                    "networking.runtime.manager_contract.missing",
                    "Cyclone INetworkManager contract was not found.",
                    "Ensure CycloneGames.Networking.Core is enabled and compiled.");
            }

            if (context.WarnWhenNoNetworkManagerInOpenScenes)
            {
                var managers = new List<INetworkManager>(4);
                FindSceneComponents(managers);
                if (managers.Count == 0)
                {
                    GASNetworkDiagnostics.Add(
                        report,
                        GASNetworkDiagnosticSeverity.Warning,
                        "gas.networking.manager.scene_missing",
                        "No Cyclone INetworkManager component was found in the open scenes.",
                        "GAS networking can still be composed from code or DI, but scene-driven bootstraps need an INetworkManager provider.");
                }
                else
                {
                    GASNetworkDiagnostics.Add(
                        report,
                        GASNetworkDiagnosticSeverity.Info,
                        "gas.networking.manager.detected",
                        $"Detected {managers.Count} Cyclone INetworkManager component(s) in the open scenes.",
                        null,
                        managers[0] as UnityEngine.Object);
                }
            }
        }

        private static void FindSceneComponents<T>(List<T> results) where T : class
        {
            MonoBehaviour[] components = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null)
                    continue;

                if (EditorUtility.IsPersistent(component))
                    continue;

                if (!component.gameObject.scene.IsValid())
                    continue;

                if (component is T typed)
                    results.Add(typed);
            }
        }
    }
}
