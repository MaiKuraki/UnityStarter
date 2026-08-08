using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public enum BuildWorkspaceHealthStatus
    {
        Clean,
        RecoveryRequired,
        Blocked,
        Busy
    }

    public enum BuildWorkspaceIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class BuildWorkspaceIssue
    {
        internal BuildWorkspaceIssue(
            BuildWorkspaceIssueSeverity severity,
            string participantId,
            string title,
            string message,
            string evidencePath,
            string transactionId = "",
            string phase = "",
            string requiredBuildTarget = "")
        {
            Severity = severity;
            ParticipantId = participantId ?? string.Empty;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            EvidencePath = evidencePath ?? string.Empty;
            TransactionId = transactionId ?? string.Empty;
            Phase = phase ?? string.Empty;
            RequiredBuildTarget = requiredBuildTarget ?? string.Empty;
        }

        public BuildWorkspaceIssueSeverity Severity { get; }
        public string ParticipantId { get; }
        public string Title { get; }
        public string Message { get; }
        public string TransactionId { get; }
        public string Phase { get; }
        public string RequiredBuildTarget { get; }
        public string EvidencePath { get; }
    }

    public sealed class BuildWorkspaceSnapshot
    {
        internal BuildWorkspaceSnapshot(
            string token,
            BuildWorkspaceHealthStatus status,
            string summary,
            IReadOnlyList<BuildWorkspaceIssue> issues,
            bool canRecover)
        {
            Token = token ?? string.Empty;
            Status = status;
            Summary = summary ?? string.Empty;
            Issues = new ReadOnlyCollection<BuildWorkspaceIssue>(
                (issues ?? Array.Empty<BuildWorkspaceIssue>()).ToArray());
            CanRecover = canRecover;
        }

        public string Token { get; }
        public BuildWorkspaceHealthStatus Status { get; }
        public string Summary { get; }
        public IReadOnlyList<BuildWorkspaceIssue> Issues { get; }
        public bool CanRecover { get; }
    }

    /// <summary>
    /// Provides a zero-write inspection boundary and an explicit, optimistic
    /// concurrency recovery command for project-owned build transactions.
    /// </summary>
    public static class BuildWorkspaceService
    {
        private const string TransactionRootRelativePath = ".buildpipeline/transactions";
        private const int MaximumTopLevelEntries = 4096;
        private const int MaximumClaimsPerParticipant = 16;
        private const int MaximumIssueMessageCharacters = 4096;
        private const long MaximumTokenFileBytes = 4L * 1024L * 1024L;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static BuildWorkspaceSnapshot Inspect()
        {
            return Inspect(GetCurrentProjectRoot());
        }

        public static BuildWorkspaceSnapshot Recover(string expectedToken)
        {
            return Recover(GetCurrentProjectRoot(), expectedToken);
        }

        internal static BuildWorkspaceSnapshot Inspect(string projectRoot)
        {
            return Inspect(projectRoot, IsEditorBusy());
        }

        internal static BuildWorkspaceSnapshot Inspect(
            string projectRoot,
            bool editorIsBusy)
        {
            IReadOnlyList<IBuildRecoveryParticipant> participants;
            try
            {
                participants = BuildPipelineRegistry.ResolveRecoveryParticipants();
            }
            catch (Exception exception)
            {
                return CreateDiscoveryFailureSnapshot(exception);
            }

            return Inspect(
                projectRoot,
                participants,
                editorIsBusy);
        }

        internal static void EnsureReady(string projectRoot)
        {
            BuildWorkspaceSnapshot snapshot = Inspect(
                projectRoot,
                editorIsBusy: false);
            if (snapshot.Status == BuildWorkspaceHealthStatus.Clean)
            {
                return;
            }

            string issues = string.Join(
                "; ",
                snapshot.Issues.Take(8).Select(issue =>
                    $"{issue.ParticipantId}: {issue.Message}"));
            throw new BuildFailedException(
                $"Build workspace status is '{snapshot.Status}'. Normal builds never recover or discard durable state implicitly. "
                + "Open Build > Pipeline > Workspace Health, or run the command-line entry point with "
                + $"{BuildCommandLineOptionNames.RecoverOnly}. Snapshot='{snapshot.Token}'. "
                + issues);
        }

        internal static BuildWorkspaceSnapshot Inspect(
            string projectRoot,
            IReadOnlyList<IBuildRecoveryParticipant> participants,
            bool editorIsBusy)
        {
            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            string transactionRoot = Path.GetFullPath(Path.Combine(
                normalizedProjectRoot,
                TransactionRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureStrictDescendant(normalizedProjectRoot, transactionRoot, "Build transaction root");

            var issues = new List<BuildWorkspaceIssue>();
            var tokenFacts = new List<string>
            {
                "workspace-format=1",
                "project=" + NormalizeForToken(normalizedProjectRoot)
            };
            var claims = new Dictionary<string, IBuildRecoveryParticipant>(PathComparer);
            var participantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IBuildRecoveryParticipant participant in participants
                         .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                if (participant == null
                    || string.IsNullOrWhiteSpace(participant.Id)
                    || !participantIds.Add(participant.Id.Trim()))
                {
                    AddBlockedIssue(
                        issues,
                        "RecoveryRegistry",
                        "Invalid recovery participant",
                        "Recovery participant identities must be non-empty and unique.",
                        transactionRoot);
                    continue;
                }

                IReadOnlyList<string> paths = participant.StateDirectoryRelativePaths;
                if (paths == null
                    || paths.Count == 0
                    || paths.Count > MaximumClaimsPerParticipant)
                {
                    AddBlockedIssue(
                        issues,
                        participant.Id,
                        "Missing recovery-state claim",
                        $"Every recovery participant must declare between 1 and {MaximumClaimsPerParticipant} project-relative state directories.",
                        transactionRoot);
                    continue;
                }

                tokenFacts.Add(
                    $"participant={participant.Id}|priority={participant.Priority}|coordinator={participant is IBuildRecoveryCoordinator}");
                foreach (string relativePath in paths)
                {
                    string claim;
                    try
                    {
                        claim = ResolveStateClaim(
                            normalizedProjectRoot,
                            transactionRoot,
                            relativePath);
                    }
                    catch (Exception exception)
                    {
                        AddBlockedIssue(
                            issues,
                            participant.Id,
                            "Invalid recovery-state claim",
                            exception.Message,
                            relativePath);
                        continue;
                    }

                    if (claims.TryGetValue(claim, out IBuildRecoveryParticipant owner))
                    {
                        AddBlockedIssue(
                            issues,
                            participant.Id,
                            "Overlapping recovery-state claim",
                            $"Participants '{owner.Id}' and '{participant.Id}' claim the same state directory.",
                            claim);
                        continue;
                    }

                    claims.Add(claim, participant);
                    tokenFacts.Add("claim=" + NormalizeForToken(claim));
                }
            }

            bool transactionRootExists =
                File.Exists(transactionRoot) || Directory.Exists(transactionRoot);
            bool transactionRootIsSafe = true;
            if (transactionRootExists)
            {
                try
                {
                    EnsureExistingPathHasNoReparsePoints(
                        normalizedProjectRoot,
                        transactionRoot,
                        "Build transaction root");
                }
                catch (Exception exception)
                {
                    transactionRootIsSafe = false;
                    AddBlockedIssue(
                        issues,
                        "Workspace",
                        "Unsafe transaction root",
                        exception.Message,
                        transactionRoot);
                }
            }

            if (!transactionRootIsSafe)
            {
                tokenFacts.Add("transaction-root=unsafe");
            }
            else if (File.Exists(transactionRoot))
            {
                AddBlockedIssue(
                    issues,
                    "Workspace",
                    "Invalid transaction root",
                    "The build transaction root is a file, not a directory.",
                    transactionRoot);
            }
            else if (Directory.Exists(transactionRoot))
            {
                InspectTransactionRoot(
                    normalizedProjectRoot,
                    transactionRoot,
                    claims,
                    issues,
                    tokenFacts);
            }
            else
            {
                tokenFacts.Add("transaction-root=absent");
            }

            bool hasBlockingIssue = issues.Any(issue =>
                issue.Severity == BuildWorkspaceIssueSeverity.Error);
            bool hasRecoveryIssue = issues.Any(issue =>
                issue.Severity == BuildWorkspaceIssueSeverity.Warning);
            BuildWorkspaceHealthStatus status;
            bool canRecover;
            string summary;
            if (editorIsBusy)
            {
                status = BuildWorkspaceHealthStatus.Busy;
                canRecover = false;
                summary = "Unity is compiling, updating assets, entering Play Mode, or building a Player. Refresh when the Editor is idle.";
            }
            else if (hasBlockingIssue)
            {
                status = BuildWorkspaceHealthStatus.Blocked;
                canRecover = false;
                summary = "Build transaction evidence is invalid, ambiguous, or owned by an unavailable integration. No automatic recovery was attempted.";
            }
            else if (hasRecoveryIssue)
            {
                status = BuildWorkspaceHealthStatus.RecoveryRequired;
                canRecover = true;
                summary = "One or more durable build transactions require explicit recovery before another build can start.";
            }
            else
            {
                status = BuildWorkspaceHealthStatus.Clean;
                canRecover = false;
                summary = "No pending build transaction evidence was found.";
            }

            tokenFacts.Add("status-evidence=" + (hasBlockingIssue ? "blocked" : hasRecoveryIssue ? "pending" : "clean"));
            return new BuildWorkspaceSnapshot(
                ComputeToken(tokenFacts),
                status,
                summary,
                issues,
                canRecover);
        }

        internal static BuildWorkspaceSnapshot Recover(
            string projectRoot,
            string expectedToken)
        {
            return Recover(
                projectRoot,
                expectedToken,
                BuildPipelineRegistry.ResolveRecoveryParticipants,
                IsEditorBusy,
                RefreshAssetDatabaseSynchronously);
        }

        internal static BuildWorkspaceSnapshot Recover(
            string projectRoot,
            string expectedToken,
            Func<IReadOnlyList<IBuildRecoveryParticipant>> participantResolver,
            Func<bool> editorBusyProbe,
            Action refreshAssets)
        {
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                throw new ArgumentException(
                    "An expected workspace snapshot token is required for recovery.",
                    nameof(expectedToken));
            }

            if (participantResolver == null)
            {
                throw new ArgumentNullException(nameof(participantResolver));
            }

            if (editorBusyProbe == null)
            {
                throw new ArgumentNullException(nameof(editorBusyProbe));
            }

            if (refreshAssets == null)
            {
                throw new ArgumentNullException(nameof(refreshAssets));
            }

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            string recoveryRunId = "recovery-"
                + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (BuildWorkspaceLease.Acquire(
                       normalizedProjectRoot,
                       recoveryRunId,
                       BuildWorkspaceOperation.Recovery))
            {
                return RecoverUnderLease(
                    normalizedProjectRoot,
                    expectedToken,
                    participantResolver,
                    editorBusyProbe,
                    refreshAssets);
            }
        }

        private static BuildWorkspaceSnapshot RecoverUnderLease(
            string normalizedProjectRoot,
            string expectedToken,
            Func<IReadOnlyList<IBuildRecoveryParticipant>> participantResolver,
            Func<bool> editorBusyProbe,
            Action refreshAssets)
        {
            IReadOnlyList<IBuildRecoveryParticipant> participants =
                participantResolver();
            BuildWorkspaceSnapshot before = Inspect(
                normalizedProjectRoot,
                participants,
                editorBusyProbe());
            if (!FixedTimeEquals(before.Token, expectedToken.Trim()))
            {
                throw new InvalidOperationException(
                    "Build workspace state changed after inspection. Refresh and review the new snapshot before recovery.");
            }

            if (before.Status == BuildWorkspaceHealthStatus.Clean)
            {
                return before;
            }

            if (before.Status != BuildWorkspaceHealthStatus.RecoveryRequired
                || !before.CanRecover)
            {
                throw new InvalidOperationException(
                    $"Build workspace status '{before.Status}' does not permit automatic recovery.");
            }

            var pendingParticipantIds = new HashSet<string>(
                before.Issues
                    .Where(issue => issue.Severity == BuildWorkspaceIssueSeverity.Warning)
                    .Select(issue => issue.ParticipantId),
                StringComparer.OrdinalIgnoreCase);
            var recoveryFailures = new List<Exception>();
            Exception refreshFailure = null;
            bool participantExecutionStarted = false;
            try
            {
                IBuildRecoveryParticipant[] pendingParticipants = participants
                    .Where(value => pendingParticipantIds.Contains(value.Id))
                    .ToArray();
                RecoverPhase(
                    pendingParticipants.Where(value => !(value is IBuildRecoveryCoordinator)),
                    normalizedProjectRoot,
                    recoveryFailures,
                    ref participantExecutionStarted);
                RecoverPhase(
                    pendingParticipants.Where(value => value is IBuildRecoveryCoordinator),
                    normalizedProjectRoot,
                    recoveryFailures,
                    ref participantExecutionStarted);
            }
            finally
            {
                if (participantExecutionStarted)
                {
                    try
                    {
                        refreshAssets();
                    }
                    catch (Exception exception)
                    {
                        refreshFailure = exception;
                    }
                }
            }

            ThrowRecoveryFailures(recoveryFailures, refreshFailure);
            // A synchronous AssetDatabase refresh may transiently report the
            // Editor as busy even though every durable participant has already
            // recovered. The pre-recovery snapshot checked the real busy state;
            // the terminal check must verify recovery evidence, not reject the
            // operation because of work initiated by this recovery itself.
            BuildWorkspaceSnapshot after = Inspect(
                normalizedProjectRoot,
                participantResolver(),
                editorIsBusy: false);
            if (after.Status != BuildWorkspaceHealthStatus.Clean)
            {
                throw new InvalidOperationException(
                    $"Explicit recovery completed, but workspace status is '{after.Status}'. "
                    + "The remaining evidence was preserved for inspection.");
            }

            return after;
        }

        private static void RefreshAssetDatabaseSynchronously()
        {
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate
                | ImportAssetOptions.ForceSynchronousImport);
        }

        private static void RecoverPhase(
            IEnumerable<IBuildRecoveryParticipant> participants,
            string projectRoot,
            ICollection<Exception> failures,
            ref bool executionStarted)
        {
            foreach (IBuildRecoveryParticipant participant in participants
                         .OrderByDescending(value => value.Priority)
                         .ThenBy(value => value.Id, StringComparer.Ordinal))
            {
                executionStarted = true;
                try
                {
                    participant.Recover(projectRoot);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        private static void ThrowRecoveryFailures(
            IReadOnlyList<Exception> recoveryFailures,
            Exception refreshFailure)
        {
            int recoveryFailureCount = recoveryFailures?.Count ?? 0;
            if (recoveryFailureCount == 0 && refreshFailure == null)
            {
                return;
            }

            if (recoveryFailureCount == 1 && refreshFailure == null)
            {
                ExceptionDispatchInfo.Capture(recoveryFailures[0]).Throw();
            }

            var failures = new List<Exception>(recoveryFailureCount + (refreshFailure == null ? 0 : 1));
            if (recoveryFailures != null)
            {
                failures.AddRange(recoveryFailures);
            }

            if (refreshFailure != null)
            {
                failures.Add(refreshFailure);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "One or more build workspace recovery operations failed.",
                    failures);
            }

            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        private static void InspectTransactionRoot(
            string projectRoot,
            string transactionRoot,
            IReadOnlyDictionary<string, IBuildRecoveryParticipant> claims,
            ICollection<BuildWorkspaceIssue> issues,
            ICollection<string> tokenFacts)
        {
            RejectReparsePoint(transactionRoot, "Build transaction root");
            string[] entries = Directory.GetFileSystemEntries(
                    transactionRoot,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, PathComparer)
                .ToArray();
            if (entries.Length > MaximumTopLevelEntries)
            {
                AddBlockedIssue(
                    issues,
                    "Workspace",
                    "Transaction inventory budget exceeded",
                    $"The transaction root contains more than {MaximumTopLevelEntries} top-level entries.",
                    transactionRoot);
                return;
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    AddBlockedIssue(
                        issues,
                        "Workspace",
                        "Unsafe transaction evidence",
                        "Build transaction state cannot contain symbolic links or reparse points.",
                        entry);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    AddTokenFact(entry, tokenFacts);
                    AddBlockedIssue(
                        issues,
                        "Workspace",
                        "Unknown transaction-root file",
                        "Only participant-owned state directories are valid below the transaction root.",
                        entry);
                    continue;
                }

                string normalizedEntry = NormalizeDirectoryPath(entry);
                bool meaningful = TryGetMeaningfulEvidence(
                    normalizedEntry,
                    tokenFacts,
                    out string evidencePath,
                    out string inspectionFailure);
                if (!string.IsNullOrEmpty(inspectionFailure))
                {
                    AddBlockedIssue(
                        issues,
                        claims.TryGetValue(normalizedEntry, out IBuildRecoveryParticipant failedOwner)
                            ? failedOwner.Id
                            : Path.GetFileName(normalizedEntry),
                        "Invalid transaction-state inventory",
                        inspectionFailure,
                        normalizedEntry);
                    continue;
                }

                if (!meaningful)
                {
                    continue;
                }

                if (!claims.TryGetValue(normalizedEntry, out IBuildRecoveryParticipant participant))
                {
                    AddBlockedIssue(
                        issues,
                        Path.GetFileName(normalizedEntry),
                        "Unavailable recovery participant",
                        "Durable transaction evidence exists, but no installed recovery participant claims this state directory. "
                        + "Reinstall the compatible integration before recovery or removal.",
                        evidencePath);
                    continue;
                }

                if (participant is IBuildRecoveryAvailability availability)
                {
                    bool isAvailable;
                    string unavailableReason;
                    try
                    {
                        isAvailable = availability.IsRecoveryAvailable(
                            projectRoot,
                            out unavailableReason);
                    }
                    catch (Exception exception)
                    {
                        isAvailable = false;
                        unavailableReason =
                            "Recovery availability inspection failed: " + exception.Message;
                    }

                    tokenFacts.Add(
                        $"availability={participant.Id}|available={isAvailable}");

                    if (!isAvailable)
                    {
                        AddBlockedIssue(
                            issues,
                            participant.Id,
                            "Recovery implementation unavailable",
                            string.IsNullOrWhiteSpace(unavailableReason)
                                ? "The recovery implementation is unavailable. Reinstall the owning package before recovery."
                                : BoundIssueMessage(unavailableReason),
                            evidencePath);
                        continue;
                    }
                }

                issues.Add(new BuildWorkspaceIssue(
                    BuildWorkspaceIssueSeverity.Warning,
                    participant.Id,
                    "Recovery required",
                    "Durable transaction evidence is present. A normal build will not modify it.",
                    evidencePath));
            }
        }

        private static bool TryGetMeaningfulEvidence(
            string stateDirectory,
            ICollection<string> tokenFacts,
            out string evidencePath,
            out string failure)
        {
            evidencePath = string.Empty;
            failure = string.Empty;
            try
            {
                RejectReparsePoint(stateDirectory, "Recovery state directory");
                string[] entries = Directory.GetFileSystemEntries(
                        stateDirectory,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, PathComparer)
                    .ToArray();
                if (entries.Length > MaximumTopLevelEntries)
                {
                    failure = $"Recovery state contains more than {MaximumTopLevelEntries} top-level entries.";
                    return false;
                }

                tokenFacts.Add("state-root=" + NormalizeForToken(stateDirectory));
                bool meaningful = false;
                foreach (string entry in entries)
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        failure = $"Recovery state contains a symbolic link or reparse point: '{entry}'.";
                        return false;
                    }

                    AddTokenFact(entry, tokenFacts);
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (!isDirectory && BuildRecoveryEvidencePolicy.IsInertLockFile(Path.GetFileName(entry)))
                    {
                        continue;
                    }

                    if (!meaningful)
                    {
                        evidencePath = entry;
                        meaningful = true;
                    }
                }

                return meaningful;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void AddTokenFact(string path, ICollection<string> facts)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory)
            {
                var info = new DirectoryInfo(path);
                facts.Add(
                    $"dir={NormalizeForToken(path)}|ticks={info.LastWriteTimeUtc.Ticks}|attr={(int)attributes}");
                return;
            }

            var file = new FileInfo(path);
            string hash = file.Length <= MaximumTokenFileBytes
                ? ComputeFileSha256(path)
                : "length-budget";
            facts.Add(
                $"file={NormalizeForToken(path)}|length={file.Length}|ticks={file.LastWriteTimeUtc.Ticks}|attr={(int)attributes}|sha256={hash}");
        }

        private static string ResolveStateClaim(
            string projectRoot,
            string transactionRoot,
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || !string.Equals(relativePath, relativePath.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Recovery-state claim path is required and may not have surrounding whitespace.");
            }

            string normalizedRelativePath = relativePath.Replace('\\', '/');
            string requiredPrefix = TransactionRootRelativePath + "/";
            string[] segments = normalizedRelativePath.Split('/');
            if (Path.IsPathRooted(normalizedRelativePath)
                || !normalizedRelativePath.StartsWith(requiredPrefix, StringComparison.Ordinal)
                || normalizedRelativePath.Length == requiredPrefix.Length
                || segments.Length != 3
                || segments.Any(segment =>
                    segment.Length == 0
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                throw new ArgumentException(
                    $"Recovery-state claim must be one direct participant-owned directory below '{TransactionRootRelativePath}': '{relativePath}'.");
            }

            string claim = NormalizeDirectoryPath(Path.Combine(
                projectRoot,
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsureStrictDescendant(transactionRoot, claim, "Recovery-state claim");
            return claim;
        }

        private static BuildWorkspaceSnapshot CreateDiscoveryFailureSnapshot(
            Exception exception)
        {
            string message = "Recovery participant discovery failed: " + exception.Message;
            var issue = new BuildWorkspaceIssue(
                BuildWorkspaceIssueSeverity.Error,
                "RecoveryRegistry",
                "Recovery registry unavailable",
                message,
                string.Empty);
            return new BuildWorkspaceSnapshot(
                ComputeToken(new[] { "workspace-format=1", message }),
                BuildWorkspaceHealthStatus.Blocked,
                "Recovery participants could not be resolved. No recovery was attempted.",
                new[] { issue },
                false);
        }

        private static void AddBlockedIssue(
            ICollection<BuildWorkspaceIssue> issues,
            string participantId,
            string title,
            string message,
            string evidencePath)
        {
            issues.Add(new BuildWorkspaceIssue(
                BuildWorkspaceIssueSeverity.Error,
                participantId,
                title,
                message,
                evidencePath));
        }

        private static string BoundIssueMessage(string message)
        {
            string value = message ?? string.Empty;
            return value.Length <= MaximumIssueMessageCharacters
                ? value
                : value.Substring(0, MaximumIssueMessageCharacters) + "...";
        }

        private static string ComputeToken(IEnumerable<string> facts)
        {
            string payload = string.Join(
                "\n",
                facts.OrderBy(value => value, StringComparer.Ordinal));
            byte[] bytes = StrictUtf8.GetBytes(payload);
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("x2"));
            }

            return builder.ToString();
        }

        private static bool FixedTimeEquals(string first, string second)
        {
            byte[] firstBytes = Encoding.ASCII.GetBytes(first ?? string.Empty);
            byte[] secondBytes = Encoding.ASCII.GetBytes(second ?? string.Empty);
            int difference = firstBytes.Length ^ secondBytes.Length;
            int length = Math.Min(firstBytes.Length, secondBytes.Length);
            for (int index = 0; index < length; index++)
            {
                difference |= firstBytes[index] ^ secondBytes[index];
            }

            return difference == 0;
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            string normalized = NormalizeDirectoryPath(projectRoot);
            if (!Directory.Exists(normalized))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root was not found: '{normalized}'.");
            }

            RejectReparsePoint(normalized, "Unity project root");
            string assets = Path.Combine(normalized, "Assets");
            string projectSettings = Path.Combine(normalized, "ProjectSettings");
            if (!Directory.Exists(assets) || !Directory.Exists(projectSettings))
            {
                throw new InvalidOperationException(
                    $"Path is not a Unity project root: '{normalized}'.");
            }

            return normalized;
        }

        private static string GetCurrentProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static bool IsEditorBusy()
        {
            return EditorBuildAvailabilityPolicy.IsBusy();
        }

        private static void RejectReparsePoint(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} cannot be a symbolic link or reparse point: '{path}'.");
            }
        }

        private static void EnsureExistingPathHasNoReparsePoints(
            string trustedRoot,
            string candidate,
            string label)
        {
            string root = NormalizeDirectoryPath(trustedRoot);
            string path = NormalizeDirectoryPath(candidate);
            EnsureStrictDescendant(root, path, label);

            string relative = path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            string[] segments = relative.Split(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    break;
                }

                RejectReparsePoint(current, label);
            }
        }

        private static void EnsureStrictDescendant(
            string root,
            string candidate,
            string label)
        {
            string prefix = NormalizeDirectoryPath(root) + Path.DirectorySeparatorChar;
            string normalizedCandidate = NormalizeDirectoryPath(candidate);
            if (!normalizedCandidate.StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException(
                    $"{label} escaped its trusted root. Root='{root}', path='{candidate}'.");
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeForToken(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/');
        }

        private static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }

    internal static class BuildRecoveryEvidencePolicy
    {
        internal static bool IsInertLockFile(string fileName)
        {
            return string.Equals(fileName, "active.lock", StringComparison.Ordinal)
                || string.Equals(fileName, "build.lock", StringComparison.Ordinal);
        }
    }
}
