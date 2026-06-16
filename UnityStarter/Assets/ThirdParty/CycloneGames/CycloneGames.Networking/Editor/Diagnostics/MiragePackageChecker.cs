using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal sealed class MiragePackageChecker : INetworkBootstrapChecker
    {
        private const string MirageServerType = "Mirage.NetworkServer";
        private const string MirageClientType = "Mirage.NetworkClient";
        private const string MirageAdapterType = "CycloneGames.Networking.Adapter.Mirage.MirageNetAdapter";

        public void Run(NetworkBootstrapContext context, NetworkBootstrapReport report)
        {
            if (!context.CheckOptionalSdkPackages)
                return;

            Type serverType = NetworkBootstrapDiagnostics.FindType(MirageServerType);
            Type clientType = NetworkBootstrapDiagnostics.FindType(MirageClientType);
            if (serverType == null && clientType == null)
                return;

            var servers = new List<Component>(4);
            var clients = new List<Component>(4);
            var adapters = new List<Component>(4);
            NetworkBootstrapDiagnostics.FindSceneComponents(serverType, servers);
            NetworkBootstrapDiagnostics.FindSceneComponents(clientType, clients);

            Type adapterType = NetworkBootstrapDiagnostics.FindType(MirageAdapterType);
            if (adapterType != null)
                NetworkBootstrapDiagnostics.FindSceneComponents(adapterType, adapters);

            if (servers.Count == 0 && clients.Count == 0 && adapters.Count == 0)
                return;

            if (adapterType == null)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirage.adapter.missing",
                    "Mirage scene components were found, but CycloneGames Mirage adapter type was not found.",
                    "Ensure CycloneGames.Networking.Adapter.Mirage is enabled and compiled.");
                return;
            }

            if (adapters.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirage.cyclone_adapter.missing",
                    "Mirage package is available, but no Cyclone MirageNetAdapter was found in the open scenes.",
                    "Add MirageNetAdapter only when this scene is intended to run through Mirage.");
            }

            if (servers.Count == 0 && clients.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "mirage.runtime_components.missing",
                    "Mirage package is available, but no Mirage NetworkServer or NetworkClient was found in the open scenes.",
                    "This is acceptable for non-Mirage scenes. Add Mirage runtime components when using Mirage as the active transport.");
            }
        }
    }
}
