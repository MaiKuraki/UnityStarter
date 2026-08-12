using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal interface IBuildEntryPointOperations
    {
        BuildCommandLineOptions ParseCommandLine(string[] arguments);
        BuildData ResolveCommandLineProfile(string profilePath);
        BuildRequest CreateCommandLineRequest(
            BuildData profile,
            BuildCommandLineOptions options);
        BuildRequest CreateInteractiveRequest(
            BuildData profile,
            BuildTarget target,
            bool debug,
            bool exportAndroidProject,
            IReadOnlyList<string> invocationIdsOverride);
        BuildRequest CreateLocalReleasePreviewRequest(
            BuildData profile,
            BuildTarget target,
            IReadOnlyList<string> invocationIdsOverride);
        BuildRunResult RunBuild(
            BuildRequest request,
            string runId,
            string requiredResultManifestPath,
            IBuildEventSink eventSink);
        void RecoverWorkspace();
    }

    internal sealed class DefaultBuildEntryPointOperations : IBuildEntryPointOperations
    {
        public static DefaultBuildEntryPointOperations Instance { get; } =
            new DefaultBuildEntryPointOperations();

        private DefaultBuildEntryPointOperations()
        {
        }

        public BuildCommandLineOptions ParseCommandLine(string[] arguments)
        {
            return BuildCommandLine.Parse(arguments);
        }

        public BuildData ResolveCommandLineProfile(string profilePath)
        {
            return BuildProfileResolver.ResolveCommandLine(profilePath);
        }

        public BuildRequest CreateCommandLineRequest(
            BuildData profile,
            BuildCommandLineOptions options)
        {
            return BuildRequestFactory.CreateForCommandLine(profile, options);
        }

        public BuildRequest CreateInteractiveRequest(
            BuildData profile,
            BuildTarget target,
            bool debug,
            bool exportAndroidProject,
            IReadOnlyList<string> invocationIdsOverride)
        {
            return BuildRequestFactory.CreateInteractive(
                profile,
                target,
                debug,
                exportAndroidProject,
                invocationIdsOverride);
        }

        public BuildRequest CreateLocalReleasePreviewRequest(
            BuildData profile,
            BuildTarget target,
            IReadOnlyList<string> invocationIdsOverride)
        {
            return BuildRequestFactory.CreateLocalReleasePreview(
                profile,
                target,
                invocationIdsOverride);
        }

        public BuildRunResult RunBuild(
            BuildRequest request,
            string runId,
            string requiredResultManifestPath,
            IBuildEventSink eventSink)
        {
            return new BuildPipelineRunner(eventSink).Run(
                request,
                runId,
                requiredResultManifestPath);
        }

        public void RecoverWorkspace()
        {
            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect();
            if (snapshot.Status == BuildWorkspaceHealthStatus.Clean)
            {
                Debug.Log(
                    "[BuildPipeline] Workspace recovery requested, but no pending " +
                    $"transaction state was found. Snapshot='{snapshot.Token}'.");
                return;
            }

            if (snapshot.Status != BuildWorkspaceHealthStatus.RecoveryRequired
                || !snapshot.CanRecover)
            {
                throw new BuildFailedException(
                    $"Workspace recovery is not available while status is '{snapshot.Status}'. "
                    + snapshot.Summary);
            }

            BuildWorkspaceSnapshot recovered = BuildWorkspaceService.Recover(snapshot.Token);
            Debug.Log(
                $"[BuildPipeline] Workspace recovery completed. Status={recovered.Status}, " +
                $"Snapshot='{recovered.Token}'.");
        }
    }

    internal sealed class BuildEntryPointExecutionResult
    {
        public BuildEntryPointExecutionResult(
            int exitCode,
            Exception failure,
            BuildRunResult buildResult,
            string runId,
            string manifestPath,
            string startedMarkerPath,
            string logPath)
        {
            ExitCode = exitCode;
            Failure = failure;
            BuildResult = buildResult;
            RunId = runId ?? string.Empty;
            ManifestPath = manifestPath ?? string.Empty;
            StartedMarkerPath = startedMarkerPath ?? string.Empty;
            LogPath = logPath ?? string.Empty;
        }

        public int ExitCode { get; }
        public Exception Failure { get; }
        public BuildRunResult BuildResult { get; }
        public string RunId { get; }
        public string ManifestPath { get; }
        public string StartedMarkerPath { get; }
        public string LogPath { get; }
        public bool Succeeded => ExitCode == BuildProcessExitCodes.Succeeded;
    }

    internal static class BuildEntryPointExecutor
    {
        public static BuildEntryPointExecutionResult ExecuteCommandLine(
            string trustedProjectRoot,
            string[] arguments,
            IBuildEntryPointOperations operations)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            string stage = "command-line-parse";
            return ExecuteWithEvidence(
                trustedProjectRoot,
                "command-line",
                session =>
                {
                    BuildCommandLineOptions options =
                        operations.ParseCommandLine(arguments)
                        ?? throw new InvalidOperationException(
                            "Command-line parsing returned no options.");
                    if (options.RecoverOnly)
                    {
                        session.SetOperation("recovery");
                        stage = "workspace-recovery";
                        operations.RecoverWorkspace();
                        return ActionResult.EarlySuccess(stage);
                    }

                    session.SetOperation("build");
                    stage = "profile-resolution";
                    BuildData profile = operations.ResolveCommandLineProfile(
                        options.BuildProfilePath)
                        ?? throw new InvalidOperationException(
                            "Build profile resolution returned no profile.");
                    stage = "request-factory";
                    BuildRequest request = operations.CreateCommandLineRequest(
                        profile,
                        options)
                        ?? throw new InvalidOperationException(
                            "Command-line request creation returned no request.");
                    stage = "build-run";
                    BuildRunResult result = operations.RunBuild(
                        request,
                        session.RunId,
                        session.ManifestPath,
                        session.CreateEventSink())
                        ?? throw new InvalidOperationException(
                            "The build runner returned no result.");
                    stage = "result-confirmation";
                    return ActionResult.FullManifest(result, "result-confirmation");
                },
                () => stage);
        }

        public static BuildEntryPointExecutionResult ExecuteInteractive(
            string trustedProjectRoot,
            Func<BuildData> resolveProfile,
            BuildTarget target,
            bool debug,
            bool exportAndroidProject,
            IReadOnlyList<string> invocationIdsOverride,
            IBuildEntryPointOperations operations)
        {
            return ExecuteInteractive(
                trustedProjectRoot,
                resolveProfile,
                target,
                debug,
                exportAndroidProject,
                invocationIdsOverride,
                localReleasePreview: false,
                operations);
        }

        public static BuildEntryPointExecutionResult ExecuteInteractive(
            string trustedProjectRoot,
            Func<BuildData> resolveProfile,
            BuildTarget target,
            bool debug,
            bool exportAndroidProject,
            IReadOnlyList<string> invocationIdsOverride,
            bool localReleasePreview,
            IBuildEntryPointOperations operations)
        {
            if (resolveProfile == null)
            {
                throw new ArgumentNullException(nameof(resolveProfile));
            }

            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            string stage = "profile-resolution";
            return ExecuteWithEvidence(
                trustedProjectRoot,
                "build",
                session =>
                {
                    BuildData profile = resolveProfile()
                        ?? throw new InvalidOperationException(
                            "Build profile resolution returned no profile.");
                    stage = "request-factory";
                    BuildRequest request = (localReleasePreview
                            ? operations.CreateLocalReleasePreviewRequest(
                                profile,
                                target,
                                invocationIdsOverride)
                            : operations.CreateInteractiveRequest(
                                profile,
                                target,
                                debug,
                                exportAndroidProject,
                                invocationIdsOverride))
                        ?? throw new InvalidOperationException(
                            "Interactive request creation returned no request.");
                    stage = "build-run";
                    BuildRunResult result = operations.RunBuild(
                        request,
                        session.RunId,
                        session.ManifestPath,
                        session.CreateEventSink())
                        ?? throw new InvalidOperationException(
                            "The build runner returned no result.");
                    stage = "result-confirmation";
                    return ActionResult.FullManifest(result, "result-confirmation");
                },
                () => stage);
        }

        private static BuildEntryPointExecutionResult ExecuteWithEvidence(
            string trustedProjectRoot,
            string operation,
            Func<BuildResultEvidenceSession, ActionResult> execute,
            Func<string> getFailureStage)
        {
            BuildResultEvidenceSession session;
            try
            {
                session = BuildResultEvidenceSession.Begin(
                    trustedProjectRoot,
                    operation);
            }
            catch (Exception exception)
            {
                Exception evidenceFailure = exception is BuildResultEvidenceException
                    ? exception
                    : new BuildResultEvidenceException(
                        "Failed to establish required build result evidence.",
                        exception);
                return new BuildEntryPointExecutionResult(
                    BuildProcessExitCodes.ResultEvidenceFailed,
                    evidenceFailure,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }

            int exitCode = BuildProcessExitCodes.BuildFailed;
            Exception primaryFailure = null;
            BuildRunResult buildResult = null;
            try
            {
                ActionResult actionResult = execute(session)
                    ?? throw new InvalidOperationException(
                        "Build entry-point execution returned no terminal action.");
                if (actionResult.BuildResult == null)
                {
                    session.WriteEarlyTerminalManifest(
                        actionResult.TerminalStage,
                        succeeded: true,
                        BuildProcessExitCodes.Succeeded,
                        failure: null);
                    exitCode = BuildProcessExitCodes.Succeeded;
                }
                else
                {
                    buildResult = actionResult.BuildResult;
                    primaryFailure = buildResult.Failure;
                    if (buildResult.Succeeded && primaryFailure != null)
                    {
                        throw new InvalidOperationException(
                            "A successful build result may not contain a terminal failure.");
                    }

                    if (!buildResult.Succeeded && primaryFailure == null)
                    {
                        primaryFailure = new BuildFailedException(
                            "The build runner reported failure without a terminal exception.");
                    }

                    session.ConfirmTerminalManifest(buildResult);
                    exitCode = buildResult.Succeeded
                        ? BuildProcessExitCodes.Succeeded
                        : BuildProcessExitCodes.FromFailure(primaryFailure);
                }
            }
            catch (Exception exception)
            {
                primaryFailure = Combine(primaryFailure, exception);
                exitCode = BuildProcessExitCodes.FromFailure(primaryFailure);
                if (!session.HasValidatedTerminalManifest)
                {
                    try
                    {
                        session.WriteEarlyTerminalManifest(
                            getFailureStage(),
                            succeeded: false,
                            exitCode,
                            primaryFailure);
                    }
                    catch (Exception evidenceException)
                    {
                        primaryFailure = Combine(
                            primaryFailure,
                            AsEvidenceFailure(evidenceException));
                        exitCode = BuildProcessExitCodes.ResultEvidenceFailed;
                    }
                }
            }

            try
            {
                session.Dispose();
            }
            catch (Exception evidenceException)
            {
                primaryFailure = Combine(
                    primaryFailure,
                    AsEvidenceFailure(evidenceException));
                exitCode = BuildProcessExitCodes.ResultEvidenceFailed;
            }

            if (!session.TerminalEvidenceConfirmed)
            {
                if (BuildProcessExitCodes.FromFailure(primaryFailure)
                    != BuildProcessExitCodes.ResultEvidenceFailed)
                {
                    primaryFailure = Combine(
                        primaryFailure,
                        new BuildResultEvidenceException(
                            "Required terminal build evidence was not confirmed. " +
                            $"Started marker retained at '{session.StartedMarkerPath}'."));
                }

                exitCode = BuildProcessExitCodes.ResultEvidenceFailed;
            }

            return new BuildEntryPointExecutionResult(
                exitCode,
                primaryFailure,
                buildResult,
                session.RunId,
                session.ManifestPath,
                session.StartedMarkerPath,
                session.LogPath);
        }

        private static Exception AsEvidenceFailure(Exception exception)
        {
            return exception is BuildResultEvidenceException
                ? exception
                : new BuildResultEvidenceException(
                    "Required build result evidence failed.",
                    exception);
        }

        private static Exception Combine(Exception first, Exception second)
        {
            if (first == null)
            {
                return second;
            }

            if (second == null || ReferenceEquals(first, second))
            {
                return first;
            }

            return new AggregateException(first, second);
        }

        private sealed class ActionResult
        {
            private ActionResult(BuildRunResult buildResult, string terminalStage)
            {
                BuildResult = buildResult;
                TerminalStage = terminalStage;
            }

            public BuildRunResult BuildResult { get; }
            public string TerminalStage { get; }

            public static ActionResult EarlySuccess(string terminalStage)
            {
                return new ActionResult(null, terminalStage);
            }

            public static ActionResult FullManifest(
                BuildRunResult buildResult,
                string terminalStage)
            {
                return new ActionResult(
                    buildResult ?? throw new ArgumentNullException(nameof(buildResult)),
                    terminalStage);
            }
        }
    }
}
