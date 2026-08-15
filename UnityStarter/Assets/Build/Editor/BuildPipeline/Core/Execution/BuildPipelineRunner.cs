using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed class BuildPipelineRunner
    {
        private const int MaximumBuildSceneCount = 1024;
        private readonly IBuildEventSink eventSink;
        private readonly Func<bool> isEditorBusy;
        private readonly Func<BuildRequest, BuildVersionContext> versionResolver;
        private readonly string trustedProjectRoot;

        public BuildPipelineRunner(IBuildEventSink eventSink = null)
            : this(
                eventSink,
                GetCurrentProjectRoot(),
                EditorBuildAvailabilityPolicy.IsBusy,
                BuildVersionResolver.Resolve)
        {
        }

        internal BuildPipelineRunner(
            IBuildEventSink eventSink,
            string trustedProjectRoot)
            : this(
                eventSink,
                trustedProjectRoot,
                EditorBuildAvailabilityPolicy.IsBusy,
                BuildVersionResolver.Resolve)
        {
        }

        internal BuildPipelineRunner(
            IBuildEventSink eventSink,
            string trustedProjectRoot,
            Func<bool> isEditorBusy)
            : this(
                eventSink,
                trustedProjectRoot,
                isEditorBusy,
                BuildVersionResolver.Resolve)
        {
        }

        internal BuildPipelineRunner(
            IBuildEventSink eventSink,
            string trustedProjectRoot,
            Func<bool> isEditorBusy,
            Func<BuildRequest, BuildVersionContext> versionResolver)
        {
            this.eventSink = eventSink ?? new ConsoleBuildEventSink();
            this.isEditorBusy = isEditorBusy
                ?? throw new ArgumentNullException(nameof(isEditorBusy));
            this.versionResolver = versionResolver
                ?? throw new ArgumentNullException(nameof(versionResolver));
            this.trustedProjectRoot = Path.GetFullPath(
                    trustedProjectRoot
                    ?? throw new ArgumentNullException(nameof(trustedProjectRoot)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public BuildRunResult Run(BuildRequest request)
        {
            string runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 8);
            return RunCore(request, runId, requiredResultManifestPath: null);
        }

        /// <summary>
        /// Executes a build under an entry-point-owned result identity. The
        /// required manifest path must exactly match the canonical path derived
        /// from the request project root and run id.
        /// </summary>
        public BuildRunResult Run(
            BuildRequest request,
            string runId,
            string requiredResultManifestPath)
        {
            if (string.IsNullOrWhiteSpace(requiredResultManifestPath))
            {
                throw new ArgumentException(
                    "A required build result manifest path is required.",
                    nameof(requiredResultManifestPath));
            }

            return RunCore(request, runId, requiredResultManifestPath);
        }

        private BuildRunResult RunCore(
            BuildRequest request,
            string runId,
            string requiredResultManifestPath)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateInvocationBoundary(request);
            BuildResultEvidenceSession.ValidateRunId(runId);
            string resultPath = BuildResultManifestWriter.GetManifestPath(request, runId);
            if (requiredResultManifestPath != null
                && !PathsEqual(resultPath, requiredResultManifestPath))
            {
                throw new ArgumentException(
                    "The required build result manifest path does not match the canonical path " +
                    $"for run '{runId}'. Expected='{resultPath}', required='{requiredResultManifestPath}'.",
                    nameof(requiredResultManifestPath));
            }

            var context = new BuildExecutionContext(request, runId, eventSink);
            var stepResults = new List<BuildStepResult>();
            IReadOnlyList<CompiledBuildStep> plan = Array.Empty<CompiledBuildStep>();
            BuildStepRequirements requirements = BuildStepRequirements.None;
            BuildGlobalStateScope globalStateScope = null;
            VersionInfoAssetScope versionInfoScope = null;
            ProjectSettingsStateGuard projectSettingsGuard = null;
            BuildWorkspaceLease workspaceLease = null;
            BuildRecipeProvenanceCapture recipeProvenance = null;
            BuildResultManifestSnapshot manifestSnapshot = null;
            Exception failure = null;
            var nonFatalFailures = new List<Exception>();

            try
            {
                workspaceLease = BuildWorkspaceLease.Acquire(
                    trustedProjectRoot,
                    runId,
                    BuildWorkspaceOperation.Build);
                EnsureEditorIsIdle();
                BuildWorkspaceService.EnsureReady(request.ProjectRoot);
                projectSettingsGuard = ProjectSettingsStateGuard.Capture(
                    request.ProjectRoot);
                ValidateRequestBoundary(request);
                BuildRecipeProvenanceCapture preflightProvenance =
                    BuildRecipeProvenanceCapture.Capture(request);
                context.SetRecipeProvenance(preflightProvenance.Entries);
                preflightProvenance.ThrowIfInvalid();
                recipeProvenance = preflightProvenance;
                context.Version = versionResolver(request);
                BuildSourceWorkspacePolicy.EnsureAllowed(request, context.Version);
                plan = BuildPlanCompiler.Compile(context);
                requirements = ResolveRequirements(context, plan);
                ValidatePlanRequirements(request, requirements);
                NotifyEventSink(
                    () => eventSink.RunStarted(context, plan),
                    "RunStarted",
                    nonFatalFailures);
                recipeProvenance.ValidateUnchanged(
                    request,
                    "before build-state mutation");

                if ((requirements & BuildStepRequirements.UnityGlobalState) != 0)
                {
                    using (ProjectSettingsStateGuard.AuthorizationWindow authorization =
                           projectSettingsGuard.BeginAuthorization(
                               "ProjectSettings/ProjectSettings.asset",
                               "ProjectSettings/EditorBuildSettings.asset"))
                    {
                        globalStateScope = BuildGlobalStateScope.CaptureAndApply(
                            request,
                            context.Version);
                        authorization.Commit();
                    }
                }

                if ((requirements & BuildStepRequirements.VersionInfoAsset) != 0)
                {
                    versionInfoScope = VersionInfoAssetScope.Create(
                        request.VersionInfoAssetPath,
                        context.Version);
                }

                foreach (CompiledBuildStep compiledStep in plan)
                {
                    IBuildStep step = compiledStep.Step;
                    BuildStepInvocation invocation = compiledStep.Invocation;
                    if (!compiledStep.IsApplicable)
                    {
                        var skipped = new BuildStepResult(
                            invocation.InvocationId,
                            invocation.StepTypeId,
                            BuildStepStatus.Skipped,
                            TimeSpan.Zero,
                            "Step is not applicable to this request.");
                        stepResults.Add(skipped);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, skipped),
                            $"StepFinished:{invocation.InvocationId}",
                            nonFatalFailures);
                        continue;
                    }

                    NotifyEventSink(
                        () => eventSink.StepStarted(context, compiledStep),
                        $"StepStarted:{invocation.InvocationId}",
                        nonFatalFailures);
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        recipeProvenance.ValidateUnchanged(
                            request,
                            invocation,
                            $"before invocation '{invocation.InvocationId}'");
                        step.Execute(context, invocation);
                        stopwatch.Stop();
                        var succeeded = new BuildStepResult(
                            invocation.InvocationId,
                            invocation.StepTypeId,
                            BuildStepStatus.Succeeded,
                            stopwatch.Elapsed,
                            "Completed.");
                        stepResults.Add(succeeded);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, succeeded),
                            $"StepFinished:{invocation.InvocationId}",
                            nonFatalFailures);
                    }
                    catch (Exception exception) when (
                        BuildProcessExitCodes.FromFailure(exception)
                            != BuildProcessExitCodes.ResultEvidenceFailed)
                    {
                        stopwatch.Stop();
                        var failed = new BuildStepResult(
                            invocation.InvocationId,
                            invocation.StepTypeId,
                            BuildStepStatus.Failed,
                            stopwatch.Elapsed,
                            exception.Message,
                            exception);
                        stepResults.Add(failed);
                        failure = Combine(failure, exception);
                        NotifyEventSink(
                            () => eventSink.StepFinished(context, failed),
                            $"StepFinished:{invocation.InvocationId}",
                            nonFatalFailures);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                failure = Combine(failure, exception);
                if (stepResults.All(result => result.Status != BuildStepStatus.Failed))
                {
                    var preflightFailure = new BuildStepResult(
                        "preflight",
                        "pipeline-preflight",
                        BuildStepStatus.Failed,
                        TimeSpan.Zero,
                        exception.Message,
                        exception);
                    stepResults.Add(preflightFailure);
                    NotifyEventSink(
                        () => eventSink.StepFinished(context, preflightFailure),
                        "StepFinished:preflight",
                        nonFatalFailures);
                }
            }
            finally
            {
                failure = VerifyProjectSettings(
                    projectSettingsGuard,
                    "Pre-restore Player publication gate",
                    failure);
                failure = DisposeScope(versionInfoScope, "VersionInfoData restore", failure);
                failure = RestoreGlobalState(
                    globalStateScope,
                    projectSettingsGuard,
                    failure);
                failure = VerifyProjectSettings(
                    projectSettingsGuard,
                    "Post-restore Player publication gate",
                    failure);
                failure = ValidateRecipeProvenance(
                    recipeProvenance,
                    request,
                    "terminal publication",
                    failure);
                failure = RevalidateSourceWorkspaceForPublication(
                    context,
                    failure);
                var provisionalResult = new BuildRunResult(
                    runId,
                    failure == null,
                    request.OutputPath,
                    resultPath,
                    stepResults,
                    failure,
                    nonFatalFailures);
                try
                {
                    context.SealForPublication();
                    manifestSnapshot =
                        BuildResultManifestWriter.FreezeForPublication(
                            context,
                            provisionalResult);
                    BuildResultManifestWriter.ValidatePublicationCapacity(
                        manifestSnapshot);
                }
                catch (Exception exception)
                {
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            "Terminal result evidence exceeded its publication-safe envelope before any deferred publication was committed.",
                            exception));
                }

                failure = FinalizeDeferredPublications(
                    context,
                    failure);
            }

            var result = new BuildRunResult(
                runId,
                failure == null,
                request.OutputPath,
                resultPath,
                stepResults,
                failure,
                nonFatalFailures);

            try
            {
                try
                {
                    if (manifestSnapshot == null)
                    {
                        throw new InvalidOperationException(
                            "The terminal result manifest snapshot was not available.");
                    }

                    BuildResultManifestWriter.Write(manifestSnapshot, result);
                }
                catch (Exception manifestException)
                {
                    var manifestFailure = new InvalidOperationException(
                        "Failed to persist the required build result manifest. " +
                        "The build invocation is failed even if artifacts were already committed; inspect the output and transaction evidence before retrying.",
                        manifestException);
                    result = new BuildRunResult(
                        runId,
                        succeeded: false,
                        request.OutputPath,
                        resultPath,
                        stepResults,
                        Combine(result.Failure, manifestFailure),
                        result.NonFatalFailures);
                    UnityEngine.Debug.LogError(manifestFailure);
                }

                NotifyTerminalEventSink(
                    () => eventSink.RunFinished(context, result),
                    "RunFinished");
            }
            finally
            {
                workspaceLease?.Dispose();
            }

            return result;
        }

        private Exception RevalidateSourceWorkspaceForPublication(
            BuildExecutionContext context,
            Exception failure)
        {
            if (failure != null || !context.Request.RequireCleanSource)
            {
                return failure;
            }

            bool terminalWorkspaceCaptured = false;
            Exception qualificationFailure = null;
            BuildSourceQualificationSuspensionScope suspension = null;
            try
            {
                BuildVersionContext initial = context.Version
                    ?? throw new InvalidOperationException(
                        "The initial build source snapshot is unavailable.");
                suspension = BuildSourceQualificationSuspensionScope.Begin(
                    context.DeferredPublications);
                BuildVersionContext terminal = versionResolver(context.Request)
                    ?? throw new InvalidOperationException(
                        "The terminal build source snapshot is unavailable.");
                context.Version = initial.WithSourceWorkspace(terminal.SourceWorkspace);
                terminalWorkspaceCaptured = true;
                BuildSourceWorkspacePolicy.EnsureAllowed(context.Request, terminal);
                ValidateSameSourceIdentity(initial, terminal);
            }
            catch (Exception exception)
            {
                qualificationFailure = exception;
            }
            finally
            {
                if (suspension != null)
                {
                    try
                    {
                        suspension.Dispose();
                    }
                    catch (Exception restorationFailure)
                    {
                        qualificationFailure = Combine(
                            qualificationFailure,
                            new InvalidOperationException(
                                "Transaction-owned downstream inputs could not be restored after terminal source qualification.",
                                restorationFailure));
                    }
                }
            }

            if (qualificationFailure == null)
            {
                return failure;
            }

            if (!terminalWorkspaceCaptured && context.Version != null)
            {
                context.Version = context.Version.WithSourceWorkspace(
                    Build.VersionControl.Editor.VersionControlWorkspaceEvidence.Unknown(
                        Build.VersionControl.Editor.VersionControlWorkspaceEvidence.CommandFailed));
            }

            return Combine(
                failure,
                new InvalidOperationException(
                    "Source workspace qualification changed before terminal publication. " +
                    "No deferred build output was published.",
                    qualificationFailure));
        }

        private static void ValidateSameSourceIdentity(
            BuildVersionContext initial,
            BuildVersionContext terminal)
        {
            EnsureSameSourceIdentityComponent(
                "provider",
                GetDetectedProvider(initial),
                GetDetectedProvider(terminal),
                StringComparison.OrdinalIgnoreCase);
            EnsureSameSourceIdentityComponent(
                "revision",
                GetDetectedRevision(initial),
                GetDetectedRevision(terminal),
                StringComparison.OrdinalIgnoreCase);
            EnsureSameSourceIdentityComponent(
                "branch",
                GetDetectedBranch(initial),
                GetDetectedBranch(terminal),
                StringComparison.Ordinal);
            EnsureSameSourceIdentityComponent(
                "commit count",
                GetDetectedCommitCount(initial),
                GetDetectedCommitCount(terminal),
                StringComparison.Ordinal);
            EnsureSameSourceIdentityComponent(
                "commit date",
                GetDetectedCommitDate(initial),
                GetDetectedCommitDate(terminal),
                StringComparison.Ordinal);
        }

        private static void EnsureSameSourceIdentityComponent(
            string component,
            string initial,
            string terminal,
            StringComparison comparison)
        {
            if (!string.Equals(initial, terminal, comparison))
            {
                throw new BuildFailedException(
                    $"The detected source {component} changed while the build was running.");
            }
        }

        private static string GetDetectedProvider(BuildVersionContext version)
        {
            return string.IsNullOrWhiteSpace(version.DetectedProviderId)
                ? version.EffectiveSourceProvider
                : version.DetectedProviderId;
        }

        private static string GetDetectedRevision(BuildVersionContext version)
        {
            return string.IsNullOrWhiteSpace(version.DetectedCommitHash)
                ? version.EffectiveSourceRevision
                : version.DetectedCommitHash;
        }

        private static string GetDetectedBranch(BuildVersionContext version)
        {
            return string.IsNullOrWhiteSpace(version.DetectedBranch)
                ? version.EffectiveSourceBranch
                : version.DetectedBranch;
        }

        private static string GetDetectedCommitCount(BuildVersionContext version)
        {
            return string.IsNullOrWhiteSpace(version.DetectedCommitCount)
                ? version.CommitCount
                : version.DetectedCommitCount;
        }

        private static string GetDetectedCommitDate(BuildVersionContext version)
        {
            return string.IsNullOrWhiteSpace(version.DetectedCommitDate)
                ? version.CommitDate
                : version.DetectedCommitDate;
        }

        private static void NotifyTerminalEventSink(
            Action callback,
            string eventName)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                if (BuildProcessExitCodes.FromFailure(exception)
                    == BuildProcessExitCodes.ResultEvidenceFailed)
                {
                    throw;
                }

                UnityEngine.Debug.LogError(
                    new InvalidOperationException(
                        $"Build event sink failed after the terminal outcome in '{eventName}'.",
                        exception));
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left ?? string.Empty),
                Path.GetFullPath(right ?? string.Empty),
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        private void ValidateRequestBoundary(BuildRequest request)
        {
            EnsureEditorIsIdle();

            if (!BuildCommandLine.IsSupportedBuildTarget(request.Target))
            {
                throw new BuildFailedException(
                    $"Unsupported player build target '{request.Target}'.");
            }

            BuildRequestFactory.ValidateLocalReleasePreviewRequest(request);

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

            if (request.BuildScenePaths.Count > MaximumBuildSceneCount)
            {
                throw new BuildFailedException(
                    $"Build request exceeds the {MaximumBuildSceneCount}-scene safety budget.");
            }

            if (request.Steps.Count == 0
                || request.Steps.Count > BuildPipelineBudgets.MaximumInvocationCount)
            {
                throw new BuildFailedException(
                    $"Build request must contain between 1 and {BuildPipelineBudgets.MaximumInvocationCount} invocations.");
            }

            int dependencyEdgeCount = 0;
            for (int index = 0; index < request.Steps.Count; index++)
            {
                try
                {
                    dependencyEdgeCount = checked(
                        dependencyEdgeCount + request.Steps[index].Dependencies.Count);
                }
                catch (OverflowException exception)
                {
                    throw new BuildFailedException(
                        "Build dependency edge count overflowed its safety budget: " +
                        exception.Message);
                }

                if (dependencyEdgeCount > BuildPipelineBudgets.MaximumDependencyEdgeCount)
                {
                    throw new BuildFailedException(
                        $"Build request exceeds the {BuildPipelineBudgets.MaximumDependencyEdgeCount}-edge dependency safety budget.");
                }
            }

            ValidateIdentity(
                () => BuildIdentityPolicy.ValidateApplicationVersion(
                    request.ApplicationVersion));

            foreach (BuildStepInvocation invocation in request.Steps)
            {
                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidateBuildIdentifier(
                        invocation.InvocationId,
                        "Build invocation identifier"));
                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidateBuildIdentifier(
                        invocation.StepTypeId,
                        "Build step type identifier"));
                if (invocation.Incrementality != BuildIncrementality.Clean
                    && invocation.Incrementality != BuildIncrementality.Incremental)
                {
                    throw new BuildFailedException(
                        $"Build invocation '{invocation.InvocationId}' has unsupported " +
                        $"incrementality mode '{invocation.Incrementality}'.");
                }
            }

            ValidateIdentity(
                () => BuildRequestFactory.ValidateAndroidExportRecipe(
                    request.Steps,
                    request.ExportAndroidProject));

            IReadOnlyList<BuildStepInvocation> hotUpdateInvocations =
                request.GetInvocationsByStepType(BuildStepTypeIds.HotUpdate);
            for (int index = 0; index < hotUpdateInvocations.Count; index++)
            {
                BuildStepInvocation invocation = hotUpdateInvocations[index];
                var configuration = invocation.Configuration as HotUpdateBuildConfiguration;
                if (configuration == null)
                {
                    continue;
                }

                string providerId = configuration.ProviderId?.Trim();
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    throw new BuildFailedException(
                        $"Hot-update invocation '{invocation.InvocationId}' returned an empty provider id.");
                }

                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidateBuildIdentifier(
                        providerId,
                        "Hot-update provider identifier"));
            }

            IReadOnlyList<BuildStepInvocation> contentInvocations =
                request.GetInvocationsByStepType(BuildStepTypeIds.AssetContent);
            for (int index = 0; index < contentInvocations.Count; index++)
            {
                BuildStepInvocation invocation = contentInvocations[index];
                var configuration = invocation.Configuration as AssetContentBuildConfiguration;
                if (configuration == null)
                {
                    continue;
                }

                string providerId = configuration.ProviderId?.Trim();
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    throw new BuildFailedException(
                        $"Asset Content invocation '{invocation.InvocationId}' returned an empty provider id.");
                }

                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidateBuildIdentifier(
                        providerId,
                        "Asset content provider identifier"));
            }

        }

        private static BuildStepRequirements ResolveRequirements(
            BuildExecutionContext context,
            IReadOnlyList<CompiledBuildStep> plan)
        {
            BuildStepRequirements requirements = BuildStepRequirements.None;
            for (int index = 0; index < plan.Count; index++)
            {
                CompiledBuildStep compiled = plan[index];
                if (!compiled.IsApplicable
                    || !(compiled.Step is IBuildStepRequirementsProvider provider))
                {
                    continue;
                }

                BuildStepRequirements declared = provider.GetRequirements(
                    context,
                    compiled.Invocation);
                const BuildStepRequirements Known =
                    BuildStepRequirements.UnityGlobalState
                    | BuildStepRequirements.VersionInfoAsset
                    | BuildStepRequirements.PlayerOutput;
                if ((declared & ~Known) != 0)
                {
                    throw new BuildFailedException(
                        $"Build invocation '{compiled.Invocation.InvocationId}' ({compiled.Step.StepTypeId}) " +
                        $"declared unknown run requirements '{declared}'.");
                }

                requirements |= declared;
            }

            return requirements;
        }

        private static void ValidatePlanRequirements(
            BuildRequest request,
            BuildStepRequirements requirements)
        {
            if ((requirements & (BuildStepRequirements.UnityGlobalState
                                 | BuildStepRequirements.PlayerOutput)) != 0)
            {
                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidatePlainText(
                        request.CompanyName,
                        "Company name",
                        256));

                try
                {
                    BuildPathPolicy.ValidatePortableFileName(
                        request.ProductName,
                        "Product name");
                }
                catch (ArgumentException exception)
                {
                    throw new BuildFailedException(
                        "Product name is not a portable file name. " +
                        exception.Message);
                }

                ValidateIdentity(
                    () => BuildIdentityPolicy.ValidateApplicationIdentifier(
                        request.ApplicationIdentifier));
            }

            if ((requirements & BuildStepRequirements.VersionInfoAsset) != 0)
            {
                ValidateVersionInfoPath(
                    request.ProjectRoot,
                    request.VersionInfoAssetPath);
            }

            if ((requirements & BuildStepRequirements.PlayerOutput) == 0)
            {
                return;
            }

            ValidateOutputShape(request);
            BuildPathPolicy.EnsureSafeDeleteTarget(
                request.ProjectRoot,
                request.OutputDirectory,
                request.BuildRoot,
                request.AllowExternalOutput);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                request.OutputDirectory,
                "Player output directory");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                request.OutputPath,
                "Player output artifact");
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

        private static void ValidateVersionInfoPath(
            string projectRoot,
            string path)
        {
            try
            {
                RuntimeVersionInfoPathPolicy.Validate(path);
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    exception.Message);
            }

            string parentRelativePath = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parentRelativePath)
                || string.Equals(parentRelativePath, "Assets", StringComparison.Ordinal))
            {
                throw new BuildFailedException(
                    "VersionInfoData must be stored in a child directory below Assets; " +
                    "the Assets root is not a valid generated-asset destination.");
            }

            try
            {
                BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                    projectRoot,
                    parentRelativePath);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is PathTooLongException)
            {
                throw new BuildFailedException(exception.Message);
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

        private void EnsureEditorIsIdle()
        {
            if (isEditorBusy())
            {
                throw new BuildFailedException(
                    "Unity is compiling or updating assets. Wait for the Editor to become idle before building.");
            }
        }

        private static Exception RestoreGlobalState(
            BuildGlobalStateScope scope,
            ProjectSettingsStateGuard guard,
            Exception failure)
        {
            if (scope == null)
            {
                return failure;
            }

            try
            {
                if (guard == null)
                {
                    scope.Dispose();
                }
                else
                {
                    using (ProjectSettingsStateGuard.AuthorizationWindow authorization =
                           guard.BeginRecoveryAuthorization(
                               "ProjectSettings/ProjectSettings.asset",
                               "ProjectSettings/EditorBuildSettings.asset"))
                    {
                        scope.Dispose();
                        authorization.Commit();
                    }
                }

                return failure;
            }
            catch (Exception exception)
            {
                return Combine(
                    failure,
                    new InvalidOperationException(
                        "Unity build settings restore failed.",
                        exception));
            }
        }

        private static Exception VerifyProjectSettings(
            ProjectSettingsStateGuard guard,
            string operation,
            Exception failure)
        {
            if (guard == null)
            {
                return failure;
            }

            try
            {
                guard.VerifyOrThrow(operation);
                return failure;
            }
            catch (Exception exception)
            {
                return Combine(failure, exception);
            }
        }

        private static Exception ValidateRecipeProvenance(
            BuildRecipeProvenanceCapture provenance,
            BuildRequest request,
            string checkpoint,
            Exception failure)
        {
            if (provenance == null)
            {
                return failure;
            }

            try
            {
                provenance.ValidateUnchanged(request, checkpoint);
                return failure;
            }
            catch (Exception exception)
            {
                return Combine(failure, exception);
            }
        }

        private static Exception FinalizeDeferredPublications(
            BuildExecutionContext context,
            Exception failure)
        {
            IReadOnlyList<IBuildDeferredPublication> publications =
                context.DeferredPublications;
            if (publications.Count == 0)
            {
                return failure;
            }

            if (failure != null)
            {
                DisposeDeferredPublications(publications, ref failure, out _);
                return failure;
            }

            BuildPublicationBarrier barrier;
            try
            {
                barrier = BuildPublicationBarrier.Begin(
                    context.Request.ProjectRoot,
                    context.RunId,
                    publications);
            }
            catch (Exception exception)
            {
                failure = Combine(
                    failure,
                    new InvalidOperationException(
                        "Failed to prepare the terminal publication barrier.",
                        exception));
                DisposeDeferredPublications(publications, ref failure, out _);
                return failure;
            }

            bool publishFailed = false;
            for (int index = 0; index < publications.Count; index++)
            {
                IBuildDeferredPublication publication = publications[index];
                try
                {
                    publication.Publish();
                }
                catch (Exception exception)
                {
                    publishFailed = true;
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            $"Deferred publication '{publication.Id}' failed before the terminal decision.",
                            exception));
                    break;
                }
            }

            if (!publishFailed)
            {
                try
                {
                    barrier.CommitDecision();
                }
                catch (Exception exception)
                {
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            "Failed to persist the terminal publication commit decision.",
                            exception));
                }
            }

            BuildPublicationDecision durableDecision = BuildPublicationDecision.None;
            bool durableDecisionRead = false;
            try
            {
                durableDecision = barrier.ReadDurableDecision();
                durableDecisionRead = true;
            }
            catch (Exception exception)
            {
                failure = Combine(
                    failure,
                    new InvalidOperationException(
                        "The terminal publication decision could not be read back from durable storage.",
                        exception));
            }

            if (durableDecision != BuildPublicationDecision.Commit)
            {
                DisposeDeferredPublications(
                    publications,
                    ref failure,
                    out bool rollbackFailed);
                if (durableDecisionRead
                    && durableDecision == BuildPublicationDecision.Rollback
                    && !rollbackFailed)
                {
                    try
                    {
                        barrier.AbortAfterRollback();
                    }
                    catch (Exception exception)
                    {
                        failure = Combine(
                            failure,
                            new InvalidOperationException(
                                "Deferred publications rolled back, but the prepared publication barrier could not be cleared.",
                                exception));
                    }
                }

                return failure;
            }

            bool completionFailed = false;
            for (int index = 0; index < publications.Count; index++)
            {
                IBuildDeferredPublication publication = publications[index];
                try
                {
                    publication.Complete();
                }
                catch (Exception exception)
                {
                    completionFailed = true;
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            $"Committed deferred publication '{publication.Id}' requires explicit recovery.",
                            exception));
                }
            }

            DisposeDeferredPublications(
                publications,
                ref failure,
                out bool committedDisposeFailed);
            if (!completionFailed && !committedDisposeFailed)
            {
                try
                {
                    barrier.Complete();
                }
                catch (Exception exception)
                {
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            "Deferred publications completed, but the committed publication barrier requires explicit recovery.",
                            exception));
                }
            }

            return failure;
        }

        private static void DisposeDeferredPublications(
            IReadOnlyList<IBuildDeferredPublication> publications,
            ref Exception failure,
            out bool disposeFailed)
        {
            disposeFailed = false;
            for (int index = publications.Count - 1; index >= 0; index--)
            {
                IBuildDeferredPublication publication = publications[index];
                try
                {
                    publication.Dispose();
                }
                catch (Exception exception)
                {
                    disposeFailed = true;
                    failure = Combine(
                        failure,
                        new InvalidOperationException(
                            $"Deferred publication cleanup failed for '{publication.Id}'.",
                            exception));
                }
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
                if (BuildProcessExitCodes.FromFailure(exception)
                    == BuildProcessExitCodes.ResultEvidenceFailed)
                {
                    throw;
                }

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
        public void RunStarted(
            BuildExecutionContext context,
            IReadOnlyList<CompiledBuildStep> plan)
        {
            string invocations = string.Join(
                " -> ",
                plan.Select(step =>
                    step.Invocation.InvocationId + ":" + step.Invocation.StepTypeId));
            UnityEngine.Debug.Log(
                $"[BuildPipeline] Run {context.RunId} started. Target={context.Request.Target}, PackageVersion={context.Version.PackageVersion}, Invocations={invocations}");
        }

        public void StepStarted(BuildExecutionContext context, CompiledBuildStep step)
        {
            UnityEngine.Debug.Log(
                $"[BuildPipeline] Invocation '{step.Invocation.InvocationId}' " +
                $"({step.Invocation.StepTypeId}) started.");
        }

        public void StepFinished(BuildExecutionContext context, BuildStepResult result)
        {
            string message =
                $"[BuildPipeline] Invocation '{result.InvocationId}' ({result.StepTypeId}) " +
                $"{result.Status} in {result.Duration.TotalSeconds:F2}s. " +
                BuildResultEvidencePolicy.NormalizeDiagnosticText(result.Message);
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
            string outputLabel = result.Succeeded ? "Output" : "RequestedOutput";
            string message =
                $"[BuildPipeline] Run {result.RunId} {(result.Succeeded ? "succeeded" : "failed")}. " +
                $"{outputLabel}='{result.OutputPath}', Result='{result.ResultManifestPath}'.";
            if (result.Succeeded)
            {
                UnityEngine.Debug.Log(message);
            }
            else
            {
                UnityEngine.Debug.LogError(
                    message + "\n" +
                    BuildResultEvidencePolicy.NormalizeException(result.Failure));
            }
        }
    }
}
