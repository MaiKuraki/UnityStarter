using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Version-gated YooAsset 3.x content build adapter discovered by the core pipeline through TypeCache.
    /// </summary>
    [AssetContentAdapterRegistration(AssetContentProviderIds.YooAsset, 100)]
    public sealed class YooAsset3BuildAdapter : IAssetContentBuildAdapter
    {
        private const int MaxPackageProfileCount = 128;
        private const int MaxCollectorPackageCount = 1024;
        private const int MaxPackageNoteLength = 512;
        private const int MaxProducedArtifactCount = 100000;

        public string ProviderId => AssetContentProviderIds.YooAsset;
        public int Priority => 100;

        public AssetContentBuildResult Validate(AssetContentBuildRequest request)
        {
            try
            {
                YooAsset3BuildPlan plan = CreateValidatedPlan(request);
                return AssetContentBuildResult.Success(
                    ProviderId,
                    GetPackageSummary(plan.Packages),
                    request.PackageVersion,
                    warnings: plan.Warnings);
            }
            catch (Exception exception)
            {
                return CreateValidationFailure(request, exception);
            }
        }

        public IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request)
        {
            string projectRoot;
            string buildOutputRoot;
            string bundledFileRoot;
            try
            {
                ResolveTransactionRoots(
                    request,
                    out projectRoot,
                    out buildOutputRoot,
                    out bundledFileRoot);
            }
            catch (Exception exception)
            {
                return new[] { CreateValidationFailure(request, exception) };
            }

            YooAsset3BuildLock buildLock;
            try
            {
                buildLock = YooAsset3BuildLock.Acquire(projectRoot, buildOutputRoot, bundledFileRoot);
            }
            catch (Exception exception)
            {
                return new[]
                {
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        string.Empty,
                        request.PackageVersion,
                        "TransactionLock",
                        exception.Message,
                        exception.ToString())
                };
            }

            using (buildLock)
            {
                try
                {
                    YooAsset3PublicationTransaction.RecoverPending(projectRoot, AssetDatabase.Refresh);
                }
                catch (YooAsset3CommittedPublicationException exception)
                {
                    return new[]
                    {
                        CreateCommittedRecoveryFailure(request, string.Empty, exception)
                    };
                }
                catch (Exception exception)
                {
                    return new[]
                    {
                        AssetContentBuildResult.Failure(
                            ProviderId,
                            string.Empty,
                            request.PackageVersion,
                            "TransactionRecovery",
                            exception.Message,
                            exception.ToString())
                    };
                }

                return BuildUnderLock(request);
            }
        }

        private IReadOnlyList<AssetContentBuildResult> BuildUnderLock(AssetContentBuildRequest request)
        {
            YooAsset3BuildPlan plan;
            try
            {
                plan = CreateValidatedPlan(request);
            }
            catch (Exception exception)
            {
                return new[] { CreateValidationFailure(request, exception) };
            }

            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan);
            var stagedPackages = new List<StagedPackageResult>(plan.Packages.Length);
            string activePackageName = string.Empty;
            var activeWarnings = new List<string>(plan.Warnings);
            try
            {
                transaction.Prepare();
                foreach (YooAsset3PackagePublication publication in transaction.Packages)
                {
                    YooAsset3PackageBuildPlan finalPlan = publication.FinalPlan;
                    activePackageName = finalPlan.PackageName;
                    activeWarnings = new List<string>(plan.Warnings);
                    YooAsset3PackageBuildPlan executionPlan = transaction.CreateExecutionPlan(request, publication);
                    executionPlan.Parameters.CheckBuildParameters();
                    YooAsset3BuildSafety.ValidatePackageOutputPath(plan.BuildOutputRoot, executionPlan);
                    YooAsset3BuildSafety.ValidateNoPathRedirection(
                        plan.ProjectRoot,
                        executionPlan.OutputPackageDirectory);

                    BuildResult buildResult;
                    try
                    {
                        buildResult = executionPlan.Run();
                    }
                    catch (Exception exception)
                    {
                        throw new YooAsset3BuildFailureException(
                            activePackageName,
                            "YooAssetPipeline",
                            exception.Message,
                            exception.ToString(),
                            exception);
                    }
                    if (buildResult == null)
                    {
                        throw new YooAsset3BuildFailureException(
                            activePackageName,
                            "YooAssetPipeline",
                            "YooAsset returned a null build result.");
                    }

                    if (!buildResult.Success)
                    {
                        throw new YooAsset3BuildFailureException(
                            activePackageName,
                            string.IsNullOrWhiteSpace(buildResult.FailedTask)
                                ? "YooAssetPipeline"
                                : buildResult.FailedTask,
                            string.IsNullOrWhiteSpace(buildResult.ErrorInfo)
                                ? "YooAsset reported a failed build without error details."
                                : buildResult.ErrorInfo,
                            buildResult.ErrorStack);
                    }

                    ValidateBuildResult(executionPlan, buildResult, activeWarnings);
                    stagedPackages.Add(new StagedPackageResult(
                        publication,
                        activeWarnings.ToArray()));
                }

                transaction.PrepareReadyDirectories();
                foreach (StagedPackageResult staged in stagedPackages)
                {
                    if (staged.Publication.BundledOperation != null)
                    {
                        ValidateBundledArtifacts(
                            staged.Publication.FinalPlan,
                            staged.Publication.BundledOperation.stage);
                    }
                }

                transaction.SealReadyDirectories();

                var publishedResults = new List<AssetContentBuildResult>(stagedPackages.Count);
                transaction.Commit(() =>
                {
                    foreach (StagedPackageResult staged in stagedPackages)
                    {
                        publishedResults.Add(CreatePublishedSuccessResult(
                            staged.Publication.FinalPlan,
                            staged.Warnings));
                    }
                }, AssetDatabase.Refresh);
                return publishedResults;
            }
            catch (YooAsset3CommittedPublicationException exception)
            {
                return new[]
                {
                    CreateCommittedRecoveryFailure(request, activePackageName, exception, activeWarnings)
                };
            }
            catch (Exception exception)
            {
                Exception failure = exception;
                try
                {
                    transaction.Abort();
                }
                catch (Exception rollbackException)
                {
                    failure = new AggregateException(
                        "YooAsset build failed and publication rollback did not complete.",
                        exception,
                        rollbackException);
                }

                if (exception is YooAsset3BuildFailureException buildFailure)
                {
                    return new[]
                    {
                        AssetContentBuildResult.Failure(
                            ProviderId,
                            buildFailure.PackageName,
                            request.PackageVersion,
                            buildFailure.FailedTask,
                            buildFailure.Message,
                            string.IsNullOrWhiteSpace(buildFailure.ErrorStack)
                                ? failure.ToString()
                                : buildFailure.ErrorStack + Environment.NewLine + failure,
                            activeWarnings.ToArray())
                    };
                }

                return new[]
                {
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        activePackageName,
                        request.PackageVersion,
                        "TransactionalPublication",
                        failure.Message,
                        failure.ToString(),
                        activeWarnings.ToArray())
                };
            }
        }

        private AssetContentBuildResult CreateCommittedRecoveryFailure(
            AssetContentBuildRequest request,
            string packageName,
            YooAsset3CommittedPublicationException exception,
            IReadOnlyList<string> warnings = null)
        {
            string journalSuffix = string.IsNullOrWhiteSpace(exception.JournalPath)
                ? string.Empty
                : $" Recovery journal: '{exception.JournalPath}'.";
            return AssetContentBuildResult.Failure(
                ProviderId,
                packageName,
                request.PackageVersion,
                "CommittedPublicationRecoveryRequired",
                exception.Message + journalSuffix,
                exception.ToString(),
                warnings);
        }

        private YooAsset3BuildPlan CreateValidatedPlan(AssetContentBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!(request.Configuration is YooAssetBuildConfig configuration))
            {
                string actualType = request.Configuration == null ? "null" : request.Configuration.GetType().FullName;
                throw new InvalidOperationException(
                    $"YooAsset provider requires {nameof(YooAssetBuildConfig)} configuration, but received '{actualType}'.");
            }

            if (request.BuildTarget == BuildTarget.NoTarget)
            {
                throw new InvalidOperationException("A concrete Unity build target is required for YooAsset content builds.");
            }

            YooAssetBuildTokenPolicy.ValidatePackageVersion(request.PackageVersion, nameof(request.PackageVersion));
            string projectRoot = YooAsset3BuildSafety.NormalizeProjectRoot(request.ProjectRoot);
            string buildOutputRoot = YooAsset3BuildSafety.ResolveBuildOutputRoot(projectRoot, configuration.buildOutputRoot);
            string bundledFileRoot = YooAsset3BuildSafety.ResolveBundledFileRoot(projectRoot, configuration.bundledFileRoot);
            YooAsset3BuildSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);

            if (File.Exists(buildOutputRoot))
            {
                throw new InvalidOperationException($"YooAsset build output root is an existing file: '{buildOutputRoot}'.");
            }

            if (File.Exists(bundledFileRoot))
            {
                throw new InvalidOperationException($"YooAsset bundled file root is an existing file: '{bundledFileRoot}'.");
            }

            ValidateCollectorSettings(out HashSet<string> collectorPackageNames);

            if (configuration.packages == null || configuration.packages.Length == 0)
            {
                throw new InvalidOperationException("YooAsset build configuration does not contain package profiles.");
            }

            if (configuration.packages.Length > MaxPackageProfileCount)
            {
                throw new InvalidOperationException(
                    $"YooAsset package profile count exceeds the safety limit of {MaxPackageProfileCount}.");
            }

            var packagePlans = new List<YooAsset3PackageBuildPlan>(configuration.packages.Length);
            var configuredPackageNames = new HashSet<string>(YooAsset3BuildSafety.PortablePathSegmentComparer);
            var warnings = new List<string>();
            if (request.Incrementality == BuildIncrementality.Clean)
            {
                warnings.Add(
                    "Clean mode does not enable YooAsset ClearBuildCacheFiles because YooAsset 3.0.5 deletes every historical package version when that flag is enabled.");
            }

            for (int index = 0; index < configuration.packages.Length; index++)
            {
                YooAssetPackageProfile profile = configuration.packages[index];
                if (profile == null)
                {
                    throw new InvalidOperationException($"YooAsset package profile at index {index} is null.");
                }

                if (!profile.enabled)
                {
                    continue;
                }

                try
                {
                    string bundledCopyParams = ValidateProfile(
                        profile,
                        collectorPackageNames,
                        configuredPackageNames);
                    ValidateGeneratedArtifactFileNames(profile.packageName, request.PackageVersion);
                    YooAsset3PackageBuildPlan packagePlan = YooAsset3BuildParameterFactory.Create(
                        request,
                        profile,
                        buildOutputRoot,
                        bundledFileRoot,
                        bundledCopyParams);

                    packagePlan.Parameters.CheckBuildParameters();
                    YooAsset3BuildSafety.ValidatePackageOutputPath(buildOutputRoot, packagePlan);
                    YooAsset3BuildSafety.ValidateNoPathRedirection(
                        projectRoot,
                        packagePlan.OutputPackageDirectory);
                    if (packagePlan.Parameters.BundledCopyOption != EBundledCopyOption.None)
                    {
                        YooAsset3BuildSafety.ValidateBundledPackagePath(
                            projectRoot,
                            bundledFileRoot,
                            packagePlan);
                    }
                    ValidateVersionCollision(packagePlan, warnings);
                    packagePlans.Add(packagePlan);
                }
                catch (Exception exception)
                {
                    throw new YooAsset3ProfileValidationException(profile.packageName, exception);
                }
            }

            if (packagePlans.Count == 0)
            {
                throw new InvalidOperationException(
                    "YooAsset content build was selected, but no package profile is enabled. Select AssetManagement None to skip content builds.");
            }

            return new YooAsset3BuildPlan(
                projectRoot,
                buildOutputRoot,
                bundledFileRoot,
                packagePlans.ToArray(),
                warnings.ToArray());
        }

        private static void ResolveTransactionRoots(
            AssetContentBuildRequest request,
            out string projectRoot,
            out string buildOutputRoot,
            out string bundledFileRoot)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!(request.Configuration is YooAssetBuildConfig configuration))
            {
                string actualType = request.Configuration == null ? "null" : request.Configuration.GetType().FullName;
                throw new InvalidOperationException(
                    $"YooAsset provider requires {nameof(YooAssetBuildConfig)} configuration, but received '{actualType}'.");
            }

            projectRoot = YooAsset3BuildSafety.NormalizeProjectRoot(request.ProjectRoot);
            buildOutputRoot = YooAsset3BuildSafety.ResolveBuildOutputRoot(projectRoot, configuration.buildOutputRoot);
            bundledFileRoot = YooAsset3BuildSafety.ResolveBundledFileRoot(projectRoot, configuration.bundledFileRoot);
            YooAsset3BuildSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, bundledFileRoot);
        }

        private static void ValidateCollectorSettings(out HashSet<string> collectorPackageNames)
        {
            string[] settingGuids = AssetDatabase.FindAssets($"t:{nameof(BundleCollectorSetting)}");
            if (settingGuids == null || settingGuids.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No {nameof(BundleCollectorSetting)} asset exists. Create and configure YooAsset Bundle Collector settings before building.");
            }

            if (settingGuids.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one {nameof(BundleCollectorSetting)} asset, but found {settingGuids.Length}. Remove ambiguous collector settings before building.");
            }

            string settingPath = AssetDatabase.GUIDToAssetPath(settingGuids[0]);
            BundleCollectorSetting loadedSetting = AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>(settingPath);
            if (loadedSetting == null)
            {
                throw new InvalidOperationException(
                    $"YooAsset Bundle Collector settings could not be loaded from '{settingPath}'.");
            }

            BundleCollectorSetting setting = BundleCollectorSettingData.Setting;
            if (setting == null || setting.Packages == null)
            {
                throw new InvalidOperationException("YooAsset Bundle Collector settings could not be loaded.");
            }
            if (setting != loadedSetting)
            {
                throw new InvalidOperationException(
                    "YooAsset Bundle Collector settings cache does not match the unique project asset. Reload scripts before building.");
            }

            if (setting.Packages.Count > MaxCollectorPackageCount)
            {
                throw new InvalidOperationException(
                    $"YooAsset collector package count exceeds the safety limit of {MaxCollectorPackageCount}.");
            }

            collectorPackageNames = new HashSet<string>(StringComparer.Ordinal);
            var fileSystemNames = new HashSet<string>(YooAsset3BuildSafety.PortablePathSegmentComparer);
            foreach (BundleCollectorPackage package in setting.Packages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.PackageName))
                {
                    throw new InvalidOperationException("YooAsset Bundle Collector settings contain a null or unnamed package.");
                }

                if (!collectorPackageNames.Add(package.PackageName))
                {
                    throw new InvalidOperationException(
                        $"YooAsset Bundle Collector settings contain duplicate package '{package.PackageName}'.");
                }

                if (!fileSystemNames.Add(package.PackageName))
                {
                    throw new InvalidOperationException(
                        $"YooAsset package names collide on case-insensitive file systems: '{package.PackageName}'.");
                }
            }
        }

        private static string ValidateProfile(
            YooAssetPackageProfile profile,
            HashSet<string> collectorPackageNames,
            HashSet<string> configuredPackageNames)
        {
            YooAssetBuildTokenPolicy.ValidatePackageName(profile.packageName, nameof(profile.packageName));
            if (!collectorPackageNames.Contains(profile.packageName))
            {
                throw new InvalidOperationException(
                    $"Package '{profile.packageName}' does not exist in YooAsset Bundle Collector settings.");
            }

            if (!configuredPackageNames.Add(profile.packageName))
            {
                throw new InvalidOperationException(
                    $"Enabled YooAsset package profiles collide on case-insensitive file systems: '{profile.packageName}'.");
            }

            if (string.IsNullOrWhiteSpace(profile.packageNote))
            {
                throw new InvalidOperationException(
                    $"Package '{profile.packageName}' requires a deterministic package note. Empty notes make YooAsset use the current time.");
            }

            if (profile.packageNote.Length > MaxPackageNoteLength)
            {
                throw new InvalidOperationException(
                    $"Package '{profile.packageName}' note exceeds the {MaxPackageNoteLength}-character safety limit.");
            }

            foreach (char character in profile.packageNote)
            {
                if (char.IsControl(character))
                {
                    throw new InvalidOperationException(
                        $"Package '{profile.packageName}' note contains a control character.");
                }
            }

            EnsureDefined(profile.buildPipeline, nameof(profile.buildPipeline), profile.packageName);
            EnsureDefined(profile.compression, nameof(profile.compression), profile.packageName);
            EnsureDefined(profile.fileNameStyle, nameof(profile.fileNameStyle), profile.packageName);
            EnsureDefined(profile.bundledCopyOption, nameof(profile.bundledCopyOption), profile.packageName);
            EnsureDefined(profile.versionCollisionPolicy, nameof(profile.versionCollisionPolicy), profile.packageName);

            string normalizedCopyParams = YooAsset3BuildSafety.NormalizeBundledCopyParams(profile);
            if (normalizedCopyParams.Length > 0)
            {
                var availableTags = new HashSet<string>(
                    BundleCollectorSettingData.Setting.GetPackageAllTags(profile.packageName),
                    StringComparer.Ordinal);
                foreach (string tag in normalizedCopyParams.Split(';'))
                {
                    if (!availableTags.Contains(tag))
                    {
                        throw new InvalidOperationException(
                            $"Package '{profile.packageName}' bundled-copy tag does not exist in Bundle Collector settings: '{tag}'.");
                    }
                }
            }

            return normalizedCopyParams;
        }

        private static void EnsureDefined<TEnum>(TEnum value, string fieldName, string packageName)
            where TEnum : struct
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new InvalidOperationException(
                    $"Package '{packageName}' has unsupported {fieldName} value '{value}'.");
            }
        }

        private static void ValidateGeneratedArtifactFileNames(string packageName, string packageVersion)
        {
            YooAsset3BuildSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion),
                "YooAsset build report file name");
            YooAsset3BuildSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetManifestBinaryFileName(packageName, packageVersion),
                "YooAsset manifest file name");
            YooAsset3BuildSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetPackageHashFileName(packageName, packageVersion),
                "YooAsset package hash file name");
            YooAsset3BuildSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetPackageVersionFileName(packageName),
                "YooAsset package version file name");
        }

        private static void ValidateVersionCollision(
            YooAsset3PackageBuildPlan packagePlan,
            List<string> warnings)
        {
            string outputDirectory = packagePlan.OutputPackageDirectory;
            if (File.Exists(outputDirectory))
            {
                throw new InvalidOperationException(
                    $"Exact package version output is an existing file: '{outputDirectory}'.");
            }

            if (!Directory.Exists(outputDirectory))
            {
                return;
            }

            if (packagePlan.Profile.versionCollisionPolicy == YooAssetVersionCollisionPolicy.FailIfVersionExists)
            {
                throw new InvalidOperationException(
                    $"Exact package version already exists: '{outputDirectory}'. Choose a new version or explicitly select ReplaceExactVersion.");
            }

            warnings.Add(
                $"Package '{packagePlan.PackageName}' will replace only its exact version directory: '{outputDirectory}'.");
        }

        private void ValidateBuildResult(
            YooAsset3PackageBuildPlan packagePlan,
            BuildResult buildResult,
            List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(buildResult.OutputPackageDirectory))
            {
                throw new InvalidOperationException("YooAsset reported success without an output package directory.");
            }

            string reportedOutputDirectory = Path.GetFullPath(buildResult.OutputPackageDirectory);
            if (!YooAsset3BuildSafety.PathsEqual(packagePlan.OutputPackageDirectory, reportedOutputDirectory))
            {
                throw new InvalidOperationException(
                    $"YooAsset output directory does not match the validated target. Expected '{packagePlan.OutputPackageDirectory}', received '{reportedOutputDirectory}'.");
            }

            CreateSuccessResultForDirectories(
                packagePlan,
                reportedOutputDirectory,
                packagePlan.Parameters.BundledCopyOption == EBundledCopyOption.None
                    ? string.Empty
                    : packagePlan.BundledPackageDirectory,
                warnings.ToArray());
        }

        private AssetContentBuildResult CreatePublishedSuccessResult(
            YooAsset3PackageBuildPlan packagePlan,
            IReadOnlyList<string> warnings)
        {
            return CreateSuccessResultForDirectories(
                packagePlan,
                packagePlan.OutputPackageDirectory,
                packagePlan.Parameters.BundledCopyOption == EBundledCopyOption.None
                    ? string.Empty
                    : packagePlan.BundledPackageDirectory,
                warnings);
        }

        private AssetContentBuildResult CreateSuccessResultForDirectories(
            YooAsset3PackageBuildPlan packagePlan,
            string outputPackageDirectory,
            string bundledPackageDirectory,
            IReadOnlyList<string> warnings)
        {
            string reportedOutputDirectory = Path.GetFullPath(outputPackageDirectory);

            if (!Directory.Exists(reportedOutputDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"YooAsset reported success, but the output directory does not exist: '{reportedOutputDirectory}'.");
            }

            string reportPath = RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetBuildReportFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetManifestBinaryFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetPackageHashFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetPackageVersionFileName(packagePlan.PackageName));

            if (packagePlan.Parameters.BundledCopyOption != EBundledCopyOption.None)
            {
                ValidateBundledArtifacts(packagePlan, bundledPackageDirectory);
            }

            string[] producedArtifacts = YooAsset3BuildSafety.EnumerateArtifacts(
                reportedOutputDirectory,
                MaxProducedArtifactCount)
                .Where(path => !YooAsset3PublicationOwnership.IsMarkerArtifact(path))
                .ToArray();
            if (producedArtifacts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"YooAsset reported success, but no package artifacts were produced in '{reportedOutputDirectory}'.");
            }

            return AssetContentBuildResult.Success(
                ProviderId,
                packagePlan.PackageName,
                packagePlan.PackageVersion,
                reportedOutputDirectory,
                bundledPackageDirectory,
                reportPath,
                producedArtifacts,
                warnings);
        }

        private static void ValidateBundledArtifacts(
            YooAsset3PackageBuildPlan packagePlan,
            string bundledPackageDirectory)
        {
            if (!Directory.Exists(bundledPackageDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"YooAsset bundled-copy option was enabled, but the bundled package directory does not exist: '{bundledPackageDirectory}'.");
            }

            RequireArtifact(
                bundledPackageDirectory,
                YooAssetConfiguration.GetManifestBinaryFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            RequireArtifact(
                bundledPackageDirectory,
                YooAssetConfiguration.GetPackageHashFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            RequireArtifact(
                bundledPackageDirectory,
                YooAssetConfiguration.GetPackageVersionFileName(packagePlan.PackageName));
            RequireArtifact(bundledPackageDirectory, "BuiltinCatalog.json");
            RequireArtifact(bundledPackageDirectory, "BuiltinCatalog.bytes");
        }

        private static string RequireArtifact(string directory, string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!YooAsset3BuildSafety.IsStrictDescendant(directory, path) || !File.Exists(path))
            {
                throw new FileNotFoundException($"Required YooAsset artifact is missing: '{path}'.", path);
            }

            return path;
        }

        private AssetContentBuildResult CreateValidationFailure(
            AssetContentBuildRequest request,
            Exception exception)
        {
            string packageName = string.Empty;
            Exception details = exception;
            if (exception is YooAsset3ProfileValidationException profileException)
            {
                packageName = profileException.PackageName;
                details = profileException.InnerException ?? profileException;
            }

            var warnings = new List<string>();
            if (request != null
                && request.Incrementality == BuildIncrementality.Clean)
            {
                warnings.Add(
                    "Clean mode does not enable YooAsset ClearBuildCacheFiles because YooAsset 3.0.5 deletes every historical package version when that flag is enabled.");
            }

            return AssetContentBuildResult.Failure(
                ProviderId,
                packageName,
                request == null ? string.Empty : request.PackageVersion,
                "Validation",
                details.Message,
                details.ToString(),
                warnings.ToArray());
        }

        private static string GetPackageSummary(YooAsset3PackageBuildPlan[] packages)
        {
            var names = new string[packages.Length];
            for (int index = 0; index < packages.Length; index++)
            {
                names[index] = packages[index].PackageName;
            }

            return string.Join(",", names);
        }

        private sealed class YooAsset3ProfileValidationException : Exception
        {
            public YooAsset3ProfileValidationException(string packageName, Exception innerException)
                : base($"YooAsset package profile '{packageName}' is invalid.", innerException)
            {
                PackageName = packageName ?? string.Empty;
            }

            public string PackageName { get; }
        }

        private sealed class StagedPackageResult
        {
            public StagedPackageResult(
                YooAsset3PackagePublication publication,
                string[] warnings)
            {
                Publication = publication;
                Warnings = warnings ?? Array.Empty<string>();
            }

            public YooAsset3PackagePublication Publication { get; }
            public string[] Warnings { get; }
        }

        private sealed class YooAsset3BuildFailureException : Exception
        {
            public YooAsset3BuildFailureException(
                string packageName,
                string failedTask,
                string message,
                string errorStack = null,
                Exception innerException = null)
                : base(message, innerException)
            {
                PackageName = packageName ?? string.Empty;
                FailedTask = string.IsNullOrWhiteSpace(failedTask) ? "YooAssetPipeline" : failedTask;
                ErrorStack = errorStack ?? string.Empty;
            }

            public string PackageName { get; }
            public string FailedTask { get; }
            public string ErrorStack { get; }
        }
    }
}
