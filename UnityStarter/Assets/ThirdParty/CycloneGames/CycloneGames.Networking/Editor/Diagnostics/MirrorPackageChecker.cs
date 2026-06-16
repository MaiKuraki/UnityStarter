using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal sealed class MirrorPackageChecker : INetworkBootstrapChecker
    {
        private const string MirrorNetworkManagerType = "Mirror.NetworkManager";
        private const string MirrorTransportType = "Mirror.Transport";
        private const string MirrorAdapterType = "CycloneGames.Networking.Adapter.Mirror.MirrorNetAdapter";

        public void Run(NetworkBootstrapContext context, NetworkBootstrapReport report)
        {
            if (!context.CheckOptionalSdkPackages)
                return;

            Type networkManagerType = NetworkBootstrapDiagnostics.FindType(MirrorNetworkManagerType);
            Type transportType = NetworkBootstrapDiagnostics.FindType(MirrorTransportType);
            if (networkManagerType == null && transportType == null)
                return;

            var managers = new List<Component>(4);
            var transports = new List<Component>(4);
            var adapters = new List<Component>(4);
            NetworkBootstrapDiagnostics.FindSceneComponents(networkManagerType, managers);
            NetworkBootstrapDiagnostics.FindSceneComponents(transportType, transports);

            Type adapterType = NetworkBootstrapDiagnostics.FindType(MirrorAdapterType);
            if (adapterType != null)
                NetworkBootstrapDiagnostics.FindSceneComponents(adapterType, adapters);

            if (managers.Count == 0 && transports.Count == 0 && adapters.Count == 0)
                return;

            if (adapterType == null)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirror.adapter.missing",
                    "Mirror scene components were found, but CycloneGames Mirror adapter type was not found.",
                    "Ensure CycloneGames.Networking.Adapter.Mirror is enabled and compiled.");
                return;
            }

            if (managers.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Error,
                    "mirror.network_manager.missing",
                    "Mirror package is available, but no Mirror NetworkManager was found in the open scenes.",
                    "Add one Mirror NetworkManager to the network composition scene.");
            }

            if (transports.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Error,
                    "mirror.transport.missing",
                    "Mirror package is available, but no Mirror Transport component was found in the open scenes.",
                    "Add a Mirror transport such as KcpTransport and make sure it is assigned as the active transport.");
            }

            if (adapters.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Error,
                    "mirror.cyclone_adapter.missing",
                    "Mirror package is available, but no Cyclone MirrorNetAdapter was found in the open scenes.",
                    "Add MirrorNetAdapter to the network composition object so gameplay code talks through Cyclone interfaces.");
            }

            if (managers.Count > 1)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirror.network_manager.multiple",
                    "Multiple Mirror NetworkManager components were found in the open scenes.",
                    "Keep a single active Mirror NetworkManager unless the scene intentionally hosts isolated test runtimes.");
            }

            if (adapters.Count > 1)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirror.cyclone_adapter.multiple",
                    "Multiple Cyclone MirrorNetAdapter components were found in the open scenes.",
                    "Keep one MirrorNetAdapter per Mirror runtime.");
            }

            if (managers.Count == 1 && transports.Count > 0 && adapters.Count == 1)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Info,
                    "mirror.bootstrap.ready",
                    "Mirror bootstrap components were found for the open scenes.",
                    null,
                    adapters[0]);
            }
        }
    }
}
