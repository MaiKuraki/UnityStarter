using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class BuildStepIds
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
            string providerId)
        {
            ApplicationVersion = applicationVersion ?? string.Empty;
            PackageVersion = packageVersion ?? string.Empty;
            if (buildNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildNumber),
                    buildNumber,
                    "Native build number must be positive.");
            }

            BuildNumber = buildNumber;
            CommitHash = commitHash ?? string.Empty;
            CommitCount = commitCount ?? string.Empty;
            Branch = branch ?? string.Empty;
            CommitDate = commitDate ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
        }

        public string ApplicationVersion { get; }
        public string PackageVersion { get; }
        public long BuildNumber { get; }
        public string CommitHash { get; }
        public string CommitCount { get; }
        public string Branch { get; }
        public string CommitDate { get; }
        public string ProviderId { get; }
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
            HybridCLRBuildConfig hybridClrConfiguration,
            BuildTarget target,
            NamedBuildTarget namedTarget,
            ScriptingImplementation scriptingBackend,
            string projectRoot,
            string buildRoot,
            string outputPath,
            string outputDirectory,
            bool outputIsFolder,
            BuildIncrementality incrementality,
            bool deleteDebugFiles,
            bool debugBuild,
            bool exportAndroidProject,
            bool allowExternalOutput,
            bool? cheatOverride,
            bool batchMode,
            string applicationVersion,
            string assetContentProviderId,
            ScriptableObject assetContentConfiguration,
            bool useHybridClr,
            bool enablePlayerObfuscation,
            IReadOnlyList<string> stepIds)
        {
            CompanyName = companyName ?? string.Empty;
            ProductName = productName ?? string.Empty;
            ApplicationIdentifier = applicationIdentifier ?? string.Empty;
            VersionInfoAssetPath = versionInfoAssetPath ?? string.Empty;
            BuildScenePaths = SnapshotStrings(buildScenePaths, nameof(buildScenePaths));
            CheatBuildMode = cheatBuildMode;
            HybridClrConfiguration = hybridClrConfiguration;
            Target = target;
            NamedTarget = namedTarget;
            ScriptingBackend = scriptingBackend;
            ProjectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
            BuildRoot = buildRoot ?? throw new ArgumentNullException(nameof(buildRoot));
            OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
            OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            OutputIsFolder = outputIsFolder;
            Incrementality = incrementality;
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
            AssetContentProviderId = assetContentProviderId?.Trim() ?? string.Empty;
            AssetContentConfiguration = assetContentConfiguration;
            UseHybridClr = useHybridClr;
            EnablePlayerObfuscation = enablePlayerObfuscation;
            StepIds = SnapshotStrings(stepIds, nameof(stepIds));
        }

        public string CompanyName { get; }
        public string ProductName { get; }
        public string ApplicationIdentifier { get; }
        public string VersionInfoAssetPath { get; }
        public IReadOnlyList<string> BuildScenePaths { get; }
        public CheatBuildMode CheatBuildMode { get; }
        public HybridCLRBuildConfig HybridClrConfiguration { get; }
        public BuildTarget Target { get; }
        public NamedBuildTarget NamedTarget { get; }
        public ScriptingImplementation ScriptingBackend { get; }
        public string ProjectRoot { get; }
        public string BuildRoot { get; }
        public string OutputPath { get; }
        public string OutputDirectory { get; }
        public bool OutputIsFolder { get; }
        public BuildIncrementality Incrementality { get; }
        public bool DeleteDebugFiles { get; }
        public bool DebugBuild { get; }
        public bool ExportAndroidProject { get; }
        public bool AllowExternalOutput { get; }
        public bool? CheatOverride { get; }
        public bool CheatEnabled { get; }
        public bool BatchMode { get; }
        public string ApplicationVersion { get; }
        public string AssetContentProviderId { get; }
        public ScriptableObject AssetContentConfiguration { get; }
        public bool UseHybridClr { get; }
        public bool EnablePlayerObfuscation { get; }
        public IReadOnlyList<string> StepIds { get; }

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
    }

    public sealed class BuildStepResult
    {
        public BuildStepResult(string stepId, BuildStepStatus status, TimeSpan duration, string message, Exception exception = null)
        {
            StepId = stepId ?? string.Empty;
            Status = status;
            Duration = duration;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public string StepId { get; }
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
            IReadOnlyList<Exception> observerFailures = null)
        {
            RunId = runId ?? string.Empty;
            Succeeded = succeeded;
            OutputPath = outputPath ?? string.Empty;
            ResultManifestPath = resultManifestPath ?? string.Empty;
            Steps = SnapshotItems(steps);
            Failure = failure;
            ObserverFailures = SnapshotItems(observerFailures);
        }

        public string RunId { get; }
        public bool Succeeded { get; }
        public string OutputPath { get; }
        public string ResultManifestPath { get; }
        public IReadOnlyList<BuildStepResult> Steps { get; }
        public Exception Failure { get; }
        public IReadOnlyList<Exception> ObserverFailures { get; }

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
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<AssetContentBuildResult> contentResults = new List<AssetContentBuildResult>();
        private readonly IReadOnlyList<AssetContentBuildResult> contentResultsView;
        private bool assetContentAdapterResolved;
        private IAssetContentBuildAdapter assetContentAdapter;
        private ExceptionDispatchInfo assetContentAdapterResolutionFailure;

        public BuildExecutionContext(BuildRequest request, string runId, IBuildEventSink eventSink)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            EventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
            contentResultsView = contentResults.AsReadOnly();
        }

        public BuildRequest Request { get; }
        public string RunId { get; }
        public IBuildEventSink EventSink { get; }
        public BuildVersionContext Version { get; set; }
        public BuildReport PlayerBuildReport { get; set; }
        public IReadOnlyList<AssetContentBuildResult> ContentResults => contentResultsView;

        public IAssetContentBuildAdapter ResolveAssetContentAdapter()
        {
            if (!assetContentAdapterResolved)
            {
                try
                {
                    assetContentAdapter = BuildPipelineRegistry.ResolveContentAdapter(
                        Request.AssetContentProviderId);
                }
                catch (Exception exception)
                {
                    assetContentAdapterResolutionFailure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    assetContentAdapterResolved = true;
                }
            }

            assetContentAdapterResolutionFailure?.Throw();
            return assetContentAdapter;
        }

        public void AddContentResult(AssetContentBuildResult result)
        {
            if (result != null)
            {
                contentResults.Add(result);
            }
        }

        public void SetValue(string key, object value)
        {
            values[key] = value;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            if (values.TryGetValue(key, out object stored) && stored is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BuildStepRegistrationAttribute : Attribute
    {
        public const int MaximumIdCharacters = 128;

        public BuildStepRegistrationAttribute(string id, int priority = 0)
        {
            BuildIdentityPolicy.ValidatePlainText(
                id,
                "Build step registration id",
                MaximumIdCharacters);
            if (id.IndexOf(',') >= 0)
            {
                throw new ArgumentException(
                    "Build step registration id may not contain ',' because CI step lists use it as their delimiter.",
                    nameof(id));
            }

            Id = id;
            Priority = priority;
        }

        public string Id { get; }
        public int Priority { get; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public bool HiddenFromAuthoring { get; set; }
    }

    public sealed class BuildStepDescriptor
    {
        internal BuildStepDescriptor(
            string id,
            string displayName,
            string description,
            string category,
            int priority,
            Type implementationType)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            Description = description?.Trim() ?? string.Empty;
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            Priority = priority;
            ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public int Priority { get; }
        public Type ImplementationType { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BuildRecoveryRegistrationAttribute : Attribute
    {
        public BuildRecoveryRegistrationAttribute(string id, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Build recovery registration id is required.", nameof(id));
            }

            Id = id.Trim();
            Priority = priority;
        }

        public string Id { get; }
        public int Priority { get; }
    }

    public sealed class CompiledBuildStep
    {
        internal CompiledBuildStep(IBuildStep step, bool isApplicable)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            IsApplicable = isApplicable;
        }

        public IBuildStep Step { get; }
        public bool IsApplicable { get; }
    }

    public interface IBuildStep
    {
        string Id { get; }
        int Priority { get; }
        bool IsApplicable(BuildExecutionContext context);
        IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context);
        IReadOnlyList<string> Validate(BuildExecutionContext context);
        void Execute(BuildExecutionContext context);
        void Cleanup(BuildExecutionContext context);
    }

    public interface IBuildRecoveryParticipant
    {
        string Id { get; }
        int Priority { get; }
        void Recover(string projectRoot);
    }

    public interface IBuildEventSink
    {
        void RunStarted(BuildExecutionContext context, IReadOnlyList<IBuildStep> plan);
        void StepStarted(BuildExecutionContext context, IBuildStep step);
        void StepFinished(BuildExecutionContext context, BuildStepResult result);
        void RunFinished(BuildExecutionContext context, BuildRunResult result);
    }
}
