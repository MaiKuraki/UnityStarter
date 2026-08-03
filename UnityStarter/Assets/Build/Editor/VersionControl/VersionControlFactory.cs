using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public static class VersionControlFactory
    {
        public static IVersionControlProvider CreateDetectedProvider()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            IReadOnlyList<IVersionControlProviderDetector> available = DiscoverAvailableDetectors(projectRoot);
            if (available.Count == 0)
            {
                Debug.Log("[VC] No supported version control provider was detected.");
                return null;
            }

            IVersionControlProviderDetector winner = available[0];
            var ties = new List<IVersionControlProviderDetector> { winner };
            for (int index = 1; index < available.Count; index++)
            {
                IVersionControlProviderDetector candidate = available[index];
                if (candidate.Priority > winner.Priority)
                {
                    winner = candidate;
                    ties.Clear();
                    ties.Add(candidate);
                }
                else if (candidate.Priority == winner.Priority)
                {
                    ties.Add(candidate);
                }
            }

            if (ties.Count > 1)
            {
                var descriptions = new string[ties.Count];
                for (int index = 0; index < ties.Count; index++)
                {
                    descriptions[index] = $"{ties[index].ProviderId} ({ties[index].GetType().FullName})";
                }

                throw new InvalidOperationException(
                    $"Multiple version-control detectors matched at priority {winner.Priority}: " +
                    string.Join(", ", descriptions));
            }

            IVersionControlProvider provider = winner.Create(projectRoot);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Version-control detector '{winner.GetType().FullName}' returned no provider.");
            }

            Debug.Log($"[VC] Detected version control provider: {winner.ProviderId}");
            return provider;
        }

        private static IReadOnlyList<IVersionControlProviderDetector> DiscoverAvailableDetectors(
            string projectRoot)
        {
            var available = new List<IVersionControlProviderDetector>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IVersionControlProviderDetector>())
            {
                if (type.IsAbstract
                    || type.IsInterface
                    || type.ContainsGenericParameters
                    || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                IVersionControlProviderDetector detector;
                try
                {
                    detector = (IVersionControlProviderDetector)Activator.CreateInstance(type);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to create version-control detector '{type.FullName}'.",
                        exception);
                }

                if (string.IsNullOrWhiteSpace(detector.ProviderId))
                {
                    throw new InvalidOperationException(
                        $"Version-control detector '{type.FullName}' returned an empty provider identifier.");
                }

                try
                {
                    if (detector.IsAvailable(projectRoot))
                    {
                        available.Add(detector);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[VC] Detector '{detector.ProviderId}' could not evaluate the workspace and was skipped: {exception.Message}");
                }
            }

            return available;
        }

    }

    internal sealed class GitVersionControlProviderDetector : IVersionControlProviderDetector
    {
        public GitVersionControlProviderDetector()
        {
        }

        public string ProviderId => "Git";
        public int Priority => 100;

        public bool IsAvailable(string projectRoot)
        {
            return VersionControlProviderGit.FindGitRoot(projectRoot) != null;
        }

        public IVersionControlProvider Create(string projectRoot)
        {
            return new VersionControlProviderGit(projectRoot);
        }
    }

    internal sealed class PerforceVersionControlProviderDetector : IVersionControlProviderDetector
    {
        public PerforceVersionControlProviderDetector()
        {
        }

        public string ProviderId => "Perforce";
        public int Priority => 50;

        public bool IsAvailable(string projectRoot)
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("P4PORT"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("P4USER"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("P4CLIENT"));
        }

        public IVersionControlProvider Create(string projectRoot)
        {
            return new VersionControlProviderPerforce();
        }
    }
}
