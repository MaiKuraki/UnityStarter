using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Limits repository analyzers to source below a verified Unity project Assets root.
    /// Package, generated, and otherwise unknown non-empty paths fail closed so external
    /// compiler inputs cannot break a consumer build through repository policy.
    /// </summary>
    /// <remarks>
    /// The ThirdParty allowlist names the in-repository CycloneGames layout
    /// (/Assets/ThirdParty/CycloneGames*/) by design. When CycloneGames packages are consumed as
    /// UPM packages (Packages/...), their source contains no /Assets/ segment and therefore falls
    /// outside repository ownership: host projects never police package code, and package-side
    /// governance belongs to the package repository's own analyzer build.
    /// </remarks>
    internal static class AnalyzerSourceScope
    {
        private sealed class CachedOwnership
        {
            internal CachedOwnership(bool isRepositoryOwned)
            {
                IsRepositoryOwned = isRepositoryOwned;
            }

            internal bool IsRepositoryOwned { get; }
        }

        private const int MAX_PROJECT_ROOT_CACHE_ENTRIES = 32;
        private const long MAX_PROJECT_VERSION_BYTES = 16 * 1024;
        private const string ASSETS_PREFIX = "Assets/";
        private const string ASSETS_SEGMENT = "/Assets/";
        // Layout policy, not project-name coupling: only the in-repository CycloneGames folders are
        // governed under Assets/ThirdParty. UPM package sources (Packages/...) fail closed above.
        private const string THIRD_PARTY_SEGMENT = "/Assets/ThirdParty/";
        private const string CYCLONEGAMES_SEGMENT = "/Assets/ThirdParty/CycloneGames/";
        private const string MEMORY_GOVERNANCE_SEGMENT =
            "/Assets/ThirdParty/CycloneGames.MemoryGovernance/";
        private const string PROJECT_VERSION_MARKER = "m_EditorVersion:";

        private static readonly ConditionalWeakTable<SyntaxTree, CachedOwnership>
            OwnershipBySyntaxTree = new ConditionalWeakTable<SyntaxTree, CachedOwnership>();
        private static readonly ConditionalWeakTable<SyntaxTree, CachedOwnership>.CreateValueCallback
            CreateCachedOwnershipCallback = CreateCachedOwnership;
        private static readonly StringComparison FileSystemPathComparison =
            GetPathComparison();
        private static readonly object ProjectRootCacheGate = new object();
        private static readonly Dictionary<string, bool> ProjectRootCache =
            new Dictionary<string, bool>(GetPathComparer());
        private static readonly Queue<string> ProjectRootCacheOrder = new Queue<string>();

        internal static bool IsRepositoryOwned(SyntaxTree syntaxTree)
        {
            if (syntaxTree == null)
            {
                return false;
            }

            return OwnershipBySyntaxTree
                .GetValue(syntaxTree, CreateCachedOwnershipCallback)
                .IsRepositoryOwned;
        }

        internal static bool IsRepositoryOwned(string? filePath)
        {
            return IsRepositoryOwned(filePath, Environment.CurrentDirectory);
        }

        internal static bool IsRepositoryOwned(
            string? filePath,
            string? relativeBaseDirectory)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                // Roslyn-focused tests can provide no physical path. Production compiler
                // inputs carry a path and therefore must pass the checks below.
                return true;
            }

            string slashPath = filePath!.Replace('\\', '/');
            string fullPath;
            try
            {
                string pathForResolution = filePath;
                if (!Path.IsPathRooted(filePath))
                {
                    if (!IsCanonicalRelativeAssetPath(
                            slashPath,
                            FileSystemPathComparison) ||
                        string.IsNullOrEmpty(relativeBaseDirectory) ||
                        !Path.IsPathRooted(relativeBaseDirectory))
                    {
                        return false;
                    }

                    pathForResolution = Path.Combine(relativeBaseDirectory!, filePath);
                }

                fullPath = Path.GetFullPath(pathForResolution);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException ||
                exception is System.Security.SecurityException)
            {
                return false;
            }

            string normalizedFullPath = fullPath.Replace('\\', '/');
            int assetsRootIndex = FindAssetsRootIndex(
                normalizedFullPath,
                FileSystemPathComparison);
            if (assetsRootIndex < 0)
            {
                return false;
            }

            string projectRoot = normalizedFullPath.Substring(0, assetsRootIndex);
            if (projectRoot.Length == 0)
            {
                projectRoot = "/";
            }
            else if (projectRoot.Length == 2 && projectRoot[1] == ':')
            {
                projectRoot += "/";
            }

            if (!HasVerifiedUnityProjectMarker(projectRoot))
            {
                return false;
            }

            string assetPath = normalizedFullPath.Substring(assetsRootIndex) + "/";
            return IsOwnedAssetPath(assetPath, FileSystemPathComparison);
        }

        internal static bool IsCanonicalRelativeAssetPath(
            string slashPath,
            StringComparison pathComparison)
        {
            if (!slashPath.StartsWith(ASSETS_PREFIX, pathComparison) ||
                slashPath.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            string[] segments = slashPath.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        internal static int FindAssetsRootIndex(
            string normalizedFullPath,
            StringComparison pathComparison)
        {
            return normalizedFullPath.LastIndexOf(
                ASSETS_SEGMENT,
                pathComparison);
        }

        internal static bool IsOwnedAssetPath(
            string normalizedAssetPath,
            StringComparison pathComparison)
        {
            if (!normalizedAssetPath.StartsWith(ASSETS_SEGMENT, pathComparison))
            {
                return false;
            }

            int thirdPartyIndex = normalizedAssetPath.IndexOf(
                THIRD_PARTY_SEGMENT,
                pathComparison);
            return thirdPartyIndex < 0 ||
                   normalizedAssetPath.IndexOf(
                       CYCLONEGAMES_SEGMENT,
                       pathComparison) >= 0 ||
                   normalizedAssetPath.IndexOf(
                       MEMORY_GOVERNANCE_SEGMENT,
                       pathComparison) >= 0;
        }

        private static bool HasVerifiedUnityProjectMarker(string projectRoot)
        {
            string canonicalRoot;
            try
            {
                canonicalRoot = Path.GetFullPath(projectRoot);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException ||
                exception is System.Security.SecurityException)
            {
                return false;
            }

            lock (ProjectRootCacheGate)
            {
                if (ProjectRootCache.TryGetValue(canonicalRoot, out bool cached))
                {
                    return cached;
                }
            }

            bool verified = VerifyUnityProjectMarker(canonicalRoot);
            lock (ProjectRootCacheGate)
            {
                if (ProjectRootCache.TryGetValue(canonicalRoot, out bool cached))
                {
                    return cached;
                }

                while (ProjectRootCache.Count >= MAX_PROJECT_ROOT_CACHE_ENTRIES)
                {
                    string expiredRoot = ProjectRootCacheOrder.Dequeue();
                    ProjectRootCache.Remove(expiredRoot);
                }

                ProjectRootCache.Add(canonicalRoot, verified);
                ProjectRootCacheOrder.Enqueue(canonicalRoot);
                return verified;
            }
        }

        private static bool VerifyUnityProjectMarker(string projectRoot)
        {
            try
            {
                string markerPath = Path.Combine(
                    projectRoot,
                    "ProjectSettings",
                    "ProjectVersion.txt");
                var marker = new FileInfo(markerPath);
                if (!marker.Exists ||
                    (marker.Attributes & (FileAttributes.Directory |
                                          FileAttributes.ReparsePoint |
                                          FileAttributes.Device)) != 0 ||
                    marker.Length <= 0 ||
                    marker.Length > MAX_PROJECT_VERSION_BYTES)
                {
                    return false;
                }

                string contents = File.ReadAllText(marker.FullName);
                return contents.IndexOf(
                           PROJECT_VERSION_MARKER,
                           StringComparison.Ordinal) >= 0;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is System.Security.SecurityException ||
                exception is NotSupportedException ||
                exception is ArgumentException)
            {
                return false;
            }
        }

        private static CachedOwnership CreateCachedOwnership(SyntaxTree syntaxTree)
        {
            return new CachedOwnership(IsRepositoryOwned(syntaxTree.FilePath));
        }

        private static StringComparer GetPathComparer()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
