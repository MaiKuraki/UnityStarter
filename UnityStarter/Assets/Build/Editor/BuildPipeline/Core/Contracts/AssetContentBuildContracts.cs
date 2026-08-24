using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Base contract for provider-owned content configuration assets. The
    /// provider identifier is derived from the assigned asset, so a build
    /// recipe cannot drift into an id/configuration mismatch.
    /// </summary>
    public abstract class AssetContentBuildConfiguration : ScriptableObject
    {
        public abstract string ProviderId { get; }
    }

    /// <summary>
    /// Describes one provider-independent content build invocation.
    /// <see cref="Purpose"/> lets adapters distinguish release builds (immutable by
    /// default) from local previews (overwritable) without mutating shared profile assets.
    /// </summary>
    public sealed class AssetContentBuildRequest
    {
        public AssetContentBuildRequest(
            string invocationId,
            BuildTarget buildTarget,
            string packageVersion,
            string projectRoot,
            AssetContentBuildConfiguration configuration,
            BuildIncrementality incrementality,
            bool batchMode,
            BuildPurpose purpose,
            bool replaceExactVersion = false)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                throw new ArgumentException(
                    "Content build invocation id is required.",
                    nameof(invocationId));
            }

            InvocationId = invocationId.Trim();
            BuildTarget = buildTarget;
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            Configuration = configuration;
            ProviderId = configuration?.ProviderId ?? string.Empty;
            Incrementality = incrementality;
            BatchMode = batchMode;
            Purpose = purpose;
            ReplaceExactVersion = replaceExactVersion;
        }

        public string InvocationId { get; }
        public string ProviderId { get; }
        public BuildTarget BuildTarget { get; }
        public string PackageVersion { get; }
        public string ProjectRoot { get; }
        public AssetContentBuildConfiguration Configuration { get; }
        public BuildIncrementality Incrementality { get; }
        public bool BatchMode { get; }
        public BuildPurpose Purpose { get; }
        public bool ReplaceExactVersion { get; }

        /// <summary>
        /// True when the invocation is an interactive local iteration (a non-batch
        /// development build or a local release preview). Local iterations use a stable,
        /// non-version-controlled version and are intentionally overwritable so repeated
        /// builds in the same workspace do not collide with committed output.
        /// </summary>
        public bool IsLocalIteration =>
            !BatchMode && (Purpose == BuildPurpose.Development || Purpose == BuildPurpose.LocalReleasePreview);

        /// <summary>
        /// True when the invocation is a local release preview. Local previews use a
        /// stable, non-version-controlled version and are intentionally overwritable so
        /// repeated previews in the same workspace do not collide with committed output.
        /// </summary>
        public bool IsLocalPreview => Purpose == BuildPurpose.LocalReleasePreview;
    }

    /// <summary>
    /// Structured result returned by an optional content build adapter.
    /// </summary>
    public sealed class AssetContentBuildResult
    {
        private static readonly string[] EmptyStrings = Array.Empty<string>();

        private AssetContentBuildResult(
            bool succeeded,
            string providerId,
            string packageName,
            string packageVersion,
            string failedTask,
            string errorInfo,
            string errorStack,
            string outputPackageDirectory,
            string bundledPackageDirectory,
            string reportPath,
            IReadOnlyList<string> producedArtifacts,
            IReadOnlyList<string> warnings)
        {
            Succeeded = succeeded;
            ProviderId = providerId ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            PackageVersion = packageVersion ?? string.Empty;
            FailedTask = failedTask ?? string.Empty;
            ErrorInfo = errorInfo ?? string.Empty;
            ErrorStack = errorStack ?? string.Empty;
            OutputPackageDirectory = outputPackageDirectory ?? string.Empty;
            BundledPackageDirectory = bundledPackageDirectory ?? string.Empty;
            ReportPath = reportPath ?? string.Empty;
            ProducedArtifacts = SnapshotStrings(producedArtifacts);
            Warnings = SnapshotStrings(warnings);
            EvidenceValueCount = checked(
                9 + ProducedArtifacts.Count + Warnings.Count);
            EvidenceUtf8Bytes =
                BuildResultEvidencePolicy.ValidateContentResult(this);
        }

        public bool Succeeded { get; }
        public string ProviderId { get; }
        public string PackageName { get; }
        public string PackageVersion { get; }
        public string FailedTask { get; }
        public string ErrorInfo { get; }
        public string ErrorStack { get; }
        public string OutputPackageDirectory { get; }
        public string BundledPackageDirectory { get; }
        public string ReportPath { get; }
        public IReadOnlyList<string> ProducedArtifacts { get; }
        public IReadOnlyList<string> Warnings { get; }
        internal long EvidenceUtf8Bytes { get; }
        internal int EvidenceValueCount { get; }

        public static AssetContentBuildResult Success(
            string providerId,
            string packageName,
            string packageVersion,
            string outputPackageDirectory = null,
            string bundledPackageDirectory = null,
            string reportPath = null,
            IReadOnlyList<string> producedArtifacts = null,
            IReadOnlyList<string> warnings = null)
        {
            return new AssetContentBuildResult(
                true,
                providerId,
                packageName,
                packageVersion,
                null,
                null,
                null,
                outputPackageDirectory,
                bundledPackageDirectory,
                reportPath,
                producedArtifacts,
                warnings);
        }

        public static AssetContentBuildResult Failure(
            string providerId,
            string packageName,
            string packageVersion,
            string failedTask,
            string errorInfo,
            string errorStack = null,
            IReadOnlyList<string> warnings = null)
        {
            return new AssetContentBuildResult(
                false,
                providerId,
                packageName,
                packageVersion,
                failedTask,
                errorInfo,
                errorStack,
                null,
                null,
                null,
                null,
                warnings);
        }

        private static IReadOnlyList<string> SnapshotStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return EmptyStrings;
            }

            var snapshot = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index] ?? string.Empty;
            }

            return new ReadOnlyCollection<string>(snapshot);
        }
    }

    /// <summary>
    /// Provider build results plus an optional sealed publication. The
    /// publication is committed by the pipeline only after every selected
    /// step and transient Unity-state restoration gate succeeds.
    /// </summary>
    public sealed class AssetContentBuildOperation
    {
        private readonly IReadOnlyList<AssetContentBuildResult> results;

        public AssetContentBuildOperation(
            IReadOnlyList<AssetContentBuildResult> results,
            IBuildDeferredPublication publication = null)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (results.Count
                > BuildResultEvidencePolicy.MaximumContentOperationResultCount)
            {
                throw new InvalidOperationException(
                    $"A content build operation may return at most {BuildResultEvidencePolicy.MaximumContentOperationResultCount} result entries.");
            }

            var snapshot = new AssetContentBuildResult[results.Count];
            for (int index = 0; index < results.Count; index++)
            {
                snapshot[index] = results[index]
                    ?? throw new ArgumentException(
                        $"Content build operation result at index {index} is null.",
                        nameof(results));
            }

            this.results = new ReadOnlyCollection<AssetContentBuildResult>(snapshot);
            Publication = publication;
        }

        public IReadOnlyList<AssetContentBuildResult> Results => results;
        public IBuildDeferredPublication Publication { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AssetContentAdapterRegistrationAttribute : Attribute
    {
        public AssetContentAdapterRegistrationAttribute(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Content adapter provider id is required.", nameof(providerId));
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                providerId,
                "Content adapter provider id");

            ProviderId = providerId.Trim();
        }

        public string ProviderId { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AssetContentProviderAuthoringAttribute : Attribute
    {
        public AssetContentProviderAuthoringAttribute(string providerId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Content provider authoring id is required.", nameof(providerId));
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                providerId,
                "Content provider authoring id");

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Content provider display name is required.", nameof(displayName));
            }

            ProviderId = providerId.Trim();
            DisplayName = displayName.Trim();
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; set; }
        public string RequiredEditorTypeName { get; set; }
        public int Order { get; set; }
    }

    public sealed class AssetContentProviderDescriptor
    {
        internal AssetContentProviderDescriptor(
            string providerId,
            string displayName,
            string description,
            int order,
            Type configurationType,
            Type adapterType,
            bool dependencyAvailable)
        {
            ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? string.Empty;
            Order = order;
            ConfigurationType = configurationType ?? throw new ArgumentNullException(nameof(configurationType));
            AdapterType = adapterType;
            DependencyAvailable = dependencyAvailable;
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int Order { get; }
        public Type ConfigurationType { get; }
        public Type AdapterType { get; }
        public bool AdapterAvailable => AdapterType != null;
        public bool DependencyAvailable { get; }
        public bool IsAvailable => AdapterAvailable && DependencyAvailable;
    }

    /// <summary>
    /// Implemented by reflection-isolated or version-gated provider adapters.
    /// </summary>
    public interface IAssetContentBuildAdapter
    {
        string ProviderId { get; }
        AssetContentBuildResult Validate(AssetContentBuildRequest request);
        AssetContentBuildOperation Build(AssetContentBuildRequest request);
    }

    /// <summary>
    /// Optional preflight contract for provider-owned terminal output paths.
    /// Every returned path must be absolute. The pipeline rejects exact and
    /// ancestor/descendant overlap across selected invocations before any
    /// provider build begins.
    /// </summary>
    public interface IAssetContentBuildOutputClaimProvider
    {
        IReadOnlyList<string> GetExclusiveOutputPaths(
            AssetContentBuildRequest request);
    }

    /// <summary>
    /// Optional provider hook for transactional state required only while Unity builds a Player.
    /// A non-empty <see cref="ExclusivePlayerSessionKey"/> claims one process-global
    /// session namespace. A Player dependency closure may contain at most one
    /// session factory for each non-empty key. An empty key declares that sessions
    /// owned by separate invocations may coexist.
    /// </summary>
    public interface IAssetContentPlayerBuildSessionFactory
    {
        string ExclusivePlayerSessionKey { get; }
        IReadOnlyList<string> ValidatePlayerBuild(AssetContentBuildRequest request);
        IDisposable BeginPlayerBuild(AssetContentBuildRequest request);
    }
}
