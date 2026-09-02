using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Pipeline.Integrations.YooAsset3.Publication;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Version-gated YooAsset 3.x content build adapter discovered by the core pipeline through TypeCache.
    /// </summary>
    [AssetContentAdapterRegistration(YooAssetBuildConfig.ProviderIdValue)]
    public sealed class YooAsset3BuildAdapter :
        IAssetContentBuildAdapter,
        IAssetContentBuildOutputClaimProvider,
        IAssetContentPlayerBuildSessionFactory
    {
        private const int MaxPackageProfileCount = 128;
        private const int MaxCollectorPackageCount = 1024;
        private const int MaxPackageNoteLength = 512;
        private const int MaxProducedArtifactTreeEntries = 100000;
        private YooAsset3DeferredPublication pendingPublication;
        private string pendingInvocationId;

        public string ProviderId => YooAssetBuildConfig.ProviderIdValue;
        public string ExclusivePlayerSessionKey => string.Empty;

        public IReadOnlyList<string> GetExclusiveOutputPaths(
            AssetContentBuildRequest request)
        {
            YooAsset3BuildPlan plan = CreateValidatedPlan(request);
            var paths = new List<string>(plan.Packages.Length * 2);
            for (int index = 0; index < plan.Packages.Length; index++)
            {
                YooAsset3PackageBuildPlan package = plan.Packages[index];
                paths.Add(package.OutputPackageDirectory);
                if (package.Parameters.BundledCopyOption != EBundledCopyOption.None)
                {
                    paths.Add(package.BundledPackageDirectory);
                }
            }

            return paths.AsReadOnly();
        }

        public AssetContentBuildResult Validate(AssetContentBuildRequest request)
        {
            try
            {
                List<string> versionFailures = YooAsset3VersionSupport.ValidateSupport(
                    request == null ? BuildIncrementality.Clean : request.Incrementality);
                if (versionFailures.Count > 0)
                {
                    throw new InvalidOperationException(
                        "The installed YooAsset version is incompatible with this integration. " +
                        string.Join(" ", versionFailures));
                }

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

        public IReadOnlyList<string> ValidatePlayerBuild(AssetContentBuildRequest request)
        {
            AssetContentBuildResult validation = Validate(request);
            if (validation != null && validation.Succeeded)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                validation?.ErrorInfo ?? "YooAsset Player build validation returned no result."
            };
        }

        public IDisposable BeginPlayerBuild(AssetContentBuildRequest request)
        {
            IReadOnlyList<string> errors = ValidatePlayerBuild(request);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "YooAsset Player build preflight failed: " + string.Join("; ", errors));
            }

            if (pendingPublication == null)
            {
                throw new InvalidOperationException(
                    "YooAsset content must be built and registered before the Player build begins.");
            }

            if (!string.Equals(
                    pendingInvocationId,
                    request.InvocationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset Player build request does not match the invocation that owns the pending content publication.");
            }

            return pendingPublication.BeginPlayerBuild();
        }

        public AssetContentBuildOperation Build(AssetContentBuildRequest request)
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
                return FailureOperation(CreateValidationFailure(request, exception));
            }

            PublicationBuildLock buildLock;
            try
            {
                buildLock = PublicationBuildLock.Acquire(projectRoot, buildOutputRoot, bundledFileRoot);
            }
            catch (Exception exception)
            {
                return FailureOperation(
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        string.Empty,
                        request.PackageVersion,
                        "TransactionLock",
                        exception.Message,
                        exception.ToString()));
            }

            using (buildLock)
            {
                try
                {
                    YooAsset3PublicationTransaction.EnsureNoPendingRecovery(
                        projectRoot,
                        request.InvocationId);
                }
                catch (Exception exception)
                {
                    return FailureOperation(
                        AssetContentBuildResult.Failure(
                            ProviderId,
                            string.Empty,
                            request.PackageVersion,
                            "RecoveryRequired",
                            exception.Message,
                            exception.ToString()));
                }

                return BuildUnderLock(request);
            }
        }

        private AssetContentBuildOperation BuildUnderLock(AssetContentBuildRequest request)
        {
            if (pendingPublication != null)
            {
                return FailureOperation(
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        string.Empty,
                        request.PackageVersion,
                        "InvocationState",
                        "This YooAsset adapter instance already owns a pending publication."));
            }

            YooAsset3BuildPlan plan;
            try
            {
                plan = CreateValidatedPlan(request);
            }
            catch (Exception exception)
            {
                return FailureOperation(CreateValidationFailure(request, exception));
            }

            YooAsset3PublicationTransaction transaction =
                YooAsset3PublicationTransaction.Create(
                    plan,
                    request.InvocationId);
            var stagedPackages = new List<StagedPackageResult>(plan.Packages.Length);
            string activePackageName = string.Empty;
            var activeWarnings = new List<string>(plan.Warnings);
            try
            {
                transaction.Prepare();
                foreach (PackagePublication publication in transaction.Packages)
                {
                    YooAsset3PackageBuildPlan finalPlan = transaction.GetFinalPlan(publication);
                    activePackageName = finalPlan.PackageName;
                    activeWarnings = new List<string>(plan.Warnings);
                    YooAsset3PackageBuildPlan executionPlan = transaction.CreateExecutionPlan(request, publication);
                    executionPlan.Parameters.CheckBuildParameters();
                    YooAsset3BuildPathValidation.ValidatePackageOutputPath(plan.BuildOutputRoot, executionPlan);
                    PublicationSafety.ValidateNoPathRedirection(
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
                        finalPlan,
                        activeWarnings.ToArray()));
                }

                transaction.PrepareReadyDirectories();
                var manifestFailures = new List<string>();
                foreach (StagedPackageResult staged in stagedPackages)
                {
                    ValidateManifestSemantics(
                        staged.FinalPlan,
                        staged.Publication.OutputOperation.stage,
                        requireAllBundles: true,
                        manifestFailures);

                    if (staged.Publication.BundledOperation != null)
                    {
                        ValidateBundledArtifacts(
                            staged.FinalPlan,
                            staged.Publication.BundledOperation.stage);
                        ValidateManifestSemantics(
                            staged.FinalPlan,
                            staged.Publication.BundledOperation.stage,
                            requireAllBundles: false,
                            manifestFailures);
                    }
                }

                if (manifestFailures.Count > 0)
                {
                    throw new YooAsset3BuildFailureException(
                        activePackageName,
                        "ManifestValidation",
                        "YooAsset manifest validation failed: " + string.Join(" ", manifestFailures));
                }

                transaction.SealReadyDirectories();

                var preparedResults = new List<AssetContentBuildResult>(stagedPackages.Count);
                foreach (StagedPackageResult staged in stagedPackages)
                {
                    preparedResults.Add(CreatePreparedSuccessResult(
                        staged.Publication,
                        staged.FinalPlan,
                        staged.Warnings));
                }

                var deferredPublication = new YooAsset3DeferredPublication(
                    transaction,
                    () =>
                    {
                        foreach (StagedPackageResult staged in stagedPackages)
                        {
                            CreatePublishedSuccessResult(
                                staged.FinalPlan,
                                staged.Warnings);
                        }
                    });
                pendingPublication = deferredPublication;
                pendingInvocationId = request.InvocationId;
                transaction = null;
                return new AssetContentBuildOperation(
                    preparedResults,
                    deferredPublication);
            }
            catch (CommittedPublicationException exception)
            {
                return FailureOperation(
                    CreateCommittedRecoveryFailure(
                        request,
                        activePackageName,
                        exception,
                        activeWarnings));
            }
            catch (Exception exception)
            {
                Exception failure = exception;
                if (transaction != null)
                {
                    try
                    {
                        transaction.Abort(AssetDatabase.Refresh);
                    }
                    catch (Exception rollbackException)
                    {
                        failure = new AggregateException(
                            "YooAsset build failed and publication rollback did not complete.",
                            exception,
                            rollbackException);
                    }
                }

                if (exception is YooAsset3BuildFailureException buildFailure)
                {
                    return FailureOperation(
                        AssetContentBuildResult.Failure(
                            ProviderId,
                            buildFailure.PackageName,
                            request.PackageVersion,
                            buildFailure.FailedTask,
                            buildFailure.Message,
                            string.IsNullOrWhiteSpace(buildFailure.ErrorStack)
                                ? failure.ToString()
                                : buildFailure.ErrorStack + Environment.NewLine + failure,
                            activeWarnings.ToArray()));
                }

                return FailureOperation(
                    AssetContentBuildResult.Failure(
                        ProviderId,
                        activePackageName,
                        request.PackageVersion,
                        "TransactionalPublication",
                        failure.Message,
                        failure.ToString(),
                        activeWarnings.ToArray()));
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private AssetContentBuildResult CreateCommittedRecoveryFailure(
            AssetContentBuildRequest request,
            string packageName,
            CommittedPublicationException exception,
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
            string projectRoot = PublicationSafety.NormalizeProjectRoot(request.ProjectRoot);
            string buildOutputRoot = YooAsset3BuildPathValidation.ResolveBuildOutputRoot(projectRoot, configuration.buildOutputRoot);
            string bundledFileRoot = YooAsset3BuildPathValidation.ResolveBundledFileRoot(projectRoot, configuration.bundledFileRoot);
            PublicationSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);

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
            var configuredPackageNames = new HashSet<string>(PublicationSafety.PortablePathSegmentComparer);
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
                    YooAsset3BuildPathValidation.ValidatePackageOutputPath(buildOutputRoot, packagePlan);
                    PublicationSafety.ValidateNoPathRedirection(
                        projectRoot,
                        packagePlan.OutputPackageDirectory);
                    if (packagePlan.Parameters.BundledCopyOption != EBundledCopyOption.None)
                    {
                        YooAsset3BuildPathValidation.ValidateBundledPackagePath(
                            projectRoot,
                            bundledFileRoot,
                            packagePlan);
                    }
                    ValidateVersionCollision(request, packagePlan, warnings);
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

            projectRoot = PublicationSafety.NormalizeProjectRoot(request.ProjectRoot);
            buildOutputRoot = YooAsset3BuildPathValidation.ResolveBuildOutputRoot(projectRoot, configuration.buildOutputRoot);
            bundledFileRoot = YooAsset3BuildPathValidation.ResolveBundledFileRoot(projectRoot, configuration.bundledFileRoot);
            PublicationSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, bundledFileRoot);
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
            var fileSystemNames = new HashSet<string>(PublicationSafety.PortablePathSegmentComparer);
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

            // The pure core takes primitives: the authoring enum never crosses into it.
            string normalizedCopyParams = PublicationSafety.NormalizeBundledCopyParams(
                profile.bundledCopyOption == YooAssetBundledCopyOption.ClearAndCopyByTags ||
                profile.bundledCopyOption == YooAssetBundledCopyOption.OnlyCopyByTags,
                profile.bundledCopyTags,
                profile.packageName);
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
            PublicationSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion),
                "YooAsset build report file name");
            PublicationSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetManifestBinaryFileName(packageName, packageVersion),
                "YooAsset manifest file name");
            PublicationSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetPackageHashFileName(packageName, packageVersion),
                "YooAsset package hash file name");
            PublicationSafety.ValidateArtifactFileName(
                YooAssetConfiguration.GetPackageVersionFileName(packageName),
                "YooAsset package version file name");
        }

        private static void ValidateVersionCollision(
            AssetContentBuildRequest request,
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

            // Local iterations (non-batch development builds and local release previews)
            // are intentionally overwritable: they reuse a stable, non-version-controlled
            // local version, so a repeated build in the same workspace collides with its
            // own prior output. An explicit ReplaceExactVersion flag authorizes the same
            // behavior for controlled CI re-publishing of an identical version. We honor
            // the override for the exact, build-owned output without mutating the shared
            // profile, which keeps real/release builds immutable under the
            // FailIfVersionExists default.
            YooAssetVersionCollisionPolicy effectivePolicy =
                (request.IsLocalIteration || request.ReplaceExactVersion)
                    ? YooAssetVersionCollisionPolicy.ReplaceExactVersion
                    : packagePlan.Profile.versionCollisionPolicy;
            if (effectivePolicy == YooAssetVersionCollisionPolicy.FailIfVersionExists)
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
            if (!PublicationSafety.PathsEqual(packagePlan.OutputPackageDirectory, reportedOutputDirectory))
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

        private AssetContentBuildResult CreatePreparedSuccessResult(
            PackagePublication publication,
            YooAsset3PackageBuildPlan packagePlan,
            IReadOnlyList<string> warnings)
        {
            string stagedOutput = publication.OutputOperation.stage;
            string stagedBundled = publication.BundledOperation == null
                ? string.Empty
                : publication.BundledOperation.stage;
            AssetContentBuildResult stagedResult = CreateSuccessResultForDirectories(
                packagePlan,
                stagedOutput,
                stagedBundled,
                warnings);

            string finalOutput = Path.GetFullPath(packagePlan.OutputPackageDirectory);
            string finalBundled = packagePlan.Parameters.BundledCopyOption == EBundledCopyOption.None
                ? string.Empty
                : Path.GetFullPath(packagePlan.BundledPackageDirectory);
            string stagedOutputRoot = Path.GetFullPath(stagedOutput);
            string[] finalArtifacts = stagedResult.ProducedArtifacts
                .Select(path => Path.Combine(
                    finalOutput,
                    GetRelativeArtifactPath(stagedOutputRoot, path)))
                .ToArray();
            string reportRelativePath = GetRelativeArtifactPath(
                stagedOutputRoot,
                stagedResult.ReportPath);

            return AssetContentBuildResult.Success(
                ProviderId,
                packagePlan.PackageName,
                packagePlan.PackageVersion,
                finalOutput,
                finalBundled,
                Path.Combine(finalOutput, reportRelativePath),
                finalArtifacts,
                warnings);
        }

        private static string GetRelativeArtifactPath(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path);
            if (!PublicationSafety.IsStrictDescendant(normalizedRoot, normalizedPath))
            {
                throw new InvalidOperationException(
                    $"Prepared YooAsset artifact escaped its sealed stage: '{normalizedPath}'.");
            }

            return normalizedPath.Substring(normalizedRoot.Length + 1);
        }

        private static AssetContentBuildOperation FailureOperation(
            AssetContentBuildResult result)
        {
            return new AssetContentBuildOperation(new[] { result });
        }

        internal AssetContentBuildResult CreateSuccessResultForDirectories(
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
            string manifestPath = RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetManifestBinaryFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            string hashPath = RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetPackageHashFileName(packagePlan.PackageName, packagePlan.PackageVersion));
            string versionPath = RequireArtifact(
                reportedOutputDirectory,
                YooAssetConfiguration.GetPackageVersionFileName(packagePlan.PackageName));

            if (packagePlan.Parameters.BundledCopyOption != EBundledCopyOption.None)
            {
                ValidateBundledArtifacts(packagePlan, bundledPackageDirectory);
            }

            int scannedFileCount = PublicationSafety.ValidateArtifactTree(
                reportedOutputDirectory,
                MaxProducedArtifactTreeEntries);
            if (scannedFileCount == 0)
            {
                throw new InvalidOperationException(
                    $"YooAsset reported success, but no package artifacts were produced in '{reportedOutputDirectory}'.");
            }

            // Keep the provider-neutral result bounded. Output and bundled roots
            // are carried by dedicated result fields; the complete tree is sealed
            // separately by the publication owner using entry/byte budgets and a
            // deterministic content digest.
            string[] producedArtifacts =
            {
                reportPath,
                manifestPath,
                hashPath,
                versionPath
            };

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

        private static void ValidateManifestSemantics(
            YooAsset3PackageBuildPlan packagePlan,
            string directory,
            bool requireAllBundles,
            List<string> failures)
        {
            YooAsset3ManifestValidator.ValidatePackageManifest(
                directory,
                packagePlan.PackageName,
                packagePlan.PackageVersion,
                packagePlan.Parameters.ManifestDecryptor,
                requireAllBundles,
                failures);
        }

        // Internal (rather than private) so the integration test assembly can drive
        // PlayerBuildSession through InternalsVisibleTo without running YooAsset or
        // AssetDatabase.Refresh. Production callers still reach these types only
        // through YooAsset3BuildAdapter.BeginPlayerBuild.
        internal sealed class YooAsset3DeferredPublication : IBuildSourceQualificationPublication
        {
            private YooAsset3PublicationTransaction transaction;
            private readonly Action validatePublishedState;
            private bool published;
            private bool activated;
            private bool completed;

            public YooAsset3DeferredPublication(
                YooAsset3PublicationTransaction transaction,
                Action validatePublishedState)
            {
                this.transaction = transaction
                    ?? throw new ArgumentNullException(nameof(transaction));
                this.validatePublishedState = validatePublishedState
                    ?? throw new ArgumentNullException(nameof(validatePublishedState));
            }

            public string Id => transaction.PublicationId;
            public string RecoveryStateRelativePath => transaction.StateRelativePath;

            public void Publish()
            {
                if (transaction == null)
                {
                    throw new ObjectDisposedException(nameof(YooAsset3DeferredPublication));
                }

                if (published)
                {
                    validatePublishedState();
                    return;
                }

                transaction.Publish(validatePublishedState, AssetDatabase.Refresh);
                published = true;
            }

            public void ActivateForDownstream()
            {
                if (transaction == null)
                {
                    throw new ObjectDisposedException(nameof(YooAsset3DeferredPublication));
                }

                if (!transaction.HasDownstreamInputs)
                {
                    return;
                }

                if (activated)
                {
                    throw new InvalidOperationException(
                        "YooAsset bundled inputs have already been activated for downstream steps.");
                }

                transaction.ActivateDownstreamInputs(AssetDatabase.Refresh);
                transaction.ValidateActivatedInputs();
                activated = true;
            }

            public IDisposable SuspendForSourceQualification()
            {
                if (transaction == null)
                {
                    throw new ObjectDisposedException(nameof(YooAsset3DeferredPublication));
                }

                if (completed)
                {
                    throw new InvalidOperationException(
                        "A completed YooAsset publication cannot be suspended for source qualification.");
                }

                return transaction.SuspendForSourceQualification();
            }

            public IDisposable BeginPlayerBuild()
            {
                if (!transaction.HasDownstreamInputs)
                {
                    return new PlayerBuildSession(null);
                }

                ActivateForDownstream();
                return new PlayerBuildSession(this);
            }

            public void Complete()
            {
                if (transaction == null)
                {
                    throw new ObjectDisposedException(nameof(YooAsset3DeferredPublication));
                }

                if (!published)
                {
                    throw new InvalidOperationException(
                        "YooAsset publication must install its stages before completion.");
                }

                // The shared barrier is committed before this call. Mark the
                // wrapper terminal first so Dispose preserves recovery evidence
                // if refresh or durable cleanup fails.
                completed = true;
                transaction.Complete(AssetDatabase.Refresh);
            }

            public void Dispose()
            {
                if (transaction == null)
                {
                    return;
                }

                Exception failure = null;
                if (!completed)
                {
                    try
                    {
                        transaction.Abort(AssetDatabase.Refresh);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }

                failure = DisposeTransaction(failure);
                if (failure != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(failure)
                        .Throw();
                }
            }

            private Exception DisposeTransaction(Exception failure)
            {
                try
                {
                    transaction.Dispose();
                }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(
                            "YooAsset publication and transaction disposal both failed.",
                            failure,
                            exception);
                }
                finally
                {
                    transaction = null;
                }

                return failure;
            }

            internal sealed class PlayerBuildSession : IDisposable
            {
                private YooAsset3DeferredPublication owner;
                private RelocationJournalDocument relocations;
                private readonly string projectRoot;
                private readonly IJournalSerializer serializer;

                internal PlayerBuildSession(YooAsset3DeferredPublication owner)
                {
                    this.owner = owner;
                    if (owner == null)
                    {
                        return;
                    }

                    projectRoot = owner.transaction.ProjectRoot;
                    serializer = UnityJournalSerializer.Instance;
                    relocations = RelocationJournalStore.Create(owner.transaction.TransactionId);

                    try
                    {
                        HidePublicationArtifacts();
                    }
                    catch (Exception hideException)
                    {
                        // Hiding moves artifacts one at a time. If it fails partway through,
                        // the already-moved entries must be moved back before construction
                        // gives up, otherwise the session leaves orphaned relocations behind.
                        if (relocations == null || relocations.entries.Length == 0)
                        {
                            throw;
                        }

                        Exception compensationFailure = null;
                        try
                        {
                            compensationFailure = RestorePublicationArtifacts();
                        }
                        catch (Exception restoreException)
                        {
                            compensationFailure = restoreException;
                        }

                        if (compensationFailure != null)
                        {
                            // The relocation list now holds only the entries that could not be
                            // compensated. Their paths must appear in the exception text because
                            // a failed constructor leaves the session unusable and recovery can
                            // only come from this message plus the durable publication journal.
                            throw new AggregateException(
                                "Some YooAsset publication artifacts were relocated and automatic compensation did not complete; " +
                                "recover the entries below through the outer durable publication journal. " +
                                DescribeRemainingRelocations(),
                                hideException,
                                compensationFailure);
                        }

                        // Compensation completed: the relocation list is empty again, so the
                        // original failure can surface without leaving orphaned artifacts behind.
                        throw;
                    }
                }

                public void Dispose()
                {
                    // Restore is idempotent and retryable: entries restored successfully are
                    // removed from the relocation list, failed entries remain for a later
                    // Dispose. ValidateActivatedInputs runs only after every artifact is back
                    // in place. A second Dispose after a failure therefore only works on the
                    // remaining relocation list and never touches the (possibly null) owner.
                    if (relocations != null && relocations.entries.Length > 0)
                    {
                        Exception restoreFailure = RestorePublicationArtifacts();
                        if (restoreFailure != null)
                        {
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                                .Capture(restoreFailure)
                                .Throw();
                        }
                    }

                    if (relocations != null)
                    {
                        RelocationJournalStore.DeleteIfClean(relocations, projectRoot);
                        relocations = null;
                    }

                    YooAsset3DeferredPublication current = owner;
                    owner = null;
                    current?.transaction.ValidateActivatedInputs();
                }

                // The bundled package directory carries a ".yoo-pub.json" ownership marker, and its
                // ".yoo-backup-<transactionId>-<n>" sibling holds the previously installed package version
                // while the deferred transaction keeps it for rollback. Unity copies every entry under
                // Assets/StreamingAssets into the Player, and the core output transaction rejects dot-prefixed
                // entry names as non-portable. Move the marker file and the backup/stage directories out of
                // StreamingAssets for the duration of the Player build and restore them afterwards so the
                // ownership evidence and deferred rollback state remain intact.
                private void HidePublicationArtifacts()
                {
                    string relocationRoot = Path.GetFullPath(Path.Combine(
                        Application.dataPath,
                        "..",
                        "Temp",
                        "BuildPipeline",
                        "YooAssetPublicationMarkers"));
                    Directory.CreateDirectory(relocationRoot);

                    foreach (PackagePublication package in owner.transaction.Packages)
                    {
                        PublicationJournalOperation operation = package.BundledOperation;
                        if (operation == null)
                        {
                            continue;
                        }

                        // Only bundled targets under Assets/StreamingAssets are copied into the
                        // Player by Unity. When managesSiblingMeta is false the target lives
                        // elsewhere, so neither its marker nor its backup/stage/protectedMeta
                        // siblings can pollute the Player output and relocating them would only
                        // add a cross-volume failure surface.
                        if (!operation.managesSiblingMeta)
                        {
                            continue;
                        }

                        RelocateIfPresent(
                            Path.Combine(
                                operation.target,
                                PublicationOwnership.MarkerFileName),
                            relocationRoot,
                            isDirectory: false);

                        // The backup directory holds the prior package version, and its ".root-meta"
                        // sibling preserves the protected target meta. Neither may be swept into the
                        // Player. The stage directory is normally absent here (it was moved to the
                        // target during activation) but is relocated defensively for other phases.
                        RelocateIfPresent(operation.backup, relocationRoot, isDirectory: true);
                        RelocateIfPresent(operation.protectedMeta, relocationRoot, isDirectory: false);
                        RelocateIfPresent(operation.stage, relocationRoot, isDirectory: true);
                    }
                }

                private void RelocateIfPresent(string originalPath, string relocationRoot, bool isDirectory)
                {
                    bool exists = isDirectory
                        ? Directory.Exists(originalPath)
                        : File.Exists(originalPath);
                    if (!exists)
                    {
                        return;
                    }

                    string relocatedPath = Path.Combine(
                        relocationRoot,
                        Guid.NewGuid().ToString("N") + (isDirectory ? ".dir" : ".file"));
                    if (!string.Equals(
                            Path.GetPathRoot(originalPath),
                            Path.GetPathRoot(relocatedPath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Refusing to relocate a YooAsset publication artifact across volumes. " +
                            $"Source='{originalPath}', destination='{relocatedPath}'. " +
                            "Configure the bundled file root on the same volume as the Unity project Temp directory.");
                    }

                    // Crash-durable protocol: record the move as Planned (flushed to disk), move,
                    // verify both sides, then record Moved. A process killed between any of these
                    // steps leaves an accurate journal for the startup recovery pass.
                    string kind = isDirectory
                        ? RelocationJournalStore.KindDirectory
                        : RelocationJournalStore.KindFile;
                    RelocationJournalStore.AppendEntry(relocations, originalPath, relocatedPath, kind);
                    RelocationJournalStore.Persist(relocations, projectRoot, serializer);

                    try
                    {
                        if (isDirectory)
                        {
                            Directory.Move(originalPath, relocatedPath);
                        }
                        else
                        {
                            File.Move(originalPath, relocatedPath);
                        }
                    }
                    catch (Exception moveException)
                    {
                        RelocationEntry entry = RelocationJournalStore.FindByRelocatedPath(
                            relocations, relocatedPath);
                        entry.state = RelocationJournalStore.ConflictState;
                        entry.attemptCount++;
                        entry.lastError = moveException.Message;
                        RelocationJournalStore.Persist(relocations, projectRoot, serializer);
                        throw;
                    }

                    bool movedSanely = isDirectory
                        ? Directory.Exists(relocatedPath) && !Directory.Exists(originalPath)
                        : File.Exists(relocatedPath) && !File.Exists(originalPath);
                    if (!movedSanely)
                    {
                        RelocationEntry entry = RelocationJournalStore.FindByRelocatedPath(
                            relocations, relocatedPath);
                        entry.state = RelocationJournalStore.ConflictState;
                        entry.attemptCount++;
                        entry.lastError = "the move reported success but the original and relocated paths contradict it.";
                        RelocationJournalStore.Persist(relocations, projectRoot, serializer);
                        throw new InvalidOperationException(
                            "YooAsset publication artifact relocation could not be verified: " +
                            $"original='{originalPath}', relocated='{relocatedPath}'.");
                    }

                    RelocationEntry moved = RelocationJournalStore.FindByRelocatedPath(
                        relocations, relocatedPath);
                    moved.state = RelocationJournalStore.MovedState;
                    moved.attemptCount++;
                    RelocationJournalStore.Persist(relocations, projectRoot, serializer);
                }

                private Exception RestorePublicationArtifacts()
                {
                    var failures = new List<string>();
                    // Reverse order: the last relocation is the first one undone.
                    for (int index = relocations.entries.Length - 1; index >= 0; index--)
                    {
                        RelocationEntry entry = relocations.entries[index];
                        if (string.Equals(entry.state, RelocationJournalStore.RestoredState, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string failure = TryRestore(entry);
                        entry.attemptCount++;
                        if (failure == null)
                        {
                            entry.state = RelocationJournalStore.RestoredState;
                            entry.lastError = string.Empty;
                        }
                        else
                        {
                            // Fail closed: keep the entry so a subsequent Dispose or the startup
                            // recovery pass can retry it. Never delete the journal early.
                            entry.lastError = failure;
                            failures.Add($"original='{entry.originalPath}' relocated='{entry.relocatedPath}': {failure}");
                        }

                        // Persist after every entry, success or failure.
                        RelocationJournalStore.Persist(relocations, projectRoot, serializer);
                    }

                    if (failures.Count > 0)
                    {
                        return new AggregateException(
                            "YooAsset Player build publication artifact restoration did not complete for every relocated entry.",
                            failures.Select(message => new InvalidOperationException(message)));
                    }

                    return null;
                }

                private string TryRestore(RelocationEntry entry)
                {
                    bool isDirectory = string.Equals(entry.kind, RelocationJournalStore.KindDirectory, StringComparison.Ordinal);
                    bool relocatedIsDirectory = Directory.Exists(entry.relocatedPath);
                    bool relocatedIsFile = File.Exists(entry.relocatedPath);
                    bool originalExists = isDirectory
                        ? Directory.Exists(entry.originalPath)
                        : File.Exists(entry.originalPath);

                    // Fail closed on a type mismatch: never move an entry whose actual filesystem
                    // kind contradicts what the relocation recorded.
                    if (isDirectory ? relocatedIsFile : relocatedIsDirectory)
                    {
                        entry.state = RelocationJournalStore.ConflictState;
                        return "relocation type mismatch: expected a " + (isDirectory ? "directory" : "file") +
                               " at the relocated path but found a " + (isDirectory ? "file" : "directory") + ".";
                    }

                    bool relocatedExists = isDirectory ? relocatedIsDirectory : relocatedIsFile;
                    if (!relocatedExists)
                    {
                        if (originalExists)
                        {
                            // Already back in place (a previous restore moved it but could not
                            // persist). Treat the entry as restored.
                            return null;
                        }

                        // Fail closed: the artifact is gone from both paths. Never pretend the
                        // restoration succeeded.
                        entry.state = RelocationJournalStore.MissingBothState;
                        return "neither the relocated artifact nor the original path exists; the artifact requires manual restoration.";
                    }

                    if (originalExists)
                    {
                        // Fail closed: never overwrite a recreated original path.
                        entry.state = RelocationJournalStore.ConflictState;
                        return "both the original and the relocated paths exist.";
                    }

                    try
                    {
                        if (isDirectory)
                        {
                            Directory.Move(entry.relocatedPath, entry.originalPath);
                        }
                        else
                        {
                            File.Move(entry.relocatedPath, entry.originalPath);
                        }

                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception.Message;
                    }
                }

                private string DescribeRemainingRelocations()
                {
                    var builder = new System.Text.StringBuilder();
                    builder.Append(" Remaining relocation entries:");
                    foreach (RelocationEntry entry in relocations.entries)
                    {
                        builder.Append(" original='");
                        builder.Append(entry.originalPath);
                        builder.Append("' relocated='");
                        builder.Append(entry.relocatedPath);
                        builder.Append("' state='");
                        builder.Append(entry.state);
                        builder.Append("';");
                    }

                    return builder.ToString();
                }
            }
        }

        private static string RequireArtifact(string directory, string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!PublicationSafety.IsStrictDescendant(directory, path) || !File.Exists(path))
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
                PackagePublication publication,
                YooAsset3PackageBuildPlan finalPlan,
                string[] warnings)
            {
                Publication = publication;
                FinalPlan = finalPlan;
                Warnings = warnings ?? Array.Empty<string>();
            }

            public PackagePublication Publication { get; }
            public YooAsset3PackageBuildPlan FinalPlan { get; }
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
