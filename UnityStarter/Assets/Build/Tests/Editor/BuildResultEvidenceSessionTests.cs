using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildResultEvidenceSessionTests
    {
        private string sandboxRoot;
        private BuildData profile;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-BuildEvidenceTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandboxRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(sandboxRoot, "ProjectSettings"));
            profile = ScriptableObject.CreateInstance<BuildData>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }

            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, true);
            }
        }

        [Test]
        public void Session_EarlyTerminalManifest_RemovesMarkerOnlyAfterDispose()
        {
            var session = BuildResultEvidenceSession.Begin(sandboxRoot, "recovery");

            Assert.That(File.Exists(session.StartedMarkerPath), Is.True);
            Assert.That(File.Exists(session.LogPath), Is.True);
            session.WriteEarlyTerminalManifest(
                "workspace-recovery",
                succeeded: true,
                BuildProcessExitCodes.Succeeded,
                failure: null);

            Assert.That(session.HasValidatedTerminalManifest, Is.True);
            Assert.That(File.Exists(session.StartedMarkerPath), Is.True);
            session.Dispose();

            Assert.That(session.TerminalEvidenceConfirmed, Is.True);
            Assert.That(File.Exists(session.ManifestPath), Is.True);
            Assert.That(File.Exists(session.StartedMarkerPath), Is.False);
            StringAssert.Contains("workspace-recovery", File.ReadAllText(session.ManifestPath));
        }

        [Test]
        public void Session_DisposeWithoutTerminalManifest_RetainsStartedMarker()
        {
            var session = BuildResultEvidenceSession.Begin(sandboxRoot, "build");
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "ended without confirmed terminal evidence"));

            session.Dispose();

            Assert.That(session.TerminalEvidenceConfirmed, Is.False);
            Assert.That(File.Exists(session.StartedMarkerPath), Is.True);
        }

        [Test]
        public void Session_DisposeWithRecordedLogFailure_RetainsMarkerAndFailsClosed()
        {
            var session = BuildResultEvidenceSession.Begin(sandboxRoot, "recovery");
            session.WriteEarlyTerminalManifest(
                "workspace-recovery",
                succeeded: true,
                BuildProcessExitCodes.Succeeded,
                failure: null);
            typeof(BuildResultEvidenceSession)
                .GetField(
                    "logWriteFailure",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(session, new IOException("simulated durable-log failure"));
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "could not finalize its required evidence log"));

            BuildResultEvidenceException exception =
                Assert.Throws<BuildResultEvidenceException>(() => session.Dispose());

            StringAssert.Contains("required build result log", exception.Message);
            Assert.That(session.TerminalEvidenceConfirmed, Is.False);
            Assert.That(File.Exists(session.StartedMarkerPath), Is.True);
        }

        [Test]
        public void Session_EarlyTerminalManifest_TamperedFailureContractIsRejected()
        {
            var session = BuildResultEvidenceSession.Begin(sandboxRoot, "build");
            session.WriteEarlyTerminalManifest(
                "profile-resolution",
                succeeded: false,
                BuildProcessExitCodes.BuildFailed,
                new InvalidOperationException("expected-early-failure"));
            string originalJson = File.ReadAllText(session.ManifestPath);
            Type manifestType = typeof(BuildResultEvidenceSession).GetNestedType(
                "EarlyTerminalManifest",
                BindingFlags.NonPublic);
            Assert.That(manifestType, Is.Not.Null);
            object expectedManifest = JsonUtility.FromJson(originalJson, manifestType);
            Assert.That(expectedManifest, Is.Not.Null);
            string tamperedJson = ReplaceRequired(
                originalJson,
                "expected-early-failure",
                "tampered-early-failure");
            File.WriteAllText(session.ManifestPath, tamperedJson);
            typeof(BuildResultEvidenceSession)
                .GetField(
                    "terminalManifestValidated",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(session, false);
            MethodInfo confirmMethod = typeof(BuildResultEvidenceSession).GetMethod(
                "ConfirmEarlyTerminalManifest",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(confirmMethod, Is.Not.Null);

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                confirmMethod.Invoke(session, new[] { expectedManifest, "failed" }));

            Assert.That(invocation.InnerException, Is.TypeOf<BuildResultEvidenceException>());
            StringAssert.Contains("failure", invocation.InnerException.Message);
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "ended without confirmed terminal evidence"));
            session.Dispose();
            Assert.That(File.Exists(session.StartedMarkerPath), Is.True);
        }

        [Test]
        public void EventSink_IsCompositeConsoleAndDurableFileSink()
        {
            var session = BuildResultEvidenceSession.Begin(sandboxRoot, "build");
            IBuildEventSink sink = session.CreateEventSink();

            Assert.That(sink, Is.TypeOf<CompositeBuildEventSink>());
            session.WriteEarlyTerminalManifest(
                "test",
                succeeded: false,
                BuildProcessExitCodes.BuildFailed,
                new InvalidOperationException("expected"));
            session.Dispose();

            StringAssert.Contains("terminal stage=test", File.ReadAllText(session.LogPath));
        }

        [Test]
        public void CompositeEventSink_FirstSinkFailure_StillNotifiesLaterSinkAndAggregates()
        {
            var expectedFailure = new InvalidOperationException("observer failed");
            var recordingSink = new RecordingEventSink();
            var sink = new CompositeBuildEventSink(
                new ThrowingEventSink(expectedFailure),
                recordingSink);

            AggregateException failure = Assert.Throws<AggregateException>(
                () => sink.RunFinished(context: null, result: null));

            Assert.That(recordingSink.RunFinishedCount, Is.EqualTo(1));
            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(expectedFailure));
        }

        [Test]
        public void ExitCodeClassifier_RecursivelyFindsBusyAndEvidenceFailures()
        {
            var busy = new BuildWorkspaceBusyException(
                "lease.lock",
                BuildWorkspaceOperation.Build,
                new IOException("locked"));
            var evidence = new BuildResultEvidenceException(
                "evidence",
                new IOException("disk"));

            Assert.That(
                BuildProcessExitCodes.FromFailure(
                    new AggregateException(
                        new InvalidOperationException("outer", busy))),
                Is.EqualTo(BuildProcessExitCodes.WorkspaceBusy));
            Assert.That(
                BuildProcessExitCodes.FromFailure(
                    new AggregateException(busy, evidence)),
                Is.EqualTo(BuildProcessExitCodes.ResultEvidenceFailed));
        }

        [Test]
        public void ExitCodeClassifier_FailsClosedWhenTraversalBudgetIsExceeded()
        {
            Exception failure = new InvalidOperationException("root");
            for (int index = 0; index < 5000; index++)
            {
                failure = new InvalidOperationException("nested", failure);
            }

            Assert.That(
                BuildProcessExitCodes.FromFailure(failure),
                Is.EqualTo(BuildProcessExitCodes.ResultEvidenceFailed));
        }

        [TestCase(FakeFailureStage.Parse, "command-line-parse")]
        [TestCase(FakeFailureStage.Profile, "profile-resolution")]
        [TestCase(FakeFailureStage.Factory, "request-factory")]
        [TestCase(FakeFailureStage.Runner, "build-run")]
        public void CommandLine_EarlyStageFailure_WritesTerminalManifest(
            FakeFailureStage failureStage,
            string expectedStage)
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                failureStage);

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.BuildFailed));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
            StringAssert.Contains(expectedStage, File.ReadAllText(result.ManifestPath));
        }

        [Test]
        public void CommandLine_ProfileSelection_ReachesRequestFactoryUnchanged()
        {
            BuildCommandLineOptions parsedOptions = BuildCommandLine.Parse(new[]
            {
                BuildCommandLineOptionNames.BuildTarget,
                nameof(BuildTarget.StandaloneWindows64),
                BuildCommandLineOptionNames.Profile,
                "Assets/BuildProfiles/Release.asset",
                BuildCommandLineOptionNames.Selection,
                "content-release"
            });
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None)
            {
                ParsedCommandLineOptions = parsedOptions
            };

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.Succeeded));
            Assert.That(
                operations.ResolvedProfilePath,
                Is.EqualTo("Assets/BuildProfiles/Release.asset"));
            Assert.That(operations.RequestCommandLineOptions, Is.SameAs(parsedOptions));
            CollectionAssert.AreEqual(
                new[] { "content-release" },
                operations.RequestCommandLineOptions.SelectedInvocationIds);
        }

        [Test]
        public void CommandLine_RecoveryFailureWithNestedBusyException_ExitsThree()
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.Recovery)
            {
                RecoverOnly = true,
                RecoveryFailure = new AggregateException(
                    new InvalidOperationException(
                        "outer",
                        new BuildWorkspaceBusyException(
                            "lease.lock",
                            BuildWorkspaceOperation.Recovery,
                            new IOException("locked"))))
            };

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.WorkspaceBusy));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
            StringAssert.Contains("workspace-recovery", File.ReadAllText(result.ManifestPath));
        }

        [Test]
        public void CommandLine_RecoverySuccess_WritesTerminalSuccessManifest()
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None)
            {
                RecoverOnly = true
            };

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.Succeeded));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
            StringAssert.Contains("\"succeeded\": true", File.ReadAllText(result.ManifestPath));
        }

        [Test]
        public void CommandLine_FailedBuildWithFullManifestAndBusyFailure_ExitsThree()
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None)
            {
                BuildFailure = new BuildWorkspaceBusyException(
                    "lease.lock",
                    BuildWorkspaceOperation.Build,
                    new IOException("locked"))
            };
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"\[BuildPipeline\].*failed"));

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.WorkspaceBusy));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
        }

        [Test]
        public void CommandLine_MismatchedRunnerResult_FallsBackToPartialManifestAndExitsTwo()
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None)
            {
                ReturnMismatchedResultPath = true
            };

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(
                result.ExitCode,
                Is.EqualTo(BuildProcessExitCodes.ResultEvidenceFailed));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
            StringAssert.Contains("result-confirmation", File.ReadAllText(result.ManifestPath));
        }

        [TestCase(FakeManifestMode.Incomplete)]
        [TestCase(FakeManifestMode.WrongFormat)]
        [TestCase(FakeManifestMode.WrongOutcome)]
        [TestCase(FakeManifestMode.Partial)]
        [TestCase(FakeManifestMode.MissingRequestField)]
        [TestCase(FakeManifestMode.WrongRecipeProvenance)]
        [TestCase(FakeManifestMode.WrongStepResult)]
        [TestCase(FakeManifestMode.WrongContentResult)]
        [TestCase(FakeManifestMode.WrongNonFatalFailure)]
        public void CommandLine_InvalidFullManifest_IsRejectedAndRetainsStartedMarker(
            FakeManifestMode manifestMode)
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None)
            {
                ManifestMode = manifestMode
            };
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "ended without confirmed terminal evidence"));

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    sandboxRoot,
                    Array.Empty<string>(),
                    operations);

            Assert.That(
                result.ExitCode,
                Is.EqualTo(BuildProcessExitCodes.ResultEvidenceFailed));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.True);
        }

        [Test]
        public void Interactive_ProfileFailure_WritesTerminalManifest()
        {
            var operations = new FakeOperations(
                profile,
                CreateRequest(),
                FakeFailureStage.None);

            BuildEntryPointExecutionResult result =
                BuildEntryPointExecutor.ExecuteInteractive(
                    sandboxRoot,
                    () => throw new InvalidOperationException("profile failed"),
                    BuildTarget.StandaloneWindows64,
                    debug: false,
                    exportAndroidProject: false,
                    invocationIdsOverride: null,
                    operations);

            Assert.That(result.ExitCode, Is.EqualTo(BuildProcessExitCodes.BuildFailed));
            Assert.That(File.Exists(result.ManifestPath), Is.True);
            Assert.That(File.Exists(result.StartedMarkerPath), Is.False);
            StringAssert.Contains("profile-resolution", File.ReadAllText(result.ManifestPath));
        }

        [Test]
        public void Runner_ExplicitManifestPathMustMatchCanonicalSessionPath()
        {
            BuildRequest request = CreateRequest();
            var runner = new BuildPipelineRunner(
                new NoOpEventSink(),
                sandboxRoot,
                () => false);

            Assert.Throws<ArgumentException>(() => runner.Run(
                request,
                "explicit-run",
                Path.Combine(sandboxRoot, "wrong.json")));
        }

        private BuildRequest CreateRequest()
        {
            string buildRoot = Path.Combine(sandboxRoot, "Build");
            string outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            return new BuildRequest(
                "TestCompany",
                "TestProduct",
                "com.test.product",
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x,
                sandboxRoot,
                buildRoot,
                Path.Combine(outputDirectory, "TestProduct.exe"),
                outputDirectory,
                outputIsFolder: false,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: false,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: false,
                applicationVersion: "0.1.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: new[]
                {
                    new BuildStepInvocation("player", BuildStepTypeIds.Player)
                });
        }

        public enum FakeFailureStage
        {
            None,
            Parse,
            Profile,
            Factory,
            Runner,
            Recovery
        }

        public enum FakeManifestMode
        {
            Full,
            Incomplete,
            WrongFormat,
            WrongOutcome,
            Partial,
            MissingRequestField,
            WrongRecipeProvenance,
            WrongStepResult,
            WrongContentResult,
            WrongNonFatalFailure
        }

        private sealed class FakeOperations : IBuildEntryPointOperations
        {
            private readonly BuildData profile;
            private readonly BuildRequest request;
            private readonly FakeFailureStage failureStage;

            public FakeOperations(
                BuildData profile,
                BuildRequest request,
                FakeFailureStage failureStage)
            {
                this.profile = profile;
                this.request = request;
                this.failureStage = failureStage;
            }

            public bool RecoverOnly { get; set; }
            public Exception RecoveryFailure { get; set; }
            public Exception BuildFailure { get; set; }
            public bool ReturnMismatchedResultPath { get; set; }
            public FakeManifestMode ManifestMode { get; set; }
            public BuildCommandLineOptions ParsedCommandLineOptions { get; set; }
            public BuildCommandLineOptions RequestCommandLineOptions { get; private set; }
            public string ResolvedProfilePath { get; private set; }

            public BuildCommandLineOptions ParseCommandLine(string[] arguments)
            {
                ThrowIf(FakeFailureStage.Parse);
                return ParsedCommandLineOptions
                    ?? new BuildCommandLineOptions { RecoverOnly = RecoverOnly };
            }

            public BuildData ResolveCommandLineProfile(string profilePath)
            {
                ThrowIf(FakeFailureStage.Profile);
                ResolvedProfilePath = profilePath;
                return profile;
            }

            public BuildRequest CreateCommandLineRequest(
                BuildData resolvedProfile,
                BuildCommandLineOptions options)
            {
                ThrowIf(FakeFailureStage.Factory);
                RequestCommandLineOptions = options;
                return request;
            }

            public BuildRequest CreateInteractiveRequest(
                BuildData resolvedProfile,
                BuildTarget target,
                bool debug,
                bool exportAndroidProject,
                IReadOnlyList<string> invocationIdsOverride)
            {
                ThrowIf(FakeFailureStage.Factory);
                return request;
            }

            public BuildRunResult RunBuild(
                BuildRequest buildRequest,
                string runId,
                string requiredResultManifestPath,
                IBuildEventSink eventSink)
            {
                ThrowIf(FakeFailureStage.Runner);
                string resultPath = ReturnMismatchedResultPath
                    ? requiredResultManifestPath + ".mismatch"
                    : requiredResultManifestPath;
                IReadOnlyList<BuildStepResult> steps =
                    ManifestMode == FakeManifestMode.WrongStepResult
                        ? new[]
                        {
                            new BuildStepResult(
                                "player",
                                BuildStepTypeIds.Player,
                                BuildStepStatus.Succeeded,
                                TimeSpan.FromMilliseconds(250),
                                "step-completed")
                        }
                        : Array.Empty<BuildStepResult>();
                IReadOnlyList<Exception> nonFatalFailures =
                    ManifestMode == FakeManifestMode.WrongNonFatalFailure
                        ? new Exception[]
                        {
                            new InvalidOperationException("observer-failed")
                        }
                        : Array.Empty<Exception>();
                var result = new BuildRunResult(
                    runId,
                    BuildFailure == null,
                    buildRequest.OutputPath,
                    resultPath,
                    steps,
                    BuildFailure,
                    nonFatalFailures);
                if (!ReturnMismatchedResultPath)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                    WriteManifest(buildRequest, eventSink, result);
                }

                return result;
            }

            private void WriteManifest(
                BuildRequest buildRequest,
                IBuildEventSink eventSink,
                BuildRunResult result)
            {
                var context = new BuildExecutionContext(
                    buildRequest,
                    result.RunId,
                    eventSink);
                if (ManifestMode == FakeManifestMode.WrongContentResult)
                {
                    context.AddContentResult(
                        "player",
                        AssetContentBuildResult.Success(
                            "TestProvider",
                            "BasePackage",
                            "1.0.0",
                            producedArtifacts: new[] { "artifact.bin" },
                            warnings: new[] { "provider-warning" }));
                }

                if (ManifestMode == FakeManifestMode.Incomplete)
                {
                    File.WriteAllText(
                        result.ResultManifestPath,
                        "{\"formatVersion\":1,\"runId\":\"" + result.RunId + "\"}");
                    eventSink.RunFinished(context, result);
                    return;
                }

                BuildRunResult manifestResult = ManifestMode == FakeManifestMode.WrongOutcome
                    ? new BuildRunResult(
                        result.RunId,
                        !result.Succeeded,
                        result.OutputPath,
                        result.ResultManifestPath,
                        result.Steps,
                        result.Failure,
                        result.NonFatalFailures)
                    : result;
                BuildResultManifestWriter.Write(context, manifestResult);
                eventSink.RunFinished(context, result);

                if (ManifestMode == FakeManifestMode.Full
                    || ManifestMode == FakeManifestMode.WrongOutcome)
                {
                    return;
                }

                string json = File.ReadAllText(result.ResultManifestPath);
                if (ManifestMode == FakeManifestMode.WrongFormat)
                {
                    json = ReplaceRequired(
                        json,
                        "\"formatVersion\": 1",
                        "\"formatVersion\": 2");
                }
                else if (ManifestMode == FakeManifestMode.Partial)
                {
                    json = ReplaceRequired(
                        json,
                        "\"partial\": false",
                        "\"partial\": true");
                }
                else if (ManifestMode == FakeManifestMode.MissingRequestField)
                {
                    string original = json;
                    json = json.Replace(
                        "  \"debugBuild\": false,\r\n",
                        string.Empty);
                    json = json.Replace(
                        "  \"debugBuild\": false,\n",
                        string.Empty);
                    if (string.Equals(original, json, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Required test manifest field was not found: 'debugBuild'.");
                    }
                }
                else if (ManifestMode == FakeManifestMode.WrongRecipeProvenance)
                {
                    json = ReplaceRequired(
                        json,
                        "\"invocationId\": \"player\"",
                        "\"invocationId\": \"tampered-player\"");
                }
                else if (ManifestMode == FakeManifestMode.WrongStepResult)
                {
                    json = ReplaceRequired(json, "step-completed", "tampered-step");
                }
                else if (ManifestMode == FakeManifestMode.WrongContentResult)
                {
                    json = ReplaceRequired(json, "TestProvider", "TamperedProvider");
                }
                else if (ManifestMode == FakeManifestMode.WrongNonFatalFailure)
                {
                    json = ReplaceRequired(json, "observer-failed", "tampered-observer");
                }

                File.WriteAllText(result.ResultManifestPath, json);
            }

            public void RecoverWorkspace()
            {
                if (failureStage == FakeFailureStage.Recovery)
                {
                    throw RecoveryFailure ?? new InvalidOperationException("recovery failed");
                }
            }

            private void ThrowIf(FakeFailureStage expected)
            {
                if (failureStage == expected)
                {
                    throw new InvalidOperationException(expected + " failed");
                }
            }
        }

        private static string ReplaceRequired(string value, string oldValue, string newValue)
        {
            string replaced = value.Replace(oldValue, newValue);
            if (string.Equals(value, replaced, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Required test manifest token was not found: '{oldValue}'.");
            }

            return replaced;
        }

        private sealed class NoOpEventSink : IBuildEventSink
        {
            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan)
            {
            }

            public void StepStarted(
                BuildExecutionContext context,
                CompiledBuildStep step)
            {
            }

            public void StepFinished(
                BuildExecutionContext context,
                BuildStepResult result)
            {
            }

            public void RunFinished(
                BuildExecutionContext context,
                BuildRunResult result)
            {
            }
        }

        private sealed class ThrowingEventSink : IBuildEventSink
        {
            private readonly Exception failure;

            public ThrowingEventSink(Exception failure)
            {
                this.failure = failure;
            }

            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan)
            {
                throw failure;
            }

            public void StepStarted(
                BuildExecutionContext context,
                CompiledBuildStep step)
            {
                throw failure;
            }

            public void StepFinished(
                BuildExecutionContext context,
                BuildStepResult result)
            {
                throw failure;
            }

            public void RunFinished(
                BuildExecutionContext context,
                BuildRunResult result)
            {
                throw failure;
            }
        }

        private sealed class RecordingEventSink : IBuildEventSink
        {
            public int RunFinishedCount { get; private set; }

            public void RunStarted(
                BuildExecutionContext context,
                IReadOnlyList<CompiledBuildStep> plan)
            {
            }

            public void StepStarted(
                BuildExecutionContext context,
                CompiledBuildStep step)
            {
            }

            public void StepFinished(
                BuildExecutionContext context,
                BuildStepResult result)
            {
            }

            public void RunFinished(
                BuildExecutionContext context,
                BuildRunResult result)
            {
                RunFinishedCount++;
            }
        }
    }
}
