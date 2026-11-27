using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Rejects a Player build when the generated DataTable outputs no longer match
    /// the workbooks and configuration that produced them.
    ///
    /// The step delegates to the pipeline's own <c>check</c> command rather than
    /// re-deriving identity here. That command already verifies the toolchain hash,
    /// the input hash, the schema hash, and every output file; a second
    /// implementation in the build loop would be a weaker copy that can drift.
    /// </summary>
    [BuildStepRegistration(
        StepTypeIdValue,
        DisplayName = "DataTable Validation",
        Description =
            "Run the DataTable pipeline check so a Player build cannot ship tables generated from stale workbooks.",
        Category = "Validation",
        ConfigurationType = typeof(DataTableValidationConfiguration),
        ConfigurationRequired = false)]
    public sealed class DataTableValidationBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "data-table-validation";

        private const string DefaultProfile = "client";
        private const int DefaultTimeoutSeconds = 600;

        public string StepTypeId => StepTypeIdValue;

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            var errors = new List<string>();
            DataTableValidationConfiguration configuration =
                invocation.GetConfiguration<DataTableValidationConfiguration>();
            string profileName = ResolveProfile(configuration);
            int timeoutSeconds = ResolveTimeoutSeconds(configuration);

            string launcher = ResolveLauncher(context, configuration, errors);
            if (launcher == null)
            {
                return errors;
            }

            var run = new PipelineRun();
            if (!run.Execute(CreateStartInfo(launcher, profileName), timeoutSeconds * 1000))
            {
                errors.Add(
                    "DataTable check did not finish within " + timeoutSeconds + " seconds: " + launcher);
                return errors;
            }

            if (run.ExitCode != 0)
            {
                errors.Add(
                    "DataTable check failed with exit code " + run.ExitCode +
                    ". Run 'pipeline generate --profile " + profileName + "' before building." +
                    Environment.NewLine + run.StandardError + run.StandardOutput);
            }

            return errors;
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
        }

        private static string ResolveProfile(DataTableValidationConfiguration configuration)
        {
            return configuration != null && !string.IsNullOrEmpty(configuration.profile)
                ? configuration.profile
                : DefaultProfile;
        }

        private static int ResolveTimeoutSeconds(DataTableValidationConfiguration configuration)
        {
            return configuration != null && configuration.timeoutSeconds > 0
                ? configuration.timeoutSeconds
                : DefaultTimeoutSeconds;
        }

        private static string ResolveLauncher(
            BuildExecutionContext context,
            DataTableValidationConfiguration configuration,
            List<string> errors)
        {
            string relative =
                configuration != null && !string.IsNullOrEmpty(configuration.launcherRelativePath)
                    ? configuration.launcherRelativePath
                    : DataTableValidationConfiguration.DefaultLauncherRelativePath;
            string extension = IsWindows ? ".bat" : ".sh";
            string candidate = Path.GetFullPath(
                Path.Combine(context.Request.ProjectRoot, relative + extension));
            if (!File.Exists(candidate))
            {
                errors.Add(
                    "DataTable pipeline launcher is missing: " + candidate +
                    ". Set Launcher Relative Path on the step configuration.");
                return null;
            }

            return candidate;
        }

        private static bool IsWindows => Path.DirectorySeparatorChar == '\\';

        private static ProcessStartInfo CreateStartInfo(string launcher, string profileName)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = IsWindows ? "cmd.exe" : "bash",
                WorkingDirectory = Path.GetDirectoryName(launcher) ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (IsWindows)
            {
                startInfo.ArgumentList.Add("/c");
            }

            startInfo.ArgumentList.Add(launcher);
            startInfo.ArgumentList.Add("check");
            startInfo.ArgumentList.Add("--profile");
            startInfo.ArgumentList.Add(profileName);
            return startInfo;
        }

        private sealed class PipelineRun
        {
            private readonly StringBuilder _standardOutput = new StringBuilder();
            private readonly StringBuilder _standardError = new StringBuilder();

            public int ExitCode { get; private set; }
            public string StandardOutput => _standardOutput.ToString();
            public string StandardError => _standardError.ToString();

            // Both pipes are read asynchronously. Sequential ReadToEnd on stdout and
            // stderr deadlocks when the child fills the stderr buffer while the parent
            // is still blocked on stdout.
            public bool Execute(ProcessStartInfo startInfo, int timeoutMilliseconds)
            {
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += OnOutput;
                process.ErrorDataReceived += OnError;
                if (!process.Start())
                {
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return false;
                }

                process.WaitForExit();
                ExitCode = process.ExitCode;
                return true;
            }

            private void OnOutput(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null)
                {
                    _standardOutput.AppendLine(args.Data);
                }
            }

            private void OnError(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null)
                {
                    _standardError.AppendLine(args.Data);
                }
            }
        }
    }
}
