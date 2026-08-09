using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CycloneGames.DataTable.Unity.Editor;
using CycloneGames.DataTable.Unity.Editor.Logging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.DataTable.Tests.Editor.Tools.Luban
{
    public sealed class DataTableLubanRunnerTests
    {
        [SetUp]
        public void SetUp()
        {
            DataTableLubanRunner.ResetLifecycleShutdownForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DataTableLubanRunner.ResetLifecycleShutdownForTests();
        }

        [Test]
        public void Profile_InvalidToolProject_FailsDuringConstruction()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string configuration = Path.Combine(repositoryRoot, "DataTable", "Luban", "build_config.ini");
            Assert.Throws<FileNotFoundException>(() => new DataTableLubanProfile(
                Path.Combine(repositoryRoot, "missing.csproj"),
                configuration,
                "client",
                1000,
                false,
                4096));
        }

        [Test]
        public void Recover_InvalidRunId_FailsBeforeExecution()
        {
            DataTableLubanProfile profile = CreateValidProfile();
            Assert.Throws<ArgumentException>(() => DataTableLubanCommand.Recover(profile, "unsafe"));
        }

        [Test]
        public void ProcessStartInfo_IsNonInteractiveAndDeterministic()
        {
            ProcessStartInfo startInfo = DataTableLubanRunner.CreateStartInfo(
                DataTableLubanCommand.Check(CreateValidProfile()));

            Assert.AreEqual("dotnet", startInfo.FileName);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.IsTrue(startInfo.CreateNoWindow);
            Assert.IsTrue(startInfo.RedirectStandardOutput);
            Assert.IsTrue(startInfo.RedirectStandardError);
            Assert.AreEqual("1", startInfo.EnvironmentVariables["DOTNET_NOLOGO"]);
            Assert.AreEqual(
                "1",
                startInfo.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"]);
            Assert.AreEqual(
                "1",
                startInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"]);
        }

        [Test]
        public void AssetRefreshPolicy_RefreshesGenerateButNeverCheck()
        {
            DataTableLubanProfile source = CreateValidProfile();
            var refreshProfile = new DataTableLubanProfile(
                source.ToolProjectPath,
                source.BuildConfigurationPath,
                source.ProfileName,
                source.TimeoutMilliseconds,
                refreshAssetsAfterSuccess: true,
                source.MaximumCapturedOutputCharacters);
            var success = new DataTableLubanRunResult(
                true,
                false,
                false,
                false,
                false,
                0,
                1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

            Assert.IsTrue(DataTableLubanRunner.ShouldRefreshAssets(
                DataTableLubanCommand.Generate(refreshProfile),
                success));
            Assert.IsFalse(DataTableLubanRunner.ShouldRefreshAssets(
                DataTableLubanCommand.Check(refreshProfile),
                success));
            Assert.IsTrue(DataTableLubanRunner.ShouldRefreshAssets(
                DataTableLubanCommand.Recover(
                    refreshProfile,
                    "0123456789abcdef0123456789abcdef"),
                success));
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void RunResult_TerminalFailureCannotAlsoSucceedOrRefreshAssets(
            bool cancelled,
            bool timedOut,
            bool recoveryRequired)
        {
            DataTableLubanProfile source = CreateValidProfile();
            var refreshProfile = new DataTableLubanProfile(
                source.ToolProjectPath,
                source.BuildConfigurationPath,
                source.ProfileName,
                source.TimeoutMilliseconds,
                refreshAssetsAfterSuccess: true,
                source.MaximumCapturedOutputCharacters);
            var terminalFailure = new DataTableLubanRunResult(
                success: true,
                cancelled,
                timedOut,
                recoveryRequired,
                outputTruncated: false,
                exitCode: 0,
                durationMilliseconds: 1,
                standardOutput: string.Empty,
                standardError: string.Empty,
                recoveryRunId: string.Empty,
                errorMessage: "Pipeline was cancelled.");

            Assert.IsFalse(terminalFailure.Success);
            Assert.AreEqual(cancelled, terminalFailure.Cancelled);
            Assert.AreEqual(timedOut, terminalFailure.TimedOut);
            Assert.AreEqual(recoveryRequired, terminalFailure.RecoveryRequired);
            Assert.IsFalse(DataTableLubanRunner.ShouldRefreshAssets(
                DataTableLubanCommand.Generate(refreshProfile),
                terminalFailure));
        }

        [Test]
        public void CapturedOutputBudget_SingleStreamCanUseEntireBudget()
        {
            var budget = new DataTableLubanRunner.CapturedOutputBudget(4096);
            var output = new DataTableLubanRunner.BoundedTextBuffer(budget);
            output.Append(new string('x', 4096).ToCharArray(), 4096);

            Assert.AreEqual(4096, output.Length);
            Assert.AreEqual(4096, budget.AcceptedCharacters);
            Assert.IsFalse(budget.WasTruncated);
        }

        [Test]
        public void CapturedOutputBudget_ConcurrentStreamsNeverExceedCombinedLimit()
        {
            var budget = new DataTableLubanRunner.CapturedOutputBudget(4096);
            var output = new DataTableLubanRunner.BoundedTextBuffer(budget);
            var error = new DataTableLubanRunner.BoundedTextBuffer(budget);
            char[] characters = new string('x', 4096).ToCharArray();
            var outputThread = new Thread(() => output.Append(characters, characters.Length));
            var errorThread = new Thread(() => error.Append(characters, characters.Length));

            outputThread.Start();
            errorThread.Start();
            Assert.IsTrue(outputThread.Join(2000));
            Assert.IsTrue(errorThread.Join(2000));
            Assert.AreEqual(4096, output.Length + error.Length);
            Assert.AreEqual(4096, budget.AcceptedCharacters);
            Assert.IsTrue(budget.WasTruncated);
        }

        [Test]
        public void CancelActiveRun_DiagnosticsReentryDoesNotHoldActiveProcessLock()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string temporaryRoot = Path.Combine(
                repositoryRoot,
                "UnityStarter",
                "Temp",
                "DataTableLubanRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            string configuration = Path.Combine(temporaryRoot, "build_config.ini");
            File.WriteAllText(configuration, "# test-only profile path\n");
            var profile = new DataTableLubanProfile(
                Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable",
                    "Tools~", "CodeGen", "CycloneGames.DataTable.CodeGen.csproj"),
                configuration,
                "client",
                1000,
                false,
                4096);
            Directory.CreateDirectory(profile.WriterLockDirectory);
            Directory.CreateDirectory(Path.Combine(profile.WriterLockDirectory, "cancel.request"));

            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            var diagnostics = new ReentrantCancellationDiagnostics();
            Assert.IsTrue(DataTableDiagnostics.TryReplace(previous, diagnostics));
            using var process = new Process();
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(process, profile));
                var worker = new Thread(() => DataTableLubanRunner.CancelActiveRun());
                worker.Start();
                Assert.IsTrue(worker.Join(3000), "Cancellation call did not complete.");
                DataTableEditorDiagnostics.DrainPendingOnMainThread();
                Assert.IsTrue(diagnostics.ReentryCompleted, "Diagnostic reentry was blocked by ActiveProcessSync.");
                Assert.IsTrue(diagnostics.ReentryResult);
                Assert.AreEqual(mainThreadId, diagnostics.WriteExceptionThreadId);
            }
            finally
            {
                DataTableLubanRunner.ClearActiveProcess(process);
                DataTableDiagnostics.TryReplace(diagnostics, previous);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_PreCancelled_ReturnsStructuredCancellationOnMainThread()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            DataTableLubanProfile profile = CreateValidProfile();
            DataTableLubanCommand command = DataTableLubanCommand.Check(profile);
            DataTableLubanRunResult result = default;
            int completionThreadId = -1;
            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            var diagnostics = new RecordingDiagnostics();
            Assert.IsTrue(DataTableDiagnostics.TryReplace(previous, diagnostics));

            try
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    yield return DataTableLubanRunner.ExecuteAsync(command, cancellation.Token).ToCoroutine(value =>
                    {
                        result = value;
                        completionThreadId = Thread.CurrentThread.ManagedThreadId;
                    });
                }
            }
            finally
            {
                DataTableDiagnostics.TryReplace(diagnostics, previous);
            }

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Cancelled);
            Assert.IsFalse(result.RecoveryRequired);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.AreEqual(mainThreadId, completionThreadId);
            Assert.IsFalse(DataTableLubanRunner.IsRunning);
            DataTableLubanRunnerState state = DataTableLubanRunner.CurrentState;
            Assert.AreEqual(DataTableLubanRunnerPhase.Cancelled, state.Phase);
            Assert.IsFalse(state.IsActive);
            Assert.IsTrue(state.HasLastResult);
            Assert.IsTrue(state.LastResult.Cancelled);
            Assert.AreEqual(DataTableLubanOperation.Check, state.Operation);
            Assert.AreEqual(profile.ProfileName, state.ProfileName);
            Assert.AreEqual(profile.BuildConfigurationPath, state.BuildConfigurationPath);
            Assert.IsTrue(diagnostics.Messages.Exists(message =>
                message.Contains("Operation=Check") &&
                message.Contains("Profile=client") &&
                message.Contains("Config=")));
        }

        [Test]
        public void ActiveProcessState_CancellationAndClearRemainObservable()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string temporaryRoot = Path.Combine(
                repositoryRoot,
                "UnityStarter",
                "Temp",
                "DataTableLubanRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            string configuration = Path.Combine(temporaryRoot, "build_config.ini");
            File.WriteAllText(configuration, "# test-only profile path\n");
            var profile = new DataTableLubanProfile(
                Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable",
                    "Tools~", "CodeGen", "CycloneGames.DataTable.CodeGen.csproj"),
                configuration,
                "client",
                1000,
                false,
                4096);
            using var process = new Process();
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(process, profile));
                DataTableLubanRunnerState running = DataTableLubanRunner.CurrentState;
                Assert.IsTrue(running.IsActive);
                Assert.IsTrue(running.CanCancel);
                Assert.AreEqual(DataTableLubanRunnerPhase.Running, running.Phase);

                Assert.IsTrue(DataTableLubanRunner.CancelActiveRun());
                DataTableLubanRunnerState cancelling = DataTableLubanRunner.CurrentState;
                Assert.Greater(cancelling.Revision, running.Revision);
                Assert.AreEqual(
                    DataTableLubanRunnerPhase.CancellationRequested,
                    cancelling.Phase);
                Assert.IsFalse(cancelling.CanCancel);
                Assert.IsTrue(DataTableLubanRunner.CancelActiveRun());
                Assert.AreEqual(
                    cancelling.Revision,
                    DataTableLubanRunner.CurrentState.Revision);
            }
            finally
            {
                DataTableLubanRunner.ClearActiveProcess(process);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }

            Assert.AreEqual(
                DataTableLubanRunnerPhase.Idle,
                DataTableLubanRunner.CurrentState.Phase);
        }

        [Test]
        public void ProcessStartGate_ShutdownBetweenAttachAndStartRejectsLateStart()
        {
            DataTableLubanProfile profile = CreateTemporaryProfile(out string temporaryRoot);
            using Process process = CreateLifecycleTestProcess(
                temporaryRoot,
                keepRunning: false,
                out string startedMarker);
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(
                    process,
                    profile,
                    DataTableLubanRunnerPhase.StartingProcess));

                DataTableLubanRunner.RequestCancellationForShutdown();

                Assert.IsFalse(DataTableLubanRunner.TryStartAttachedProcess(process));
                Assert.IsFalse(File.Exists(startedMarker));
            }
            finally
            {
                DataTableLubanRunner.ClearActiveProcess(process);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }

            Assert.IsFalse(DataTableLubanRunner.TryBeginRun(
                DataTableLubanCommand.Check(profile),
                out _),
                "Clearing the completed owner must not reopen a lifecycle shutdown gate.");
        }


        [Test]
        public void LifecycleShutdown_WithoutActiveRunRejectsFutureRunReservation()
        {
            DataTableLubanProfile profile = CreateValidProfile();

            DataTableLubanRunner.RequestCancellationForShutdown();

            Assert.IsFalse(DataTableLubanRunner.TryBeginRun(
                DataTableLubanCommand.Check(profile),
                out _));
        }

        [Test]
        public void ProcessStartGate_NormalAttachedProcessCanStart()
        {
            DataTableLubanProfile profile = CreateTemporaryProfile(out string temporaryRoot);
            using Process process = CreateLifecycleTestProcess(
                temporaryRoot,
                keepRunning: false,
                out string startedMarker);
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(
                    process,
                    profile,
                    DataTableLubanRunnerPhase.StartingProcess));

                Assert.IsTrue(DataTableLubanRunner.TryStartAttachedProcess(process));
                Assert.IsTrue(process.WaitForExit(5000), "The lifecycle test process did not exit.");
                Assert.IsTrue(File.Exists(startedMarker));
            }
            finally
            {
                DataTableLubanRunner.ClearActiveProcess(process);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }
        }

        [Test]
        public void ProcessStartGate_ShutdownTerminatesStartedOwner()
        {
            DataTableLubanProfile profile = CreateTemporaryProfile(out string temporaryRoot);
            using Process process = CreateLifecycleTestProcess(
                temporaryRoot,
                keepRunning: true,
                out string startedMarker);
            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            var diagnostics = new RecordingDiagnostics();
            Assert.IsTrue(DataTableDiagnostics.TryReplace(previous, diagnostics));
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(
                    process,
                    profile,
                    DataTableLubanRunnerPhase.StartingProcess));
                Assert.IsTrue(DataTableLubanRunner.TryStartAttachedProcess(process));
                Assert.IsTrue(
                    SpinWait.SpinUntil(() => File.Exists(startedMarker), 3000),
                    "The lifecycle test process did not report that it started.");

                DataTableLubanRunner.RequestCancellationForShutdown();
                DataTableEditorDiagnostics.DrainPendingOnMainThread();

                Assert.IsTrue(process.WaitForExit(3000), "Shutdown left the child process running.");
                Assert.IsTrue(diagnostics.Messages.Exists(message =>
                    message.Contains("shutdown or assembly reload terminated")));
            }
            finally
            {
                if (!process.HasExited)
                {
                    DataTableLubanRunner.TryTerminateProcessTree(process, 3000, out _);
                }

                DataTableLubanRunner.ClearActiveProcess(process);
                DataTableDiagnostics.TryReplace(diagnostics, previous);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }
        }

        [Test]
        public void CompletingPhase_RejectsUserCancellationButShutdownStillTerminatesOwner()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string temporaryRoot = Path.Combine(
                repositoryRoot,
                "UnityStarter",
                "Temp",
                "DataTableLubanRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            string configuration = Path.Combine(temporaryRoot, "build_config.ini");
            File.WriteAllText(configuration, "# test-only profile path\n");
            var profile = new DataTableLubanProfile(
                Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable",
                    "Tools~", "CodeGen", "CycloneGames.DataTable.CodeGen.csproj"),
                configuration,
                "client",
                1000,
                false,
                4096);
            Directory.CreateDirectory(profile.WriterLockDirectory);
            string cancellationRequest = Path.Combine(
                profile.WriterLockDirectory,
                "cancel.request");
            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            var diagnostics = new RecordingDiagnostics();
            Assert.IsTrue(DataTableDiagnostics.TryReplace(previous, diagnostics));
            using var process = new Process();
            try
            {
                Assert.IsTrue(DataTableLubanRunner.TrySetActiveProcess(
                    process,
                    profile,
                    DataTableLubanRunnerPhase.Completing));
                DataTableLubanRunnerState before = DataTableLubanRunner.CurrentState;
                Assert.IsFalse(before.CanCancel);

                bool accepted = true;
                var worker = new Thread(() => accepted = DataTableLubanRunner.CancelActiveRun());
                worker.Start();
                Assert.IsTrue(worker.Join(3000), "Cancellation call did not complete.");

                Assert.IsFalse(accepted);
                Assert.AreEqual(before.Revision, DataTableLubanRunner.CurrentState.Revision);
                Assert.AreEqual(
                    DataTableLubanRunnerPhase.Completing,
                    DataTableLubanRunner.CurrentState.Phase);
                Assert.IsFalse(File.Exists(cancellationRequest));

                DataTableLubanRunner.RequestCancellationForShutdown();
                DataTableEditorDiagnostics.DrainPendingOnMainThread();

                Assert.AreEqual(before.Revision, DataTableLubanRunner.CurrentState.Revision);
                Assert.AreEqual(
                    DataTableLubanRunnerPhase.Completing,
                    DataTableLubanRunner.CurrentState.Phase);
                Assert.IsFalse(File.Exists(cancellationRequest));
                Assert.IsTrue(diagnostics.Messages.Exists(message =>
                    message.Contains("shutdown or assembly reload terminated")));
            }
            finally
            {
                DataTableLubanRunner.ClearActiveProcess(process);
                DataTableDiagnostics.TryReplace(diagnostics, previous);
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }
        }

        [Test]
        public void TerminateProcessTree_UnstartedProcessHasNoChildTree()
        {
            using var process = new Process();
            Assert.IsTrue(DataTableLubanRunner.TryTerminateProcessTree(
                process,
                1000,
                out string error));
            Assert.IsEmpty(error);
        }

        [Test]
        public void ProcessExit_UnconfirmedReaderTerminationRequiresRecoveryEvenAfterExitZero()
        {
            Assert.IsTrue(DataTableLubanRunner.RequiresRecoveryAfterProcessExit(
                exitCode: 0,
                terminationUnconfirmed: true,
                writerLockExists: false));
        }

        [Test]
        public void LoggingBootstrap_DoesNotReplaceOrReleaseExternallyOwnedDiagnostics()
        {
            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            var external = new RecordingDiagnostics();
            Assert.IsTrue(DataTableDiagnostics.TryReplace(previous, external));
            try
            {
                DataTableLoggingEditorBootstrap.InstallIfAvailable();
                Assert.AreSame(external, DataTableDiagnostics.Current);

                DataTableLoggingEditorBootstrap.ReleaseOwnedDiagnostics();
                Assert.AreSame(external, DataTableDiagnostics.Current);
            }
            finally
            {
                Assert.IsTrue(DataTableDiagnostics.TryReplace(external, previous));
            }
        }

        [Test]
        public void LoggingBootstrap_InstallsAndReleasesItsOwnedAdapter()
        {
            IDataTableDiagnostics previous = DataTableDiagnostics.Current;
            Assert.IsTrue(DataTableDiagnostics.TryReplace(
                previous,
                NullDataTableDiagnostics.Instance));
            try
            {
                DataTableLoggingEditorBootstrap.InstallIfAvailable();
                IDataTableDiagnostics installed = DataTableDiagnostics.Current;
                Assert.IsNotNull(installed);
                Assert.AreNotSame(NullDataTableDiagnostics.Instance, installed);

                DataTableLoggingEditorBootstrap.ReleaseOwnedDiagnostics();
                Assert.AreSame(
                    NullDataTableDiagnostics.Instance,
                    DataTableDiagnostics.Current);
            }
            finally
            {
                Assert.IsTrue(DataTableDiagnostics.TryReplace(
                    NullDataTableDiagnostics.Instance,
                    previous));
            }
        }

        private static DataTableLubanProfile CreateValidProfile()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            return new DataTableLubanProfile(
                Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable",
                    "Tools~", "CodeGen", "CycloneGames.DataTable.CodeGen.csproj"),
                Path.Combine(repositoryRoot, "DataTable", "Luban", "build_config.ini"),
                "client",
                1000,
                false,
                4096);
        }

        private static DataTableLubanProfile CreateTemporaryProfile(out string temporaryRoot)
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            temporaryRoot = Path.Combine(
                repositoryRoot,
                "UnityStarter",
                "Temp",
                "DataTableLubanRunnerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            string configuration = Path.Combine(temporaryRoot, "build_config.ini");
            File.WriteAllText(configuration, "# test-only profile path\n");
            return new DataTableLubanProfile(
                Path.Combine(
                    repositoryRoot,
                    "UnityStarter", "Assets", "ThirdParty", "CycloneGames", "CycloneGames.DataTable",
                    "Tools~", "CodeGen", "CycloneGames.DataTable.CodeGen.csproj"),
                configuration,
                "client",
                1000,
                false,
                4096);
        }

        private static Process CreateLifecycleTestProcess(
            string temporaryRoot,
            bool keepRunning,
            out string startedMarker)
        {
            startedMarker = Path.Combine(temporaryRoot, "started.marker");
            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            string scriptPath = Path.Combine(
                temporaryRoot,
                isWindows ? "lifecycle-test.cmd" : "lifecycle-test.sh");
            string script = isWindows
                ? "@echo off\r\n" +
                  "> \"" + startedMarker + "\" echo started\r\n" +
                  (keepRunning ? "ping 127.0.0.1 -n 31 > nul\r\n" : string.Empty)
                : "#!/bin/sh\n" +
                  "printf started > '" + startedMarker.Replace("'", "'\\''") + "'\n" +
                  (keepRunning ? "sleep 30\n" : string.Empty);
            File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = isWindows
                    ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                    : "/bin/sh",
                Arguments = isWindows
                    ? "/d /c \"\"" + scriptPath + "\"\""
                    : "\"" + scriptPath.Replace("\"", "\\\"") + "\"",
                WorkingDirectory = temporaryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return new Process { StartInfo = startInfo };
        }

        private sealed class ReentrantCancellationDiagnostics : IDataTableDiagnostics
        {
            private int _reentered;

            public bool ReentryCompleted { get; private set; }
            public bool ReentryResult { get; private set; }
            public int WriteExceptionThreadId { get; private set; }

            public bool IsEnabled(DataTableDiagnosticLevel level, string category)
            {
                return true;
            }

            public void Write(
                DataTableDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void WriteException(
                DataTableDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                WriteExceptionThreadId = Thread.CurrentThread.ManagedThreadId;
                if (Interlocked.Exchange(ref _reentered, 1) != 0)
                {
                    return;
                }

                var thread = new Thread(() => ReentryResult = DataTableLubanRunner.CancelActiveRun());
                thread.Start();
                ReentryCompleted = thread.Join(1000);
            }
        }

        private sealed class RecordingDiagnostics : IDataTableDiagnostics
        {
            public List<string> Messages { get; } = new List<string>();

            public bool IsEnabled(DataTableDiagnosticLevel level, string category)
            {
                return true;
            }

            public void Write(
                DataTableDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                Messages.Add(message ?? string.Empty);
            }

            public void WriteException(
                DataTableDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                Messages.Add(message ?? string.Empty);
            }
        }
    }
}
