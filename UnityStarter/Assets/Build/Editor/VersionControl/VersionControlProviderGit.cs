using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Build.VersionControl.Editor
{
    internal sealed class VersionControlProviderGit :
        IVersionControlProvider,
        IVersionControlWorkspaceProvider
    {
        private const string GitExecutable = "git";
        private const int ProcessTimeoutMilliseconds = 10000;
        private const int MaximumProcessOutputCharacters = 64 * 1024;
        private const int MaximumCaptureAttempts = 2;

        private static readonly Regex LfsFilesEnvelopeRegex = new Regex(
            "\\A\\s*\\{\\s*\\\"files\\\"\\s*:\\s*\\{(?<entries>[\\s\\S]*)\\}\\s*\\}\\s*\\z",
            RegexOptions.CultureInvariant);

        private readonly string projectRoot;
        private readonly IVersionControlCommandRunner commandRunner;
        private readonly IReadOnlyDictionary<string, string> environment;

        public VersionControlProviderGit(string projectRoot)
            : this(projectRoot, new VersionControlCommandRunner())
        {
        }

        internal VersionControlProviderGit(
            string projectRoot,
            IVersionControlCommandRunner commandRunner)
        {
            string normalizedRoot = Path.GetFullPath(
                projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)));
            this.projectRoot = FindGitRoot(normalizedRoot)
                ?? throw new InvalidOperationException(
                    $"No Git worktree was found for '{normalizedRoot}'.");
            this.commandRunner = commandRunner
                ?? throw new ArgumentNullException(nameof(commandRunner));
            environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GIT_CONFIG_COUNT"] = "1",
                ["GIT_CONFIG_KEY_0"] = "safe.directory",
                ["GIT_CONFIG_VALUE_0"] = this.projectRoot,
                ["GIT_OPTIONAL_LOCKS"] = "0",
                ["GIT_TERMINAL_PROMPT"] = "0"
            };
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

        public VersionControlWorkspaceEvidence CaptureWorkspace()
        {
            return CaptureWorkspace(CancellationToken.None);
        }

        public VersionControlWorkspaceEvidence CaptureWorkspace(
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryRunGitCommand(
                        "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none",
                        cancellationToken,
                        out string statusBefore,
                        out string firstFailure))
                {
                    return VersionControlWorkspaceEvidence.Unknown(
                        firstFailure ?? VersionControlWorkspaceEvidence.CommandFailed);
                }

                VersionControlWorkspaceEvidence workspace = CaptureWorkspace(
                    statusBefore,
                    initialFailureCode: null,
                    cancellationToken);
                if (!TryRunGitCommand(
                        "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none",
                        cancellationToken,
                        out string statusAfter,
                        out string secondFailure))
                {
                    return VersionControlWorkspaceEvidence.Unknown(
                        secondFailure ?? VersionControlWorkspaceEvidence.CommandFailed);
                }

                return string.Equals(statusBefore, statusAfter, StringComparison.Ordinal)
                    ? workspace
                    : VersionControlWorkspaceEvidence.Unknown(
                        VersionControlWorkspaceEvidence.IncoherentSnapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return VersionControlWorkspaceEvidence.Unknown(
                    VersionControlWorkspaceEvidence.CommandFailed);
            }
        }

        public VersionControlMetadata Capture()
        {
            Exception lastFailure = null;
            for (int attempt = 1; attempt <= MaximumCaptureAttempts; attempt++)
            {
                try
                {
                    string headBefore = RunGitCommand("rev-parse --verify HEAD").Trim();
                    string logRecord = RunGitCommand("log -1 --format=%H%x1f%cI HEAD").Trim();
                    string commitCount = RunGitCommand("rev-list --count HEAD").Trim();
                    string branch = RunGitCommand(
                        "symbolic-ref --quiet --short HEAD",
                        allowExitCodeOne: true).Trim();

                    bool statusAvailable = TryRunGitCommand(
                        "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none",
                        out string statusBefore,
                        out string statusFailureCode);
                    VersionControlWorkspaceEvidence workspace = CaptureWorkspace(
                        statusAvailable ? statusBefore : null,
                        statusFailureCode);

                    string statusAfter = null;
                    if (statusAvailable
                        && (!TryRunGitCommand(
                                "status --porcelain=v2 -z --untracked-files=all --ignore-submodules=none",
                                out statusAfter,
                                out string secondStatusFailure)
                            || !string.Equals(statusBefore, statusAfter, StringComparison.Ordinal)))
                    {
                        lastFailure = new InvalidOperationException(
                            "Git workspace changed while build source evidence was being captured. " +
                            (secondStatusFailure ?? VersionControlWorkspaceEvidence.IncoherentSnapshot));
                        continue;
                    }

                    string headAfter = RunGitCommand("rev-parse --verify HEAD").Trim();
                    if (!string.Equals(headBefore, headAfter, StringComparison.Ordinal))
                    {
                        lastFailure = new InvalidOperationException(
                            "Git HEAD changed while build version metadata was being captured.");
                        continue;
                    }

                    string[] logFields = logRecord.Split(
                        new[] { '\u001f' },
                        StringSplitOptions.None);
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
                        logFields[1],
                        workspace);
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

        private VersionControlWorkspaceEvidence CaptureWorkspace(
            string statusOutput,
            string initialFailureCode,
            CancellationToken cancellationToken = default)
        {
            string failureCode = string.IsNullOrEmpty(initialFailureCode)
                ? VersionControlWorkspaceEvidence.NoFailure
                : initialFailureCode;

            VersionControlWorkspaceComponentEvidence tracked;
            VersionControlWorkspaceComponentEvidence untracked;
            VersionControlWorkspaceComponentEvidence submodulesFromStatus;
            if (statusOutput == null
                || !TryParseStatus(
                    statusOutput,
                    out tracked,
                    out untracked,
                    out submodulesFromStatus))
            {
                tracked = UnknownComponent();
                untracked = UnknownComponent();
                submodulesFromStatus = UnknownComponent();
                failureCode = FirstFailure(
                    failureCode,
                    VersionControlWorkspaceEvidence.MalformedOutput);
            }

            VersionControlWorkspaceComponentEvidence submodules =
                CaptureSubmoduleStatus(
                    submodulesFromStatus,
                    ref failureCode,
                    cancellationToken);
            VersionControlWorkspaceComponentEvidence gitLfs =
                CaptureGitLfsStatus(ref failureCode, cancellationToken);
            return new VersionControlWorkspaceEvidence(
                tracked,
                untracked,
                submodules,
                gitLfs,
                failureCode);
        }

        private VersionControlWorkspaceComponentEvidence CaptureSubmoduleStatus(
            VersionControlWorkspaceComponentEvidence statusEvidence,
            ref string failureCode,
            CancellationToken cancellationToken)
        {
            if (!TryRunGitCommand(
                    "submodule status --recursive",
                    cancellationToken,
                    out string output,
                    out string commandFailure))
            {
                failureCode = FirstFailure(failureCode, commandFailure);
                return UnknownComponent();
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                if (statusEvidence.Status == VersionControlWorkspaceComponentStatus.Dirty)
                {
                    failureCode = FirstFailure(
                        failureCode,
                        VersionControlWorkspaceEvidence.IncoherentSnapshot);
                    return UnknownComponent();
                }

                return new VersionControlWorkspaceComponentEvidence(
                    VersionControlWorkspaceComponentStatus.NotApplicable,
                    0);
            }

            int commandDirtyCount = 0;
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                if (!IsValidSubmoduleStatusLine(line))
                {
                    failureCode = FirstFailure(
                        failureCode,
                        VersionControlWorkspaceEvidence.MalformedOutput);
                    return UnknownComponent();
                }

                switch (line[0])
                {
                    case ' ':
                        break;
                    case '+':
                    case 'U':
                        commandDirtyCount++;
                        break;
                    case '-':
                        failureCode = FirstFailure(
                            failureCode,
                            VersionControlWorkspaceEvidence.IncoherentSnapshot);
                        return UnknownComponent();
                    default:
                        failureCode = FirstFailure(
                            failureCode,
                            VersionControlWorkspaceEvidence.MalformedOutput);
                        return UnknownComponent();
                }
            }

            if (statusEvidence.Status == VersionControlWorkspaceComponentStatus.Unknown)
            {
                return commandDirtyCount > 0
                    ? new VersionControlWorkspaceComponentEvidence(
                        VersionControlWorkspaceComponentStatus.Dirty,
                        commandDirtyCount)
                    : UnknownComponent();
            }

            int dirtyCount = Math.Max(
                statusEvidence.ChangeCount.GetValueOrDefault(),
                commandDirtyCount);
            return new VersionControlWorkspaceComponentEvidence(
                dirtyCount == 0
                    ? VersionControlWorkspaceComponentStatus.Clean
                    : VersionControlWorkspaceComponentStatus.Dirty,
                dirtyCount);
        }

        private VersionControlWorkspaceComponentEvidence CaptureGitLfsStatus(
            ref string failureCode,
            CancellationToken cancellationToken)
        {
            if (!TryRunGitCommand(
                    "lfs status --json",
                    cancellationToken,
                    out string status,
                    out string statusFailure))
            {
                failureCode = FirstFailure(failureCode, statusFailure);
                return UnknownComponent();
            }

            Match envelope = LfsFilesEnvelopeRegex.Match(status ?? string.Empty);
            if (!envelope.Success)
            {
                failureCode = FirstFailure(
                    failureCode,
                    VersionControlWorkspaceEvidence.MalformedOutput);
                return UnknownComponent();
            }

            string entries = envelope.Groups["entries"].Value.Trim();
            bool clean = entries.Length == 0;
            if (!clean && (entries[0] != '"' || entries.IndexOf(':') < 0))
            {
                failureCode = FirstFailure(
                    failureCode,
                    VersionControlWorkspaceEvidence.MalformedOutput);
                return UnknownComponent();
            }

            return new VersionControlWorkspaceComponentEvidence(
                clean
                    ? VersionControlWorkspaceComponentStatus.Clean
                    : VersionControlWorkspaceComponentStatus.Dirty,
                clean ? 0 : (int?)null);
        }

        internal static bool TryParseStatus(
            string output,
            out VersionControlWorkspaceComponentEvidence tracked,
            out VersionControlWorkspaceComponentEvidence untracked,
            out VersionControlWorkspaceComponentEvidence submodules)
        {
            tracked = null;
            untracked = null;
            submodules = null;
            if (output == null)
            {
                return false;
            }

            int trackedCount = 0;
            int untrackedCount = 0;
            int dirtySubmoduleCount = 0;
            bool skipRenameOrigin = false;
            string[] records = output.Split('\0');
            for (int index = 0; index < records.Length; index++)
            {
                string record = records[index];
                if (record.Length == 0)
                {
                    continue;
                }

                if (skipRenameOrigin)
                {
                    skipRenameOrigin = false;
                    continue;
                }

                if (record.StartsWith("? ", StringComparison.Ordinal))
                {
                    untrackedCount++;
                    continue;
                }

                if (record.StartsWith("! ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!(record.StartsWith("1 ", StringComparison.Ordinal)
                      || record.StartsWith("2 ", StringComparison.Ordinal)
                      || record.StartsWith("u ", StringComparison.Ordinal)))
                {
                    return false;
                }

                string[] fields = record.Split(new[] { ' ' }, 5);
                if (fields.Length < 3 || !IsValidSubmoduleField(fields[2]))
                {
                    return false;
                }

                trackedCount++;
                string submoduleField = fields[2];
                if (submoduleField[0] == 'S'
                    && (submoduleField[1] != '.'
                        || submoduleField[2] != '.'
                        || submoduleField[3] != '.'))
                {
                    dirtySubmoduleCount++;
                }

                skipRenameOrigin = record[0] == '2';
            }

            if (skipRenameOrigin)
            {
                return false;
            }

            tracked = CreateCountedComponent(trackedCount);
            untracked = CreateCountedComponent(untrackedCount);
            submodules = CreateCountedComponent(dirtySubmoduleCount);
            return true;
        }

        private static bool IsValidSubmoduleField(string value)
        {
            if (value == null || value.Length != 4)
            {
                return false;
            }

            if (value[0] == 'N')
            {
                return value[1] == '.' && value[2] == '.' && value[3] == '.';
            }

            return value[0] == 'S'
                   && (value[1] == '.' || value[1] == 'C')
                   && (value[2] == '.' || value[2] == 'M')
                   && (value[3] == '.' || value[3] == 'U');
        }

        private static bool IsValidSubmoduleStatusLine(string line)
        {
            if (string.IsNullOrEmpty(line)
                || (line[0] != ' '
                    && line[0] != '+'
                    && line[0] != '-'
                    && line[0] != 'U'))
            {
                return false;
            }

            int separatorIndex = line.IndexOf(' ', 1);
            int hashLength = separatorIndex - 1;
            if ((hashLength != 40 && hashLength != 64)
                || separatorIndex >= line.Length - 1)
            {
                return false;
            }

            for (int index = 1; index < separatorIndex; index++)
            {
                char character = line[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')
                      || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private string RunGitCommand(string arguments, bool allowExitCodeOne = false)
        {
            return commandRunner.Run(
                GitExecutable,
                arguments,
                projectRoot,
                environment,
                ProcessTimeoutMilliseconds,
                MaximumProcessOutputCharacters,
                allowExitCodeOne);
        }

        private bool TryRunGitCommand(
            string arguments,
            out string output,
            out string failureCode)
        {
            return TryRunGitCommand(
                arguments,
                CancellationToken.None,
                out output,
                out failureCode);
        }

        private bool TryRunGitCommand(
            string arguments,
            CancellationToken cancellationToken,
            out string output,
            out string failureCode)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (commandRunner is ICancellableVersionControlCommandRunner cancellable)
                {
                    output = cancellable.Run(
                        GitExecutable,
                        arguments,
                        projectRoot,
                        environment,
                        ProcessTimeoutMilliseconds,
                        MaximumProcessOutputCharacters,
                        false,
                        cancellationToken);
                }
                else
                {
                    output = RunGitCommand(arguments);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                failureCode = null;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (VersionControlCommandException exception)
            {
                output = null;
                failureCode = exception.FailureCode;
                return false;
            }
            catch (Exception)
            {
                output = null;
                failureCode = VersionControlWorkspaceEvidence.CommandFailed;
                return false;
            }
        }

        private static VersionControlWorkspaceComponentEvidence CreateCountedComponent(
            int count)
        {
            return new VersionControlWorkspaceComponentEvidence(
                count == 0
                    ? VersionControlWorkspaceComponentStatus.Clean
                    : VersionControlWorkspaceComponentStatus.Dirty,
                count);
        }

        private static VersionControlWorkspaceComponentEvidence UnknownComponent()
        {
            return new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Unknown);
        }

        private static string FirstFailure(string current, string candidate)
        {
            return string.Equals(
                    current,
                    VersionControlWorkspaceEvidence.NoFailure,
                    StringComparison.Ordinal)
                ? candidate ?? VersionControlWorkspaceEvidence.CommandFailed
                : current;
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
