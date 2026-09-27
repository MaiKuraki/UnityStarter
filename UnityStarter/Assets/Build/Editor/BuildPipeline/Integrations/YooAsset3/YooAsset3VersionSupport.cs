using System;
using System.Collections.Generic;
using System.Reflection;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Reflectively gates the YooAsset 3.x API surface that this integration compiles
    /// against. When the package is upgraded within the supported
    /// <c>[3.0.6,4.0.0)</c> range, the gated assembly still compiles, so a member that
    /// was renamed or removed would otherwise fail only at an arbitrary build step.
    /// This check reports every missing shape up front and fails closed.
    /// </summary>
    /// <remarks>
    /// Scope is limited to shapes this integration references. Runtime-only capability surfaces that
    /// other optional assemblies depend on belong to those assemblies' own gates: asserting them here
    /// would fail the build integration for API drift it cannot be affected by, and would pin this
    /// assembly's version floor to an unrelated consumer.
    /// </remarks>
    internal static class YooAsset3VersionSupport
    {
        private static readonly string[] RequiredPipelines =
        {
            "ScriptableBuildPipeline",
            "RawFileBuildPipeline",
            "ArchiveFileBuildPipeline"
        };

        private static readonly string[] RequiredBundledCopyOptions =
        {
            "None",
            "ClearAndCopyAll",
            "ClearAndCopyByTags",
            "OnlyCopyAll",
            "OnlyCopyByTags"
        };

        private static readonly string[] RequiredFileNameStyles =
        {
            "HashName",
            "BundleName",
            "BundleName_HashName"
        };

        private static readonly string[] RequiredBuildParameterProperties =
        {
            "BuildOutputRoot",
            "BundledFileRoot",
            "BuildPipeline",
            "BuildBundleType",
            "BuildTarget",
            "PackageName",
            "PackageVersion",
            "PackageNote",
            "ClearBuildCacheFiles",
            "UseAssetDependencyDB",
            "EnableSharePackRule",
            "SingleReferencedPackAlone",
            "VerifyBuildingResult",
            "FileNameStyle",
            "BundledCopyOption",
            "BundledCopyParams",
            "BundleEncryptor",
            "ManifestEncryptor",
            "ManifestDecryptor"
        };

        public static List<string> ValidateSupport(BuildIncrementality incrementality)
        {
            var failures = new List<string>();

            // YooAsset-3.0.6 EBuildPipeline.cs
            // This integration selects Scriptable, RawFile, and ArchiveFile pipelines;
            // their enum members must remain present and spelled exactly.
            foreach (string pipeline in RequiredPipelines)
            {
                if (!Enum.IsDefined(typeof(EBuildPipeline), pipeline))
                {
                    failures.Add(
                        $"YooAsset.Editor.EBuildPipeline no longer defines the '{pipeline}' member.");
                }
            }

            // YooAsset-3.0.6 IBuildPipeline.cs
            // BuildResult Run(BuildParameters, bool) is the single native build entry point.
            MethodInfo run = typeof(IBuildPipeline).GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Instance);
            ParameterInfo[] runParameters = run == null ? Array.Empty<ParameterInfo>() : run.GetParameters();
            if (run == null
                || runParameters.Length != 2
                || runParameters[0].ParameterType != typeof(BuildParameters)
                || runParameters[1].ParameterType != typeof(bool)
                || run.ReturnType != typeof(BuildResult))
            {
                failures.Add(
                    "YooAsset.Editor.IBuildPipeline no longer exposes BuildResult Run(BuildParameters, bool).");
            }

            // YooAsset-3.0.6 BuildParameters.cs
            // Every property below is written by YooAsset3BuildParameterFactory; each
            // must remain a public, writable property with the recorded type.
            foreach (string propertyName in RequiredBuildParameterProperties)
            {
                PropertyInfo property = typeof(BuildParameters).GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                {
                    failures.Add(
                        $"YooAsset.Editor.BuildParameters no longer exposes a writable '{propertyName}' property.");
                }
            }

            // YooAsset-3.0.6 EBundledCopyOption.cs
            // Bundled-copy mode is mapped 1:1 into BuildParameters.BundledCopyOption.
            foreach (string option in RequiredBundledCopyOptions)
            {
                if (!Enum.IsDefined(typeof(EBundledCopyOption), option))
                {
                    failures.Add(
                        $"YooAsset.Editor.EBundledCopyOption no longer defines the '{option}' member.");
                }
            }

            // YooAsset-3.0.6 EFileNameStyle.cs
            // Note: EFileNameStyle lives in the YooAsset (runtime) namespace, and its
            // "bundle name + hash" member is spelled BundleName_HashName.
            foreach (string style in RequiredFileNameStyles)
            {
                if (!Enum.IsDefined(typeof(EFileNameStyle), style))
                {
                    failures.Add(
                        $"YooAsset.EFileNameStyle no longer defines the '{style}' member.");
                }
            }

            // YooAsset-3.0.6
            //   IBundleEncryptor.cs, IManifestEncryptor.cs, IManifestDecryptor.cs
            // The cryptography boundary hands these interfaces to BuildParameters.
            if (!typeof(IBundleEncryptor).IsInterface)
            {
                failures.Add("YooAsset.IBundleEncryptor is no longer an interface.");
            }

            if (!typeof(IManifestEncryptor).IsInterface)
            {
                failures.Add("YooAsset.IManifestEncryptor is no longer an interface.");
            }

            if (!typeof(IManifestDecryptor).IsInterface)
            {
                failures.Add("YooAsset.IManifestDecryptor is no longer an interface.");
            }

            // Incremental uses the same native BuildParameters shapes as Clean (see the
            // integration README): both select a concrete pipeline and reuse YooAsset's
            // native build cache. There is no additional public API to verify for
            // Incremental; YooAssetSettings is internal and DefaultBuildPipeline is a
            // per-pipeline private method rather than a public type.
            return failures;
        }
    }
}
