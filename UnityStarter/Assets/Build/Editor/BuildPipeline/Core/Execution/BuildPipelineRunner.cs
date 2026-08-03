using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed class BuildPipelineRunner
    {
        private const int MaximumBuildSceneCount = 1024;
        private const int MaximumBuildStepCount = 256;
        private readonly IBuildEventSink eventSink;
        private readonly Func<bool> isEditorBusy;
        private readonly string trustedProjectRoot;

        public BuildPipelineRunner(IBuildEventSink eventSink = null)
            : this(eventSink, GetCurrentProjectRoot(), IsUnityEditorBusy)
        {
        }

        internal BuildPipelineRunner(
            IBuildEventSink eventSink,
            string trustedProjectRoot)
            : this(eventSink, trustedProjectRoot, IsUnityEditorBusy)
        {
        }

        internal BuildPipelineRunner(
            IBuildEventSink eventSink,
            string trustedProjectRoot,
            Func<bool> isEditorBusy)
        {
            this.eventSink = eventSink ?? new ConsoleBuildEventSink();
            this.isEditorBusy = isEditorBusy
                ?? throw new ArgumentNullException(nameof(isEditorBusy));
            this.trustedProjectRoot = Path.GetFullPath(
                    trustedProjectRoot
                    ?? throw new ArgumentNullException(nameof(trustedProjectRoot)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public BuildRunResult Run(BuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateInvocationBoundary(request);
            string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string resultPath = BuildResultManifestWriter.GetManifestPath(request, runId);
            var context = new BuildExecutionContext(request, runId, eventSink);
            var stepResults = new List<BuildStepResult>();
            var executedSteps = new List<IBuildStep>();
            IReadOnlyList<CompiledBuildStep> plan = Array.Empty<CompiledBuildStep>();
            BuildGlobalStateScope globalStateScope = null;
            VersionInfoAssetScope versionInfoScope = null;
            Exception failure = null;
            var eventSinkFailures = new List<Exception>();

            try
            {
                // Project-central recovery precedes feature applicability and current
                // configuration validation. Disabling, removing, or reconfiguring an optional
                // provider cannot orphan a transaction created by an earlier request.
                IReadOnlyList<IBuildRecoveryParticipant> recoveryParticipants =
                    BuildPipelineRegistry.ResolveRecoveryParticipants();
                foreach (IBuildRecoveryParticipant recoveryParticipant in recoveryParticipants)
                {
                    recoveryParticipant.Recover(request.ProjectRoot);
                }

                OptionalRecoveryStateGuard.EnsureNoUnavailableRecoveryState(
                    request.ProjectRoot,
                    recoveryParticipants);

                ValidateRequest(request);
                context.Version = BuildVersionResolver.Resolve(request);
                plan = BuildPlanCompiler.Compile(context);
                NotifyEventSink(
                    () => eventSink.RunStarted(context, plan.Select(entry => entry.Step).ToArray()),
                    "RunStarted",
                    eventSinkFailures);

                globalStateScope = BuildGlobalStateScope.CaptureAndApply(request, context.Version);
                versionInfoScope = VersionInfoAssetScope.Create(
                    request.VersionInfoAssetPath,
                    context.Version);

                foreach (CompiledBuildStep compiledStep in plan)
                {
                    IBuildStep step = compiledStep.Step;
                    if (!compiledStep.IsApplicable)
                    {
                        var skipped = new BuildStepResult(step.Id, BuildStepStatus.Skipped, TimeSpan.Zero, "Step is not applicable to this request.");
                        stepResults.Add(skipped);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, skipped),
                            $"StepFinished:{step.Id}",
                            eventSinkFailures);
                        continue;
                    }

                    NotifyEventSink(
                        () => eventSink.StepStarted(context, step),
                        $"StepStarted:{step.Id}",
                        eventSinkFailures);
                    var stopwatch = Stopwatch.StartNew();
                    executedSteps.Add(step);
                    try
                    {
                        step.Execute(context);
                        stopwatch.Stop();
                        var succeeded = new BuildStepResult(step.Id, BuildStepStatus.Succeeded, stopwatch.Elapsed, "Completed.");
                        stepResults.Add(succeeded);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, succeeded),
                            $"StepFinished:{step.Id}",
                            eventSinkFailures);
                    }
                    catch (Exception exception)
                    {
                        stopwatch.Stop();
                        var failed = new BuildStepResult(step.Id, BuildStepStatus.Failed, stopwatch.Elapsed, exception.Message, exception);
                        stepResults.Add(failed);
                        failure = Combine(failure, exception);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, failed),
                            $"StepFinished:{step.Id}",
                            eventSinkFailures);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                failure = Combine(failure, exception);
                if (stepResults.All(result => result.Status != BuildStepStatus.Failed))
                {
                    var preflightFailure = new BuildStepResult("preflight", BuildStepStatus.Failed, TimeSpan.Zero, exception.Message, exception);
                    stepResults.Add(preflightFailure);
                    NotifyEventSink(
                        () => eventSink.StepFinished(context, preflightFailure),
                        "StepFinished:preflight",
                        eventSinkFailures);
                }
            }
            finally
            {
                for (int index = executedSteps.Count - 1; index >= 0; index--)
                {
                    IBuildStep step = executedSteps[index];
                    try
                    {
                        step.Cleanup(context);
                    }
                    catch (Exception cleanupException)
                    {
                        failure = Combine(failure, new InvalidOperationException($"Cleanup failed for step '{step.Id}'.", cleanupException));
                        stepResults.Add(new BuildStepResult(
                            step.Id + ":cleanup",
                            BuildStepStatus.Failed,
                            TimeSpan.Zero,
                            cleanupException.Message,
                            cleanupException));
                    }
                }

                failure = DisposeScope(versionInfoScope, "VersionInfoData restore", failure);
                failure = DisposeScope(globalStateScope, "Unity build settings restore", failure);
            }

            var result = new BuildRunResult(
                runId,
                failure == null,
                request.OutputPath,
                resultPath,
                stepResults,
                failure,
                eventSinkFailures);

            NotifyEventSink(
                () => eventSink.RunFinished(context, result),
                "RunFinished",
                eventSinkFailures);
            if (eventSinkFailures.Count != result.ObserverFailures.Count)
            {
                result = new BuildRunResult(
                    runId,
                    failure == null,
                    request.OutputPath,
                    resultPath,
                    stepResults,
                    failure,
                    eventSinkFailures);
            }

            try
            {
                BuildResultManifestWriter.Write(context, result);
            }
            catch (Exception manifestException)
            {
                failure = Combine(failure, new InvalidOperationException("Failed to write the build result manifest.", manifestException));
                result = new BuildRunResult(
                    runId,
                    false,
                    request.OutputPath,
                    resultPath,
                    stepResults,
                    failure,
                    eventSinkFailures);
                UnityEngine.Debug.LogException(manifestException);
            }

            return result;
        }

        private void ValidateRequest(BuildRequest request)
        {
            if (isEditorBusy())
            {
                throw new BuildFailedException("Unity is compiling or updating assets. Wait for the Editor to become idle before building.");
            }

            if (!BuildCommandLine.IsSupportedBuildTarget(request.Target))
            {
                throw new BuildFailedException(
                    $"Unsupported player build target '{request.Target}'.");
            }

            NamedBuildTarget expectedNamedTarget =
                BuildRequestFactory.GetNamedBuildTarget(request.Target);
            if (!request.NamedTarget.Equals(expectedNamedTarget))
            {
                throw new BuildFailedException(
                    $"Named build target '{request.NamedTarget}' does not match player target '{request.Target}'.");
            }

            if (request.ScriptingBackend != ScriptingImplementation.Mono2x
                && request.ScriptingBackend != ScriptingImplementation.IL2CPP)
            {
                throw new BuildFailedException(
                    $"Unsupported scripting backend '{request.ScriptingBackend}'.");
            }

            if (request.Incrementality != BuildIncrementality.Clean
                && request.Incrementality != BuildIncrementality.Incremental)
            {
                throw new BuildFailedException(
                    $"Unsupported build incrementality mode '{request.Incrementality}'.");
            }

            if (request.BuildScenePaths.Count > MaximumBuildSceneCount)
            {
                throw new BuildFailedException(
                    $"Build request exceeds the {MaximumBuildSceneCount}-scene safety budget.");
            }

            if (request.StepIds.Count == 0
                || request.StepIds.Count > MaximumBuildStepCount)
            {
                throw new BuildFailedException(
                    $"Build request must contain between 1 and {MaximumBuildStepCount} steps.");
            }

            ValidateIdentity(
                () => BuildIdentityPolicy.ValidateApplicationVersion(
                    request.ApplicationVersion));

            ValidateIdentity(
                () => BuildIdentityPolicy.ValidatePlainText(
                    request.CompanyName,
                    "Company name",
                    256));

            try
            {
                BuildPathPolicy.ValidatePortableFileName(request.ProductName, "Product name");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    "Product name is not a portable file name. " + exception.Message);
            }

            ValidateIdentity(
                () => BuildIdentityPolicy.ValidateApplicationIdentifier(
                    request.ApplicationIdentifier));
            ValidateVersionInfoPath(request.VersionInfoAssetPath);

            foreach (string stepId in request.StepIds)
            {
                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidatePlainText(
                        stepId,
                        "Build step identifier",
                        BuildStepRegistrationAttribute.MaximumIdCharacters));
            }

            ValidateIdentity(
                () => BuildRequestFactory.ValidateAndroidExportRecipe(
                    request.StepIds,
                    request.ExportAndroidProject));
            ValidateIdentity(
                () => BuildRequestFactory.ValidateContentOnlyRecipeBinding(
                    request.StepIds,
                    request.AssetContentProviderId,
                    request.AssetContentConfiguration));

            bool hasContentProvider = !string.IsNullOrWhiteSpace(request.AssetContentProviderId);
            bool hasContentConfiguration = request.AssetContentConfiguration != null;
            if (hasContentProvider != hasContentConfiguration)
            {
                throw new BuildFailedException(
                    "Asset content provider id and configuration must either both be set or both be empty.");
            }

            if (hasContentProvider)
            {
                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidatePlainText(
                        request.AssetContentProviderId,
                        "Asset content provider identifier",
                        128));
            }

            ValidateOutputShape(request);

            BuildPathPolicy.EnsureSafeDeleteTarget(
                request.ProjectRoot,
                request.OutputDirectory,
                request.BuildRoot,
                request.AllowExternalOutput);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                request.OutputDirectory,
                "Player output directory");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                request.OutputPath,
                "Player output artifact");
        }

        private static bool IsUnityEditorBusy()
        {
            return EditorApplication.isCompiling || EditorApplication.isUpdating;
        }

        private static void ValidateIdentity(Action validation)
        {
            try
            {
                validation();
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(exception.Message);
            }
        }

        private static void ValidateVersionInfoPath(string path)
        {
            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    path,
                    "VersionInfoData path");
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    "VersionInfoData path is not a portable project-relative path. " +
                    exception.Message);
            }

            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "VersionInfoData path must be a project-relative .asset path below Assets.");
            }
        }

        private static void ValidateOutputShape(BuildRequest request)
        {
            bool expectedFolder = request.Target == BuildTarget.StandaloneOSX
                || request.Target == BuildTarget.iOS
                || request.Target == BuildTarget.WebGL
                || (request.Target == BuildTarget.Android
                    && request.ExportAndroidProject);
            if (request.OutputIsFolder != expectedFolder)
            {
                throw new BuildFailedException(
                    $"Output kind does not match target '{request.Target}'. Expected " +
                    (expectedFolder ? "a directory." : "a file artifact."));
            }

            if (request.ExportAndroidProject && request.Target != BuildTarget.Android)
            {
                throw new BuildFailedException(
                    "Android project export is valid only for the Android target.");
            }

            if (request.Target == BuildTarget.StandaloneWindows64
                && !request.OutputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Windows Player output must end with .exe.");
            }

            if (request.Target == BuildTarget.StandaloneOSX
                && !request.OutputPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "macOS Player output must end with .app.");
            }

            if (request.Target == BuildTarget.Android
                && !request.ExportAndroidProject
                && !request.OutputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                && !request.OutputPath.EndsWith(".aab", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException(
                    "Android package output must end with .apk or .aab.");
            }

            string expectedDirectory = request.OutputIsFolder
                ? Path.GetFullPath(request.OutputPath)
                : Path.GetFullPath(Path.GetDirectoryName(request.OutputPath)
                    ?? string.Empty);
            string actualDirectory = Path.GetFullPath(request.OutputDirectory);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(
                    expectedDirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    actualDirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    comparison))
            {
                throw new BuildFailedException(
                    "Player output artifact and output directory describe different publication roots.");
            }
        }

        private void ValidateInvocationBoundary(BuildRequest request)
        {
            string requestedProjectRoot = Path.GetFullPath(request.ProjectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(trustedProjectRoot, requestedProjectRoot, comparison))
            {
                throw new BuildFailedException(
                    "BuildRequest.ProjectRoot must identify the Unity project loaded by this Editor process. " +
                    $"Current='{trustedProjectRoot}', requested='{requestedProjectRoot}'.");
            }

            BuildPathPolicy.EnsureSafeBuildRoot(trustedProjectRoot, request.BuildRoot);
        }

        private static string GetCurrentProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static Exception DisposeScope(IDisposable scope, string operation, Exception failure)
        {
            if (scope == null)
            {
                return failure;
            }

            try
            {
                scope.Dispose();
                return failure;
            }
            catch (Exception exception)
            {
                return Combine(failure, new InvalidOperationException(operation + " failed.", exception));
            }
        }

        private static void NotifyEventSink(
            Action callback,
            string callbackName,
            ICollection<Exception> failures)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Build event sink callback '{callbackName}' failed.",
                    exception));
            }
        }

        private static Exception Combine(Exception first, Exception second)
        {
            if (first == null)
            {
                return second;
            }

            return new AggregateException(first, second);
        }
    }

    public sealed class ConsoleBuildEventSink : IBuildEventSink
    {
        public void RunStarted(BuildExecutionContext context, IReadOnlyList<IBuildStep> plan)
        {
            string stepIds = string.Join(" -> ", plan.Select(step => step.Id));
            UnityEngine.Debug.Log(
                $"[BuildPipeline] Run {context.RunId} started. Target={context.Request.Target}, PackageVersion={context.Version.PackageVersion}, Steps={stepIds}");
        }

        public void StepStarted(BuildExecutionContext context, IBuildStep step)
        {
            UnityEngine.Debug.Log($"[BuildPipeline] Step '{step.Id}' started.");
        }

        public void StepFinished(BuildExecutionContext context, BuildStepResult result)
        {
            string message = $"[BuildPipeline] Step '{result.StepId}' {result.Status} in {result.Duration.TotalSeconds:F2}s. {result.Message}";
            if (result.Status == BuildStepStatus.Failed)
            {
                UnityEngine.Debug.LogError(message);
            }
            else if (result.Status == BuildStepStatus.Skipped)
            {
                UnityEngine.Debug.Log(message);
            }
            else
            {
                UnityEngine.Debug.Log(message);
            }
        }

        public void RunFinished(BuildExecutionContext context, BuildRunResult result)
        {
            string message =
                $"[BuildPipeline] Run {result.RunId} {(result.Succeeded ? "succeeded" : "failed")}. " +
                $"Output='{result.OutputPath}', Result='{result.ResultManifestPath}'.";
            if (result.Succeeded)
            {
                UnityEngine.Debug.Log(message);
            }
            else
            {
                UnityEngine.Debug.LogError(message + "\n" + result.Failure);
            }
        }
    }

    internal static class BuildResultManifestWriter
    {
        private const int BufferSize = 8192;
        private const int MaximumManifestBytes = 64 * 1024 * 1024;

        [Serializable]
        private sealed class Manifest
        {
            public string schemaVersion = "3";
            public string runId;
            public bool succeeded;
            public string unityVersion;
            public string target;
            public string applicationVersion;
            public string packageVersion;
            public long buildNumber;
            public string commitHash;
            public string versionControlProvider;
            public string branch;
            public string outputPath;
            public string outputDirectory;
            public string failure;
            public string[] observerFailures;
            public StepEntry[] steps;
            public ContentEntry[] content;
        }

        [Serializable]
        private sealed class StepEntry
        {
            public string id;
            public string status;
            public double durationSeconds;
            public string message;
        }

        [Serializable]
        private sealed class ContentEntry
        {
            public bool succeeded;
            public string providerId;
            public string packageName;
            public string packageVersion;
            public string failedTask;
            public string errorInfo;
            public string errorStack;
            public string outputPackageDirectory;
            public string bundledPackageDirectory;
            public string reportPath;
            public string[] artifacts;
            public string[] warnings;
        }

        public static string GetManifestPath(BuildRequest request, string runId)
        {
            string path = Path.Combine(
                request.BuildRoot,
                ".buildpipeline",
                "results",
                runId + ".json");
            return BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                path,
                "Build result manifest",
                ".tmp".Length);
        }

        public static void Write(BuildExecutionContext context, BuildRunResult result)
        {
            string path = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                result.ResultManifestPath,
                "Build result manifest",
                ".tmp".Length);
            string directory = Path.GetDirectoryName(path);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                directory,
                "Build result manifest directory");
            Directory.CreateDirectory(directory);

            var manifest = new Manifest
            {
                runId = result.RunId,
                succeeded = result.Succeeded,
                unityVersion = Application.unityVersion,
                target = context.Request.Target.ToString(),
                applicationVersion = context.Request.ApplicationVersion,
                packageVersion = context.Version?.PackageVersion ?? string.Empty,
                buildNumber = context.Version?.BuildNumber ?? 0,
                commitHash = context.Version?.CommitHash ?? string.Empty,
                versionControlProvider = context.Version?.ProviderId ?? string.Empty,
                branch = context.Version?.Branch ?? string.Empty,
                outputPath = result.OutputPath,
                outputDirectory = context.Request.OutputDirectory,
                failure = result.Failure?.ToString() ?? string.Empty,
                observerFailures = result.ObserverFailures
                    .Select(observerFailure => observerFailure.ToString())
                    .ToArray(),
                steps = result.Steps.Select(step => new StepEntry
                {
                    id = step.StepId,
                    status = step.Status.ToString(),
                    durationSeconds = step.Duration.TotalSeconds,
                    message = step.Message
                }).ToArray(),
                content = context.ContentResults.Select(content => new ContentEntry
                {
                    succeeded = content.Succeeded,
                    providerId = content.ProviderId,
                    packageName = content.PackageName,
                    packageVersion = content.PackageVersion,
                    failedTask = content.FailedTask,
                    errorInfo = content.ErrorInfo,
                    errorStack = content.ErrorStack,
                    outputPackageDirectory = content.OutputPackageDirectory,
                    bundledPackageDirectory = content.BundledPackageDirectory,
                    reportPath = content.ReportPath,
                    artifacts = content.ProducedArtifacts.ToArray(),
                    warnings = content.Warnings.ToArray()
                }).ToArray()
            };

            string json = JsonUtility.ToJson(manifest, true);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
            if (bytes.Length > MaximumManifestBytes)
            {
                throw new IOException(
                    $"Build result manifest exceeds the {MaximumManifestBytes}-byte safety budget: '{path}'.");
            }

            string temporaryPath = path + ".tmp";
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                temporaryPath,
                "Build result manifest temporary file");
            bool ownsTemporaryFile = false;
            Exception writeFailure = null;
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           BufferSize,
                           FileOptions.WriteThrough))
                {
                    ownsTemporaryFile = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                File.Move(temporaryPath, path);
                ownsTemporaryFile = false;
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }

            Exception cleanupFailure = null;
            try
            {
                if (ownsTemporaryFile && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (writeFailure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "Build result manifest write and temporary-file cleanup both failed.",
                    writeFailure,
                    cleanupFailure);
            }

            if (writeFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(writeFailure).Throw();
            }

            if (cleanupFailure != null)
            {
                throw new IOException(
                    $"Build result manifest was written, but temporary file '{temporaryPath}' could not be removed.",
                    cleanupFailure);
            }
        }
    }
}
