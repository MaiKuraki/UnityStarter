using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Build.VersionControl.Editor
{
    internal sealed class VersionControlProviderGit : IVersionControlProvider
    {
        private const string GitExecutable = "git";
        private const int ProcessTimeoutMilliseconds = 10000;
        private const int MaximumProcessOutputCharacters = 64 * 1024;
        private const int MaximumCaptureAttempts = 2;

        private readonly string projectRoot;

        public VersionControlProviderGit(string projectRoot)
        {
            string normalizedRoot = Path.GetFullPath(
                projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)));
            this.projectRoot = FindGitRoot(normalizedRoot)
                ?? throw new InvalidOperationException(
                    $"No Git worktree was found for '{normalizedRoot}'.");
        }

        internal static string FindGitRoot(string startDirectory)
        {
            string directory = Path.GetFullPath(startDirectory);
            string volumeRoot = Path.GetPathRoot(directory);
            while (directory != null && directory.Length >= volumeRoot.Length)
            {
                string gitPath = Path.Combine(directory, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return directory;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        public VersionControlMetadata Capture()
        {
            Exception lastFailure = null;
            for (int attempt = 1; attempt <= MaximumCaptureAttempts; attempt++)
            {
                try
                {
                    string headBefore = RunGitCommand("rev-parse --verify HEAD");
                    string logRecord = RunGitCommand("log -1 --format=%H%x1f%cI HEAD");
                    string commitCount = RunGitCommand("rev-list --count HEAD");
                    string branch = RunGitCommand("symbolic-ref --quiet --short HEAD", allowExitCodeOne: true);
                    string headAfter = RunGitCommand("rev-parse --verify HEAD");
                    if (!string.Equals(headBefore, headAfter, StringComparison.Ordinal))
                    {
                        lastFailure = new InvalidOperationException(
                            "Git HEAD changed while build version metadata was being captured.");
                        continue;
                    }

                    string[] logFields = logRecord.Split(new[] { '\u001f' }, StringSplitOptions.None);
                    if (logFields.Length != 2
                        || !string.Equals(logFields[0], headBefore, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Git log metadata did not match the captured HEAD revision.");
                    }

                    if (string.IsNullOrWhiteSpace(branch))
                    {
                        branch = "detached-" + ShortenHash(headBefore);
                    }

                    ValidateHash(headBefore);
                    ValidateCommitCount(commitCount);
                    ValidateCommitDate(logFields[1]);
                    ValidateText(branch, "Git branch", 512);
                    return new VersionControlMetadata(
                        "Git",
                        ShortenHash(headBefore),
                        commitCount,
                        branch,
                        logFields[1]);
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }
            }

            throw new InvalidOperationException(
                $"Failed to capture a coherent Git metadata snapshot after {MaximumCaptureAttempts} attempts.",
                lastFailure);
        }

        private string RunGitCommand(string arguments, bool allowExitCodeOne = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GitExecutable,
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.EnvironmentVariables["GIT_CONFIG_COUNT"] = "1";
            startInfo.EnvironmentVariables["GIT_CONFIG_KEY_0"] = "safe.directory";
            startInfo.EnvironmentVariables["GIT_CONFIG_VALUE_0"] = projectRoot;
            startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        $"Git process did not start: git {arguments}");
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(ProcessTimeoutMilliseconds))
                {
                    TryKill(process);
                    process.WaitForExit();
                    throw new TimeoutException(
                        $"Git command timed out after {ProcessTimeoutMilliseconds} ms: git {arguments}");
                }

                process.WaitForExit();
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                if (output.Length > MaximumProcessOutputCharacters
                    || error.Length > MaximumProcessOutputCharacters)
                {
                    throw new InvalidOperationException(
                        $"Git command exceeded its output budget: git {arguments}");
                }

                if (process.ExitCode != 0
                    && !(allowExitCodeOne && process.ExitCode == 1))
                {
                    throw new InvalidOperationException(
                        $"Git command failed with exit code {process.ExitCode}: git {arguments}. {error.Trim()}");
                }

                return output.Trim();
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string ShortenHash(string hash)
        {
            return hash.Substring(0, Math.Min(12, hash.Length));
        }

        private static void ValidateHash(string value)
        {
            if (value == null || (value.Length != 40 && value.Length != 64))
            {
                throw new InvalidOperationException("Git returned an invalid HEAD hash.");
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')
                      || (character >= 'A' && character <= 'F')))
                {
                    throw new InvalidOperationException("Git returned an invalid HEAD hash.");
                }
            }
        }

        private static void ValidateCommitCount(string value)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long count)
                || count < 0)
            {
                throw new InvalidOperationException("Git returned an invalid commit count.");
            }
        }

        private static void ValidateCommitDate(string value)
        {
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                throw new InvalidOperationException("Git returned an invalid commit date.");
            }
        }

        private static void ValidateText(string value, string displayName, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                throw new InvalidOperationException($"{displayName} is empty or exceeds its length budget.");
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                {
                    throw new InvalidOperationException($"{displayName} contains a control character.");
                }
            }
        }
    }
}
