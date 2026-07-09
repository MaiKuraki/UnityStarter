using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.Networking;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    public static class NetworkBootstrapDiagnostics
    {
        private static readonly INetworkBootstrapChecker[] Checkers =
        {
            new CycloneRuntimeChecker(),
            new MirrorPackageChecker(),
            new MiragePackageChecker(),
            new BackendSdkPackageChecker()
        };

        private static readonly Dictionary<string, Type> TypeLookupCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        public static NetworkBootstrapReport Run(NetworkBootstrapPreset preset = null)
        {
            var context = new NetworkBootstrapContext(preset);
            var report = new NetworkBootstrapReport();

            for (int i = 0; i < Checkers.Length; i++)
            {
                Checkers[i].Run(context, report);
            }

            return report;
        }

        public static void LogReport(NetworkBootstrapReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            IReadOnlyList<NetworkBootstrapIssue> issues = report.Issues;
            if (issues.Count == 0)
            {
                Debug.Log("[network.bootstrap.ready] Network bootstrap diagnostics completed with no issues.");
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                NetworkBootstrapIssue issue = issues[i];
                string message = string.IsNullOrEmpty(issue.Action)
                    ? $"[{issue.Code}] {issue.Message}"
                    : $"[{issue.Code}] {issue.Message} {issue.Action}";

                switch (issue.Severity)
                {
                    case NetworkBootstrapIssueSeverity.Error:
                        Debug.LogError(message, issue.Context);
                        break;
                    case NetworkBootstrapIssueSeverity.Warning:
                        Debug.LogWarning(message, issue.Context);
                        break;
                    default:
                        Debug.Log(message, issue.Context);
                        break;
                }
            }
        }

        internal static void Add(
            NetworkBootstrapReport report,
            NetworkBootstrapIssueSeverity severity,
            string code,
            string message,
            string action = null,
            UnityEngine.Object context = null)
        {
            report.Add(new NetworkBootstrapIssue(severity, code, message, action, context));
        }

        internal static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            if (TypeLookupCache.TryGetValue(fullName, out Type cachedType))
                return cachedType;

            Type type = Type.GetType(fullName, false);
            if (type != null)
            {
                TypeLookupCache[fullName] = type;
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    TypeLookupCache[fullName] = type;
                    return type;
                }
            }

            TypeLookupCache[fullName] = null;
            return null;
        }

        internal static void FindSceneComponents(Type componentType, List<Component> results)
        {
            if (componentType == null)
                return;

            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(componentType);
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is not Component component)
                    continue;

                if (EditorUtility.IsPersistent(component))
                    continue;

                if (!component.gameObject.scene.IsValid())
                    continue;

                results.Add(component);
            }
        }

        internal static void FindSceneComponents<T>(List<T> results) where T : class
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

        internal static bool IsTypeLoaded(string fullName)
        {
            return FindType(fullName) != null;
        }
    }
}
