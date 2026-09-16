using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// One directory tree that participates in a portable-path audit. Scopes are
    /// explicit so a build never scans the whole checkout implicitly, and each scope
    /// decides its own strictness: content that the Player copies verbatim needs
    /// stricter rules than content that Unity always renames.
    /// </summary>
    public sealed class PortableAssetPathAuditScope
    {
        public PortableAssetPathAuditScope(
            string displayName,
            string absoluteRoot,
            bool rejectNonAscii,
            bool enforceWin32PathBudget,
            bool isError,
            bool nonAsciiIsError = true,
            IReadOnlyList<string> excludedRelativePrefixes = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Audit scope display name is required.", nameof(displayName));
            }

            if (string.IsNullOrWhiteSpace(absoluteRoot))
            {
                throw new ArgumentException("Audit scope root is required.", nameof(absoluteRoot));
            }

            if (!Path.IsPathRooted(absoluteRoot))
            {
                throw new ArgumentException(
                    "Audit scope root must be an absolute path.",
                    nameof(absoluteRoot));
            }

            DisplayName = displayName.Trim();
            AbsoluteRoot = Path.GetFullPath(absoluteRoot);
            RejectNonAscii = rejectNonAscii;
            EnforceWin32PathBudget = enforceWin32PathBudget;
            IsError = isError;
            NonAsciiIsError = nonAsciiIsError;
            ExcludedRelativePrefixes = NormalizeExcludedPrefixes(
                excludedRelativePrefixes,
                nameof(excludedRelativePrefixes));
        }

        public string DisplayName { get; }

        public string AbsoluteRoot { get; }

        /// <summary>
        /// Prefixes relative to <see cref="AbsoluteRoot"/> that are skipped, using
        /// '/'-separated segments. Exclusions belong to the scope on purpose: with one
        /// shared project-relative list, an exclusion that covers another scope's root
        /// would silently drop that scope. Here a prefix can only ever remove entries
        /// below the scope that declares it, and a scope is scanned whenever it is
        /// declared.
        /// </summary>
        public IReadOnlyList<string> ExcludedRelativePrefixes { get; }

        /// <summary>
        /// Reject any segment that leaves printable ASCII. Enabled for every surface
        /// whose names survive into the Player, an archive, or a URL.
        /// </summary>
        public bool RejectNonAscii { get; }

        /// <summary>
        /// Reject entries whose composed path exceeds the Win32 MAX_PATH budget that
        /// the Unity toolchain and Windows build agents still enforce.
        /// </summary>
        public bool EnforceWin32PathBudget { get; }

        /// <summary>
        /// Severity of this scope's violations. <c>false</c> downgrades them to
        /// warnings for content that is known not to reach a Player.
        /// </summary>
        public bool IsError { get; }

        /// <summary>
        /// Severity of non-ASCII segments specifically, when
        /// <see cref="RejectNonAscii"/> is enabled. Names that the Player copies
        /// verbatim are addressed at runtime and must never leave ASCII; names inside
        /// the ordinary asset tree are re-addressed by Unity and are a portability
        /// risk (case-sensitive checkouts, provider bundle names, MAX_PATH) rather
        /// than an immediate runtime failure.
        /// </summary>
        public bool NonAsciiIsError { get; }

        private static IReadOnlyList<string> NormalizeExcludedPrefixes(
            IReadOnlyList<string> excludedRelativePrefixes,
            string parameterName)
        {
            if (excludedRelativePrefixes == null || excludedRelativePrefixes.Count == 0)
            {
                return Array.Empty<string>();
            }

            var normalized = new List<string>(excludedRelativePrefixes.Count);
            for (int index = 0; index < excludedRelativePrefixes.Count; index++)
            {
                string prefix = excludedRelativePrefixes[index];
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                prefix = prefix.Replace('\\', '/').Trim('/');
                if (prefix.Length == 0)
                {
                    throw new ArgumentException(
                        $"Excluded prefix at index {index} is empty after normalization. " +
                        "A prefix must name a path below the scope root; not declaring the scope is how it is skipped.",
                        parameterName);
                }

                normalized.Add(prefix);
            }

            return normalized.Count == 0
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : normalized.AsReadOnly();
        }
    }

    /// <summary>
    /// One rejected entry. Carries the scope, the project-independent relative path,
    /// the offending segment, and the rule that rejected it.
    /// </summary>
    public sealed class PortableAssetPathFinding
    {
        internal PortableAssetPathFinding(
            string scopeName,
            string relativePath,
            string segment,
            PortablePathViolationKind kind,
            string message,
            bool isError)
        {
            ScopeName = scopeName ?? string.Empty;
            RelativePath = relativePath ?? string.Empty;
            Segment = segment ?? string.Empty;
            Kind = kind;
            Message = message ?? string.Empty;
            IsError = isError;
        }

        public string ScopeName { get; }

        public string RelativePath { get; }

        public string Segment { get; }

        public PortablePathViolationKind Kind { get; }

        public string Message { get; }

        public bool IsError { get; }
    }

    /// <summary>
    /// Bounded audit result. The audit never throws for content problems: it reports
    /// them, so the caller decides whether the run fails or only warns.
    /// </summary>
    public sealed class PortableAssetPathAuditResult
    {
        internal PortableAssetPathAuditResult(
            IReadOnlyList<PortableAssetPathFinding> findings,
            IReadOnlyList<string> errorMessages,
            IReadOnlyList<string> warningMessages,
            int scannedEntryCount,
            int ignoredEntryCount,
            int reparsePointCount,
            int unreadableDirectoryCount,
            string firstUnreadableDirectory,
            int omittedFindingCount,
            bool entryBudgetExceeded)
        {
            Findings = findings;
            ErrorMessages = errorMessages;
            WarningMessages = warningMessages;
            ScannedEntryCount = scannedEntryCount;
            IgnoredEntryCount = ignoredEntryCount;
            ReparsePointCount = reparsePointCount;
            UnreadableDirectoryCount = unreadableDirectoryCount;
            FirstUnreadableDirectory = firstUnreadableDirectory ?? string.Empty;
            OmittedFindingCount = omittedFindingCount;
            EntryBudgetExceeded = entryBudgetExceeded;
        }

        public IReadOnlyList<PortableAssetPathFinding> Findings { get; }

        public IReadOnlyList<string> ErrorMessages { get; }

        public IReadOnlyList<string> WarningMessages { get; }

        public int ScannedEntryCount { get; }

        /// <summary>
        /// Entries Unity itself ignores during import: names starting with '.', or
        /// ending with '~'. They are reported separately because a developer who
        /// expects them to ship has a different problem than one with a bad name.
        /// </summary>
        public int IgnoredEntryCount { get; }

        /// <summary>
        /// Symbolic links and junctions that the audit refuses to follow. They are
        /// reported so a linked-in content root cannot hide unportable names.
        /// </summary>
        public int ReparsePointCount { get; }

        /// <summary>
        /// Directories the audit could not enumerate. A gate must not certify
        /// coverage it does not have, so this is reported instead of being dropped.
        /// </summary>
        public int UnreadableDirectoryCount { get; }

        /// <summary>
        /// First directory that could not be read, relative to the project root.
        /// Empty when <see cref="UnreadableDirectoryCount"/> is zero. Kept as one
        /// path so the diagnosis is actionable without unbounded storage.
        /// </summary>
        public string FirstUnreadableDirectory { get; }

        /// <summary>
        /// Findings past the reporting cap. Non-zero means the audit stopped
        /// collecting details but the check is still valid.
        /// </summary>
        public int OmittedFindingCount { get; }

        /// <summary>
        /// The audit stopped early because the entry budget was exhausted. The
        /// reported result covers only the entries that were scanned.
        /// </summary>
        public bool EntryBudgetExceeded { get; }

        /// <summary>
        /// True when the scan did not cover everything it was asked to cover, for
        /// any reason. A caller that treats the audit as a gate must fail on this:
        /// passing a build on a partial scan silently removes the check.
        /// </summary>
        public bool IsCoverageIncomplete => EntryBudgetExceeded || UnreadableDirectoryCount > 0;

        public int ErrorCount => ErrorMessages.Count;

        /// <summary>
        /// Actionable explanation for why coverage is incomplete, or <c>null</c> when
        /// the scan was complete. Kept beside the counters so a gate cannot report
        /// the condition without saying what to do about it.
        /// </summary>
        public string DescribeIncompleteCoverage(string suggestedMaximumEntryCountSetting)
        {
            if (EntryBudgetExceeded)
            {
                return "Asset path audit coverage is incomplete: the scan stopped at its entry budget, " +
                    $"so entries beyond {ScannedEntryCount} were never checked. Raise " +
                    $"{suggestedMaximumEntryCountSetting} deliberately, or exclude content that never " +
                    "reaches a Player.";
            }

            if (UnreadableDirectoryCount > 0)
            {
                return "Asset path audit coverage is incomplete: " +
                    $"{UnreadableDirectoryCount} {DescribeDirectories(UnreadableDirectoryCount)} " +
                    $"could not be read, starting at '{FirstUnreadableDirectory}'. " +
                    "Fix permissions or the checkout before trusting this gate.";
            }

            return null;
        }

        private static string DescribeDirectories(int count)
        {
            return count == 1 ? "directory" : "directories";
        }

        public string DescribeSummary()
        {
            string summary =
                $"Scanned {ScannedEntryCount} entries, {IgnoredEntryCount} ignored by Unity, " +
                $"{ReparsePointCount} reparse point(s); {ErrorMessages.Count} error(s), " +
                $"{WarningMessages.Count} warning(s).";
            if (OmittedFindingCount > 0)
            {
                summary += $" {OmittedFindingCount} further finding(s) were not reported.";
            }

            if (EntryBudgetExceeded)
            {
                summary += " The scan stopped at the entry budget, so the result is partial.";
            }

            if (UnreadableDirectoryCount > 0)
            {
                summary += $" {UnreadableDirectoryCount} {DescribeDirectories(UnreadableDirectoryCount)} " +
                    "could not be read.";
            }

            return summary;
        }
    }

    /// <summary>
    /// Zero-write, bounded scanner for names that a working copy accepts but a Player
    /// build, a CDN, or a case-sensitive checkout does not.
    ///
    /// Bounded by design: a total entry budget, a finding cap, deterministic ordering,
    /// no symlink traversal, and no exception on unreadable content. A build
    /// preflight must never turn into an unbounded walk.
    /// </summary>
    public static class PortableAssetPathAudit
    {
        public const int DefaultMaximumEntryCount = 200000;
        public const int DefaultMaximumFindingCount = 100;

        /// <summary>
        /// Names below the project root that never contain shippable content. Skipped
        /// only as a direct child of a scope root, so a real content folder that
        /// happens to be called "Logs" deeper in the tree is still audited.
        /// </summary>
        private static readonly string[] NonContentDirectoryNames =
        {
            ".git",
            "Library",
            "Temp",
            "obj",
            "Obj",
            "Logs",
            "UserSettings"
        };

        /// <summary>
        /// Strict audit of <c>Assets/StreamingAssets</c>. Unity copies this tree
        /// verbatim into the Player, so every name here must survive the target
        /// platform's file system, archive index, and URL handling.
        /// </summary>
        public static PortableAssetPathAuditResult AuditStreamingAssets(string projectRoot)
        {
            string root = Path.Combine(
                Path.GetFullPath(projectRoot),
                "Assets",
                "StreamingAssets");
            var scopes = new[]
            {
                new PortableAssetPathAuditScope(
                    "StreamingAssets",
                    root,
                    rejectNonAscii: true,
                    enforceWin32PathBudget: true,
                    isError: true)
            };

            return Audit(projectRoot, scopes);
        }

        /// <summary>
        /// Scans every declared scope and reports the names that are not portable.
        /// Each scope applies its own <see cref="PortableAssetPathAuditScope.ExcludedRelativePrefixes"/>.
        /// </summary>
        public static PortableAssetPathAuditResult Audit(
            string projectRoot,
            IReadOnlyList<PortableAssetPathAuditScope> scopes,
            int maximumEntryCount = DefaultMaximumEntryCount,
            int maximumFindingCount = DefaultMaximumFindingCount)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            if (scopes == null || scopes.Count == 0)
            {
                throw new ArgumentException("At least one audit scope is required.", nameof(scopes));
            }

            if (maximumEntryCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumEntryCount),
                    maximumEntryCount,
                    "The audit entry budget must be positive.");
            }

            if (maximumFindingCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumFindingCount),
                    maximumFindingCount,
                    "The audit finding budget must be positive.");
            }

            string project = Path.GetFullPath(projectRoot);
            var findings = new List<PortableAssetPathFinding>();
            var errorMessages = new List<string>();
            var warningMessages = new List<string>();
            int scannedEntryCount = 0;
            int ignoredEntryCount = 0;
            int reparsePointCount = 0;
            int unreadableDirectoryCount = 0;
            string firstUnreadableDirectory = string.Empty;
            int omittedFindingCount = 0;
            bool entryBudgetExceeded = false;

            var pending = new Stack<PendingDirectory>();
            for (int scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
            {
                PortableAssetPathAuditScope scope = scopes[scopeIndex]
                    ?? throw new ArgumentException(
                        $"Audit scope at index {scopeIndex} is null.",
                        nameof(scopes));
                EnsureScopeInsideProject(project, scope);
                if (Directory.Exists(scope.AbsoluteRoot))
                {
                    pending.Push(new PendingDirectory(
                        scope,
                        scope.AbsoluteRoot,
                        string.Empty,
                        depth: 0,
                        scope.ExcludedRelativePrefixes));
                }
            }

            while (pending.Count > 0)
            {
                PendingDirectory current = pending.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(current.AbsolutePath);
                }
                catch (Exception)
                {
                    // A permissions problem is not a naming problem, so it is not
                    // reported as a finding. It still means the gate did not see
                    // everything it was asked to see, and dropping it silently would
                    // be a hole a locked directory could hide content behind.
                    unreadableDirectoryCount++;
                    if (firstUnreadableDirectory.Length == 0)
                    {
                        firstUnreadableDirectory = current.RelativePath.Length == 0
                            ? current.Scope.DisplayName
                            : current.Scope.DisplayName + "/" + current.RelativePath;
                    }

                    continue;
                }

                // Ordinal sorting keeps CI reports reproducible across hosts.
                Array.Sort(entries, StringComparer.Ordinal);
                for (int index = 0; index < entries.Length; index++)
                {
                    if (scannedEntryCount >= maximumEntryCount)
                    {
                        entryBudgetExceeded = true;
                        break;
                    }

                    scannedEntryCount++;
                    string entryPath = entries[index];
                    string name = Path.GetFileName(entryPath);
                    if (IsUnityIgnoredName(name))
                    {
                        ignoredEntryCount++;
                        continue;
                    }

                    if (current.Depth == 0 && IsNonContentDirectoryName(name))
                    {
                        ignoredEntryCount++;
                        continue;
                    }

                    string relativePath = current.RelativePath.Length == 0
                        ? name
                        : current.RelativePath + "/" + name;

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entryPath);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Never follow a link: the audit must stay inside the declared
                        // scope. Reported so a linked content root cannot silently hide
                        // unportable names from the check.
                        reparsePointCount++;
                        continue;
                    }

                    if (IsExcluded(current.Exclusions, relativePath))
                    {
                        ignoredEntryCount++;
                        continue;
                    }

                    AddFinding(
                        findings,
                        errorMessages,
                        warningMessages,
                        current.Scope,
                        relativePath,
                        name,
                        entryPath,
                        ref omittedFindingCount,
                        maximumFindingCount);

                    if (isDirectory)
                    {
                        pending.Push(new PendingDirectory(
                            current.Scope,
                            entryPath,
                            relativePath,
                            current.Depth + 1,
                            current.Exclusions));
                    }
                }

                if (entryBudgetExceeded)
                {
                    break;
                }
            }

            return new PortableAssetPathAuditResult(
                findings.AsReadOnly(),
                errorMessages.AsReadOnly(),
                warningMessages.AsReadOnly(),
                scannedEntryCount,
                ignoredEntryCount,
                reparsePointCount,
                unreadableDirectoryCount,
                firstUnreadableDirectory,
                omittedFindingCount,
                entryBudgetExceeded);
        }

        private static void AddFinding(
            List<PortableAssetPathFinding> findings,
            List<string> errorMessages,
            List<string> warningMessages,
            PortableAssetPathAuditScope scope,
            string relativePath,
            string segment,
            string absolutePath,
            ref int omittedFindingCount,
            int maximumFindingCount)
        {
            PortablePathSegmentViolation violation = PortableAssetPathPolicy.ValidateSegment(
                segment,
                scope.RejectNonAscii);

            string message;
            PortablePathViolationKind kind;
            if (violation.HasViolation)
            {
                kind = violation.Kind;
                message =
                    $"{scope.DisplayName}: '{relativePath}'. Segment '{segment}' " +
                    PortableAssetPathPolicy.DescribeSegmentViolation(
                        violation.Kind,
                        violation.OffendingText);
            }
            else if (scope.EnforceWin32PathBudget
                     && absolutePath.Length > BuildPathPolicy.Win32MaxPathCharacters)
            {
                kind = PortablePathViolationKind.PathTooLong;
                message =
                    $"{scope.DisplayName}: '{relativePath}' composes a {absolutePath.Length}-character " +
                    $"path, which exceeds the Win32 MAX_PATH budget of " +
                    $"{BuildPathPolicy.Win32MaxPathCharacters}. Shorten the repository checkout or the name.";
            }
            else
            {
                return;
            }

            if (findings.Count >= maximumFindingCount)
            {
                omittedFindingCount++;
                return;
            }

            bool isError = scope.IsError
                && (kind != PortablePathViolationKind.NonAsciiCharacter
                    || scope.NonAsciiIsError);
            findings.Add(new PortableAssetPathFinding(
                scope.DisplayName,
                relativePath,
                segment,
                kind,
                message,
                isError));
            if (isError)
            {
                errorMessages.Add(message);
            }
            else
            {
                warningMessages.Add(message);
            }
        }

        private static void EnsureScopeInsideProject(
            string projectRoot,
            PortableAssetPathAuditScope scope)
        {
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, scope.AbsoluteRoot))
            {
                throw new ArgumentException(
                    $"Audit scope '{scope.DisplayName}' must resolve inside the Unity project root. " +
                    $"Project: '{projectRoot}', scope: '{scope.AbsoluteRoot}'.",
                    nameof(scope));
            }
        }

        private static bool IsUnityIgnoredName(string name)
        {
            // Unity skips dot-prefixed entries and '~'-suffixed entries (the UPM
            // Samples~ convention) during import, so they never reach a build.
            return name.Length == 0
                || name[0] == '.'
                || name[name.Length - 1] == '~';
        }

        private static bool IsNonContentDirectoryName(string name)
        {
            for (int index = 0; index < NonContentDirectoryNames.Length; index++)
            {
                if (string.Equals(
                        name,
                        NonContentDirectoryNames[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcluded(
            IReadOnlyList<string> exclusions,
            string relativePath)
        {
            for (int index = 0; index < exclusions.Count; index++)
            {
                string prefix = exclusions[index];
                if (string.Equals(relativePath, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (relativePath.Length > prefix.Length
                    && relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && relativePath[prefix.Length] == '/')
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct PendingDirectory
        {
            public PendingDirectory(
                PortableAssetPathAuditScope scope,
                string absolutePath,
                string relativePath,
                int depth,
                IReadOnlyList<string> exclusions)
            {
                Scope = scope;
                AbsolutePath = absolutePath;
                RelativePath = relativePath;
                Depth = depth;
                Exclusions = exclusions;
            }

            public PortableAssetPathAuditScope Scope { get; }

            public string AbsolutePath { get; }

            public string RelativePath { get; }

            public int Depth { get; }

            /// <summary>Prefixes relative to the scope root that are skipped.</summary>
            public IReadOnlyList<string> Exclusions { get; }
        }
    }
}
