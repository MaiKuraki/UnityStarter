using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Build.Pipeline.Editor
{
    internal enum ProjectSettingsStateChangeKind
    {
        Added,
        Deleted,
        Modified
    }

    internal sealed class ProjectSettingsStateChange
    {
        internal ProjectSettingsStateChange(
            string projectRelativePath,
            ProjectSettingsStateChangeKind kind,
            string baselineSha256,
            string currentSha256)
        {
            ProjectRelativePath = projectRelativePath
                ?? throw new ArgumentNullException(nameof(projectRelativePath));
            Kind = kind;
            BaselineSha256 = baselineSha256;
            CurrentSha256 = currentSha256;
        }

        internal string ProjectRelativePath { get; }

        internal ProjectSettingsStateChangeKind Kind { get; }

        internal string BaselineSha256 { get; }

        internal string CurrentSha256 { get; }
    }

    internal sealed class ProjectSettingsStateVerificationResult
    {
        private const int MaximumExceptionPaths = 32;
        private readonly IReadOnlyList<ProjectSettingsStateChange> changes;

        internal ProjectSettingsStateVerificationResult(
            IReadOnlyList<ProjectSettingsStateChange> changes)
        {
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            this.changes = new ReadOnlyCollection<ProjectSettingsStateChange>(
                changes.ToArray());
        }

        internal IReadOnlyList<ProjectSettingsStateChange> Changes => changes;

        internal bool IsClean => changes.Count == 0;

        internal void ThrowIfChanged(string operation = null)
        {
            if (IsClean)
            {
                return;
            }

            string context = string.IsNullOrWhiteSpace(operation)
                ? "ProjectSettings verification"
                : operation.Trim();
            string details = string.Join(
                "; ",
                changes
                    .Take(MaximumExceptionPaths)
                    .Select(change =>
                        $"{change.Kind}: '{change.ProjectRelativePath}'"));
            if (changes.Count > MaximumExceptionPaths)
            {
                details += $"; and {changes.Count - MaximumExceptionPaths} more change(s)";
            }

            throw new InvalidOperationException(
                $"{context} detected {changes.Count} unauthorized ProjectSettings change(s). "
                + details
                + ". No files were modified by the state guard.");
        }
    }

    /// <summary>
    /// Captures and verifies a deterministic, read-only SHA-256 baseline for every
    /// regular file below the project's ProjectSettings directory.
    /// </summary>
    /// <remarks>
    /// Callers must hold the build workspace lease while using this guard. An
    /// authorization window changes only the in-memory baseline; this type never
    /// creates, deletes, or writes project files.
    /// </remarks>
    internal sealed class ProjectSettingsStateGuard
    {
        private const string ProjectSettingsDirectoryName = "ProjectSettings";
        private const int MaximumTrackedFileCount = 16384;
        private const long MaximumTrackedFileLength = 512L * 1024L * 1024L;
        private const long MaximumTrackedTotalLength = 2L * 1024L * 1024L * 1024L;
        private const int MaximumRelativePathCharacters = 2048;
        private const int HashBufferSize = 64 * 1024;

        private readonly string projectRoot;
        private readonly string projectSettingsRoot;
        private readonly SortedDictionary<string, string> baseline;
        private AuthorizationWindow activeAuthorization;

        private ProjectSettingsStateGuard(
            string projectRoot,
            string projectSettingsRoot,
            SortedDictionary<string, string> baseline)
        {
            this.projectRoot = projectRoot;
            this.projectSettingsRoot = projectSettingsRoot;
            this.baseline = baseline;
        }

        internal static ProjectSettingsStateGuard Capture(string projectRoot)
        {
            string normalizedProjectRoot = NormalizeExistingDirectory(
                projectRoot,
                "Unity project root");
            RejectReparsePoint(normalizedProjectRoot, "Unity project root");

            string settingsRoot = Path.GetFullPath(Path.Combine(
                normalizedProjectRoot,
                ProjectSettingsDirectoryName));
            EnsureStrictDescendant(
                normalizedProjectRoot,
                settingsRoot,
                "ProjectSettings directory");
            if (!Directory.Exists(settingsRoot))
            {
                if (File.Exists(settingsRoot))
                {
                    throw new InvalidOperationException(
                        $"ProjectSettings path is not a directory: '{settingsRoot}'.");
                }

                throw new DirectoryNotFoundException(
                    $"ProjectSettings directory was not found: '{settingsRoot}'.");
            }

            RejectReparsePoint(settingsRoot, "ProjectSettings directory");
            var guard = new ProjectSettingsStateGuard(
                normalizedProjectRoot,
                NormalizeDirectoryPath(settingsRoot),
                new SortedDictionary<string, string>(PathComparer));
            guard.ReplaceBaseline(guard.CaptureSnapshot());
            return guard;
        }

        internal ProjectSettingsStateVerificationResult Verify()
        {
            EnsureNoAuthorizationIsActive("verify ProjectSettings");
            return Compare(baseline, CaptureSnapshot());
        }

        internal void VerifyOrThrow(string operation = null)
        {
            Verify().ThrowIfChanged(operation);
        }

        internal AuthorizationWindow BeginAuthorization(
            params string[] projectRelativePaths)
        {
            return BeginAuthorization(
                requireCleanBaseline: true,
                projectRelativePaths);
        }

        internal AuthorizationWindow BeginRecoveryAuthorization(
            params string[] projectRelativePaths)
        {
            return BeginAuthorization(
                requireCleanBaseline: false,
                projectRelativePaths);
        }

        private AuthorizationWindow BeginAuthorization(
            bool requireCleanBaseline,
            params string[] projectRelativePaths)
        {
            EnsureNoAuthorizationIsActive("begin another authorization window");
            if (projectRelativePaths == null)
            {
                throw new ArgumentNullException(nameof(projectRelativePaths));
            }

            if (projectRelativePaths.Length == 0)
            {
                throw new ArgumentException(
                    "At least one explicit ProjectSettings file is required.",
                    nameof(projectRelativePaths));
            }

            SortedDictionary<string, string> openingSnapshot = CaptureSnapshot();
            if (requireCleanBaseline)
            {
                Compare(baseline, openingSnapshot)
                    .ThrowIfChanged("Opening a ProjectSettings authorization window");
            }

            var allowedPaths = new SortedSet<string>(PathComparer);
            for (int index = 0; index < projectRelativePaths.Length; index++)
            {
                string normalizedPath = NormalizeAuthorizedPath(
                    projectRelativePaths[index]);
                if (!allowedPaths.Add(normalizedPath))
                {
                    throw new ArgumentException(
                        $"ProjectSettings authorization path was specified more than once: '{normalizedPath}'.",
                        nameof(projectRelativePaths));
                }
            }

            var window = new AuthorizationWindow(
                this,
                allowedPaths.ToArray(),
                openingSnapshot);
            activeAuthorization = window;
            return window;
        }

        private ProjectSettingsStateVerificationResult CommitAuthorization(
            AuthorizationWindow window,
            IReadOnlyCollection<string> allowedPaths,
            SortedDictionary<string, string> openingSnapshot)
        {
            EnsureActiveAuthorization(window);
            try
            {
                SortedDictionary<string, string> current = CaptureSnapshot();
                ProjectSettingsStateVerificationResult windowChanges = Compare(
                    openingSnapshot,
                    current);
                ProjectSettingsStateChange[] unauthorized = windowChanges.Changes
                    .Where(change => !allowedPaths.Contains(
                        change.ProjectRelativePath,
                        PathComparer))
                    .ToArray();
                if (unauthorized.Length > 0)
                {
                    new ProjectSettingsStateVerificationResult(unauthorized)
                        .ThrowIfChanged(
                            "Committing a ProjectSettings authorization window");
                }

                ProjectSettingsStateVerificationResult baselineChanges = Compare(
                    baseline,
                    current);

                foreach (string allowedPath in allowedPaths)
                {
                    if (current.TryGetValue(allowedPath, out string sha256))
                    {
                        baseline[allowedPath] = sha256;
                    }
                    else
                    {
                        baseline.Remove(allowedPath);
                    }
                }

                return baselineChanges;
            }
            finally
            {
                activeAuthorization = null;
            }
        }

        private void CancelAuthorization(AuthorizationWindow window)
        {
            EnsureActiveAuthorization(window);
            activeAuthorization = null;
        }

        private SortedDictionary<string, string> CaptureSnapshot()
        {
            EnsureNoReparsePoints(
                projectSettingsRoot,
                projectSettingsRoot,
                includeAnchor: true);

            var snapshot = new SortedDictionary<string, string>(PathComparer);
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(projectSettingsRoot);
            long totalLength = 0;

            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                EnsureStrictDescendantOrEqual(
                    projectSettingsRoot,
                    directory,
                    "ProjectSettings scan directory");
                RejectReparsePoint(directory, "ProjectSettings scan directory");

                string[] entries = Directory
                    .EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFullPath)
                    .OrderBy(path => path, PathComparer)
                    .ToArray();
                for (int index = entries.Length - 1; index >= 0; index--)
                {
                    string entry = entries[index];
                    EnsureStrictDescendant(
                        projectSettingsRoot,
                        entry,
                        "ProjectSettings entry");
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"ProjectSettings cannot contain a symbolic link or reparse point: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(entry);
                        continue;
                    }

                    if ((attributes & FileAttributes.Device) != 0)
                    {
                        throw new InvalidOperationException(
                            $"ProjectSettings contains an unsupported device entry: '{entry}'.");
                    }

                    string relativePath = GetProjectRelativePath(entry);
                    if (relativePath.Length > MaximumRelativePathCharacters)
                    {
                        throw new PathTooLongException(
                            $"ProjectSettings relative path exceeds {MaximumRelativePathCharacters} characters: '{relativePath}'.");
                    }

                    if (snapshot.Count >= MaximumTrackedFileCount)
                    {
                        throw new InvalidOperationException(
                            $"ProjectSettings contains more than {MaximumTrackedFileCount} regular files.");
                    }

                    string sha256 = ComputeSha256(entry, out long length);
                    if (length > MaximumTrackedFileLength)
                    {
                        throw new InvalidOperationException(
                            $"ProjectSettings file exceeds the {MaximumTrackedFileLength}-byte verification budget: '{relativePath}'.");
                    }

                    totalLength = checked(totalLength + length);
                    if (totalLength > MaximumTrackedTotalLength)
                    {
                        throw new InvalidOperationException(
                            $"ProjectSettings files exceed the {MaximumTrackedTotalLength}-byte verification budget.");
                    }

                    snapshot.Add(relativePath, sha256);
                }
            }

            return snapshot;
        }

        private string NormalizeAuthorizedPath(string projectRelativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRelativePath))
            {
                throw new ArgumentException(
                    "ProjectSettings authorization path is required.",
                    nameof(projectRelativePath));
            }

            if (!string.Equals(
                    projectRelativePath,
                    projectRelativePath.Trim(),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"ProjectSettings authorization path may not start or end with whitespace: '{projectRelativePath}'.",
                    nameof(projectRelativePath));
            }

            string normalized = projectRelativePath.Replace('\\', '/');
            BuildPathPolicy.ValidatePortableProjectRelativePath(
                normalized,
                "ProjectSettings authorization path");
            if (Path.IsPathRooted(normalized)
                || normalized.StartsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"ProjectSettings authorization path must be project-relative: '{projectRelativePath}'.",
                    nameof(projectRelativePath));
            }

            string requiredPrefix = ProjectSettingsDirectoryName + "/";
            if (!normalized.StartsWith(requiredPrefix, PathComparison)
                || normalized.Length == requiredPrefix.Length)
            {
                throw new ArgumentException(
                    $"Authorization is restricted to files below '{ProjectSettingsDirectoryName}/': '{projectRelativePath}'.",
                    nameof(projectRelativePath));
            }

            if (normalized.Length > MaximumRelativePathCharacters)
            {
                throw new PathTooLongException(
                    $"ProjectSettings authorization path exceeds {MaximumRelativePathCharacters} characters: '{normalized}'.");
            }

            string absolutePath = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            EnsureStrictDescendant(
                projectSettingsRoot,
                absolutePath,
                "ProjectSettings authorization path");
            if (Directory.Exists(absolutePath))
            {
                throw new ArgumentException(
                    $"ProjectSettings authorization path must identify a file, not a directory: '{normalized}'.",
                    nameof(projectRelativePath));
            }

            EnsureNoReparsePoints(
                projectSettingsRoot,
                absolutePath,
                includeAnchor: true);
            return normalized;
        }

        private string GetProjectRelativePath(string absolutePath)
        {
            string normalizedPath = Path.GetFullPath(absolutePath);
            EnsureStrictDescendant(
                projectRoot,
                normalizedPath,
                "ProjectSettings file");
            string prefix = AppendDirectorySeparator(projectRoot);
            return normalizedPath.Substring(prefix.Length).Replace('\\', '/');
        }

        private void ReplaceBaseline(SortedDictionary<string, string> replacement)
        {
            baseline.Clear();
            foreach (KeyValuePair<string, string> entry in replacement)
            {
                baseline.Add(entry.Key, entry.Value);
            }
        }

        private void EnsureNoAuthorizationIsActive(string operation)
        {
            if (activeAuthorization != null)
            {
                throw new InvalidOperationException(
                    $"Cannot {operation} while a ProjectSettings authorization window is active.");
            }
        }

        private void EnsureActiveAuthorization(AuthorizationWindow window)
        {
            if (!ReferenceEquals(activeAuthorization, window))
            {
                throw new InvalidOperationException(
                    "The ProjectSettings authorization window is not active or does not belong to this guard.");
            }
        }

        private static ProjectSettingsStateVerificationResult Compare(
            SortedDictionary<string, string> expected,
            SortedDictionary<string, string> current)
        {
            var paths = new SortedSet<string>(expected.Keys, PathComparer);
            paths.UnionWith(current.Keys);
            var changes = new List<ProjectSettingsStateChange>();
            foreach (string path in paths)
            {
                bool hadBaseline = expected.TryGetValue(path, out string baselineHash);
                bool existsNow = current.TryGetValue(path, out string currentHash);
                if (!hadBaseline)
                {
                    changes.Add(new ProjectSettingsStateChange(
                        path,
                        ProjectSettingsStateChangeKind.Added,
                        null,
                        currentHash));
                }
                else if (!existsNow)
                {
                    changes.Add(new ProjectSettingsStateChange(
                        path,
                        ProjectSettingsStateChangeKind.Deleted,
                        baselineHash,
                        null));
                }
                else if (!string.Equals(
                             baselineHash,
                             currentHash,
                             StringComparison.Ordinal))
                {
                    changes.Add(new ProjectSettingsStateChange(
                        path,
                        ProjectSettingsStateChangeKind.Modified,
                        baselineHash,
                        currentHash));
                }
            }

            return new ProjectSettingsStateVerificationResult(changes);
        }

        private static string ComputeSha256(string path, out long length)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       HashBufferSize,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                length = stream.Length;
                if (length > MaximumTrackedFileLength)
                {
                    throw new InvalidOperationException(
                        $"ProjectSettings file exceeds the {MaximumTrackedFileLength}-byte verification budget: '{path}'.");
                }

                string result = ToHex(sha256.ComputeHash(stream));
                if (stream.Length != length)
                {
                    throw new IOException(
                        $"ProjectSettings file length changed while it was being verified: '{path}'.");
                }

                RejectReparsePoint(path, "ProjectSettings file");
                return result;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = bytes[index];
                characters[index * 2] = alphabet[value >> 4];
                characters[index * 2 + 1] = alphabet[value & 0x0F];
            }

            return new string(characters);
        }

        private static string NormalizeExistingDirectory(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"{label} is required.", nameof(path));
            }

            string normalized = NormalizeDirectoryPath(path);
            if (!Directory.Exists(normalized))
            {
                if (File.Exists(normalized))
                {
                    throw new InvalidOperationException(
                        $"{label} is not a directory: '{normalized}'.");
                }

                throw new DirectoryNotFoundException(
                    $"{label} was not found: '{normalized}'.");
            }

            return normalized;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root)
                && string.Equals(fullPath, root, PathComparison))
            {
                return root;
            }

            return fullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static void EnsureNoReparsePoints(
            string anchor,
            string target,
            bool includeAnchor)
        {
            string normalizedAnchor = NormalizeDirectoryPath(anchor);
            string normalizedTarget = Path.GetFullPath(target);
            EnsureStrictDescendantOrEqual(
                normalizedAnchor,
                normalizedTarget,
                "ProjectSettings path");

            string current = Directory.Exists(normalizedTarget)
                || File.Exists(normalizedTarget)
                    ? normalizedTarget
                    : Path.GetDirectoryName(normalizedTarget);
            while (!string.IsNullOrEmpty(current)
                   && (PathsEqual(current, normalizedAnchor)
                       || IsStrictDescendant(normalizedAnchor, current)))
            {
                bool isAnchor = PathsEqual(current, normalizedAnchor);
                if ((includeAnchor || !isAnchor)
                    && (Directory.Exists(current) || File.Exists(current)))
                {
                    RejectReparsePoint(current, "ProjectSettings path");
                }

                if (isAnchor)
                {
                    return;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new InvalidOperationException(
                $"ProjectSettings path escaped its trusted root. Root: '{normalizedAnchor}', path: '{normalizedTarget}'.");
        }

        private static void RejectReparsePoint(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} cannot be a symbolic link or reparse point: '{path}'.");
            }
        }

        private static void EnsureStrictDescendant(
            string root,
            string candidate,
            string label)
        {
            if (!IsStrictDescendant(root, candidate))
            {
                throw new InvalidOperationException(
                    $"{label} escaped its trusted root. Root: '{root}', path: '{candidate}'.");
            }
        }

        private static void EnsureStrictDescendantOrEqual(
            string root,
            string candidate,
            string label)
        {
            if (!PathsEqual(root, candidate)
                && !IsStrictDescendant(root, candidate))
            {
                throw new InvalidOperationException(
                    $"{label} escaped its trusted root. Root: '{root}', path: '{candidate}'.");
            }
        }

        private static bool IsStrictDescendant(string root, string candidate)
        {
            string prefix = AppendDirectorySeparator(root);
            return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                NormalizeDirectoryPath(first),
                NormalizeDirectoryPath(second),
                PathComparison);
        }

        private static string AppendDirectorySeparator(string path)
        {
            string normalized = NormalizeDirectoryPath(path);
            return normalized.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? normalized
                : normalized + Path.DirectorySeparatorChar;
        }

        private static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        internal sealed class AuthorizationWindow : IDisposable
        {
            private readonly ProjectSettingsStateGuard owner;
            private readonly IReadOnlyCollection<string> allowedPaths;
            private readonly SortedDictionary<string, string> openingSnapshot;
            private bool completed;

            internal AuthorizationWindow(
                ProjectSettingsStateGuard owner,
                IReadOnlyCollection<string> allowedPaths,
                SortedDictionary<string, string> openingSnapshot)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.allowedPaths = allowedPaths
                    ?? throw new ArgumentNullException(nameof(allowedPaths));
                this.openingSnapshot = openingSnapshot
                    ?? throw new ArgumentNullException(nameof(openingSnapshot));
            }

            internal ProjectSettingsStateVerificationResult Commit()
            {
                if (completed)
                {
                    throw new InvalidOperationException(
                        "The ProjectSettings authorization window has already completed.");
                }

                completed = true;
                return owner.CommitAuthorization(
                    this,
                    allowedPaths,
                    openingSnapshot);
            }

            public void Dispose()
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                owner.CancelAuthorization(this);
            }
        }
    }
}
