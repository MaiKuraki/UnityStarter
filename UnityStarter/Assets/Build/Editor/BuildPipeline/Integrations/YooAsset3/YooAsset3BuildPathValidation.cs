using System;
using System.IO;
using Build.Pipeline.Editor.Integrations.YooAsset3Core;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Build-time path resolution and validation that depends on the YooAsset
    /// Editor assembly (<see cref="BundleBuilderHelper"/>) or the gated
    /// <see cref="YooAsset3PackageBuildPlan"/>. These methods must stay in the
    /// gated integration assembly; everything reachable from publication recovery
    /// lives in <c>YooAsset3BuildSafety</c> in the core assembly.
    /// </summary>
    internal static class YooAsset3BuildPathValidation
    {
        public static string ResolveBuildOutputRoot(string projectRoot, string configuredPath)
        {
            return YooAssetBuildRootPolicy.ResolveBuildOutputRoot(projectRoot, configuredPath);
        }

        public static string ResolveBundledFileRoot(string projectRoot, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                string defaultRoot = Path.GetFullPath(
                    BundleBuilderHelper.GetStreamingAssetsRoot());
                return YooAssetBuildRootPolicy.ValidateBundledFileRoot(
                    projectRoot,
                    defaultRoot);
            }

            return YooAssetBuildRootPolicy.ResolveConfiguredBundledFileRoot(
                projectRoot,
                configuredPath);
        }

        public static void ValidatePackageOutputPath(
            string buildOutputRoot,
            YooAsset3PackageBuildPlan packagePlan)
        {
            string packageRoot = Path.GetFullPath(packagePlan.Parameters.GetPackageRootDirectory());
            string outputDirectory = Path.GetFullPath(packagePlan.OutputPackageDirectory);
            string parentDirectory = Path.GetDirectoryName(outputDirectory);

            if (!YooAsset3BuildSafety.IsStrictDescendant(buildOutputRoot, packageRoot) ||
                string.IsNullOrEmpty(parentDirectory) ||
                !YooAsset3BuildSafety.PathsEqual(packageRoot, parentDirectory))
            {
                throw new InvalidOperationException(
                    $"Unsafe YooAsset version output path for package '{packagePlan.PackageName}': '{outputDirectory}'.");
            }
        }

        public static void ValidateBundledPackagePath(
            string projectRoot,
            string bundledFileRoot,
            YooAsset3PackageBuildPlan packagePlan)
        {
            string bundledPackageDirectory = Path.GetFullPath(packagePlan.BundledPackageDirectory);
            string parentDirectory = Path.GetDirectoryName(bundledPackageDirectory);
            if (string.IsNullOrEmpty(parentDirectory) ||
                !YooAsset3BuildSafety.PathsEqual(bundledFileRoot, parentDirectory) ||
                !YooAsset3BuildSafety.IsStrictDescendant(bundledFileRoot, bundledPackageDirectory))
            {
                throw new InvalidOperationException(
                    $"Unsafe YooAsset bundled package path for package '{packagePlan.PackageName}': '{bundledPackageDirectory}'.");
            }

            // YooAsset can delete or overwrite this directory depending on the
            // explicit bundled-copy option. Refuse path redirection before its
            // task receives control.
            YooAsset3BuildSafety.EnsureNoReparsePoints(projectRoot, bundledPackageDirectory);
        }
    }
}
