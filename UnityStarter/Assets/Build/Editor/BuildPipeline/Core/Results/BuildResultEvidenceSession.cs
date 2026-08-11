using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace Build.Pipeline.Editor
{
    public static class BuildProcessExitCodes
    {
        private const int MaximumExceptionGraphNodes = 4096;

        public const int Succeeded = 0;
        public const int BuildFailed = 1;
        public const int ResultEvidenceFailed = 2;
        public const int WorkspaceBusy = 3;

        public static int FromFailure(Exception failure)
        {
            if (failure == null)
            {
                return BuildFailed;
            }

            bool containsEvidenceFailure = false;
            bool containsWorkspaceBusy = false;
            int inspectedCount = 0;
            var pending = new Stack<Exception>();
            var visited = new HashSet<Exception>(ExceptionReferenceComparer.Instance);
            pending.Push(failure);
            while (pending.Count > 0 && inspectedCount < MaximumExceptionGraphNodes)
            {
                Exception current = pending.Pop();
                if (current == null || !visited.Add(current))
                {
                    continue;
                }

                inspectedCount++;
                containsEvidenceFailure |= current is BuildResultEvidenceException;
                containsWorkspaceBusy |= current is BuildWorkspaceBusyException;
                if (current is AggregateException aggregate)
                {
                    IReadOnlyList<Exception> innerExceptions = aggregate.InnerExceptions;
                    for (int index = 0; index < innerExceptions.Count; index++)
                    {
                        Exception inner = innerExceptions[index];
                        if (inner != null)
                        {
                            pending.Push(inner);
                        }
                    }
                }

                if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            if (containsEvidenceFailure || pending.Count > 0)
            {
                return ResultEvidenceFailed;
            }

            return containsWorkspaceBusy ? WorkspaceBusy : BuildFailed;
        }

        private sealed class ExceptionReferenceComparer : IEqualityComparer<Exception>
        {
            public static ExceptionReferenceComparer Instance { get; } =
                new ExceptionReferenceComparer();

            public bool Equals(Exception left, Exception right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(Exception exception)
            {
                return RuntimeHelpers.GetHashCode(exception);
            }
        }
    }

    public sealed class BuildResultEvidenceException : IOException
    {
        internal BuildResultEvidenceException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Creates result evidence before command-line parsing or profile loading.
    /// A started marker remains after an abrupt process exit and is removed only
    /// after a terminal manifest is durably present.
    /// </summary>
    internal sealed class BuildResultEvidenceSession : IDisposable, IBuildEventSink
    {
        private const int BufferSize = 8192;
        private const int MaximumEventCharacters = 32 * 1024;
        private const int MaximumEvidenceJsonBytes = 64 * 1024 * 1024;
        private const long MaximumLogBytes = 64L * 1024L * 1024L;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly object logGate = new object();
        private readonly FileStream logStream;
        private readonly DateTime startedUtc;
        private Exception logWriteFailure;
        private BuildExecutionContext terminalContext;
        private BuildRunResult terminalResult;
        private bool terminalManifestValidated;
        private bool terminalEvidenceConfirmed;
        private bool disposed;

        private BuildResultEvidenceSession(
            string projectRoot,
            string operation,
            string runId,
            string manifestPath,
            string startedMarkerPath,
            string logPath,
            DateTime startedUtc,
            FileStream logStream)
        {
            ProjectRoot = projectRoot;
            Operation = operation;
            RunId = runId;
            ManifestPath = manifestPath;
            StartedMarkerPath = startedMarkerPath;
            LogPath = logPath;
            this.startedUtc = startedUtc;
            this.logStream = logStream;
        }

        public string ProjectRoot { get; }
        public string Operation { get; private set; }
        public string RunId { get; }
        public string ManifestPath { get; }
        public string StartedMarkerPath { get; }
        public string LogPath { get; }
        internal bool HasValidatedTerminalManifest => terminalManifestValidated;
        internal bool TerminalEvidenceConfirmed => terminalEvidenceConfirmed;

        public static BuildResultEvidenceSession Begin(
            string trustedProjectRoot,
            string operation)
        {
            string projectRoot = NormalizeProjectRoot(trustedProjectRoot);
            BuildIdentityPolicy.ValidatePlainText(operation, "Build operation", 64);
            string resultsDirectory = BuildPathPolicy.EnsureSafeBuildRoot(
                projectRoot,
                Path.Combine(projectRoot, ".buildpipeline", "results"));
            Directory.CreateDirectory(resultsDirectory);
            BuildPathPolicy.EnsureSafeBuildRoot(projectRoot, resultsDirectory);

            string runId = CreateRunId();
            string manifestPath = EnsureResultPath(
                Path.Combine(resultsDirectory, runId + ".json"),
                "Build result manifest");
            string startedMarkerPath = EnsureResultPath(
                Path.Combine(resultsDirectory, runId + ".started.json"),
                "Build started marker");
            string logPath = EnsureResultPath(
                Path.Combine(resultsDirectory, runId + ".log"),
                "Build result log");
            DateTime startedUtc = DateTime.UtcNow;

            FileStream logStream = null;
            bool ownsLog = false;
            bool markerCreated = false;
            try
            {
                logStream = new FileStream(
                    logPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.WriteThrough);
                ownsLog = true;
                WriteStartedMarker(
                    startedMarkerPath,
                    operation,
                    runId,
                    startedUtc,
                    manifestPath,
                    logPath);
                markerCreated = true;

                var session = new BuildResultEvidenceSession(
                    projectRoot,
                    operation,
                    runId,
                    manifestPath,
                    startedMarkerPath,
                    logPath,
                    startedUtc,
                    logStream);
                session.Append("session", "started");
                Debug.Log(
                    $"[BuildPipeline.Result] RunId='{runId}' Manifest='{manifestPath}' Log='{logPath}'.");
                logStream = null;
                ownsLog = false;
                return session;
            }
            catch (Exception exception)
            {
                try
                {
                    logStream?.Dispose();
                }
                catch
                {
                    // Preserve the evidence initialization failure.
                }

                if (ownsLog && !markerCreated)
                {
                    TryDeleteOwnedFile(logPath);
                }

                throw new BuildResultEvidenceException(
                    markerCreated
                        ? $"Failed to initialize required build result evidence. " +
                          $"The started marker was retained at '{startedMarkerPath}'."
                        : "Failed to initialize required build result evidence.",
                    exception);
            }
        }

        public void SetOperation(string operation)
        {
            ThrowIfDisposed();
            BuildIdentityPolicy.ValidatePlainText(operation, "Build operation", 64);
            Operation = operation;
            Append("session", "operation=" + operation);
        }

        public IBuildEventSink CreateEventSink()
        {
            ThrowIfDisposed();
            return new CompositeBuildEventSink(new ConsoleBuildEventSink(), this);
        }

        public void ConfirmTerminalManifest(BuildRunResult result)
        {
            ThrowIfDisposed();
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (!string.Equals(result.RunId, RunId, StringComparison.Ordinal)
                || !PathsEqual(result.ResultManifestPath, ManifestPath))
            {
                throw new BuildResultEvidenceException(
                    "The build result does not belong to the active evidence session.");
            }

            ConfirmTerminalManifest(
                result,
                result.Succeeded ? "succeeded" : "failed");
        }

        public void WriteEarlyTerminalManifest(
            string stage,
            bool succeeded,
            int processExitCode,
            Exception failure)
        {
            ThrowIfDisposed();
            BuildIdentityPolicy.ValidatePlainText(stage, "Build failure stage", 128);
            if (succeeded && failure != null)
            {
                throw new ArgumentException(
                    "A successful early terminal manifest cannot contain a failure.",
                    nameof(failure));
            }

            if (!succeeded && failure == null)
            {
                throw new ArgumentNullException(
                    nameof(failure),
                    "A failed early terminal manifest requires a failure.");
            }

            if (processExitCode < BuildProcessExitCodes.Succeeded
                || processExitCode > BuildProcessExitCodes.WorkspaceBusy
                || succeeded != (processExitCode == BuildProcessExitCodes.Succeeded))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(processExitCode),
                    processExitCode,
                    "The process exit code is inconsistent with the terminal outcome.");
            }

            string failureText =
                BuildResultEvidencePolicy.NormalizeException(failure);
            var manifest = new EarlyTerminalManifest
            {
                formatVersion = BuildResultManifestFormat.CurrentVersion,
                operation = Operation,
                runId = RunId,
                succeeded = succeeded,
                partial = true,
                stage = stage,
                processExitCode = processExitCode,
                startedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture),
                finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                failureType = failure?.GetType().FullName ?? string.Empty,
                failure = failureText,
                logPath = LogPath
            };

            Append(
                "session",
                succeeded
                    ? $"terminal stage={stage} exit={processExitCode} succeeded"
                    : $"terminal stage={stage} exit={processExitCode} failure={failureText}");
            try
            {
                WriteNewJsonAtomically(ManifestPath, JsonUtility.ToJson(manifest, true));
                ConfirmEarlyTerminalManifest(
                    manifest,
                    succeeded ? "succeeded" : "failed");
            }
            catch (BuildResultEvidenceException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildResultEvidenceException(
                    "Failed to persist the required early terminal build manifest.",
                    exception);
            }
        }

        public void RunStarted(
            BuildExecutionContext context,
            IReadOnlyList<CompiledBuildStep> plan)
        {
            var builder = new StringBuilder(256);
            builder.Append("plan=");
            for (int index = 0; index < plan.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(plan[index].Invocation.InvocationId)
                    .Append(':')
                    .Append(plan[index].Invocation.StepTypeId);
            }

            Append("run-started", builder.ToString());
        }

        public void StepStarted(BuildExecutionContext context, CompiledBuildStep step)
        {
            Append(
                "step-started",
                $"invocation={step.Invocation.InvocationId} type={step.Invocation.StepTypeId}");
        }

        public void StepFinished(BuildExecutionContext context, BuildStepResult result)
        {
            Append(
                "step-finished",
                $"invocation={result.InvocationId} type={result.StepTypeId} " +
                $"status={result.Status} durationSeconds={result.Duration.TotalSeconds.ToString("R", CultureInfo.InvariantCulture)} " +
                $"message={result.Message} failure={result.Exception}");
        }

        public void RunFinished(BuildExecutionContext context, BuildRunResult result)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            Append(
                "run-finished",
                $"succeeded={result.Succeeded} failure={result.Failure}");
            terminalContext = context;
            terminalResult = result;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Exception closeFailure = null;
            lock (logGate)
            {
                try
                {
                    logStream.Flush(true);
                }
                catch (Exception exception)
                {
                    closeFailure = exception;
                }
                finally
                {
                    try
                    {
                        logStream.Dispose();
                    }
                    catch (Exception exception)
                    {
                        closeFailure = Combine(closeFailure, exception);
                    }
                }
            }

            disposed = true;
            Exception requiredLogFailure = Combine(logWriteFailure, closeFailure);
            if (requiredLogFailure != null)
            {
                Debug.LogError(
                    $"[BuildPipeline.Result] Run '{RunId}' could not finalize its required evidence log. " +
                    $"Started marker retained at '{StartedMarkerPath}'.");
                throw new BuildResultEvidenceException(
                    $"Failed to finalize the required build result log '{LogPath}'.",
                    requiredLogFailure);
            }

            if (!terminalManifestValidated)
            {
                Debug.LogError(
                    $"[BuildPipeline.Result] Run '{RunId}' ended without confirmed terminal evidence. " +
                    $"Started marker retained at '{StartedMarkerPath}'.");
                return;
            }

            try
            {
                File.Delete(StartedMarkerPath);
            }
            catch (Exception exception)
            {
                throw new BuildResultEvidenceException(
                    $"Terminal manifest exists, but the started marker could not be removed: '{StartedMarkerPath}'.",
                    exception);
            }

            if (File.Exists(StartedMarkerPath))
            {
                throw new BuildResultEvidenceException(
                    $"Terminal manifest exists, but the started marker remains: '{StartedMarkerPath}'.");
            }

            terminalEvidenceConfirmed = true;
        }

        private void ConfirmTerminalManifest(
            BuildRunResult expectedResult,
            string terminalState)
        {
            if (terminalContext == null
                || terminalResult == null
                || !string.Equals(terminalResult.RunId, expectedResult.RunId, StringComparison.Ordinal))
            {
                throw new BuildResultEvidenceException(
                    "The terminal build event was not durably observed before manifest confirmation.");
            }

            string json = ReadTerminalManifestJson();
            var manifest = new BuildResultManifestFormat.Document
            {
                succeeded = !expectedResult.Succeeded,
                partial = true,
                debugBuild = !terminalContext.Request.DebugBuild,
                deleteDebugFiles = !terminalContext.Request.DeleteDebugFiles,
                exportAndroidProject = !terminalContext.Request.ExportAndroidProject,
                allowExternalOutput = !terminalContext.Request.AllowExternalOutput,
                outputIsFolder = !terminalContext.Request.OutputIsFolder,
                cheatEnabled = !terminalContext.Request.CheatEnabled,
                playerPipelineCompatibilityRevision = -1,
                playerExtensionFingerprint = "<invalid>"
            };
            DeserializeTerminalManifest(json, manifest, "full build");

            var violations = new List<string>();
            RequireEqual(
                manifest.formatVersion,
                BuildResultManifestFormat.CurrentVersion,
                "formatVersion",
                violations);
            RequireEqual(manifest.operation, "build", "operation", violations);
            RequireEqual(manifest.runId, RunId, "runId", violations);
            ValidateFullManifestRequestFields(manifest, expectedResult, violations);
            if (manifest.succeeded != expectedResult.Succeeded)
            {
                violations.Add("succeeded does not match the in-memory build result");
            }

            if (manifest.partial)
            {
                violations.Add("partial must be false for a full build manifest");
            }

            RequireTerminalTimestamp(manifest.startedUtc, "startedUtc", violations);
            RequireTerminalTimestamp(manifest.finishedUtc, "finishedUtc", violations);
            ValidateTimestampOrder(
                manifest.startedUtc,
                manifest.finishedUtc,
                violations);
            RequireNonEmptyStrings(
                violations,
                (manifest.unityVersion, "unityVersion"),
                (manifest.target, "target"),
                (manifest.namedBuildTarget, "namedBuildTarget"),
                (manifest.scriptingBackend, "scriptingBackend"),
                (manifest.applicationVersion, "applicationVersion"),
                (manifest.buildRoot, "buildRoot"),
                (manifest.outputPath, "outputPath"),
                (manifest.outputDirectory, "outputDirectory"),
                (manifest.versionInfoAssetPath, "versionInfoAssetPath"),
                (manifest.cheatBuildMode, "cheatBuildMode"));
            RequireStrings(
                violations,
                (manifest.packageVersion, "packageVersion"),
                (manifest.identityOrigin, "identityOrigin"),
                (manifest.failure, "failure"));
            RequireObject(manifest.detectedIdentity, "detectedIdentity", violations);
            RequireObject(manifest.effectiveIdentity, "effectiveIdentity", violations);
            RequireObject(manifest.ciIdentity, "ciIdentity", violations);
            RequireObject(manifest.sourceWorkspace, "sourceWorkspace", violations);
            RequireArray(manifest.buildScenePaths, "buildScenePaths", violations);
            RequireArray(manifest.nonFatalFailures, "nonFatalFailures", violations);
            RequireArray(manifest.recipeInvocations, "recipeInvocations", violations);
            RequireArray(manifest.steps, "steps", violations);
            RequireArray(manifest.content, "content", violations);
            ValidateNestedCollections(manifest, violations);
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget =
                BuildResultEvidencePolicy.CreateDiagnosticBudget();
            ValidateNonFatalFailures(
                manifest.nonFatalFailures,
                expectedResult.NonFatalFailures,
                diagnosticBudget,
                violations);
            ValidateRecipeInvocations(
                manifest.recipeInvocations,
                terminalContext.RecipeProvenance,
                diagnosticBudget,
                violations);
            ValidateStepResults(
                manifest.steps,
                expectedResult.Steps,
                diagnosticBudget,
                violations);
            ValidateContentResults(
                manifest.content,
                terminalContext.ContentResults,
                violations);

            ThrowIfContractInvalid("full build", violations);
            MarkTerminalManifestValidated(terminalState);
        }

        private void ValidateFullManifestRequestFields(
            BuildResultManifestFormat.Document manifest,
            BuildRunResult expectedResult,
            ICollection<string> violations)
        {
            BuildRequest request = terminalContext.Request;
            BuildVersionContext version = terminalContext.Version;
            RequireEqual(
                manifest.unityVersion,
                Application.unityVersion,
                "unityVersion",
                violations);
            RequireEqual(manifest.target, request.Target.ToString(), "target", violations);
            RequireEqual(
                manifest.namedBuildTarget,
                request.NamedTarget.TargetName,
                "namedBuildTarget",
                violations);
            RequireEqual(
                manifest.scriptingBackend,
                request.ScriptingBackend.ToString(),
                "scriptingBackend",
                violations);
            RequireEqual(manifest.debugBuild, request.DebugBuild, "debugBuild", violations);
            RequireEqual(
                manifest.deleteDebugFiles,
                request.DeleteDebugFiles,
                "deleteDebugFiles",
                violations);
            RequireEqual(
                manifest.exportAndroidProject,
                request.ExportAndroidProject,
                "exportAndroidProject",
                violations);
            RequireEqual(
                manifest.allowExternalOutput,
                request.AllowExternalOutput,
                "allowExternalOutput",
                violations);
            RequireEqual(
                manifest.outputIsFolder,
                request.OutputIsFolder,
                "outputIsFolder",
                violations);
            RequireEqual(
                manifest.applicationVersion,
                request.ApplicationVersion,
                "applicationVersion",
                violations);
            RequireEqual(
                manifest.packageVersion,
                version?.PackageVersion ?? string.Empty,
                "packageVersion",
                violations);
            RequireEqual(manifest.buildRoot, request.BuildRoot, "buildRoot", violations);
            RequireEqual(
                manifest.outputPath,
                expectedResult.OutputPath,
                "outputPath",
                violations);
            RequireEqual(
                manifest.outputDirectory,
                request.OutputDirectory,
                "outputDirectory",
                violations);
            RequireEqual(
                manifest.versionInfoAssetPath,
                request.VersionInfoAssetPath,
                "versionInfoAssetPath",
                violations);
            RequireEqual(
                manifest.cheatBuildMode,
                request.CheatBuildMode.ToString(),
                "cheatBuildMode",
                violations);
            RequireEqual(
                manifest.cheatEnabled,
                request.CheatEnabled,
                "cheatEnabled",
                violations);
            ValidateSourceWorkspace(
                manifest.sourceWorkspace,
                BuildResultManifestWriter.CreateSourceWorkspaceEntry(request, version),
                violations);
            RequireEqual(
                manifest.playerPipelineCompatibilityRevision,
                PlayerOutputTransaction.PlayerPipelineCompatibilityRevision,
                "playerPipelineCompatibilityRevision",
                violations);
            RequireEqual(
                manifest.playerExtensionFingerprint,
                PlayerBuildExtensionFingerprint.ResolveForEvidence(terminalContext),
                "playerExtensionFingerprint",
                violations);
            RequireEqual(
                manifest.failure,
                BuildResultEvidencePolicy.NormalizeException(
                    expectedResult.Failure),
                "failure",
                violations);
            RequireSequenceEqual(
                manifest.buildScenePaths,
                request.BuildScenePaths,
                "buildScenePaths",
                violations);
        }

        private void ConfirmEarlyTerminalManifest(
            EarlyTerminalManifest expected,
            string terminalState)
        {
            string json = ReadTerminalManifestJson();
            var manifest = new EarlyTerminalManifest
            {
                succeeded = !expected.succeeded,
                partial = false,
                processExitCode = int.MinValue
            };
            DeserializeTerminalManifest(json, manifest, "early terminal");

            var violations = new List<string>();
            RequireEqual(
                manifest.formatVersion,
                BuildResultManifestFormat.CurrentVersion,
                "formatVersion",
                violations);
            RequireEqual(manifest.operation, Operation, "operation", violations);
            RequireEqual(manifest.runId, RunId, "runId", violations);
            RequireEqual(manifest.stage, expected.stage, "stage", violations);
            RequireEqual(manifest.logPath, LogPath, "logPath", violations);
            RequireEqual(
                manifest.unityVersion,
                expected.unityVersion,
                "unityVersion",
                violations);
            RequireEqual(
                manifest.failureType,
                expected.failureType,
                "failureType",
                violations);
            RequireEqual(
                manifest.failure,
                expected.failure,
                "failure",
                violations);
            if (manifest.succeeded != expected.succeeded)
            {
                violations.Add("succeeded does not match the expected early terminal outcome");
            }

            if (!manifest.partial)
            {
                violations.Add("partial must be true for an early terminal manifest");
            }

            if (manifest.processExitCode != expected.processExitCode)
            {
                violations.Add("processExitCode does not match the expected early terminal outcome");
            }

            if (manifest.succeeded
                != (manifest.processExitCode == BuildProcessExitCodes.Succeeded))
            {
                violations.Add("succeeded and processExitCode are inconsistent");
            }

            RequireEqual(
                manifest.startedUtc,
                expected.startedUtc,
                "startedUtc",
                violations);
            RequireTerminalTimestamp(manifest.startedUtc, "startedUtc", violations);
            RequireTerminalTimestamp(manifest.finishedUtc, "finishedUtc", violations);
            ValidateTimestampOrder(
                manifest.startedUtc,
                manifest.finishedUtc,
                violations);
            RequireNonEmptyStrings(
                violations,
                (manifest.unityVersion, "unityVersion"));
            RequireStrings(
                violations,
                (manifest.failureType, "failureType"),
                (manifest.failure, "failure"));
            if (manifest.succeeded)
            {
                if (!string.IsNullOrEmpty(manifest.failureType)
                    || !string.IsNullOrEmpty(manifest.failure))
                {
                    violations.Add("successful early terminal evidence must not contain a failure");
                }
            }
            else if (string.IsNullOrEmpty(manifest.failureType)
                     || string.IsNullOrEmpty(manifest.failure))
            {
                violations.Add("failed early terminal evidence must contain failure type and details");
            }

            ThrowIfContractInvalid("early terminal", violations);
            MarkTerminalManifestValidated(terminalState);
        }

        private string ReadTerminalManifestJson()
        {
            FileInfo manifest = new FileInfo(ManifestPath);
            if (!manifest.Exists
                || manifest.Length <= 0
                || manifest.Length > MaximumEvidenceJsonBytes)
            {
                throw new BuildResultEvidenceException(
                    $"Required terminal build manifest is missing, empty, or exceeds the safety budget: '{ManifestPath}'.");
            }

            try
            {
                return File.ReadAllText(ManifestPath, StrictUtf8);
            }
            catch (Exception exception)
            {
                throw new BuildResultEvidenceException(
                    $"Required terminal build manifest is not valid UTF-8 text: '{ManifestPath}'.",
                    exception);
            }
        }

        private void DeserializeTerminalManifest(
            string json,
            object manifest,
            string contractName)
        {
            try
            {
                JsonUtility.FromJsonOverwrite(json, manifest);
            }
            catch (Exception exception)
            {
                throw new BuildResultEvidenceException(
                    $"Required {contractName} manifest is not valid JSON: '{ManifestPath}'.",
                    exception);
            }
        }

        private void MarkTerminalManifestValidated(string terminalState)
        {
            Append("session", "terminal-manifest-confirmed state=" + terminalState);
            terminalManifestValidated = true;
        }

        private void ThrowIfContractInvalid(
            string contractName,
            IReadOnlyList<string> violations)
        {
            if (violations.Count == 0)
            {
                return;
            }

            throw new BuildResultEvidenceException(
                $"Required {contractName} manifest violates format contract " +
                $"{BuildResultManifestFormat.CurrentVersion}: " +
                string.Join("; ", violations) + $". Path: '{ManifestPath}'.");
        }

        private static void RequireEqual(
            int actual,
            int expected,
            string fieldName,
            ICollection<string> violations)
        {
            if (actual != expected)
            {
                violations.Add(fieldName + " is missing or invalid");
            }
        }

        private static void RequireEqual(
            string actual,
            string expected,
            string fieldName,
            ICollection<string> violations)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                violations.Add(fieldName + " is missing or invalid");
            }
        }

        private static void RequireEqual(
            bool actual,
            bool expected,
            string fieldName,
            ICollection<string> violations)
        {
            if (actual != expected)
            {
                violations.Add(fieldName + " is missing or invalid");
            }
        }

        private static void RequireSequenceEqual(
            string[] actual,
            IReadOnlyList<string> expected,
            string fieldName,
            ICollection<string> violations)
        {
            if (actual == null || actual.Length != expected.Count)
            {
                violations.Add(fieldName + " is missing or invalid");
                return;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                if (!string.Equals(actual[index], expected[index], StringComparison.Ordinal))
                {
                    violations.Add(fieldName + " is missing or invalid");
                    return;
                }
            }
        }

        private static void RequireStrings(
            ICollection<string> violations,
            params (string Value, string Name)[] fields)
        {
            for (int index = 0; index < fields.Length; index++)
            {
                if (fields[index].Value == null)
                {
                    violations.Add(fields[index].Name + " is missing");
                }
            }
        }

        private static void RequireNonEmptyStrings(
            ICollection<string> violations,
            params (string Value, string Name)[] fields)
        {
            for (int index = 0; index < fields.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(fields[index].Value))
                {
                    violations.Add(fields[index].Name + " is missing or empty");
                }
            }
        }

        private static void RequireObject(
            object value,
            string fieldName,
            ICollection<string> violations)
        {
            if (value == null)
            {
                violations.Add(fieldName + " is missing");
            }
        }

        private static void RequireArray<T>(
            T[] value,
            string fieldName,
            ICollection<string> violations)
        {
            if (value == null)
            {
                violations.Add(fieldName + " is missing");
            }
        }

        private static void RequireTerminalTimestamp(
            string value,
            string fieldName,
            ICollection<string> violations)
        {
            if (!TryParseTerminalTimestamp(value, out _))
            {
                violations.Add(fieldName + " is missing or is not a round-trip UTC timestamp");
            }
        }

        private static void ValidateTimestampOrder(
            string started,
            string finished,
            ICollection<string> violations)
        {
            if (TryParseTerminalTimestamp(started, out DateTime startedUtcValue)
                && TryParseTerminalTimestamp(finished, out DateTime finishedUtcValue)
                && finishedUtcValue < startedUtcValue)
            {
                violations.Add("finishedUtc precedes startedUtc");
            }
        }

        private static bool TryParseTerminalTimestamp(string value, out DateTime timestamp)
        {
            return DateTime.TryParseExact(
                       value,
                       "O",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out timestamp)
                   && timestamp.Kind == DateTimeKind.Utc;
        }

        private static void ValidateNestedCollections(
            BuildResultManifestFormat.Document manifest,
            ICollection<string> violations)
        {
            ValidateIdentity(
                manifest.detectedIdentity,
                "detectedIdentity",
                violations);
            ValidateIdentity(
                manifest.effectiveIdentity,
                "effectiveIdentity",
                violations);
            if (manifest.ciIdentity != null)
            {
                RequireStrings(
                    violations,
                    (manifest.ciIdentity.provider, "ciIdentity.provider"),
                    (manifest.ciIdentity.runId, "ciIdentity.runId"));
            }

            if (manifest.sourceWorkspace != null)
            {
                RequireNonEmptyStrings(
                    violations,
                    (manifest.sourceWorkspace.policy, "sourceWorkspace.policy"),
                    (manifest.sourceWorkspace.overallStatus, "sourceWorkspace.overallStatus"),
                    (manifest.sourceWorkspace.failureCode, "sourceWorkspace.failureCode"));
                ValidateWorkspaceComponent(
                    manifest.sourceWorkspace.trackedChanges,
                    "sourceWorkspace.trackedChanges",
                    violations);
                ValidateWorkspaceComponent(
                    manifest.sourceWorkspace.untrackedChanges,
                    "sourceWorkspace.untrackedChanges",
                    violations);
                ValidateWorkspaceComponent(
                    manifest.sourceWorkspace.submodules,
                    "sourceWorkspace.submodules",
                    violations);
                ValidateWorkspaceComponent(
                    manifest.sourceWorkspace.gitLfs,
                    "sourceWorkspace.gitLfs",
                    violations);
            }

            if (manifest.recipeInvocations != null)
            {
                for (int index = 0; index < manifest.recipeInvocations.Length; index++)
                {
                    BuildResultManifestFormat.RecipeInvocationEntry invocation =
                        manifest.recipeInvocations[index];
                    if (invocation == null)
                    {
                        violations.Add($"recipeInvocations[{index}] is null");
                    }
                    else if (invocation.dependencies == null)
                    {
                        violations.Add(
                            $"recipeInvocations[{index}].dependencies is missing");
                    }
                    else
                    {
                        string prefix = $"recipeInvocations[{index}]";
                        RequireStrings(
                            violations,
                            (invocation.invocationId, prefix + ".invocationId"),
                            (invocation.stepTypeId, prefix + ".stepTypeId"),
                            (invocation.incrementality, prefix + ".incrementality"),
                            (invocation.configurationAssetPath, prefix + ".configurationAssetPath"),
                            (invocation.configurationAssetGuid, prefix + ".configurationAssetGuid"),
                            (invocation.configurationLocalFileId, prefix + ".configurationLocalFileId"),
                            (invocation.configurationType, prefix + ".configurationType"),
                            (invocation.configurationAssetSha256, prefix + ".configurationAssetSha256"),
                            (invocation.configurationDependencyHash, prefix + ".configurationDependencyHash"),
                            (invocation.validationError, prefix + ".validationError"));
                        for (int dependencyIndex = 0;
                             dependencyIndex < invocation.dependencies.Length;
                             dependencyIndex++)
                        {
                            BuildResultManifestFormat.DependencyEntry dependency =
                                invocation.dependencies[dependencyIndex];
                            if (dependency == null)
                            {
                                violations.Add(
                                    $"{prefix}.dependencies[{dependencyIndex}] is null");
                                continue;
                            }

                            RequireNonEmptyStrings(
                                violations,
                                (dependency.invocationId,
                                    $"{prefix}.dependencies[{dependencyIndex}].invocationId"),
                                (dependency.mode,
                                    $"{prefix}.dependencies[{dependencyIndex}].mode"));
                        }
                    }
                }
            }

            if (manifest.steps != null)
            {
                for (int index = 0; index < manifest.steps.Length; index++)
                {
                    if (manifest.steps[index] == null)
                    {
                        violations.Add($"steps[{index}] is null");
                        continue;
                    }

                    BuildResultManifestFormat.StepEntry step = manifest.steps[index];
                    string prefix = $"steps[{index}]";
                    RequireStrings(
                        violations,
                        (step.invocationId, prefix + ".invocationId"),
                        (step.stepTypeId, prefix + ".stepTypeId"),
                        (step.status, prefix + ".status"),
                        (step.message, prefix + ".message"));
                }
            }

            if (manifest.content == null)
            {
                return;
            }

            for (int index = 0; index < manifest.content.Length; index++)
            {
                BuildResultManifestFormat.ContentEntry content = manifest.content[index];
                if (content == null)
                {
                    violations.Add($"content[{index}] is null");
                    continue;
                }

                if (content.artifacts == null)
                {
                    violations.Add($"content[{index}].artifacts is missing");
                }

                if (content.warnings == null)
                {
                    violations.Add($"content[{index}].warnings is missing");
                }

                string prefix = $"content[{index}]";
                RequireStrings(
                    violations,
                    (content.invocationId, prefix + ".invocationId"),
                    (content.providerId, prefix + ".providerId"),
                    (content.packageName, prefix + ".packageName"),
                    (content.packageVersion, prefix + ".packageVersion"),
                    (content.failedTask, prefix + ".failedTask"),
                    (content.errorInfo, prefix + ".errorInfo"),
                    (content.errorStack, prefix + ".errorStack"),
                    (content.outputPackageDirectory, prefix + ".outputPackageDirectory"),
                    (content.bundledPackageDirectory, prefix + ".bundledPackageDirectory"),
                    (content.reportPath, prefix + ".reportPath"));
            }
        }

        private static void ValidateNonFatalFailures(
            string[] actual,
            IReadOnlyList<Exception> expected,
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget,
            ICollection<string> violations)
        {
            string[] expectedEntries =
                BuildResultEvidencePolicy.NormalizeExceptions(
                    expected,
                    diagnosticBudget);
            if (actual == null || actual.Length != expectedEntries.Length)
            {
                violations.Add("nonFatalFailures does not match the in-memory build result");
                return;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                if (!string.Equals(
                        actual[index],
                        expectedEntries[index],
                        StringComparison.Ordinal))
                {
                    violations.Add(
                        $"nonFatalFailures[{index}] does not match the in-memory build result");
                }
            }
        }

        private static void ValidateRecipeInvocations(
            BuildResultManifestFormat.RecipeInvocationEntry[] actual,
            IReadOnlyList<BuildRecipeProvenanceEntry> expected,
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget,
            ICollection<string> violations)
        {
            int expectedCount = expected?.Count ?? 0;
            if (actual == null || actual.Length != expectedCount)
            {
                violations.Add("recipeInvocations does not match the captured recipe provenance");
                return;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                BuildResultManifestFormat.RecipeInvocationEntry actualEntry = actual[index];
                BuildRecipeProvenanceEntry expectedEntry = expected[index];
                string prefix = $"recipeInvocations[{index}]";
                if (actualEntry == null || expectedEntry == null)
                {
                    violations.Add(prefix + " does not match the captured recipe provenance");
                    continue;
                }

                RequireEqual(actualEntry.order, expectedEntry.Order, prefix + ".order", violations);
                RequireEqual(
                    actualEntry.invocationId,
                    expectedEntry.InvocationId,
                    prefix + ".invocationId",
                    violations);
                RequireEqual(
                    actualEntry.stepTypeId,
                    expectedEntry.StepTypeId,
                    prefix + ".stepTypeId",
                    violations);
                RequireEqual(
                    actualEntry.incrementality,
                    expectedEntry.Incrementality.ToString(),
                    prefix + ".incrementality",
                    violations);
                RequireEqual(
                    actualEntry.hasConfiguration,
                    expectedEntry.HasConfiguration,
                    prefix + ".hasConfiguration",
                    violations);
                RequireEqual(
                    actualEntry.configurationAssetPath,
                    expectedEntry.ConfigurationAssetPath,
                    prefix + ".configurationAssetPath",
                    violations);
                RequireEqual(
                    actualEntry.configurationAssetGuid,
                    expectedEntry.ConfigurationAssetGuid,
                    prefix + ".configurationAssetGuid",
                    violations);
                RequireEqual(
                    actualEntry.configurationLocalFileId,
                    expectedEntry.ConfigurationLocalFileId,
                    prefix + ".configurationLocalFileId",
                    violations);
                RequireEqual(
                    actualEntry.configurationType,
                    expectedEntry.ConfigurationType,
                    prefix + ".configurationType",
                    violations);
                RequireEqual(
                    actualEntry.configurationAssetSha256,
                    expectedEntry.ConfigurationAssetSha256,
                    prefix + ".configurationAssetSha256",
                    violations);
                RequireEqual(
                    actualEntry.configurationDependencyHash,
                    expectedEntry.ConfigurationDependencyHash,
                    prefix + ".configurationDependencyHash",
                    violations);
                RequireEqual(
                    actualEntry.configurationDependencyCount,
                    expectedEntry.ConfigurationDependencyCount,
                    prefix + ".configurationDependencyCount",
                    violations);
                RequireEqual(
                    actualEntry.validationError,
                    diagnosticBudget.NormalizeText(
                        expectedEntry.ValidationError),
                    prefix + ".validationError",
                    violations);

                IReadOnlyList<BuildInvocationDependency> expectedDependencies =
                    expectedEntry.Dependencies;
                if (actualEntry.dependencies == null
                    || actualEntry.dependencies.Length != expectedDependencies.Count)
                {
                    violations.Add(
                        prefix + ".dependencies does not match the captured recipe provenance");
                    continue;
                }

                for (int dependencyIndex = 0;
                     dependencyIndex < actualEntry.dependencies.Length;
                     dependencyIndex++)
                {
                    BuildResultManifestFormat.DependencyEntry actualDependency =
                        actualEntry.dependencies[dependencyIndex];
                    BuildInvocationDependency expectedDependency =
                        expectedDependencies[dependencyIndex];
                    string dependencyPrefix =
                        $"{prefix}.dependencies[{dependencyIndex}]";
                    if (actualDependency == null || expectedDependency == null)
                    {
                        violations.Add(
                            dependencyPrefix
                            + " does not match the captured recipe provenance");
                        continue;
                    }

                    RequireEqual(
                        actualDependency.invocationId,
                        expectedDependency.InvocationId,
                        dependencyPrefix + ".invocationId",
                        violations);
                    RequireEqual(
                        actualDependency.mode,
                        expectedDependency.Mode.ToString(),
                        dependencyPrefix + ".mode",
                        violations);
                }
            }
        }

        private static void ValidateStepResults(
            BuildResultManifestFormat.StepEntry[] actual,
            IReadOnlyList<BuildStepResult> expected,
            BuildResultEvidencePolicy.DiagnosticBudget diagnosticBudget,
            ICollection<string> violations)
        {
            BuildResultManifestFormat.StepEntry[] expectedEntries =
                BuildResultEvidencePolicy.CreateStepEntries(
                    expected,
                    diagnosticBudget);
            if (actual == null || actual.Length != expectedEntries.Length)
            {
                violations.Add("steps does not match the in-memory build result");
                return;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                BuildResultManifestFormat.StepEntry actualEntry = actual[index];
                BuildResultManifestFormat.StepEntry expectedEntry =
                    expectedEntries[index];
                string prefix = $"steps[{index}]";
                if (actualEntry == null || expectedEntry == null)
                {
                    violations.Add(prefix + " does not match the in-memory build result");
                    continue;
                }

                RequireEqual(
                    actualEntry.invocationId,
                    expectedEntry.invocationId,
                    prefix + ".invocationId",
                    violations);
                RequireEqual(
                    actualEntry.stepTypeId,
                    expectedEntry.stepTypeId,
                    prefix + ".stepTypeId",
                    violations);
                RequireEqual(
                    actualEntry.status,
                    expectedEntry.status,
                    prefix + ".status",
                    violations);
                RequireDurationEqual(
                    actualEntry.durationSeconds,
                    expectedEntry.durationSeconds,
                    prefix + ".durationSeconds",
                    violations);
                RequireEqual(
                    actualEntry.message,
                    expectedEntry.message,
                    prefix + ".message",
                    violations);
            }
        }

        private static void ValidateContentResults(
            BuildResultManifestFormat.ContentEntry[] actual,
            IReadOnlyList<AssetContentInvocationResult> expected,
            ICollection<string> violations)
        {
            BuildResultManifestFormat.ContentEntry[] expectedEntries =
                BuildResultEvidencePolicy.CreateContentEntries(expected);
            if (actual == null || actual.Length != expectedEntries.Length)
            {
                violations.Add("content does not match the in-memory provider results");
                return;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                BuildResultManifestFormat.ContentEntry actualEntry = actual[index];
                BuildResultManifestFormat.ContentEntry expectedEntry =
                    expectedEntries[index];
                string prefix = $"content[{index}]";
                if (actualEntry == null || expectedEntry == null)
                {
                    violations.Add(prefix + " does not match the in-memory provider result");
                    continue;
                }

                RequireEqual(
                    actualEntry.invocationId,
                    expectedEntry.invocationId,
                    prefix + ".invocationId",
                    violations);
                RequireEqual(
                    actualEntry.succeeded,
                    expectedEntry.succeeded,
                    prefix + ".succeeded",
                    violations);
                RequireEqual(
                    actualEntry.providerId,
                    expectedEntry.providerId,
                    prefix + ".providerId",
                    violations);
                RequireEqual(
                    actualEntry.packageName,
                    expectedEntry.packageName,
                    prefix + ".packageName",
                    violations);
                RequireEqual(
                    actualEntry.packageVersion,
                    expectedEntry.packageVersion,
                    prefix + ".packageVersion",
                    violations);
                RequireEqual(
                    actualEntry.failedTask,
                    expectedEntry.failedTask,
                    prefix + ".failedTask",
                    violations);
                RequireEqual(
                    actualEntry.errorInfo,
                    expectedEntry.errorInfo,
                    prefix + ".errorInfo",
                    violations);
                RequireEqual(
                    actualEntry.errorStack,
                    expectedEntry.errorStack,
                    prefix + ".errorStack",
                    violations);
                RequireEqual(
                    actualEntry.outputPackageDirectory,
                    expectedEntry.outputPackageDirectory,
                    prefix + ".outputPackageDirectory",
                    violations);
                RequireEqual(
                    actualEntry.bundledPackageDirectory,
                    expectedEntry.bundledPackageDirectory,
                    prefix + ".bundledPackageDirectory",
                    violations);
                RequireEqual(
                    actualEntry.reportPath,
                    expectedEntry.reportPath,
                    prefix + ".reportPath",
                    violations);
                RequireSequenceEqual(
                    actualEntry.artifacts,
                    expectedEntry.artifacts,
                    prefix + ".artifacts",
                    violations);
                RequireSequenceEqual(
                    actualEntry.warnings,
                    expectedEntry.warnings,
                    prefix + ".warnings",
                    violations);
            }
        }

        private static void RequireDurationEqual(
            double actual,
            double expected,
            string fieldName,
            ICollection<string> violations)
        {
            const double MaximumRoundTripDifferenceSeconds = 0.000001d;
            if (double.IsNaN(actual)
                || double.IsInfinity(actual)
                || Math.Abs(actual - expected) > MaximumRoundTripDifferenceSeconds)
            {
                violations.Add(fieldName + " does not match the in-memory build result");
            }
        }

        private static void ValidateIdentity(
            BuildResultManifestFormat.BuildIdentityEntry identity,
            string prefix,
            ICollection<string> violations)
        {
            if (identity == null)
            {
                return;
            }

            RequireStrings(
                violations,
                (identity.sourceProvider, prefix + ".sourceProvider"),
                (identity.sourceRevision, prefix + ".sourceRevision"),
                (identity.sourceBranch, prefix + ".sourceBranch"),
                (identity.sourceCommitCount, prefix + ".sourceCommitCount"),
                (identity.sourceCommitDate, prefix + ".sourceCommitDate"));
        }

        private static void ValidateSourceWorkspace(
            BuildResultManifestFormat.SourceWorkspaceEntry actual,
            BuildResultManifestFormat.SourceWorkspaceEntry expected,
            ICollection<string> violations)
        {
            if (actual == null || expected == null)
            {
                return;
            }

            RequireEqual(actual.policy, expected.policy, "sourceWorkspace.policy", violations);
            RequireEqual(actual.required, expected.required, "sourceWorkspace.required", violations);
            RequireEqual(
                actual.overallStatus,
                expected.overallStatus,
                "sourceWorkspace.overallStatus",
                violations);
            RequireEqual(
                actual.failureCode,
                expected.failureCode,
                "sourceWorkspace.failureCode",
                violations);
            ValidateWorkspaceComponentEqual(
                actual.trackedChanges,
                expected.trackedChanges,
                "sourceWorkspace.trackedChanges",
                violations);
            ValidateWorkspaceComponentEqual(
                actual.untrackedChanges,
                expected.untrackedChanges,
                "sourceWorkspace.untrackedChanges",
                violations);
            ValidateWorkspaceComponentEqual(
                actual.submodules,
                expected.submodules,
                "sourceWorkspace.submodules",
                violations);
            ValidateWorkspaceComponentEqual(
                actual.gitLfs,
                expected.gitLfs,
                "sourceWorkspace.gitLfs",
                violations);
        }

        private static void ValidateWorkspaceComponent(
            BuildResultManifestFormat.WorkspaceComponentEntry component,
            string prefix,
            ICollection<string> violations)
        {
            if (component == null)
            {
                violations.Add(prefix + " is missing");
                return;
            }

            if (string.IsNullOrEmpty(component.status))
            {
                violations.Add(prefix + ".status is missing");
            }

            if (component.changeCount < 0
                || (!component.hasChangeCount && component.changeCount != 0))
            {
                violations.Add(prefix + ".changeCount is invalid");
            }
        }

        private static void ValidateWorkspaceComponentEqual(
            BuildResultManifestFormat.WorkspaceComponentEntry actual,
            BuildResultManifestFormat.WorkspaceComponentEntry expected,
            string prefix,
            ICollection<string> violations)
        {
            if (actual == null || expected == null)
            {
                return;
            }

            RequireEqual(actual.status, expected.status, prefix + ".status", violations);
            RequireEqual(
                actual.hasChangeCount,
                expected.hasChangeCount,
                prefix + ".hasChangeCount",
                violations);
            RequireEqual(
                actual.changeCount,
                expected.changeCount,
                prefix + ".changeCount",
                violations);
        }

        private void Append(string phase, string message)
        {
            ThrowIfDisposed();
            string normalized = NormalizeEvent(message);
            string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                + "\t" + phase
                + "\t" + normalized
                + Environment.NewLine;
            byte[] bytes = StrictUtf8.GetBytes(line);
            lock (logGate)
            {
                if (logWriteFailure != null)
                {
                    throw new BuildResultEvidenceException(
                        $"The required build result log is no longer writable: '{LogPath}'.",
                        logWriteFailure);
                }

                try
                {
                    if (logStream.Length + bytes.Length > MaximumLogBytes)
                    {
                        throw new IOException(
                            $"Build result log exceeds the {MaximumLogBytes}-byte safety budget: '{LogPath}'.");
                    }

                    logStream.Write(bytes, 0, bytes.Length);
                    logStream.Flush(true);
                }
                catch (Exception exception)
                {
                    logWriteFailure = exception;
                    throw new BuildResultEvidenceException(
                        $"Failed to persist the required build result log '{LogPath}'.",
                        exception);
                }
            }
        }

        private static string NormalizeEvent(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > MaximumEventCharacters)
            {
                text = text.Substring(0, MaximumEventCharacters) + "...[truncated]";
            }

            return text.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void WriteStartedMarker(
            string path,
            string operation,
            string runId,
            DateTime startedUtc,
            string manifestPath,
            string logPath)
        {
            int processId;
            using (Process process = Process.GetCurrentProcess())
            {
                processId = process.Id;
            }

            var marker = new StartedMarker
            {
                operation = operation,
                runId = runId,
                startedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture),
                processId = processId,
                manifestPath = manifestPath,
                logPath = logPath
            };
            WriteNewJsonAtomically(path, JsonUtility.ToJson(marker, true));
        }

        private static void WriteNewJsonAtomically(string path, string json)
        {
            byte[] bytes = StrictUtf8.GetBytes(json ?? string.Empty);
            if (bytes.Length > MaximumEvidenceJsonBytes)
            {
                throw new BuildResultEvidenceException(
                    $"Build result evidence exceeds the {MaximumEvidenceJsonBytes}-byte safety budget: '{path}'.");
            }

            string temporaryPath = BuildPathPolicy.EnsureWin32MaxPathBudget(
                path + ".tmp",
                "Build result evidence temporary file");
            bool ownsTemporaryFile = false;
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
            finally
            {
                if (ownsTemporaryFile && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string NormalizeProjectRoot(string trustedProjectRoot)
        {
            if (string.IsNullOrWhiteSpace(trustedProjectRoot))
            {
                throw new ArgumentException(
                    "A trusted Unity project root is required.",
                    nameof(trustedProjectRoot));
            }

            string projectRoot = Path.GetFullPath(trustedProjectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(Path.Combine(projectRoot, "Assets"))
                || !Directory.Exists(Path.Combine(projectRoot, "ProjectSettings")))
            {
                throw new InvalidOperationException(
                    $"Build result evidence root is not a Unity project: '{projectRoot}'.");
            }

            return projectRoot;
        }

        private static string CreateRunId()
        {
            return DateTime.UtcNow.ToString(
                       "yyyyMMdd'T'HHmmssfff'Z'",
                       CultureInfo.InvariantCulture)
                   + "-"
                   + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        internal static void ValidateRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)
                || !string.Equals(runId, runId.Trim(), StringComparison.Ordinal)
                || runId.Length > 128)
            {
                throw new ArgumentException(
                    "Build run id is required, may not have surrounding whitespace, and must not exceed 128 characters.",
                    nameof(runId));
            }

            for (int index = 0; index < runId.Length; index++)
            {
                char character = runId[index];
                bool allowed = character >= 'a' && character <= 'z'
                    || character >= 'A' && character <= 'Z'
                    || character >= '0' && character <= '9'
                    || character == '-'
                    || character == '_';
                if (!allowed)
                {
                    throw new ArgumentException(
                        "Build run id may contain only ASCII letters, digits, '-' and '_'.",
                        nameof(runId));
                }
            }
        }

        private static string EnsureResultPath(string path, string displayName)
        {
            return BuildPathPolicy.EnsureWin32MaxPathBudget(
                path,
                displayName,
                ".tmp".Length);
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

        private static void TryDeleteOwnedFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Preserve the initialization failure.
            }
        }

        private static Exception Combine(Exception first, Exception second)
        {
            if (first == null)
            {
                return second;
            }

            if (second == null)
            {
                return first;
            }

            return new AggregateException(first, second);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(BuildResultEvidenceSession));
            }
        }

        [Serializable]
        private sealed class StartedMarker
        {
            public int formatVersion = BuildResultManifestFormat.CurrentVersion;
            public string operation;
            public string runId;
            public string startedUtc;
            public int processId;
            public string manifestPath;
            public string logPath;
        }

        [Serializable]
        private sealed class EarlyTerminalManifest
        {
            public int formatVersion;
            public string operation;
            public string runId;
            public bool succeeded;
            public bool partial;
            public string stage;
            public int processExitCode;
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string failureType;
            public string failure;
            public string logPath;
        }

    }

    internal sealed class CompositeBuildEventSink : IBuildEventSink
    {
        private readonly IReadOnlyList<IBuildEventSink> sinks;

        public CompositeBuildEventSink(params IBuildEventSink[] sinks)
        {
            if (sinks == null || sinks.Length == 0)
            {
                throw new ArgumentException(
                    "At least one build event sink is required.",
                    nameof(sinks));
            }

            var snapshot = new IBuildEventSink[sinks.Length];
            for (int index = 0; index < sinks.Length; index++)
            {
                snapshot[index] = sinks[index]
                    ?? throw new ArgumentException(
                        $"Build event sink at index {index} is null.",
                        nameof(sinks));
            }

            this.sinks = Array.AsReadOnly(snapshot);
        }

        public void RunStarted(
            BuildExecutionContext context,
            IReadOnlyList<CompiledBuildStep> plan)
        {
            Dispatch(sink => sink.RunStarted(context, plan));
        }

        public void StepStarted(BuildExecutionContext context, CompiledBuildStep step)
        {
            Dispatch(sink => sink.StepStarted(context, step));
        }

        public void StepFinished(BuildExecutionContext context, BuildStepResult result)
        {
            Dispatch(sink => sink.StepFinished(context, result));
        }

        public void RunFinished(BuildExecutionContext context, BuildRunResult result)
        {
            Dispatch(sink => sink.RunFinished(context, result));
        }

        private void Dispatch(Action<IBuildEventSink> notification)
        {
            List<Exception> failures = null;
            for (int index = 0; index < sinks.Count; index++)
            {
                try
                {
                    notification(sinks[index]);
                }
                catch (Exception exception)
                {
                    if (failures == null)
                    {
                        failures = new List<Exception>();
                    }

                    failures.Add(exception);
                }
            }

            if (failures != null)
            {
                throw new AggregateException(
                    "One or more build event sinks failed.",
                    failures);
            }
        }
    }
}
