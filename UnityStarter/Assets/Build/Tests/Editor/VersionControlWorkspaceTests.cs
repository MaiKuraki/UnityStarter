using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Build.Pipeline.Editor;
using Build.VersionControl.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class VersionControlWorkspaceTests
    {
        private string sandboxRoot;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-VersionControlTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(sandboxRoot, ".git"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, true);
            }
        }

        [Test]
        public void GitPorcelainV2Parser_ReportsOnlyCountsAndComponentStates()
        {
            string output =
                "1 M. N... 100644 100644 100644 abc def Assets/secret.txt\0"
                + "? credentials/private.key\0"
                + "1 .M S.M. 160000 160000 160000 abc def Modules/vendor\0"
                + "2 R. N... 100644 100644 100644 abc def R100 Assets/new-name.txt\0"
                + "Assets/old-name.txt\0";

            bool parsed = VersionControlProviderGit.TryParseStatus(
                output,
                out VersionControlWorkspaceComponentEvidence tracked,
                out VersionControlWorkspaceComponentEvidence untracked,
                out VersionControlWorkspaceComponentEvidence submodules);

            Assert.That(parsed, Is.True);
            Assert.That(tracked.Status, Is.EqualTo(VersionControlWorkspaceComponentStatus.Dirty));
            Assert.That(tracked.ChangeCount, Is.EqualTo(3));
            Assert.That(untracked.ChangeCount, Is.EqualTo(1));
            Assert.That(submodules.ChangeCount, Is.EqualTo(1));
        }

        [Test]
        public void GitPorcelainV2Parser_InvalidSubmoduleFieldFailsClosed()
        {
            const string Output =
                "1 M. X... 100644 100644 100644 abc def Assets/changed.txt\0";

            bool parsed = VersionControlProviderGit.TryParseStatus(
                Output,
                out _,
                out _,
                out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void GitCapture_SubmoduleReportedByBothCommandsIsCountedOnce()
        {
            const string Status =
                "1 .M S.M. 160000 160000 160000 abc def Modules/vendor\0";
            const string SubmoduleStatus =
                "+0123456789abcdef0123456789abcdef01234567 Modules/vendor (heads/main)\n";
            var runner = new FakeGitRunner(
                workspaceFailureCode: null,
                statusOutput: Status,
                submoduleOutput: SubmoduleStatus);
            var provider = new VersionControlProviderGit(sandboxRoot, runner);

            VersionControlMetadata metadata = provider.Capture();

            Assert.That(
                metadata.Workspace.Submodules.Status,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Dirty));
            Assert.That(metadata.Workspace.Submodules.ChangeCount, Is.EqualTo(1));
        }

        [Test]
        public void GitCapture_CleanLfsStatusDoesNotRequirePathInventory()
        {
            var runner = new FakeGitRunner(
                workspaceFailureCode: null,
                statusOutput: string.Empty,
                submoduleOutput: string.Empty,
                lfsInventoryFailureCode:
                    VersionControlWorkspaceEvidence.OutputLimitExceeded,
                lfsStatusOutput: "{\"files\":{}}");
            var provider = new VersionControlProviderGit(sandboxRoot, runner);

            VersionControlMetadata metadata = provider.Capture();

            Assert.That(
                metadata.Workspace.GitLfs.Status,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Clean));
            Assert.That(metadata.Workspace.IsVerifiedClean, Is.True);
        }

        [Test]
        public void GitCapture_MalformedLfsJsonFailsClosed()
        {
            var runner = new FakeGitRunner(
                workspaceFailureCode: null,
                statusOutput: string.Empty,
                submoduleOutput: string.Empty,
                lfsInventoryOutput: "tracked.bin\n",
                lfsStatusOutput: "garbage {\"files\":{}}");
            var provider = new VersionControlProviderGit(sandboxRoot, runner);

            VersionControlMetadata metadata = provider.Capture();

            Assert.That(
                metadata.Workspace.GitLfs.Status,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Unknown));
            Assert.That(
                metadata.Workspace.FailureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.MalformedOutput));
        }

        [TestCase(VersionControlWorkspaceEvidence.CommandTimedOut)]
        [TestCase(VersionControlWorkspaceEvidence.ExecutableUnavailable)]
        [TestCase(VersionControlWorkspaceEvidence.OutputLimitExceeded)]
        [TestCase(VersionControlWorkspaceEvidence.CommandFailed)]
        public void GitCapture_WhenWorkspaceCommandCannotBeEstablished_ReturnsUnknownEvidence(
            string failureCode)
        {
            var runner = new FakeGitRunner(failureCode);
            var provider = new VersionControlProviderGit(sandboxRoot, runner);

            VersionControlMetadata metadata = provider.Capture();

            Assert.That(metadata.Workspace.IsVerifiedClean, Is.False);
            Assert.That(
                metadata.Workspace.OverallStatus,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Unknown));
            Assert.That(metadata.Workspace.FailureCode, Is.EqualTo(failureCode));
        }

        [Test]
        public void GitWorkspaceCapability_CapturesWithoutMetadataCommands()
        {
            var runner = new WorkspaceOnlyGitRunner(
                firstStatus: string.Empty,
                secondStatus: string.Empty);
            IVersionControlProvider provider =
                new VersionControlProviderGit(sandboxRoot, runner);

            Assert.That(provider, Is.InstanceOf<IVersionControlWorkspaceProvider>());
            VersionControlWorkspaceEvidence workspace =
                ((IVersionControlWorkspaceProvider)provider).CaptureWorkspace(
                    CancellationToken.None);

            Assert.That(workspace.IsVerifiedClean, Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none",
                    "submodule status --recursive",
                    "lfs status --json",
                    "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none"
                },
                runner.Commands);
        }

        [Test]
        public void GitWorkspaceCapability_ChangingStatusFailsClosed()
        {
            var runner = new WorkspaceOnlyGitRunner(
                firstStatus: string.Empty,
                secondStatus: "? Assets/transient.asset\0");
            var provider = (IVersionControlWorkspaceProvider)
                new VersionControlProviderGit(sandboxRoot, runner);

            VersionControlWorkspaceEvidence workspace = provider.CaptureWorkspace(
                CancellationToken.None);

            Assert.That(
                workspace.OverallStatus,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Unknown));
            Assert.That(
                workspace.FailureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.IncoherentSnapshot));
        }

        [Test]
        public void GitWorkspaceCapability_PreCancelled_DoesNotStartCommand()
        {
            var runner = new WorkspaceOnlyGitRunner(
                firstStatus: string.Empty,
                secondStatus: string.Empty);
            var provider = (IVersionControlWorkspaceProvider)
                new VersionControlProviderGit(sandboxRoot, runner);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    provider.CaptureWorkspace(cancellation.Token));
            }

            Assert.That(runner.Commands, Is.Empty);
        }

        [Test]
        public void GitWorkspaceCapability_CommandFailureReturnsUnknownWithoutDiagnosticLeak()
        {
            var runner = new WorkspaceOnlyGitRunner(
                firstStatus: string.Empty,
                secondStatus: string.Empty,
                failOnStatusCall: 2,
                failureCode: VersionControlWorkspaceEvidence.CommandTimedOut,
                failureMessage: "workspace failure at C:\\sensitive\\project");
            var provider = (IVersionControlWorkspaceProvider)
                new VersionControlProviderGit(sandboxRoot, runner);
            VersionControlWorkspaceEvidence workspace = null;

            Assert.DoesNotThrow(() =>
                workspace = provider.CaptureWorkspace(CancellationToken.None));

            Assert.That(
                workspace.OverallStatus,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Unknown));
            Assert.That(
                workspace.FailureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.CommandTimedOut));
            StringAssert.DoesNotContain("sensitive", workspace.FailureCode);
        }

        [Test]
        public void WorkspaceEvidence_FailureCodePreventsVerifiedClean()
        {
            var clean = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Clean,
                0);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            var evidence = new VersionControlWorkspaceEvidence(
                clean,
                clean,
                notApplicable,
                notApplicable,
                VersionControlWorkspaceEvidence.CommandFailed);

            Assert.That(evidence.OverallStatus, Is.EqualTo(VersionControlWorkspaceComponentStatus.Clean));
            Assert.That(evidence.IsVerifiedClean, Is.False);
        }

        [Test]
        public void PublicContracts_ExposeOneExplicitCurrentConstructor()
        {
            Assert.That(typeof(BuildRequest).GetConstructors(), Has.Length.EqualTo(1));
            Assert.That(typeof(BuildVersionContext).GetConstructors(), Has.Length.EqualTo(1));
            Assert.That(typeof(VersionControlMetadata).GetConstructors(), Has.Length.EqualTo(1));
            Assert.That(
                typeof(BuildRequest).GetConstructors()[0].GetParameters()[24].ParameterType,
                Is.EqualTo(typeof(BuildPurpose)));
            Assert.That(
                typeof(BuildVersionContext).GetConstructors()[0].GetParameters()[8].ParameterType,
                Is.EqualTo(typeof(VersionControlWorkspaceEvidence)));
            Assert.That(
                typeof(VersionControlMetadata).GetConstructors()[0].GetParameters()[5].ParameterType,
                Is.EqualTo(typeof(VersionControlWorkspaceEvidence)));
        }

        [Test]
        public void PerforceTaggedReconcileParser_SeparatesTrackedAndUntrackedCounts()
        {
            string output =
                "... depotFile //depot/game/changed.cs\n"
                + "... action edit\n"
                + "... depotFile //depot/game/new.asset\n"
                + "... action add\n";

            bool parsed = VersionControlProviderPerforce.TryCountReconcileActions(
                output,
                out int tracked,
                out int untracked);

            Assert.That(parsed, Is.True);
            Assert.That(tracked, Is.EqualTo(1));
            Assert.That(untracked, Is.EqualTo(1));
        }

        [Test]
        public void PerforceTaggedOpenedParser_ErrorRecordFailsClosed()
        {
            const string Output =
                "... code error\n"
                + "... data Perforce password invalid or unset.\n"
                + "... severity 3\n"
                + "... generic 6\n";

            bool parsed = VersionControlProviderPerforce.TryCountTaggedField(
                Output,
                "depotFile",
                out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void PerforceTaggedReconcileParser_ErrorRecordFailsClosed()
        {
            const string Output =
                "... code error\n"
                + "... data Perforce password invalid or unset.\n"
                + "... severity 3\n"
                + "... generic 6\n";

            bool parsed = VersionControlProviderPerforce.TryCountReconcileActions(
                Output,
                out _,
                out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void PerforceTaggedStatusParser_NonEmptyUnknownSchemaFailsClosed()
        {
            const string Output =
                "... depotFile //depot/game/changed.cs\n"
                + "... unexpectedState modified\n";

            bool parsed = VersionControlProviderPerforce.TryCountReconcileActions(
                Output,
                out _,
                out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void PerforceCapture_WorkspaceChangesBetweenSnapshotsFailsClosed()
        {
            var runner = new ChangingPerforceRunner();
            var provider = new VersionControlProviderPerforce(sandboxRoot, runner);

            VersionControlMetadata metadata = provider.Capture();

            Assert.That(
                metadata.Workspace.OverallStatus,
                Is.EqualTo(VersionControlWorkspaceComponentStatus.Unknown));
            Assert.That(
                metadata.Workspace.FailureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.IncoherentSnapshot));
            Assert.That(runner.AllowedExitCodeOne, Is.False);
        }

        [Test]
        public void PerforceWorkspaceCapability_CapturesWithoutMetadataCommands()
        {
            var runner = new WorkspaceOnlyPerforceRunner();
            IVersionControlProvider provider =
                new VersionControlProviderPerforce(sandboxRoot, runner);

            Assert.That(provider, Is.InstanceOf<IVersionControlWorkspaceProvider>());
            VersionControlWorkspaceEvidence workspace =
                ((IVersionControlWorkspaceProvider)provider).CaptureWorkspace(
                    CancellationToken.None);

            Assert.That(workspace.IsVerifiedClean, Is.True);
            CollectionAssert.AreEqual(
                new[] { "-ztag status", "-ztag status" },
                runner.Commands);
        }

        [Test]
        public void PerforceWorkspaceCapability_PreCancelled_DoesNotStartCommand()
        {
            var runner = new WorkspaceOnlyPerforceRunner();
            var provider = (IVersionControlWorkspaceProvider)
                new VersionControlProviderPerforce(sandboxRoot, runner);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    provider.CaptureWorkspace(cancellation.Token));
            }

            Assert.That(runner.Commands, Is.Empty);
        }

        [Test]
        public void CommandRunner_MissingExecutableProducesStableFailureWithoutArguments()
        {
            var runner = new VersionControlCommandRunner();

            VersionControlCommandException exception =
                Assert.Throws<VersionControlCommandException>(
                    () => runner.Run(
                        "definitely-not-a-real-vcs-executable",
                        "--password secret-value",
                        sandboxRoot,
                        environment: null,
                        timeoutMilliseconds: 1000,
                        maximumOutputCharacters: 1024));

            Assert.That(
                exception.FailureCode,
                Is.EqualTo(VersionControlWorkspaceEvidence.ExecutableUnavailable));
            StringAssert.DoesNotContain("secret-value", exception.Message);
            StringAssert.DoesNotContain("--password", exception.Message);
        }

        [Test]
        public void CommandRunner_ReaderWaitUsesBoundedDeadline()
        {
            var output = new TaskCompletionSource<string>();
            var error = new TaskCompletionSource<string>();
            var stopwatch = Stopwatch.StartNew();

            bool completed = VersionControlCommandRunner.WaitForReaders(
                output.Task,
                error.Task,
                timeoutMilliseconds: 25);

            stopwatch.Stop();
            Assert.That(completed, Is.False);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000));
        }

        private sealed class FakeGitRunner : IVersionControlCommandRunner
        {
            private readonly string workspaceFailureCode;
            private readonly string statusOutput;
            private readonly string submoduleOutput;
            private readonly string lfsInventoryFailureCode;
            private readonly string lfsInventoryOutput;
            private readonly string lfsStatusOutput;

            internal FakeGitRunner(string workspaceFailureCode)
                : this(workspaceFailureCode, string.Empty, string.Empty)
            {
            }

            internal FakeGitRunner(
                string workspaceFailureCode,
                string statusOutput,
                string submoduleOutput,
                string lfsInventoryFailureCode = null,
                string lfsInventoryOutput = "",
                string lfsStatusOutput = "{\"files\":{}}")
            {
                this.workspaceFailureCode = workspaceFailureCode;
                this.statusOutput = statusOutput ?? string.Empty;
                this.submoduleOutput = submoduleOutput ?? string.Empty;
                this.lfsInventoryFailureCode = lfsInventoryFailureCode;
                this.lfsInventoryOutput = lfsInventoryOutput ?? string.Empty;
                this.lfsStatusOutput = lfsStatusOutput ?? string.Empty;
            }

            public string Run(
                string executable,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environment,
                int timeoutMilliseconds,
                int maximumOutputCharacters,
                bool allowExitCodeOne = false)
            {
                switch (arguments)
                {
                    case "rev-parse --verify HEAD":
                        return "0123456789abcdef0123456789abcdef01234567\n";
                    case "log -1 --format=%H%x1f%cI HEAD":
                        return "0123456789abcdef0123456789abcdef01234567\u001f2026-08-11T00:00:00Z\n";
                    case "rev-list --count HEAD":
                        return "42\n";
                    case "symbolic-ref --quiet --short HEAD":
                        return "main\n";
                    case "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none":
                        if (workspaceFailureCode != null)
                        {
                            throw new VersionControlCommandException(
                                workspaceFailureCode,
                                "workspace command failed");
                        }

                        return statusOutput;
                    case "submodule status --recursive":
                        return submoduleOutput;
                    case "lfs ls-files --name-only":
                        if (lfsInventoryFailureCode != null)
                        {
                            throw new VersionControlCommandException(
                                lfsInventoryFailureCode,
                                "LFS inventory command failed");
                        }

                        return lfsInventoryOutput;
                    case "lfs status --json":
                        return lfsStatusOutput;
                    default:
                        throw new InvalidOperationException("Unexpected fake Git command.");
                }
            }
        }

        private sealed class WorkspaceOnlyGitRunner : IVersionControlCommandRunner
        {
            private readonly string firstStatus;
            private readonly string secondStatus;
            private readonly int failOnStatusCall;
            private readonly string failureCode;
            private readonly string failureMessage;
            private int statusCalls;

            internal WorkspaceOnlyGitRunner(
                string firstStatus,
                string secondStatus,
                int failOnStatusCall = 0,
                string failureCode = null,
                string failureMessage = null)
            {
                this.firstStatus = firstStatus ?? string.Empty;
                this.secondStatus = secondStatus ?? string.Empty;
                this.failOnStatusCall = failOnStatusCall;
                this.failureCode = failureCode;
                this.failureMessage = failureMessage;
            }

            internal List<string> Commands { get; } = new List<string>();

            public string Run(
                string executable,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environment,
                int timeoutMilliseconds,
                int maximumOutputCharacters,
                bool allowExitCodeOne = false)
            {
                Commands.Add(arguments);
                switch (arguments)
                {
                    case "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none":
                        statusCalls++;
                        if (statusCalls == failOnStatusCall)
                        {
                            throw new VersionControlCommandException(
                                failureCode ?? VersionControlWorkspaceEvidence.CommandFailed,
                                failureMessage ?? "workspace status failed");
                        }

                        return statusCalls == 1 ? firstStatus : secondStatus;
                    case "submodule status --recursive":
                        return string.Empty;
                    case "lfs status --json":
                        return "{\"files\":{}}";
                    default:
                        throw new InvalidOperationException(
                            "Workspace-only Git capture invoked a metadata command.");
                }
            }
        }

        private sealed class ChangingPerforceRunner : IVersionControlCommandRunner
        {
            private int statusCalls;

            internal bool AllowedExitCodeOne { get; private set; }

            public string Run(
                string executable,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environment,
                int timeoutMilliseconds,
                int maximumOutputCharacters,
                bool allowExitCodeOne = false)
            {
                AllowedExitCodeOne |= allowExitCodeOne;
                switch (arguments)
                {
                    case "changes -m 1 -s submitted":
                        return "Change 42 on 2026/08/11 by build@test 'release'\n";
                    case "client -o":
                        return "Client: test-client\n";
                    case "-ztag status":
                        statusCalls++;
                        return statusCalls == 1
                            ? string.Empty
                            : "... depotFile //depot/game/changed.cs\n... action edit\n";
                    default:
                        throw new InvalidOperationException("Unexpected fake Perforce command.");
                }
            }
        }

        private sealed class WorkspaceOnlyPerforceRunner : IVersionControlCommandRunner
        {
            internal List<string> Commands { get; } = new List<string>();

            public string Run(
                string executable,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environment,
                int timeoutMilliseconds,
                int maximumOutputCharacters,
                bool allowExitCodeOne = false)
            {
                Commands.Add(arguments);
                if (string.Equals(arguments, "-ztag status", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                throw new InvalidOperationException(
                    "Workspace-only Perforce capture invoked a metadata command.");
            }
        }
    }
}
