using System.Collections.Generic;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal sealed class CycloneRuntimeChecker : INetworkBootstrapChecker
    {
        public void Run(NetworkBootstrapContext context, NetworkBootstrapReport report)
        {
            var transports = new List<INetTransport>(8);
            var managers = new List<INetworkManager>(8);
            var runtimeProviders = new List<INetworkRuntimeContextProvider>(8);

            NetworkBootstrapDiagnostics.FindSceneComponents(transports);
            NetworkBootstrapDiagnostics.FindSceneComponents(managers);
            NetworkBootstrapDiagnostics.FindSceneComponents(runtimeProviders);

            if (context.RequireCycloneTransport && transports.Count == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Error,
                    "cyclone.transport.missing",
                    "No Cyclone INetTransport component was found in the open scenes.",
                    "Add a concrete adapter component such as MirrorNetAdapter, MirageNetAdapter, Nakama adapter, or LocalLoop transport bootstrap.");
            }

            if (context.RequireSingleNetworkManager && managers.Count > 1)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Warning,
                    "cyclone.manager.multiple",
                    "Multiple Cyclone INetworkManager components were found in the open scenes.",
                    "Keep one composition root per active network runtime unless the scene intentionally hosts multiple independent runtimes.");
            }

            NetworkBackendFeatures features = NetworkBackendFeatures.None;
            for (int i = 0; i < transports.Count; i++)
            {
                INetTransport transport = transports[i];
                NetworkLifecycleSnapshot snapshot = NetworkLifecycle.GetSnapshot(transport);
                features |= snapshot.Features;

                if (!snapshot.IsAvailable)
                {
                    NetworkBootstrapDiagnostics.Add(
                        report,
                        NetworkBootstrapIssueSeverity.Error,
                        "cyclone.transport.unavailable",
                        "A Cyclone transport exists but is not available on the current platform or configuration.",
                        snapshot.LastErrorMessage,
                        transport as UnityEngine.Object);
                }

                if (snapshot.State == NetworkLifecycleState.Faulted)
                {
                    NetworkBootstrapDiagnostics.Add(
                        report,
                        NetworkBootstrapIssueSeverity.Error,
                        "cyclone.transport.faulted",
                        "A Cyclone transport reports a faulted lifecycle state.",
                        snapshot.LastErrorMessage,
                        transport as UnityEngine.Object);
                }
            }

            if (context.RequiredFeatures != NetworkBackendFeatures.None
                && (features & context.RequiredFeatures) != context.RequiredFeatures)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Error,
                    "cyclone.features.missing",
                    "The open scenes do not provide all required network backend features.",
                    $"Required: {context.RequiredFeatures}. Found: {features}.");
            }

            if (context.RequireRuntimeContextForCycloneManagers)
            {
                for (int i = 0; i < managers.Count; i++)
                {
                    if (managers[i] is INetworkRuntimeContextProvider provider && provider.RuntimeContext != null)
                        continue;

                    NetworkBootstrapDiagnostics.Add(
                        report,
                        NetworkBootstrapIssueSeverity.Warning,
                        "cyclone.runtime_context.missing",
                        "A Cyclone INetworkManager does not expose an initialized INetworkRuntimeContext.",
                        "Adapters should provide runtime context so gameplay systems can query features and services without concrete SDK references.",
                        managers[i] as UnityEngine.Object);
                }
            }

            if (transports.Count > 0 && report.ErrorCount == 0)
            {
                NetworkBootstrapDiagnostics.Add(
                    report,
                    NetworkBootstrapIssueSeverity.Info,
                    "cyclone.transport.detected",
                    $"Detected {transports.Count} Cyclone transport component(s) with features: {features}.",
                    null);
            }
        }
    }
}
