using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class BuildPipelineBudgets
    {
        public const int MaximumInvocationCount = 256;
        public const int MaximumDependencyEdgeCount = 4096;
        public const int MaximumDeferredPublicationCount = 512;
        public const int MaximumExclusiveOutputPathClaimCount = 4096;
    }

    public static class BuildStepTypeIds
    {
        public const string HotUpdate = "hot-update";
        public const string AssetContent = "asset-content";
        public const string Player = "player";
    }

    public enum BuildStepStatus
    {
        Succeeded,
        Skipped,
        Failed
    }

    public enum BuildIncrementality
    {
        Clean,
        Incremental
    }

    public enum BuildDependencyMode
    {
        Required,
        IfSelected
    }

    public enum BuildStepMultiplicity
    {
        Single,
        Multiple
    }

    [Serializable]
    public sealed class BuildInvocationDependency
    {
        [SerializeField] private string invocationId;
        [SerializeField] private BuildDependencyMode mode;

        public BuildInvocationDependency(
            string invocationId,
            BuildDependencyMode mode = BuildDependencyMode.Required)
        {
            if (mode != BuildDependencyMode.Required
                && mode != BuildDependencyMode.IfSelected)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Build dependency mode must be Required or IfSelected.");
            }

            this.invocationId = invocationId ?? string.Empty;
            this.mode = mode;
        }

        public string InvocationId => invocationId ?? string.Empty;
        public BuildDependencyMode Mode => mode;

        internal BuildInvocationDependency Snapshot()
        {
            return new BuildInvocationDependency(InvocationId, mode);
        }
    }

    [Flags]
    public enum BuildStepRequirements
    {
        None = 0,
        UnityGlobalState = 1 << 0,
        VersionInfoAsset = 1 << 1,
        PlayerOutput = 1 << 2
    }

    /// <summary>
    /// Immutable invocation data for one selected build step. Configuration is
    /// referenced directly as a Unity asset in Editor workflows and resolved
    /// from an Assets-relative path by CI overrides.
    /// </summary>
    public sealed class BuildStepInvocation
    {
        public BuildStepInvocation(
            string invocationId,
            string stepTypeId,
            ScriptableObject configuration = null,
            BuildIncrementality incrementality = BuildIncrementality.Clean,
            IReadOnlyList<BuildInvocationDependency> dependencies = null)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                throw new ArgumentException(
                    "Build step invocation identity is required.",
                    nameof(invocationId));
            }

            if (string.IsNullOrWhiteSpace(stepTypeId))
            {
                throw new ArgumentException(
                    "Build step type identity is required.",
                    nameof(stepTypeId));
            }

            InvocationId = invocationId.Trim();
            StepTypeId = stepTypeId.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                InvocationId,
                "Build invocation id");
            BuildIdentityPolicy.ValidateBuildIdentifier(
                StepTypeId,
                "Build step type id");
            Configuration = configuration;
            if (incrementality != BuildIncrementality.Clean
                && incrementality != BuildIncrementality.Incremental)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(incrementality),
                    incrementality,
                    "Build incrementality must be Clean or Incremental.");
            }

            Incrementality = incrementality;
            Dependencies = SnapshotDependencies(dependencies);
        }

        public string InvocationId { get; }
        public string StepTypeId { get; }
        public ScriptableObject Configuration { get; }
        public BuildIncrementality Incrementality { get; }
        public IReadOnlyList<BuildInvocationDependency> Dependencies { get; }

        public T GetConfiguration<T>() where T : ScriptableObject
        {
            return Configuration as T;
        }

        public T GetRequiredConfiguration<T>() where T : ScriptableObject
        {
            T configuration = GetConfiguration<T>();
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    $"Build invocation '{InvocationId}' ({StepTypeId}) requires a {typeof(T).Name} configuration asset.");
            }

            return configuration;
        }

        private static IReadOnlyList<BuildInvocationDependency> SnapshotDependencies(
            IReadOnlyList<BuildInvocationDependency> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return Array.Empty<BuildInvocationDependency>();
            }

            var snapshot = new BuildInvocationDependency[dependencies.Count];
            for (int index = 0; index < dependencies.Count; index++)
            {
                snapshot[index] = dependencies[index]?.Snapshot()
                    ?? throw new ArgumentException(
                        $"Build invocation dependency at index {index} is null.",
                        nameof(dependencies));
            }

            return new ReadOnlyCollection<BuildInvocationDependency>(snapshot);
        }
    }

    public sealed class BuildVersionContext
    {
        public BuildVersionContext(
            string applicationVersion,
            string packageVersion,
            long buildNumber,
            string commitHash,
            string commitCount,
            string branch,
            string commitDate,
            string providerId,
            Build.VersionControl.Editor.VersionControlWorkspaceEvidence sourceWorkspace,
            BuildIdentityOrigin identityOrigin = BuildIdentityOrigin.VersionControl,
            string detectedCommitHash = null,
            string detectedCommitCount = null,
            string detectedBranch = null,
            string detectedCommitDate = null,
            string detectedProviderId = null,
            long? detectedBuildNumber = null,
            string ciProvider = null,
            string ciRunId = null)
        {
            ApplicationVersion = applicationVersion ?? string.Empty;
            PackageVersion = packageVersion ?? string.Empty;
            if (buildNumber <= 0 || buildNumber > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildNumber),
                    buildNumber,
                    $"Native build number must be between 1 and {int.MaxValue}.");
            }

            BuildNumber = buildNumber;
            CommitHash = commitHash ?? string.Empty;
            CommitCount = commitCount ?? string.Empty;
            Branch = branch ?? string.Empty;
            CommitDate = commitDate ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
            IdentityOrigin = identityOrigin;
            DetectedCommitHash = detectedCommitHash ?? string.Empty;
            DetectedCommitCount = detectedCommitCount ?? string.Empty;
            DetectedBranch = detectedBranch ?? string.Empty;
            DetectedCommitDate = detectedCommitDate ?? string.Empty;
            DetectedProviderId = detectedProviderId ?? string.Empty;
            DetectedBuildNumber = detectedBuildNumber;
            CiProvider = ciProvider ?? string.Empty;
            CiRunId = ciRunId ?? string.Empty;
            SourceWorkspace = sourceWorkspace
                ?? Build.VersionControl.Editor.VersionControlWorkspaceEvidence.Unknown(
                    Build.VersionControl.Editor.VersionControlWorkspaceEvidence.MetadataUnavailable);
        }

        public string ApplicationVersion { get; }
        public string PackageVersion { get; }
        public long BuildNumber { get; }
        public string CommitHash { get; }
        public string CommitCount { get; }
        public string Branch { get; }
        public string CommitDate { get; }
        public string ProviderId { get; }
        public BuildIdentityOrigin IdentityOrigin { get; }
        public long EffectiveBuildNumber => BuildNumber;
        public string EffectiveSourceProvider => ProviderId;
        public string EffectiveSourceRevision => CommitHash;
        public string EffectiveSourceBranch => Branch;
        public string DetectedCommitHash { get; }
        public string DetectedCommitCount { get; }
        public string DetectedBranch { get; }
        public string DetectedCommitDate { get; }
        public string DetectedProviderId { get; }
        public long? DetectedBuildNumber { get; }
        public string CiProvider { get; }
        public string CiRunId { get; }
        public Build.VersionControl.Editor.VersionControlWorkspaceEvidence SourceWorkspace { get; }

        internal BuildVersionContext WithSourceWorkspace(
            Build.VersionControl.Editor.VersionControlWorkspaceEvidence sourceWorkspace)
        {
            return new BuildVersionContext(
                ApplicationVersion,
                PackageVersion,
                BuildNumber,
                CommitHash,
                CommitCount,
                Branch,
                CommitDate,
                ProviderId,
                sourceWorkspace,
                IdentityOrigin,
                DetectedCommitHash,
                DetectedCommitCount,
                DetectedBranch,
                DetectedCommitDate,
                DetectedProviderId,
                DetectedBuildNumber,
                CiProvider,
                CiRunId);
        }
    }

    public enum BuildPurpose
    {
        Release = 0,
        Development = 1,
        LocalReleasePreview = 2
    }

    public sealed class BuildRequest
    {
        public BuildRequest(
            string companyName,
            string productName,
            string applicationIdentifier,
            string versionInfoAssetPath,
            IReadOnlyList<string> buildScenePaths,
            CheatBuildMode cheatBuildMode,
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation scriptingBackend,
            string projectRoot,
            string buildRoot,
            string outputPath,
            string outputDirectory,
            bool outputIsFolder,
            bool deleteDebugFiles,
            bool debugBuild,
            bool exportAndroidProject,
            bool allowExternalOutput,
            bool? cheatOverride,
            bool batchMode,
            string applicationVersion,
            BuildIdentityOverride identityOverride,
            IReadOnlyList<BuildStepInvocation> steps,
            BuildSourceCleanlinessPolicy sourceCleanlinessPolicy,
            BuildPurpose purpose)
        {
            CompanyName = companyName ?? string.Empty;
            ProductName = productName ?? string.Empty;
            ApplicationIdentifier = applicationIdentifier ?? string.Empty;
            VersionInfoAssetPath = versionInfoAssetPath ?? string.Empty;
            BuildScenePaths = SnapshotStrings(buildScenePaths, nameof(buildScenePaths));
            CheatBuildMode = cheatBuildMode;
            Target = target;
            NamedTarget = namedTarget;
            ScriptingBackend = scriptingBackend;
            ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            BuildRoot = buildRoot ?? throw new ArgumentNullException(nameof(buildRoot));
            OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
            OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            OutputIsFolder = outputIsFolder;
            DeleteDebugFiles = deleteDebugFiles;
            DebugBuild = debugBuild;
            ExportAndroidProject = exportAndroidProject;
            AllowExternalOutput = allowExternalOutput;
            CheatOverride = cheatOverride;
            CheatEnabled = CheatBuildDefineUtility.ShouldRequestCheat(
                cheatBuildMode,
                debugBuild,
                cheatOverride);
            BatchMode = batchMode;
            ApplicationVersion = applicationVersion ?? throw new ArgumentNullException(nameof(applicationVersion));
            IdentityOverride = identityOverride ?? throw new ArgumentNullException(nameof(identityOverride));
            Steps = SnapshotSteps(steps, nameof(steps));
            if (purpose != BuildPurpose.Release
                && purpose != BuildPurpose.Development
                && purpose != BuildPurpose.LocalReleasePreview)
            {
                throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null);
            }

            if (purpose == BuildPurpose.Release && debugBuild
                || purpose == BuildPurpose.Development && !debugBuild
                || purpose == BuildPurpose.LocalReleasePreview
                   && (debugBuild || batchMode || exportAndroidProject || allowExternalOutput))
            {
                throw new ArgumentException(
                    "Build purpose is incompatible with the requested build flags.",
                    nameof(purpose));
            }

            Purpose = purpose;
            if (sourceCleanlinessPolicy != BuildSourceCleanlinessPolicy.RequireClean
                && sourceCleanlinessPolicy != BuildSourceCleanlinessPolicy.AllowDirtyDevelopment
                && sourceCleanlinessPolicy != BuildSourceCleanlinessPolicy.AllowDirtyLocalRelease)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceCleanlinessPolicy),
                    sourceCleanlinessPolicy,
                    "Source cleanliness policy must be RequireClean, AllowDirtyDevelopment, " +
                    "or AllowDirtyLocalRelease.");
            }

            SourceCleanlinessPolicy = sourceCleanlinessPolicy;
            RequireCleanSource = BuildSourceWorkspacePolicy.RequiresVerifiedClean(
                batchMode,
                purpose,
                sourceCleanlinessPolicy);

            var stepTypeIds = new string[Steps.Count];
            for (int index = 0; index < Steps.Count; index++)
            {
                stepTypeIds[index] = Steps[index].StepTypeId;
            }

            StepTypeIds = new ReadOnlyCollection<string>(stepTypeIds);
        }

        public string CompanyName { get; }
        public string ProductName { get; }
        public string ApplicationIdentifier { get; }
        public string VersionInfoAssetPath { get; }
        public IReadOnlyList<string> BuildScenePaths { get; }
        public CheatBuildMode CheatBuildMode { get; }
        public BuildTarget Target { get; }
        public NamedBuildTarget NamedTarget { get; }
        public ScriptingImplementation ScriptingBackend { get; }
        public string ProjectRoot { get; }
        public string BuildRoot { get; }
        public string OutputPath { get; }
        public string OutputDirectory { get; }
        public bool OutputIsFolder { get; }
        public bool DeleteDebugFiles { get; }
        public bool DebugBuild { get; }
        public bool ExportAndroidProject { get; }
        public bool AllowExternalOutput { get; }
        public bool? CheatOverride { get; }
        public bool CheatEnabled { get; }
        public bool BatchMode { get; }
        public string ApplicationVersion { get; }
        public BuildIdentityOverride IdentityOverride { get; }
        public BuildSourceCleanlinessPolicy SourceCleanlinessPolicy { get; }
        public BuildPurpose Purpose { get; }
        public bool RequireCleanSource { get; }
        public bool CanPublishReleaseBaseline =>
            Purpose == BuildPurpose.Release && RequireCleanSource;
        public IReadOnlyList<BuildStepInvocation> Steps { get; }
        public IReadOnlyList<string> StepTypeIds { get; }

        public bool HasStepType(string stepTypeId)
        {
            if (string.IsNullOrWhiteSpace(stepTypeId))
            {
                return false;
            }

            for (int index = 0; index < Steps.Count; index++)
            {
                if (string.Equals(
                        Steps[index].StepTypeId,
                        stepTypeId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public BuildStepInvocation GetInvocation(string invocationId)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                return null;
            }

            for (int index = 0; index < Steps.Count; index++)
            {
                BuildStepInvocation step = Steps[index];
                if (string.Equals(
                        step.InvocationId,
                        invocationId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return step;
                }
            }

            return null;
        }

        public IReadOnlyList<BuildStepInvocation> GetInvocationsByStepType(
            string stepTypeId)
        {
            if (string.IsNullOrWhiteSpace(stepTypeId))
            {
                return Array.Empty<BuildStepInvocation>();
            }

            var matches = new List<BuildStepInvocation>();
            for (int index = 0; index < Steps.Count; index++)
            {
                BuildStepInvocation invocation = Steps[index];
                if (string.Equals(
                        invocation.StepTypeId,
                        stepTypeId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(invocation);
                }
            }

            return matches.AsReadOnly();
        }

        private static IReadOnlyList<string> SnapshotStrings(
            IReadOnlyList<string> values,
            string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var snapshot = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index];
            }

            return new ReadOnlyCollection<string>(snapshot);
        }

        private static IReadOnlyList<BuildStepInvocation> SnapshotSteps(
            IReadOnlyList<BuildStepInvocation> values,
            string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var snapshot = new BuildStepInvocation[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                BuildStepInvocation value = values[index]
                    ?? throw new ArgumentException(
                        $"Build step invocation at index {index} is null.",
                        parameterName);
                snapshot[index] = new BuildStepInvocation(
                    value.InvocationId,
                    value.StepTypeId,
                    value.Configuration,
                    value.Incrementality,
                    value.Dependencies);
            }

            return new ReadOnlyCollection<BuildStepInvocation>(snapshot);
        }
    }

    public sealed class BuildStepResult
    {
        public BuildStepResult(
            string invocationId,
            string stepTypeId,
            BuildStepStatus status,
            TimeSpan duration,
            string message,
            Exception exception = null)
        {
            InvocationId = invocationId ?? string.Empty;
            StepTypeId = stepTypeId ?? string.Empty;
            Status = status;
            Duration = duration;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public string InvocationId { get; }
        public string StepTypeId { get; }
        public BuildStepStatus Status { get; }
        public TimeSpan Duration { get; }
        public string Message { get; }
        public Exception Exception { get; }
    }

    public sealed class BuildRunResult
    {
        public BuildRunResult(
            string runId,
            bool succeeded,
            string outputPath,
            string resultManifestPath,
            IReadOnlyList<BuildStepResult> steps,
            Exception failure,
            IReadOnlyList<Exception> nonFatalFailures = null)
        {
            RunId = runId ?? string.Empty;
            Succeeded = succeeded;
            OutputPath = outputPath ?? string.Empty;
            ResultManifestPath = resultManifestPath ?? string.Empty;
            Steps = SnapshotItems(steps);
            Failure = failure;
            NonFatalFailures = SnapshotItems(nonFatalFailures);
        }

        public string RunId { get; }
        public bool Succeeded { get; }
        public string OutputPath { get; }
        public string ResultManifestPath { get; }
        public IReadOnlyList<BuildStepResult> Steps { get; }
        public Exception Failure { get; }
        /// <summary>
        /// Diagnostics that could not change the already-determined terminal
        /// build outcome, such as observer failures. Required result-manifest
        /// persistence is not non-fatal.
        /// </summary>
        public IReadOnlyList<Exception> NonFatalFailures { get; }

        internal BuildRunResult WithNonFatalFailure(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var failures = new Exception[NonFatalFailures.Count + 1];
            for (int index = 0; index < NonFatalFailures.Count; index++)
            {
                failures[index] = NonFatalFailures[index];
            }

            failures[failures.Length - 1] = exception;
            return new BuildRunResult(
                RunId,
                Succeeded,
                OutputPath,
                ResultManifestPath,
                Steps,
                Failure,
                failures);
        }

        private static IReadOnlyList<T> SnapshotItems<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<T>();
            }

            var snapshot = new T[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index];
            }

            return new ReadOnlyCollection<T>(snapshot);
        }
    }

    public sealed class BuildExecutionContext
    {
        private readonly Dictionary<string, IHotUpdateBuildAdapter> hotUpdateAdapters =
            new Dictionary<string, IHotUpdateBuildAdapter>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IAssetContentBuildAdapter> assetContentAdapters =
            new Dictionary<string, IAssetContentBuildAdapter>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AssetContentInvocationResult> contentResults =
            new List<AssetContentInvocationResult>();
        private readonly List<IBuildDeferredPublication> deferredPublications = new List<IBuildDeferredPublication>();
        private readonly List<ExclusiveOutputPathClaim> exclusiveOutputPathClaims =
            new List<ExclusiveOutputPathClaim>();
        private readonly IReadOnlyList<AssetContentInvocationResult> contentResultsView;
        private IReadOnlyList<BuildRecipeProvenanceEntry> recipeProvenance =
            Array.Empty<BuildRecipeProvenanceEntry>();
        private bool recipeProvenanceCaptured;
        private IReadOnlyList<CompiledBuildStep> plan =
            Array.Empty<CompiledBuildStep>();
        private string playerExtensionFingerprint = string.Empty;
        private bool playerExtensionFingerprintCaptured;
        private BuildVersionContext version;
        private BuildReport playerBuildReport;
        private long contentEvidenceUtf8Bytes;
        private int contentEvidenceValueCount;
        private bool sealedForPublication;

        public BuildExecutionContext(BuildRequest request, string runId, IBuildEventSink eventSink)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            EventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
            StartedUtc = DateTime.UtcNow;
            contentResultsView = contentResults.AsReadOnly();
        }

        public BuildRequest Request { get; }
        public string RunId { get; }
        public IBuildEventSink EventSink { get; }
        public DateTime StartedUtc { get; }
        public BuildVersionContext Version
        {
            get => version;
            set
            {
                ThrowIfSealedForPublication(nameof(Version));
                version = value;
            }
        }

        public BuildReport PlayerBuildReport
        {
            get => playerBuildReport;
            set
            {
                ThrowIfSealedForPublication(nameof(PlayerBuildReport));
                playerBuildReport = value;
            }
        }
        public IReadOnlyList<AssetContentInvocationResult> ContentResults => contentResultsView;
        public IReadOnlyList<CompiledBuildStep> Plan => plan;
        internal IReadOnlyList<IBuildDeferredPublication> DeferredPublications => deferredPublications;
        internal IReadOnlyList<BuildRecipeProvenanceEntry> RecipeProvenance =>
            recipeProvenance;
        internal bool RecipeProvenanceCaptured => recipeProvenanceCaptured;
        internal bool IsSealedForPublication => sealedForPublication;

        internal void SealForPublication()
        {
            sealedForPublication = true;
        }

        internal void SetPlayerExtensionFingerprint(string fingerprint)
        {
            ThrowIfSealedForPublication(nameof(SetPlayerExtensionFingerprint));
            string validated = RequirePlayerExtensionFingerprint(fingerprint);
            if (!playerExtensionFingerprintCaptured)
            {
                playerExtensionFingerprint = validated;
                playerExtensionFingerprintCaptured = true;
                return;
            }

            if (!string.Equals(
                    playerExtensionFingerprint,
                    validated,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player extension fingerprint changed after its immutable run snapshot was captured.");
            }
        }

        internal bool TryGetPlayerExtensionFingerprint(out string fingerprint)
        {
            fingerprint = playerExtensionFingerprint;
            return playerExtensionFingerprintCaptured;
        }

        internal string GetRequiredPlayerExtensionFingerprint()
        {
            if (!playerExtensionFingerprintCaptured)
            {
                throw new InvalidOperationException(
                    "Player extension fingerprint was not captured during preflight.");
            }

            return playerExtensionFingerprint;
        }

        private static string RequirePlayerExtensionFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)
                || fingerprint.Length != 64)
            {
                throw new ArgumentException(
                    "Player extension fingerprint must be a 64-character SHA-256 digest.",
                    nameof(fingerprint));
            }

            for (int index = 0; index < fingerprint.Length; index++)
            {
                char character = fingerprint[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException(
                        "Player extension fingerprint must use lowercase hexadecimal SHA-256 text.",
                        nameof(fingerprint));
                }
            }

            return fingerprint;
        }

        internal void SetPlan(IReadOnlyList<CompiledBuildStep> compiledPlan)
        {
            ThrowIfSealedForPublication(nameof(SetPlan));
            if (compiledPlan == null)
            {
                throw new ArgumentNullException(nameof(compiledPlan));
            }

            var snapshot = new CompiledBuildStep[compiledPlan.Count];
            for (int index = 0; index < compiledPlan.Count; index++)
            {
                snapshot[index] = compiledPlan[index]
                    ?? throw new ArgumentException(
                        $"Compiled build step at index {index} is null.",
                        nameof(compiledPlan));
            }

            plan = new ReadOnlyCollection<CompiledBuildStep>(snapshot);
        }

        public IReadOnlyList<BuildStepInvocation> GetDependencyInvocations(
            BuildStepInvocation invocation,
            string stepTypeId = null)
        {
            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            var dependencyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectDependencyIds(invocation, dependencyIds);
            var dependencies = new List<BuildStepInvocation>(dependencyIds.Count);
            for (int index = 0; index < plan.Count; index++)
            {
                BuildStepInvocation candidate = plan[index].Invocation;
                if (dependencyIds.Contains(candidate.InvocationId)
                    && (string.IsNullOrWhiteSpace(stepTypeId)
                        || string.Equals(
                            candidate.StepTypeId,
                            stepTypeId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    dependencies.Add(candidate);
                }
            }

            return dependencies.AsReadOnly();
        }

        private void CollectDependencyIds(
            BuildStepInvocation invocation,
            ISet<string> dependencyIds)
        {
            for (int index = 0; index < invocation.Dependencies.Count; index++)
            {
                string dependencyId = invocation.Dependencies[index]?.InvocationId;
                BuildStepInvocation dependency = Request.GetInvocation(dependencyId);
                if (dependency == null || !dependencyIds.Add(dependency.InvocationId))
                {
                    continue;
                }

                CollectDependencyIds(dependency, dependencyIds);
            }
        }

        internal void SetRecipeProvenance(
            IReadOnlyList<BuildRecipeProvenanceEntry> provenance)
        {
            ThrowIfSealedForPublication(nameof(SetRecipeProvenance));
            if (provenance == null)
            {
                throw new ArgumentNullException(nameof(provenance));
            }

            var snapshot = new BuildRecipeProvenanceEntry[provenance.Count];
            for (int index = 0; index < provenance.Count; index++)
            {
                snapshot[index] = provenance[index]
                    ?? throw new ArgumentException(
                        $"Build recipe provenance entry at index {index} is null.",
                        nameof(provenance));
            }

            recipeProvenance = new ReadOnlyCollection<BuildRecipeProvenanceEntry>(
                snapshot);
            recipeProvenanceCaptured = true;
        }

        public IHotUpdateBuildAdapter ResolveHotUpdateAdapter(
            BuildStepInvocation invocation)
        {
            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            HotUpdateBuildConfiguration configuration =
                invocation.GetRequiredConfiguration<HotUpdateBuildConfiguration>();
            string providerId = configuration.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException(
                    $"Hot-update invocation '{invocation.InvocationId}' returned an empty provider id.");
            }

            string cacheKey = invocation.InvocationId;
            if (!hotUpdateAdapters.TryGetValue(
                    cacheKey,
                    out IHotUpdateBuildAdapter adapter))
            {
                ThrowIfSealedForPublication(nameof(ResolveHotUpdateAdapter));
                adapter = HotUpdateBuildAdapterRegistry.ResolveAdapter(providerId);
                if (adapter != null
                    && configuration.GetType() != adapter.ConfigurationType)
                {
                    throw new InvalidOperationException(
                        $"Hot-update provider '{providerId}' expects {adapter.ConfigurationType.Name}, " +
                        $"but invocation '{invocation.InvocationId}' references {configuration.GetType().Name}.");
                }

                hotUpdateAdapters.Add(cacheKey, adapter);
            }

            return adapter;
        }

        public IAssetContentBuildAdapter ResolveAssetContentAdapter(
            BuildStepInvocation invocation)
        {
            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            AssetContentBuildConfiguration configuration =
                invocation.GetRequiredConfiguration<AssetContentBuildConfiguration>();
            string providerId = configuration.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException(
                    $"Content invocation '{invocation.InvocationId}' returned an empty provider id.");
            }

            string cacheKey = invocation.InvocationId;
            if (!assetContentAdapters.TryGetValue(
                    cacheKey,
                    out IAssetContentBuildAdapter adapter))
            {
                ThrowIfSealedForPublication(nameof(ResolveAssetContentAdapter));
                adapter = BuildPipelineRegistry.ResolveContentAdapter(providerId);
                assetContentAdapters.Add(cacheKey, adapter);
            }

            return adapter;
        }

        public void AddContentResult(
            string invocationId,
            AssetContentBuildResult result)
        {
            ThrowIfSealedForPublication(nameof(AddContentResult));
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                throw new ArgumentException(
                    "Content result invocation id is required.",
                    nameof(invocationId));
            }

            if (contentResults.Count
                >= BuildResultEvidencePolicy.MaximumContentResultCount)
            {
                throw new InvalidOperationException(
                    $"A build run may record at most {BuildResultEvidencePolicy.MaximumContentResultCount} content result entries.");
            }

            long resultBytes = result?.EvidenceUtf8Bytes
                ?? throw new ArgumentNullException(nameof(result));
            BuildResultEvidencePolicy.RequireRunContentBytes(
                contentEvidenceUtf8Bytes,
                resultBytes);
            BuildResultEvidencePolicy.RequireRunContentValueCount(
                contentEvidenceValueCount,
                result.EvidenceValueCount);
            contentResults.Add(new AssetContentInvocationResult(invocationId, result));
            contentEvidenceUtf8Bytes += resultBytes;
            contentEvidenceValueCount += result.EvidenceValueCount;
        }

        internal void RegisterExclusiveOutputPaths(
            string invocationId,
            IReadOnlyList<string> paths)
        {
            ThrowIfSealedForPublication(nameof(RegisterExclusiveOutputPaths));
            BuildIdentityPolicy.ValidateBuildIdentifier(
                invocationId,
                "Exclusive output claim invocation id");
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            if (paths.Count > BuildPipelineBudgets.MaximumExclusiveOutputPathClaimCount)
            {
                throw new InvalidOperationException(
                    $"A build run may declare at most {BuildPipelineBudgets.MaximumExclusiveOutputPathClaimCount} exclusive output paths.");
            }

            var pending = new List<ExclusiveOutputPathClaim>(paths.Count);
            for (int index = 0; index < paths.Count; index++)
            {
                string normalizedPath = NormalizeExclusiveOutputPath(
                    paths[index],
                    invocationId,
                    index);
                bool alreadyRegistered = false;
                for (int existingIndex = 0;
                     existingIndex < exclusiveOutputPathClaims.Count;
                     existingIndex++)
                {
                    ExclusiveOutputPathClaim existing =
                        exclusiveOutputPathClaims[existingIndex];
                    if (string.Equals(
                            existing.InvocationId,
                            invocationId,
                            StringComparison.Ordinal)
                        && OutputPathsEqual(existing.Path, normalizedPath))
                    {
                        alreadyRegistered = true;
                        break;
                    }

                    if (OutputPathsOverlap(existing.Path, normalizedPath))
                    {
                        throw new InvalidOperationException(
                            $"Build invocation '{invocationId}' claims output '{normalizedPath}', " +
                            $"which overlaps invocation '{existing.InvocationId}' output '{existing.Path}'. " +
                            "Assign independent publication roots or combine the outputs into one provider invocation.");
                    }
                }

                if (alreadyRegistered)
                {
                    continue;
                }

                for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
                {
                    if (OutputPathsOverlap(pending[pendingIndex].Path, normalizedPath))
                    {
                        throw new InvalidOperationException(
                            $"Build invocation '{invocationId}' declares overlapping exclusive outputs " +
                            $"'{pending[pendingIndex].Path}' and '{normalizedPath}'.");
                    }
                }

                pending.Add(new ExclusiveOutputPathClaim(invocationId, normalizedPath));
            }

            if (exclusiveOutputPathClaims.Count
                > BuildPipelineBudgets.MaximumExclusiveOutputPathClaimCount - pending.Count)
            {
                throw new InvalidOperationException(
                    $"A build run may declare at most {BuildPipelineBudgets.MaximumExclusiveOutputPathClaimCount} exclusive output paths.");
            }

            exclusiveOutputPathClaims.AddRange(pending);
        }

        private static string NormalizeExclusiveOutputPath(
            string path,
            string invocationId,
            int index)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocationId}' exclusive output at index {index} must be an absolute path.");
            }

            string normalized = Path.GetFullPath(path);
            string root = Path.GetPathRoot(normalized);
            if (!string.Equals(
                    normalized,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }

            return normalized;
        }

        private static bool OutputPathsOverlap(string left, string right)
        {
            StringComparison comparison = GetPathComparison();
            if (OutputPathsEqual(left, right))
            {
                return true;
            }

            string leftPrefix = left.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? left
                : left + Path.DirectorySeparatorChar;
            string rightPrefix = right.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? right
                : right + Path.DirectorySeparatorChar;
            return left.StartsWith(rightPrefix, comparison)
                || right.StartsWith(leftPrefix, comparison);
        }

        private static bool OutputPathsEqual(string left, string right)
        {
            return string.Equals(left, right, GetPathComparison());
        }

        private static StringComparison GetPathComparison()
        {
            // Build authoring paths must remain portable across workstations and
            // CI volumes. macOS commonly uses case-insensitive APFS despite '/'
            // separators, so separator style cannot determine path identity.
            return StringComparison.OrdinalIgnoreCase;
        }

        private sealed class ExclusiveOutputPathClaim
        {
            internal ExclusiveOutputPathClaim(string invocationId, string path)
            {
                InvocationId = invocationId;
                Path = path;
            }

            internal string InvocationId { get; }
            internal string Path { get; }
        }

        /// <summary>
        /// Transfers terminal publication ownership to the current run. After
        /// this call succeeds, the runner is solely responsible for Publish,
        /// Complete, and Dispose across success, failure, and recovery paths.
        /// </summary>
        public void RegisterDeferredPublication(IBuildDeferredPublication publication)
        {
            ThrowIfSealedForPublication(nameof(RegisterDeferredPublication));
            if (publication == null)
            {
                throw new ArgumentNullException(nameof(publication));
            }

            BuildIdentityPolicy.ValidatePlainText(
                publication.Id,
                "Deferred publication id",
                BuildStepRegistrationAttribute.MaximumIdCharacters);
            if (deferredPublications.Count >= BuildPipelineBudgets.MaximumDeferredPublicationCount)
            {
                throw new InvalidOperationException(
                    $"A build run may register at most {BuildPipelineBudgets.MaximumDeferredPublicationCount} deferred publications.");
            }

            for (int index = 0; index < deferredPublications.Count; index++)
            {
                if (string.Equals(
                        deferredPublications[index].Id,
                        publication.Id,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Deferred publication id '{publication.Id}' is already registered for this run.");
                }
            }

            deferredPublications.Add(publication);
        }

        private void ThrowIfSealedForPublication(string mutation)
        {
            if (sealedForPublication)
            {
                throw new InvalidOperationException(
                    $"Build execution context mutation '{mutation}' is not allowed after the publication snapshot is sealed.");
            }
        }

    }

    public sealed class AssetContentInvocationResult
    {
        public AssetContentInvocationResult(
            string invocationId,
            AssetContentBuildResult result)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                throw new ArgumentException(
                    "Content result invocation id is required.",
                    nameof(invocationId));
            }

            InvocationId = invocationId.Trim();
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public string InvocationId { get; }
        public AssetContentBuildResult Result { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BuildStepRegistrationAttribute : Attribute
    {
        public const int MaximumIdCharacters = 128;

        public BuildStepRegistrationAttribute(string id)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(
                id,
                "Build step registration id");
            if (id.IndexOf(',') >= 0 || id.IndexOf('=') >= 0)
            {
                throw new ArgumentException(
                    "Build step registration id may not contain ',' or '=' because CI recipe options use them as delimiters.",
                    nameof(id));
            }

            StepTypeId = id;
        }

        public string StepTypeId { get; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public bool HiddenFromAuthoring { get; set; }
        public Type ConfigurationType { get; set; }
        public bool ConfigurationRequired { get; set; }
        public BuildStepMultiplicity Multiplicity { get; set; } =
            BuildStepMultiplicity.Single;
    }

    public sealed class BuildStepDescriptor
    {
        internal BuildStepDescriptor(
            string id,
            string displayName,
            string description,
            string category,
            Type implementationType,
            Type configurationType,
            bool configurationRequired,
            BuildStepMultiplicity multiplicity)
        {
            StepTypeId = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            Description = description?.Trim() ?? string.Empty;
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
            ConfigurationType = configurationType;
            ConfigurationRequired = configurationRequired;
            Multiplicity = multiplicity;
        }

        public string StepTypeId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public Type ImplementationType { get; }
        public Type ConfigurationType { get; }
        public bool ConfigurationRequired { get; }
        public BuildStepMultiplicity Multiplicity { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BuildRecoveryRegistrationAttribute : Attribute
    {
        public const int MaximumIdCharacters = 128;

        public BuildRecoveryRegistrationAttribute(string id, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(id)
                || !string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Build recovery registration id is required and may not have surrounding whitespace.",
                    nameof(id));
            }

            BuildIdentityPolicy.ValidatePlainText(
                id,
                "Build recovery registration id",
                MaximumIdCharacters);

            Id = id;
            Priority = priority;
        }

        public string Id { get; }
        public int Priority { get; }
    }

    public sealed class CompiledBuildStep
    {
        internal CompiledBuildStep(
            BuildStepInvocation invocation,
            IBuildStep step,
            bool isApplicable)
        {
            Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
            Step = step ?? throw new ArgumentNullException(nameof(step));
            IsApplicable = isApplicable;
        }

        public BuildStepInvocation Invocation { get; }
        public IBuildStep Step { get; }
        public bool IsApplicable { get; }
    }

    public interface IBuildStep
    {
        string StepTypeId { get; }
        bool IsApplicable(BuildExecutionContext context, BuildStepInvocation invocation);
        IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation);
        void Execute(BuildExecutionContext context, BuildStepInvocation invocation);
    }

    /// <summary>
    /// Declares run-wide state envelopes required by an applicable step. Steps
    /// that do not implement this contract remain isolated from PlayerSettings,
    /// VersionInfoData, and Player output validation.
    /// </summary>
    public interface IBuildStepRequirementsProvider
    {
        BuildStepRequirements GetRequirements(
            BuildExecutionContext context,
            BuildStepInvocation invocation);
    }

    public interface IBuildRecoveryParticipant
    {
        string Id { get; }
        int Priority { get; }
        IReadOnlyList<string> StateDirectoryRelativePaths { get; }
        void Recover(string projectRoot);
    }

    /// <summary>
    /// Marks a recovery participant that coordinates state owned by other
    /// participants. Coordinators always run after every ordinary participant,
    /// independently of registration ids and priorities.
    /// </summary>
    public interface IBuildRecoveryCoordinator
    {
    }

    /// <summary>
    /// Optional zero-write capability probe for recovery participants whose
    /// implementation lives in a removable package assembly.
    /// </summary>
    public interface IBuildRecoveryAvailability
    {
        bool IsRecoveryAvailable(string projectRoot, out string unavailableReason);
    }

    /// <summary>
    /// A durable publication owned by the current run. A publication may stay
    /// staged until the terminal barrier, or may also implement
    /// <see cref="IBuildDownstreamInputPublication"/> when later build steps
    /// must consume its reversible output before the terminal decision.
    /// </summary>
    public interface IBuildDeferredPublication : IDisposable
    {
        string Id { get; }
        string RecoveryStateRelativePath { get; }
        void Publish();
        void Complete();
    }

    /// <summary>
    /// A publication whose output must become visible to later build steps.
    /// Activation must retain enough durable state for Dispose or recovery to
    /// restore the exact pre-run state until the shared terminal barrier commits.
    /// </summary>
    public interface IBuildDownstreamInputPublication : IBuildDeferredPublication
    {
        void ActivateForDownstream();
    }

    /// <summary>
    /// An activated downstream publication whose transaction-owned workspace
    /// mutations can be hidden while the runner qualifies the source checkout.
    /// The returned scope must restore the exact publication-ready state when
    /// disposed. Implementations must fail closed when either state cannot be
    /// proven and must retain durable recovery evidence across interruption.
    /// </summary>
    public interface IBuildSourceQualificationPublication
        : IBuildDownstreamInputPublication
    {
        IDisposable SuspendForSourceQualification();
    }

    public interface IBuildEventSink
    {
        void RunStarted(BuildExecutionContext context, IReadOnlyList<CompiledBuildStep> plan);
        void StepStarted(BuildExecutionContext context, CompiledBuildStep step);
        void StepFinished(BuildExecutionContext context, BuildStepResult result);
        void RunFinished(BuildExecutionContext context, BuildRunResult result);
    }
}
