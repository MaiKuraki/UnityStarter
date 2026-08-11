using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Build.VersionControl.Editor
{
    internal sealed class VersionControlProviderPerforce : IVersionControlProvider
    {
        private const string P4Executable = "p4";
        private const int ProcessTimeoutMilliseconds = 10000;
        private const int MaximumProcessOutputCharacters = 64 * 1024;

        private static readonly Regex ChangeNumberRegex = new Regex(
            @"Change\s+(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ChangeDateRegex = new Regex(
            @"Change\s+\d+\s+on\s+(\d{4}/\d{2}/\d{2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string projectRoot;
        private readonly IVersionControlCommandRunner commandRunner;

        public VersionControlProviderPerforce(string projectRoot)
            : this(projectRoot, new VersionControlCommandRunner())
        {
        }

        internal VersionControlProviderPerforce(
            string projectRoot,
            IVersionControlCommandRunner commandRunner)
        {
            this.projectRoot = Path.GetFullPath(
                projectRoot ?? throw new ArgumentNullException(nameof(projectRoot)));
            this.commandRunner = commandRunner
                ?? throw new ArgumentNullException(nameof(commandRunner));
        }

        public VersionControlMetadata Capture()
        {
            string changeOutput = RunP4Command("changes -m 1 -s submitted").Trim();
            Match changeMatch = ChangeNumberRegex.Match(changeOutput);
            if (!changeMatch.Success)
            {
                throw new InvalidOperationException(
                    "Perforce did not return a latest submitted changelist.");
            }

            string change = changeMatch.Groups[1].Value;
            if (!long.TryParse(
                    change,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long changeNumber)
                || changeNumber <= 0)
            {
                throw new InvalidOperationException(
                    "Perforce returned an invalid submitted changelist number.");
            }

            Match dateMatch = ChangeDateRegex.Match(changeOutput);
            if (!dateMatch.Success
                || !DateTime.TryParseExact(
                    dateMatch.Groups[1].Value,
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                throw new InvalidOperationException(
                    "Perforce returned an invalid submitted changelist date.");
            }

            string clientOutput = RunP4Command("client -o");
            Match streamMatch = Regex.Match(
                clientOutput,
                @"^Stream:\s+(.+)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            Match clientMatch = Regex.Match(
                clientOutput,
                @"^Client:\s+(.+)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            string branch = streamMatch.Success
                ? streamMatch.Groups[1].Value.Trim()
                : clientMatch.Success
                    ? clientMatch.Groups[1].Value.Trim()
                    : string.Empty;
            ValidateBranch(branch);

            return new VersionControlMetadata(
                "Perforce",
                change,
                change,
                branch,
                parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CaptureWorkspace());
        }

        private VersionControlWorkspaceEvidence CaptureWorkspace()
        {
            if (!TryRunP4Command(
                    "-ztag status",
                    out string firstStatus,
                    out string firstFailure))
            {
                return CreateUnknownWorkspace(firstFailure);
            }

            if (!TryRunP4Command(
                    "-ztag status",
                    out string secondStatus,
                    out string secondFailure))
            {
                return CreateUnknownWorkspace(secondFailure);
            }

            if (!string.Equals(firstStatus, secondStatus, StringComparison.Ordinal))
            {
                return CreateUnknownWorkspace(
                    VersionControlWorkspaceEvidence.IncoherentSnapshot);
            }

            if (!TryCountReconcileActions(
                    secondStatus,
                    out int trackedCount,
                    out int untrackedCount))
            {
                return CreateUnknownWorkspace(
                    VersionControlWorkspaceEvidence.MalformedOutput);
            }

            var tracked = CreateCountedComponent(trackedCount);
            var untracked = CreateCountedComponent(untrackedCount);
            var notApplicableComponent = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            return new VersionControlWorkspaceEvidence(
                tracked,
                untracked,
                notApplicableComponent,
                notApplicableComponent);
        }

        private static VersionControlWorkspaceEvidence CreateUnknownWorkspace(
            string failureCode)
        {
            var unknown = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.Unknown);
            var notApplicable = new VersionControlWorkspaceComponentEvidence(
                VersionControlWorkspaceComponentStatus.NotApplicable,
                0);
            return new VersionControlWorkspaceEvidence(
                unknown,
                unknown,
                notApplicable,
                notApplicable,
                string.IsNullOrEmpty(failureCode)
                    ? VersionControlWorkspaceEvidence.CommandFailed
                    : failureCode);
        }

        internal static bool TryCountTaggedField(
            string output,
            string fieldName,
            out int count)
        {
            count = 0;
            if (output == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            string prefix = "... " + fieldName + " ";
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
                else if (IsTaggedErrorLine(line))
                {
                    return false;
                }
                else if (!line.StartsWith("... ", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryCountReconcileActions(
            string output,
            out int trackedCount,
            out int untrackedCount)
        {
            trackedCount = 0;
            untrackedCount = 0;
            if (output == null)
            {
                return false;
            }

            const string ActionPrefix = "... action ";
            bool sawOutput = false;
            bool sawAction = false;
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                sawOutput = true;

                if (!line.StartsWith("... ", StringComparison.Ordinal))
                {
                    return false;
                }

                if (IsTaggedErrorLine(line))
                {
                    return false;
                }

                if (!line.StartsWith(ActionPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                sawAction = true;

                string action = line.Substring(ActionPrefix.Length).Trim();
                if (string.Equals(action, "add", StringComparison.Ordinal))
                {
                    untrackedCount++;
                }
                else if (string.Equals(action, "edit", StringComparison.Ordinal)
                         || string.Equals(action, "delete", StringComparison.Ordinal)
                         || string.Equals(action, "move/add", StringComparison.Ordinal)
                         || string.Equals(action, "move/delete", StringComparison.Ordinal))
                {
                    trackedCount++;
                }
                else
                {
                    return false;
                }
            }

            return !sawOutput || sawAction;
        }

        private static bool IsTaggedErrorLine(string line)
        {
            return line.StartsWith("... code ", StringComparison.Ordinal)
                   || line.StartsWith("... data ", StringComparison.Ordinal)
                   || line.StartsWith("... severity ", StringComparison.Ordinal)
                   || line.StartsWith("... generic ", StringComparison.Ordinal);
        }

        private string RunP4Command(string arguments, bool allowExitCodeOne = false)
        {
            return commandRunner.Run(
                P4Executable,
                arguments,
                projectRoot,
                environment: null,
                ProcessTimeoutMilliseconds,
                MaximumProcessOutputCharacters,
                allowExitCodeOne);
        }

        private bool TryRunP4Command(
            string arguments,
            out string output,
            out string failureCode,
            bool allowExitCodeOne = false)
        {
            try
            {
                output = RunP4Command(arguments, allowExitCodeOne);
                failureCode = null;
                return true;
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

        private static void ValidateBranch(string branch)
        {
            if (string.IsNullOrWhiteSpace(branch) || branch.Length > 512)
            {
                throw new InvalidOperationException(
                    "Perforce client metadata does not contain a bounded Stream or Client name.");
            }

            for (int index = 0; index < branch.Length; index++)
            {
                if (char.IsControl(branch[index]))
                {
                    throw new InvalidOperationException(
                        "Perforce Stream or Client name contains a control character.");
                }
            }
        }
    }
}
