using System;
using System.IO;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    internal sealed class YooAsset3BuildPlan
    {
        public YooAsset3BuildPlan(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            YooAsset3PackageBuildPlan[] packages,
            string[] warnings)
        {
            ProjectRoot = projectRoot;
            BuildOutputRoot = buildOutputRoot;
            BundledFileRoot = bundledFileRoot;
            Packages = packages;
            Warnings = warnings;
        }

        public string ProjectRoot { get; }
        public string BuildOutputRoot { get; }
        public string BundledFileRoot { get; }
        public YooAsset3PackageBuildPlan[] Packages { get; }
        public string[] Warnings { get; }
    }

    internal sealed class YooAsset3PackageBuildPlan
    {
        // Hash-named YooAsset bundles and their extensions fit this reservation.
        // Package metadata names are validated exactly below because they include
        // the configurable package name, version, and YooAsset file prefix.
        private const int YooAssetGeneratedChildPathReserve = 64;
        private readonly IBuildPipeline pipeline;

        public YooAsset3PackageBuildPlan(
            YooAssetPackageProfile profile,
            BuildParameters parameters,
            IBuildPipeline pipeline,
            string bundledCopyParams)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            BundledCopyParams = bundledCopyParams ?? string.Empty;
            OutputPackageDirectory = BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                System.IO.Path.GetFullPath(parameters.GetPackageOutputDirectory()),
                $"YooAsset package output directory '{parameters.PackageName}'",
                1 + YooAssetGeneratedChildPathReserve);
            BundledPackageDirectory = BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                System.IO.Path.GetFullPath(parameters.GetBundledRootDirectory()),
                $"YooAsset bundled package directory '{parameters.PackageName}'",
                1 + YooAssetGeneratedChildPathReserve);
            ValidateKnownArtifactPathBudgets(
                OutputPackageDirectory,
                parameters.PackageName,
                parameters.PackageVersion,
                "output");
            ValidateKnownArtifactPathBudgets(
                BundledPackageDirectory,
                parameters.PackageName,
                parameters.PackageVersion,
                "bundled");
        }

        public YooAssetPackageProfile Profile { get; }
        public BuildParameters Parameters { get; }
        public string PackageName => Parameters.PackageName;
        public string PackageVersion => Parameters.PackageVersion;
        public string OutputPackageDirectory { get; }
        public string BundledPackageDirectory { get; }
        public string BundledCopyParams { get; }

        public BuildResult Run()
        {
            return pipeline.Run(Parameters, true);
        }

        private static void ValidateKnownArtifactPathBudgets(
            string directory,
            string packageName,
            string packageVersion,
            string role)
        {
            string[] fileNames =
            {
                YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion),
                YooAssetConfiguration.GetManifestBinaryFileName(packageName, packageVersion),
                YooAssetConfiguration.GetPackageHashFileName(packageName, packageVersion),
                YooAssetConfiguration.GetPackageVersionFileName(packageName)
            };
            foreach (string fileName in fileNames)
            {
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    Path.Combine(directory, fileName),
                    $"YooAsset {role} package artifact '{fileName}'");
            }
        }
    }

    internal static class YooAsset3BuildParameterFactory
    {
        private const int ArchiveFileAlignment = 4;

        public static YooAsset3PackageBuildPlan Create(
            AssetContentBuildRequest request,
            YooAssetPackageProfile profile,
            string buildOutputRoot,
            string bundledFileRoot,
            string bundledCopyParams,
            string packageOutputDirectoryOverride = null,
            string bundledPackageDirectoryOverride = null)
        {
            BuildParameters parameters;
            IBuildPipeline pipeline;

            switch (profile.buildPipeline)
            {
                case YooAssetBuildPipelineKind.Scriptable:
                    var scriptableParameters = new TransactionalScriptableBuildParameters(
                        packageOutputDirectoryOverride,
                        bundledPackageDirectoryOverride)
                    {
                        CompressOption = MapCompression(profile.compression),
                        BuiltinShadersBundleName = GetBuiltinShaderBundleName(profile.packageName)
                    };
                    parameters = scriptableParameters;
                    pipeline = new ScriptableBuildPipeline();
                    SetCommonParameters(
                        parameters,
                        request,
                        profile,
                        buildOutputRoot,
                        bundledFileRoot,
                        bundledCopyParams,
                        EBuildPipeline.ScriptableBuildPipeline,
                        EBundleType.AssetBundle);
                    break;

                case YooAssetBuildPipelineKind.RawFile:
                    parameters = new TransactionalRawFileBuildParameters(
                        packageOutputDirectoryOverride,
                        bundledPackageDirectoryOverride);
                    pipeline = new RawFileBuildPipeline();
                    SetCommonParameters(
                        parameters,
                        request,
                        profile,
                        buildOutputRoot,
                        bundledFileRoot,
                        bundledCopyParams,
                        EBuildPipeline.RawFileBuildPipeline,
                        EBundleType.RawBundle);
                    break;

                case YooAssetBuildPipelineKind.Legacy:
                    parameters = new TransactionalLegacyBuildParameters(
                        packageOutputDirectoryOverride,
                        bundledPackageDirectoryOverride)
                    {
                        CompressOption = MapCompression(profile.compression)
                    };
                    pipeline = new LegacyBuildPipeline();
                    SetCommonParameters(
                        parameters,
                        request,
                        profile,
                        buildOutputRoot,
                        bundledFileRoot,
                        bundledCopyParams,
                        EBuildPipeline.LegacyBuildPipeline,
                        EBundleType.AssetBundle);
                    break;

                case YooAssetBuildPipelineKind.ArchiveFile:
                    parameters = new TransactionalArchiveFileBuildParameters(
                        packageOutputDirectoryOverride,
                        bundledPackageDirectoryOverride)
                    {
                        FileAlignment = ArchiveFileAlignment
                    };
                    pipeline = new ArchiveFileBuildPipeline();
                    SetCommonParameters(
                        parameters,
                        request,
                        profile,
                        buildOutputRoot,
                        bundledFileRoot,
                        bundledCopyParams,
                        EBuildPipeline.ArchiveFileBuildPipeline,
                        EBundleType.ArchiveBundle);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(profile.buildPipeline),
                        profile.buildPipeline,
                        "Unsupported YooAsset build pipeline profile.");
            }

            return new YooAsset3PackageBuildPlan(profile, parameters, pipeline, bundledCopyParams);
        }

        private static void SetCommonParameters(
            BuildParameters parameters,
            AssetContentBuildRequest request,
            YooAssetPackageProfile profile,
            string buildOutputRoot,
            string bundledFileRoot,
            string bundledCopyParams,
            EBuildPipeline pipeline,
            EBundleType bundleType)
        {
            parameters.BuildOutputRoot = buildOutputRoot;
            parameters.BundledFileRoot = bundledFileRoot;
            parameters.BuildPipeline = pipeline.ToString();
            parameters.BuildBundleType = (int)bundleType;
            parameters.BuildTarget = request.BuildTarget;
            parameters.PackageName = profile.packageName;
            parameters.PackageVersion = request.PackageVersion;
            parameters.PackageNote = profile.packageNote.Trim();

            // YooAsset 3.0.5 couples this flag to deleting the whole package root,
            // including every historical version. Exact-version replacement is
            // handled by the adapter's guarded collision policy instead.
            parameters.ClearBuildCacheFiles = false;

            parameters.UseAssetDependencyDB = profile.useAssetDependencyDatabase;
            parameters.EnableSharePackRule = profile.enableSharePackRule;
            parameters.VerifyBuildingResult = profile.verifyBuildingResult;
            parameters.FileNameStyle = MapFileNameStyle(profile.fileNameStyle);
            parameters.BundledCopyOption = MapBundledCopyOption(profile.bundledCopyOption);
            parameters.BundledCopyParams = bundledCopyParams;
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            bool uniqueBundleName = BundleCollectorSettingData.Setting.UniqueBundleName;
            BundlePackRuleResult packRuleResult = DefaultBundlePackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        private static ECompressOption MapCompression(YooAssetCompression compression)
        {
            switch (compression)
            {
                case YooAssetCompression.Uncompressed:
                    return ECompressOption.Uncompressed;
                case YooAssetCompression.LZMA:
                    return ECompressOption.LZMA;
                case YooAssetCompression.LZ4:
                    return ECompressOption.LZ4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(compression), compression, "Unsupported compression profile.");
            }
        }

        private static EFileNameStyle MapFileNameStyle(YooAssetFileNameStyle fileNameStyle)
        {
            switch (fileNameStyle)
            {
                case YooAssetFileNameStyle.HashName:
                    return EFileNameStyle.HashName;
                case YooAssetFileNameStyle.BundleName:
                    return EFileNameStyle.BundleName;
                case YooAssetFileNameStyle.BundleNameAndHash:
                    return EFileNameStyle.BundleName_HashName;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fileNameStyle), fileNameStyle, "Unsupported file-name style.");
            }
        }

        private static EBundledCopyOption MapBundledCopyOption(YooAssetBundledCopyOption copyOption)
        {
            switch (copyOption)
            {
                case YooAssetBundledCopyOption.None:
                    return EBundledCopyOption.None;
                case YooAssetBundledCopyOption.ClearAndCopyAll:
                    return EBundledCopyOption.ClearAndCopyAll;
                case YooAssetBundledCopyOption.ClearAndCopyByTags:
                    return EBundledCopyOption.ClearAndCopyByTags;
                case YooAssetBundledCopyOption.OnlyCopyAll:
                    return EBundledCopyOption.OnlyCopyAll;
                case YooAssetBundledCopyOption.OnlyCopyByTags:
                    return EBundledCopyOption.OnlyCopyByTags;
                default:
                    throw new ArgumentOutOfRangeException(nameof(copyOption), copyOption, "Unsupported bundled-copy option.");
            }
        }

        private sealed class TransactionalScriptableBuildParameters : ScriptableBuildParameters
        {
            private readonly string packageOutputDirectoryOverride;
            private readonly string bundledPackageDirectoryOverride;

            public TransactionalScriptableBuildParameters(string packageOutput, string bundledOutput)
            {
                packageOutputDirectoryOverride = NormalizeOverride(packageOutput);
                bundledPackageDirectoryOverride = NormalizeOverride(bundledOutput);
            }

            protected override string GetPackageOutputDirectoryCore()
            {
                return packageOutputDirectoryOverride.Length == 0
                    ? base.GetPackageOutputDirectoryCore()
                    : packageOutputDirectoryOverride;
            }

            protected override string GetBundledRootDirectoryCore()
            {
                return bundledPackageDirectoryOverride.Length == 0
                    ? base.GetBundledRootDirectoryCore()
                    : bundledPackageDirectoryOverride;
            }
        }

        private sealed class TransactionalRawFileBuildParameters : RawFileBuildParameters
        {
            private readonly string packageOutputDirectoryOverride;
            private readonly string bundledPackageDirectoryOverride;

            public TransactionalRawFileBuildParameters(string packageOutput, string bundledOutput)
            {
                packageOutputDirectoryOverride = NormalizeOverride(packageOutput);
                bundledPackageDirectoryOverride = NormalizeOverride(bundledOutput);
            }

            protected override string GetPackageOutputDirectoryCore()
            {
                return packageOutputDirectoryOverride.Length == 0
                    ? base.GetPackageOutputDirectoryCore()
                    : packageOutputDirectoryOverride;
            }

            protected override string GetBundledRootDirectoryCore()
            {
                return bundledPackageDirectoryOverride.Length == 0
                    ? base.GetBundledRootDirectoryCore()
                    : bundledPackageDirectoryOverride;
            }
        }

        private sealed class TransactionalLegacyBuildParameters : LegacyBuildParameters
        {
            private readonly string packageOutputDirectoryOverride;
            private readonly string bundledPackageDirectoryOverride;

            public TransactionalLegacyBuildParameters(string packageOutput, string bundledOutput)
            {
                packageOutputDirectoryOverride = NormalizeOverride(packageOutput);
                bundledPackageDirectoryOverride = NormalizeOverride(bundledOutput);
            }

            protected override string GetPackageOutputDirectoryCore()
            {
                return packageOutputDirectoryOverride.Length == 0
                    ? base.GetPackageOutputDirectoryCore()
                    : packageOutputDirectoryOverride;
            }

            protected override string GetBundledRootDirectoryCore()
            {
                return bundledPackageDirectoryOverride.Length == 0
                    ? base.GetBundledRootDirectoryCore()
                    : bundledPackageDirectoryOverride;
            }
        }

        private sealed class TransactionalArchiveFileBuildParameters : ArchiveFileBuildParameters
        {
            private readonly string packageOutputDirectoryOverride;
            private readonly string bundledPackageDirectoryOverride;

            public TransactionalArchiveFileBuildParameters(string packageOutput, string bundledOutput)
            {
                packageOutputDirectoryOverride = NormalizeOverride(packageOutput);
                bundledPackageDirectoryOverride = NormalizeOverride(bundledOutput);
            }

            protected override string GetPackageOutputDirectoryCore()
            {
                return packageOutputDirectoryOverride.Length == 0
                    ? base.GetPackageOutputDirectoryCore()
                    : packageOutputDirectoryOverride;
            }

            protected override string GetBundledRootDirectoryCore()
            {
                return bundledPackageDirectoryOverride.Length == 0
                    ? base.GetBundledRootDirectoryCore()
                    : bundledPackageDirectoryOverride;
            }
        }

        private static string NormalizeOverride(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        }
    }
}
