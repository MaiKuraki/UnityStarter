using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class AssetContentProviderIds
    {
        public const string Addressables = "addressables";
        public const string YooAsset = "yooasset";
    }

    /// <summary>
    /// Describes one provider-independent content build invocation.
    /// </summary>
    public sealed class AssetContentBuildRequest
    {
        public AssetContentBuildRequest(
            BuildTarget buildTarget,
            string packageVersion,
            string projectRoot,
            ScriptableObject configuration,
            BuildIncrementality incrementality,
            bool batchMode)
        {
            BuildTarget = buildTarget;
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            Configuration = configuration;
            Incrementality = incrementality;
            BatchMode = batchMode;
        }

        public BuildTarget BuildTarget { get; }
        public string PackageVersion { get; }
        public string ProjectRoot { get; }
        public ScriptableObject Configuration { get; }
        public BuildIncrementality Incrementality { get; }
        public bool BatchMode { get; }
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

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AssetContentAdapterRegistrationAttribute : Attribute
    {
        public AssetContentAdapterRegistrationAttribute(string providerId, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Content adapter provider id is required.", nameof(providerId));
            }

            ProviderId = providerId.Trim();
            Priority = priority;
        }

        public string ProviderId { get; }
        public int Priority { get; }
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
        int Priority { get; }
        AssetContentBuildResult Validate(AssetContentBuildRequest request);
        IReadOnlyList<AssetContentBuildResult> Build(AssetContentBuildRequest request);
    }

    /// <summary>
    /// Optional provider hook for transactional state required only while Unity builds a Player.
    /// </summary>
    public interface IAssetContentPlayerBuildSessionFactory
    {
        IReadOnlyList<string> ValidatePlayerBuild(AssetContentBuildRequest request);
        IDisposable BeginPlayerBuild(AssetContentBuildRequest request);
    }
}
