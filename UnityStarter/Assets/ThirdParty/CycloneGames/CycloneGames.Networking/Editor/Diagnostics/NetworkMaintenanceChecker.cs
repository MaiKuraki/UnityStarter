using System.Collections.Generic;
using CycloneGames.Networking.Maintenance;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    /// <summary>
    /// Thin Editor-side adapter for the Core maintenance coverage contract.
    /// All judgement logic lives in CycloneGames.Networking.Maintenance
    /// (NetworkMaintenanceCoordinator.Evaluate / NetworkMaintenanceRequirements),
    /// which is pure C# and verifiable without Unity. This checker only discovers
    /// scene components that implement the maintenance interfaces and surfaces
    /// actionable guidance; it cannot prove that a host calls
    /// Poll() or ReleaseConnection() at runtime.
    /// </summary>
    internal sealed class NetworkMaintenanceChecker : INetworkBootstrapChecker
    {
        public void Run(NetworkBootstrapContext context, NetworkBootstrapReport report)
        {
            var components = new List<MonoBehaviour>(64);
            NetworkBootstrapDiagnostics.FindSceneComponents(components);

            int periodicTargets = 0;
            int releaseTargets = 0;
            for (int i = 0; i < components.Count; i++)
            {
                MonoBehaviour component = components[i];
                if (component is INetworkPeriodicMaintenanceTarget)
                    periodicTargets++;
                if (component is INetworkConnectionReleaseTarget)
                    releaseTargets++;
            }

            if (periodicTargets == 0 && releaseTargets == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "MAINT-NO-TARGETS",
                    "No maintenance targets were found in the open scenes.",
                    "Register the required ids listed in NetworkMaintenanceRequirements with a NetworkMaintenanceCoordinator, call Poll(serverTick) once per network tick and ReleaseConnection(id) on disconnect.");
                return;
            }

            NetworkBootstrapDiagnostics.Add(
                report,
                NetworkBootstrapIssueSeverity.Info,
                "MAINT-TARGETS",
                "Found " + periodicTargets + " periodic and " + releaseTargets + " connection-release maintenance targets.",
                "Verify the coordinator covers every required id via NetworkMaintenanceCoordinator.Evaluate().");
        }
    }
}
