using System;
using System.IO;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Defines provider configuration path rules without taking a dependency on
    /// YooAsset assemblies. Both the Inspector and the typed integration use
    /// this policy so authoring validation cannot drift from build preflight.
    /// </summary>
    internal static class YooAssetBuildRootPolicy
    {
        internal const string DefaultBuildOutputRoot = "Bundles";

        internal static string ResolveBuildOutputRoot(
            string projectRoot,
            string configuredPath)
        {
            string relativePath = NormalizeConfiguredRelativePath(
                configuredPath,
                DefaultBuildOutputRoot,
                "Build output root");
            return BuildPathPolicy.ResolveBuildRoot(projectRoot, relativePath);
        }

        internal static string ResolveConfiguredBundledFileRoot(
            string projectRoot,
            string configuredPath)
        {
            string relativePath = NormalizeConfiguredRelativePath(
                configuredPath,
                defaultPath: null,
                "Bundled file root");
            string resolved = BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                projectRoot,
                relativePath);
            return EnsureInsideStreamingAssets(projectRoot, resolved);
        }

        internal static string ValidateBundledFileRoot(
            string projectRoot,
            string resolvedPath)
        {
            string resolved = BuildPathPolicy.EnsureGeneratedAssetsDirectory(
                projectRoot,
                resolvedPath);
            return EnsureInsideStreamingAssets(projectRoot, resolved);
        }

        private static string EnsureInsideStreamingAssets(
            string projectRoot,
            string resolved)
        {
            string streamingAssetsRoot = Path.GetFullPath(
                Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            if (!FileSystemPathsEqual(streamingAssetsRoot, resolved)
                && !BuildPathPolicy.IsStrictDescendant(streamingAssetsRoot, resolved))
            {
                throw new InvalidOperationException(
                    $"Bundled file root must be inside StreamingAssets. Root: '{streamingAssetsRoot}', target: '{resolved}'.");
            }

            return resolved;
        }

        internal static void EnsureRootsDoNotOverlap(
            string buildOutputRoot,
            string bundledFileRoot)
        {
            if (PortablePathsEqual(buildOutputRoot, bundledFileRoot)
                || IsPortableStrictDescendant(buildOutputRoot, bundledFileRoot)
                || IsPortableStrictDescendant(bundledFileRoot, buildOutputRoot))
            {
                throw new InvalidOperationException(
                    $"Build output and bundled file roots must not overlap. Build root: '{buildOutputRoot}', bundled root: '{bundledFileRoot}'.");
            }
        }

        private static string NormalizeConfiguredRelativePath(
            string configuredPath,
            string defaultPath,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                if (defaultPath != null)
                {
                    return defaultPath;
                }

                throw new ArgumentException($"{displayName} is required.", nameof(configuredPath));
            }

            if (!string.Equals(configuredPath, configuredPath.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{displayName} must not start or end with whitespace.",
                    nameof(configuredPath));
            }

            return configuredPath.Replace('\\', '/');
        }

        private static bool FileSystemPathsEqual(string left, string right)
        {
            return string.Equals(
                NormalizeAbsolutePath(left),
                NormalizeAbsolutePath(right),
                FileSystemPathComparison);
        }

        private static bool PortablePathsEqual(string left, string right)
        {
            return string.Equals(
                NormalizeAbsolutePath(left),
                NormalizeAbsolutePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPortableStrictDescendant(
            string parentPath,
            string childPath)
        {
            string parent = NormalizeAbsolutePath(parentPath) + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAbsolutePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static StringComparison FileSystemPathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
