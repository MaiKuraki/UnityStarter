using System.Text.RegularExpressions;

namespace CycloneGames.Analyzers.Verifier
{
    internal sealed class VerifierReport
    {
        private VerifierReport(bool succeeded, string message, string? retainedProjectPath)
        {
            Succeeded = succeeded;
            Message = message;
            RetainedProjectPath = retainedProjectPath;
        }

        internal bool Succeeded { get; }
        internal string Message { get; }
        internal string? RetainedProjectPath { get; }

        internal static VerifierReport Passed(string message) =>
            new VerifierReport(true, message, null);

        internal static VerifierReport PassedWithRetainedProject(string message, string retainedProjectPath) =>
            new VerifierReport(true, message, retainedProjectPath);

        internal static VerifierReport Failed(string message, string? retainedProjectPath = null) =>
            new VerifierReport(false, message, retainedProjectPath);

        internal void Print(TextWriter writer)
        {
            writer.WriteLine(Message);
            if (RetainedProjectPath != null)
            {
                writer.WriteLine("Temporary project retained for diagnosis: " + RetainedProjectPath);
            }
        }
    }

    internal sealed class ActivationVerifier
    {
        private static readonly Regex AnalyzerLoadFailure = new Regex(
            "Failed to load analyzer|analyzer.*could not be loaded|CS8032",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly VerifierOptions _options;

        internal ActivationVerifier(VerifierOptions options)
        {
            _options = options;
        }

        internal VerifierReport Run()
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(_options.TimeoutSeconds);
            try
            {
                string solutionRoot = UnityProjectLocator.FindSolutionRoot(AppContext.BaseDirectory);
                string unityProjectRoot = _options.UnityProjectRoot ??
                    UnityProjectLocator.FindUnityProjectRoot(solutionRoot);
                string unityCsproj = Path.Combine(
                    solutionRoot, "CycloneGames.Analyzers.Unity", "CycloneGames.Analyzers.Unity.csproj");
                string installedAnalyzer = Path.Combine(
                    unityProjectRoot, "Assets", "Analyzers", "CycloneGames.Analyzers.dll");
                string installedAnalyzerMeta = installedAnalyzer + ".meta";
                string fixtureSource = Path.Combine(
                    solutionRoot, "CycloneGames.Analyzers.Unity", "Integration", "ForbiddenUnityApiViolation.cs.txt");
                string projectVersion = Path.Combine(
                    unityProjectRoot, "ProjectSettings", "ProjectVersion.txt");

                foreach (string required in new[]
                {
                    _options.UnityEditorPath!, installedAnalyzer, installedAnalyzerMeta, fixtureSource, projectVersion
                })
                {
                    if (!File.Exists(required))
                    {
                        return VerifierReport.Failed(
                            "Required analyzer verification input is missing: " + required);
                    }
                }

                if (!_options.SkipBuild)
                {
                    ProcessRunResult buildResult = OwnedProcessRunner.Run(
                        "dotnet",
                        new[]
                        {
                            "build",
                            unityCsproj,
                            "-c", "Release",
                            "-p:UseSharedCompilation=false",
                            "-p:UnityProjectRoot=" + unityProjectRoot,
                            "--nologo"
                        },
                        RemainingMilliseconds(deadlineUtc));
                    if (buildResult.TimedOut || !buildResult.TerminationConfirmed)
                    {
                        return VerifierReport.Failed("Timed out building the Unity-compatible analyzer.");
                    }
                    if (buildResult.ExitCode != 0)
                    {
                        return VerifierReport.Failed(
                            "Unity-compatible analyzer build failed with exit code " + buildResult.ExitCode + ". " +
                            buildResult.StandardError);
                    }
                }

                using var temporary = TemporaryUnityProject.Create();
                temporary.Prepare(installedAnalyzer, installedAnalyzerMeta, fixtureSource, projectVersion);
                string logPath = Path.Combine(temporary.Root, "UnityAnalyzerVerification.log");

                DateTime launchTimeUtc = DateTime.UtcNow;
                ProcessRunResult unityResult = OwnedProcessRunner.Run(
                    _options.UnityEditorPath!,
                    new[]
                    {
                        "-batchmode",
                        "-nographics",
                        "-quit",
                        "-projectPath", temporary.Root,
                        "-logFile", logPath
                    },
                    RemainingMilliseconds(deadlineUtc));

                WindowsCompilerServerCleanup.StopServersStartedAfter(_options.UnityEditorPath!, launchTimeUtc);

                if (unityResult.TimedOut || !unityResult.TerminationConfirmed)
                {
                    temporary.RetainForDiagnosis();
                    return VerifierReport.Failed(
                        "Unity verification " +
                        (unityResult.TimedOut
                            ? "timed out"
                            : "could not confirm owned process-tree termination") +
                        ". Log: " + logPath,
                        temporary.Root);
                }

                string log = ReadLogBeforeDeadline(logPath, deadlineUtc);
                if (AnalyzerLoadFailure.IsMatch(log))
                {
                    temporary.RetainForDiagnosis();
                    return VerifierReport.Failed(
                        "Unity failed to load CycloneGames.Analyzers. Log: " + logPath,
                        temporary.Root);
                }
                if (!log.Contains("CG0010", StringComparison.Ordinal))
                {
                    temporary.RetainForDiagnosis();
                    return VerifierReport.Failed(
                        "Unity compilation did not report the expected CG0010 diagnostic. Log: " + logPath,
                        temporary.Root);
                }

                string passedMessage = BuildPassedMessage(unityResult.ExitCode);
                if (_options.KeepTemporaryProject)
                {
                    temporary.RetainForDiagnosis();
                    return VerifierReport.PassedWithRetainedProject(passedMessage, temporary.Root);
                }

                return VerifierReport.Passed(passedMessage);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is InvalidOperationException ||
                exception is System.Security.SecurityException)
            {
                return VerifierReport.Failed(exception.Message);
            }
        }

        private static string BuildPassedMessage(int unityExitCode)
        {
            string message =
                "Unity analyzer activation verification passed (analyzer loaded and CG0010 emitted).";
            if (unityExitCode != 0)
            {
                message += " Unity exited with code " + unityExitCode +
                           " because the fixture deliberately triggers the Error-severity CG0010 diagnostic; " +
                           "this compile failure is the expected signal and is not a verification failure.";
            }

            return message;
        }

        private static int RemainingMilliseconds(DateTime deadlineUtc)
        {
            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining.TotalMilliseconds <= 0)
            {
                throw new InvalidOperationException("Verification deadline expired.");
            }

            return (int)Math.Min(int.MaxValue, Math.Max(1, remaining.TotalMilliseconds));
        }

        private static string ReadLogBeforeDeadline(string logPath, DateTime deadlineUtc)
        {
            while (DateTime.UtcNow < deadlineUtc)
            {
                try
                {
                    if (File.Exists(logPath))
                    {
                        return File.ReadAllText(logPath);
                    }
                }
                catch (IOException)
                {
                    // Unity may still hold the log briefly after exit; retry until the deadline.
                }

                Thread.Sleep(250);
            }

            throw new InvalidOperationException(
                "Could not read the Unity verification log before the deadline: " + logPath);
        }
    }
}
