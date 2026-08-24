using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Build.Pipeline.Editor
{
    public static class BuildPathPolicy
    {
        // Unity 2022.3 Editor/Mono and third-party build tools still reach Win32
        // APIs that remain limited by the Win32 MAX_PATH contract. MAX_PATH includes the
        // terminating null, so the longest path passed to those APIs is 259
        // UTF-16 code units. Keep this policy host-independent so a profile
        // validated on macOS/Linux does not later fail on a Windows CI agent.
        public const int Win32MaxPathCharacters = 259;
        public const int Win32MaxDirectoryPathCharacters = 247;

        private const int MaximumDeleteTreeEntryCount = 1000000;
        private const int MaximumPortableRelativePathUtf8ByteCount = 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly char[] PortableInvalidFileNameCharacters =
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };

        private static readonly HashSet<string> PortableReservedFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                "COM1",
                "COM2",
                "COM3",
                "COM4",
                "COM5",
                "COM6",
                "COM7",
                "COM8",
                "COM9",
                "LPT1",
                "LPT2",
                "LPT3",
                "LPT4",
                "LPT5",
                "LPT6",
                "LPT7",
                "LPT8",
                "LPT9"
            };

        /// <summary>
        /// Validates an absolute path against the Win32 MAX_PATH budget.
        /// <paramref name="reservedSuffixCharacters"/> reserves capacity for a
        /// known suffix that will be appended later, including any separator.
        /// Extended/device path prefixes are intentionally rejected because
        /// Unity APIs and package code do not consume them consistently.
        /// </summary>
        public static string EnsureWin32MaxPathBudget(
            string path,
            string displayName,
            int reservedSuffixCharacters = 0)
        {
            string name = string.IsNullOrWhiteSpace(displayName)
                ? "Build path"
                : displayName;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"{name} is required.", nameof(path));
            }

            if (reservedSuffixCharacters < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reservedSuffixCharacters),
                    reservedSuffixCharacters,
                    "The reserved path suffix cannot be negative.");
            }

            RejectWindowsDeviceNamespace(path, name);
            string fullPath = Path.GetFullPath(path);
            RejectWindowsDeviceNamespace(fullPath, name);

            long requiredCharacters = checked((long)fullPath.Length + reservedSuffixCharacters);
            if (requiredCharacters > Win32MaxPathCharacters)
            {
                string reservation = reservedSuffixCharacters == 0
                    ? string.Empty
                    : $", reserved suffix={reservedSuffixCharacters}";
                throw new PathTooLongException(
                    $"{name} exceeds the Unity 2022.3/Mono Win32 MAX_PATH budget. " +
                    $"Path length={fullPath.Length}{reservation}, required={requiredCharacters}, " +
                    $"maximum={Win32MaxPathCharacters}. Shorten the repository checkout, " +
                    "configured output root, product/package name, or generated artifact path. " +
                    "The build pipeline intentionally does not pass Windows extended-path prefixes to Unity APIs. " +
                    $"Path: '{fullPath}'.");
            }

            return fullPath;
        }

        /// <summary>
        /// Validates a directory passed to MAX_PATH-limited Win32 CreateDirectory/MoveFile
        /// paths. The 247-character ceiling preserves the documented 8.3 child
        /// capacity below MAX_PATH. A child reservation is additionally checked
        /// against the 259-character file-path ceiling.
        /// </summary>
        public static string EnsureWin32MaxDirectoryPathBudget(
            string path,
            string displayName,
            int reservedChildPathCharacters = 0)
        {
            string fullPath = EnsureWin32MaxPathBudget(
                path,
                displayName,
                reservedChildPathCharacters);
            if (fullPath.Length > Win32MaxDirectoryPathCharacters)
            {
                string name = string.IsNullOrWhiteSpace(displayName)
                    ? "Build directory"
                    : displayName;
                throw new PathTooLongException(
                    $"{name} exceeds the Unity 2022.3/Mono Win32 MAX_PATH directory budget. " +
                    $"Path length={fullPath.Length}, maximum={Win32MaxDirectoryPathCharacters}. " +
                    "Shorten the repository checkout or configured output directory. " +
                    $"Path: '{fullPath}'.");
            }

            return fullPath;
        }

        public static void ValidatePortableFileName(
            string value,
            string displayName,
            int maximumUtf8ByteCount = 240)
        {
            string name = displayName ?? "File name";
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{name} is required.", nameof(value));
            }

            if (maximumUtf8ByteCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumUtf8ByteCount),
                    maximumUtf8ByteCount,
                    "The UTF-8 file-name budget must be positive.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
                || value.StartsWith(".", StringComparison.Ordinal)
                || value.EndsWith(".", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{name} '{value}' may not start or end with whitespace or a period.",
                    nameof(value));
            }

            if (value.IndexOfAny(PortableInvalidFileNameCharacters) >= 0)
            {
                throw new ArgumentException(
                    $"{name} contains a character that is not portable across supported build hosts.",
                    nameof(value));
            }

            foreach (char character in value)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException($"{name} contains a control character.", nameof(value));
                }
            }

            int extensionSeparatorIndex = value.IndexOf('.');
            string deviceName = extensionSeparatorIndex < 0
                ? value
                : value.Substring(0, extensionSeparatorIndex);
            if (PortableReservedFileNames.Contains(deviceName))
            {
                throw new ArgumentException(
                    $"{name} is reserved by a supported build host: '{value}'.",
                    nameof(value));
            }

            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException($"{name} contains invalid Unicode.", nameof(value), exception);
            }

            if (byteCount > maximumUtf8ByteCount)
            {
                throw new ArgumentException(
                    $"{name} exceeds the portable UTF-8 limit of {maximumUtf8ByteCount} bytes: {byteCount} bytes.",
                    nameof(value));
            }
        }

        public static void ValidatePortableProjectRelativePath(string value, string displayName)
        {
            string name = displayName ?? "Project-relative path";
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{name} is required.", nameof(value));
            }

            string normalized = value.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(value))
            {
                throw new ArgumentException($"{name} must be project-relative.", nameof(value));
            }

            int pathByteCount;
            try
            {
                pathByteCount = StrictUtf8.GetByteCount(normalized);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException($"{name} contains invalid Unicode.", nameof(value), exception);
            }

            if (pathByteCount > MaximumPortableRelativePathUtf8ByteCount)
            {
                throw new ArgumentException(
                    $"{name} exceeds the portable UTF-8 limit of {MaximumPortableRelativePathUtf8ByteCount} bytes.",
                    nameof(value));
            }

            string[] segments = normalized.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0)
                {
                    throw new ArgumentException(
                        $"{name} contains an empty path segment.",
                        nameof(value));
                }

                ValidatePortableFileName(segments[index], $"{name} segment {index + 1}");
            }
        }

        public static string ResolveBuildRoot(string projectRoot, string configuredPath)
        {
            string root = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException("Build output root is required.", nameof(configuredPath));
            }

            if (Path.IsPathRooted(configuredPath))
            {
                throw new ArgumentException("Build output root must be project-relative for portable profiles.", nameof(configuredPath));
            }

            string resolved = Path.GetFullPath(Path.Combine(root, configuredPath));
            EnsureStrictDescendant(root, resolved, "Build output root must be inside the Unity project root.");
            EnsureNotProtectedProjectDirectory(root, resolved);
            ValidatePortableProjectRelativePath(configuredPath, "Build output root");
            EnsureNoReparsePoints(root, resolved, includeAnchor: false);
            return EnsureWin32MaxDirectoryPathBudget(resolved, "Build output root");
        }

        /// <summary>
        /// Validates an already resolved build root supplied through the public pipeline API.
        /// </summary>
        public static string EnsureSafeBuildRoot(string projectRoot, string buildRoot)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            string resolved = Path.GetFullPath(
                buildRoot ?? throw new ArgumentNullException(nameof(buildRoot)));
            EnsureStrictDescendant(
                project,
                resolved,
                "Build output root must be inside the current Unity project root.");
            EnsureSafeConcreteTarget(project, resolved);
            EnsureNoReparsePoints(project, resolved, includeAnchor: false);
            if (File.Exists(resolved))
            {
                throw new InvalidOperationException(
                    $"Build output root resolves to a file: '{resolved}'.");
            }

            return EnsureWin32MaxDirectoryPathBudget(resolved, "Build output root");
        }

        public static string ResolveOutputPath(
            string projectRoot,
            string buildRoot,
            string requestedPath,
            bool relativeToBuildRoot,
            bool allowExternalOutput)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            string approvedBuildRoot = Path.GetFullPath(buildRoot ?? throw new ArgumentNullException(nameof(buildRoot)));
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                throw new ArgumentException("Player output path is required.", nameof(requestedPath));
            }

            string resolved;
            if (Path.IsPathRooted(requestedPath))
            {
                resolved = Path.GetFullPath(requestedPath);
            }
            else
            {
                string relativeRoot = relativeToBuildRoot ? approvedBuildRoot : project;
                resolved = Path.GetFullPath(Path.Combine(relativeRoot, requestedPath));
            }

            EnsureSafeConcreteTarget(project, resolved);
            if (!allowExternalOutput)
            {
                EnsureStrictDescendant(
                    approvedBuildRoot,
                    resolved,
                    "Player output must be inside the configured build root. Use " +
                    $"{BuildCommandLineOptionNames.AllowExternalOutput} for an explicit external destination.");
            }

            string reparseAnchor = allowExternalOutput ? Path.GetPathRoot(resolved) : approvedBuildRoot;
            EnsureNoReparsePoints(reparseAnchor, resolved, includeAnchor: !allowExternalOutput);
            if (Path.IsPathRooted(requestedPath))
            {
                ValidatePortableAbsolutePathSegments(resolved, "Player output path");
            }
            else
            {
                ValidatePortableProjectRelativePath(requestedPath, "Player output path");
            }

            return EnsureWin32MaxPathBudget(resolved, "Player output path");
        }

        public static string ResolveOutputDirectory(
            string projectRoot,
            string buildRoot,
            string outputPath,
            bool outputIsFolder,
            bool allowExternalOutput)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            string approvedBuildRoot = Path.GetFullPath(buildRoot ?? throw new ArgumentNullException(nameof(buildRoot)));
            string artifact = Path.GetFullPath(outputPath ?? throw new ArgumentNullException(nameof(outputPath)));
            string directory = outputIsFolder ? artifact : Path.GetDirectoryName(artifact);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException($"Player output must have a dedicated output directory: '{artifact}'.");
            }

            directory = Path.GetFullPath(directory);
            EnsureSafeDeleteTarget(
                project,
                directory,
                approvedBuildRoot,
                allowExternalOutput);
            return EnsureWin32MaxDirectoryPathBudget(directory, "Player output directory");
        }

        public static string ResolveGeneratedAssetsDirectory(string projectRoot, string configuredPath)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException("Generated Assets directory is required.", nameof(configuredPath));
            }

            if (Path.IsPathRooted(configuredPath))
            {
                throw new ArgumentException("Generated Assets directory must be project-relative.", nameof(configuredPath));
            }

            string normalized = configuredPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || ContainsTraversalSegment(normalized))
            {
                throw new InvalidOperationException(
                    $"Generated output must be an explicit project-relative child of Assets: '{configuredPath}'.");
            }

            ValidatePortableProjectRelativePath(configuredPath, "Generated Assets directory");

            string resolved = Path.GetFullPath(Path.Combine(project, configuredPath));
            return EnsureGeneratedAssetsDirectory(project, resolved);
        }

        public static string EnsureGeneratedAssetsDirectory(string projectRoot, string directoryPath)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Generated Assets directory is required.", nameof(directoryPath));
            }

            string resolved = Path.GetFullPath(directoryPath);
            string assetsRoot = Path.Combine(project, "Assets");
            EnsureStrictDescendant(assetsRoot, resolved, "Generated output must remain inside Assets.");
            EnsureNoReparsePoints(assetsRoot, resolved, includeAnchor: true);
            if (File.Exists(resolved))
            {
                throw new InvalidOperationException($"Generated output directory resolves to a file: '{resolved}'.");
            }

            return EnsureWin32MaxDirectoryPathBudget(resolved, "Generated Assets directory");
        }

        public static string EnsureSafeReadableFile(string approvedRoot, string filePath)
        {
            if (string.IsNullOrWhiteSpace(approvedRoot))
            {
                throw new ArgumentException("Approved source root is required.", nameof(approvedRoot));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Source file path is required.", nameof(filePath));
            }

            string root = Path.GetFullPath(approvedRoot);
            string file = Path.GetFullPath(filePath);
            EnsureStrictDescendant(root, file, "Source file must remain inside its approved root.");
            EnsureNoReparsePoints(root, file, includeAnchor: true);
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("Required source artifact was not found.", file);
            }

            return EnsureWin32MaxPathBudget(file, "Source artifact");
        }

        public static string ResolvePublicationSourceRoot(
            string projectRoot,
            string configuredPath,
            bool allowExternalSource)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException("Publication source root is required.", nameof(configuredPath));
            }

            string source = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(project, configuredPath));
            EnsureSafeConcreteTarget(project, source);

            bool isInsideProject = IsStrictDescendant(project, source);
            if (!isInsideProject && !allowExternalSource)
            {
                throw new InvalidOperationException(
                    $"Publication source must be inside the Unity project unless external profile sources are explicitly enabled: '{source}'.");
            }

            string anchor = isInsideProject ? project : Path.GetPathRoot(source);
            EnsureNoReparsePoints(anchor, source, includeAnchor: false);
            if (File.Exists(source))
            {
                throw new InvalidOperationException(
                    $"Publication source root resolves to a file: '{source}'.");
            }

            return EnsureWin32MaxDirectoryPathBudget(source, "Publication source root");
        }

        public static void EnsureSafeDeleteTarget(string projectRoot, string targetPath, string approvedBuildRoot, bool allowExternalOutput)
        {
            string project = NormalizeExistingRoot(projectRoot, nameof(projectRoot));
            string target = Path.GetFullPath(targetPath ?? throw new ArgumentNullException(nameof(targetPath)));
            string approvedRoot = Path.GetFullPath(
                approvedBuildRoot ?? throw new ArgumentNullException(nameof(approvedBuildRoot)));
            EnsureSafeConcreteTarget(project, target);

            if (PortablePathsEqual(target, approvedRoot) || IsPortableStrictDescendant(target, approvedRoot))
            {
                throw new InvalidOperationException(
                    $"A clean build requires a dedicated directory and may not delete the approved build root or an ancestor. " +
                    $"Root: '{approvedRoot}', target: '{target}'.");
            }

            if (!allowExternalOutput)
            {
                EnsureStrictDescendant(approvedRoot, target, "Refusing to delete outside the configured build root.");
            }

            string reparseAnchor = allowExternalOutput ? Path.GetPathRoot(target) : approvedRoot;
            EnsureNoReparsePoints(reparseAnchor, target, includeAnchor: !allowExternalOutput);
        }

        public static void EnsureSafeDeleteDirectoryTree(
            string projectRoot,
            string targetPath,
            string approvedBuildRoot,
            bool allowExternalOutput)
        {
            EnsureSafeDeleteTarget(projectRoot, targetPath, approvedBuildRoot, allowExternalOutput);
            string root = Path.GetFullPath(targetPath);
            if (!Directory.Exists(root))
            {
                return;
            }

            var pending = new Stack<string>();
            pending.Push(root);
            int entryCount = 0;
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    entryCount++;
                    if (entryCount > MaximumDeleteTreeEntryCount)
                    {
                        throw new InvalidOperationException(
                            $"Refusing to delete a directory tree with more than {MaximumDeleteTreeEntryCount} entries: '{root}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Refusing to recursively delete a directory tree containing a reparse-point entry: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }
        }

        public static bool IsStrictDescendant(string parentPath, string childPath)
        {
            string parent = AppendDirectorySeparator(Path.GetFullPath(parentPath));
            string child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, PathComparison);
        }

        // Non-Windows volumes can be either case-sensitive or case-insensitive.
        // Containment authorization therefore uses the conservative host rule:
        // Windows ignores casing; other hosts require exact casing. Separate
        // portable ownership checks below always ignore casing so a protected
        // directory cannot be reached through a casing alias on macOS or Windows.
        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static string NormalizeExistingRoot(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", parameterName);
            }

            return Path.GetFullPath(path);
        }

        private static void EnsureStrictDescendant(string parent, string child, string message)
        {
            if (!IsStrictDescendant(parent, child))
            {
                throw new InvalidOperationException($"{message} Parent: '{Path.GetFullPath(parent)}', target: '{Path.GetFullPath(child)}'.");
            }
        }

        private static void EnsureSafeConcreteTarget(string projectRoot, string target)
        {
            string fullTarget = Path.GetFullPath(target);
            string volumeRoot = Path.GetPathRoot(fullTarget);
            if (string.IsNullOrEmpty(volumeRoot) || PortablePathsEqual(volumeRoot, fullTarget))
            {
                throw new InvalidOperationException($"Refusing to use a volume root as a build target: '{fullTarget}'.");
            }

            if (PortablePathsEqual(projectRoot, fullTarget) || IsPortableStrictDescendant(fullTarget, projectRoot))
            {
                throw new InvalidOperationException(
                    $"Refusing to use the Unity project root or one of its ancestor directories as a build target: '{fullTarget}'.");
            }

            string parent = Path.GetDirectoryName(fullTarget);
            if (string.IsNullOrEmpty(parent) || PortablePathsEqual(parent, volumeRoot))
            {
                throw new InvalidOperationException(
                    $"Refusing to use a top-level volume entry as a build target: '{fullTarget}'. Use a dedicated nested build directory.");
            }

            EnsureNotProtectedProjectDirectory(projectRoot, fullTarget);
            EnsureNotWellKnownSystemDirectory(fullTarget);
            EnsureTargetIsNotReparsePoint(fullTarget);
        }

        private static void EnsureNotProtectedProjectDirectory(string projectRoot, string target)
        {
            string[] protectedNames =
            {
                ".git",
                "Assets",
                "Packages",
                "ProjectSettings",
                "Library",
                "UserSettings",
                "Temp",
                "Obj",
                "Logs"
            };
            foreach (string name in protectedNames)
            {
                string protectedPath = Path.Combine(projectRoot, name);
                if (PortablePathsEqual(protectedPath, target) || IsPortableStrictDescendant(protectedPath, target))
                {
                    throw new InvalidOperationException($"Refusing to use protected Unity project data as a build target: '{target}'.");
                }
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                TrimEndingDirectorySeparators(Path.GetFullPath(left)),
                TrimEndingDirectorySeparators(Path.GetFullPath(right)),
                PathComparison);
        }

        private static bool PortablePathsEqual(string left, string right)
        {
            return string.Equals(
                TrimEndingDirectorySeparators(Path.GetFullPath(left)),
                TrimEndingDirectorySeparators(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPortableStrictDescendant(string parentPath, string childPath)
        {
            string parent = AppendDirectorySeparator(Path.GetFullPath(parentPath));
            string child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNotWellKnownSystemDirectory(string target)
        {
            Environment.SpecialFolder[] protectedFolders =
            {
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System
            };

            foreach (Environment.SpecialFolder folder in protectedFolders)
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path) && PortablePathsEqual(path, target))
                {
                    throw new InvalidOperationException($"Refusing to use a protected operating-system directory as a build target: '{target}'.");
                }
            }
        }

        private static void EnsureTargetIsNotReparsePoint(string target)
        {
            if (!Directory.Exists(target) && !File.Exists(target))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Refusing to clean a symbolic link or reparse-point build target: '{target}'.");
            }
        }

        private static void EnsureNoReparsePoints(string anchor, string target, bool includeAnchor)
        {
            string fullAnchor = Path.GetFullPath(anchor);
            string fullTarget = Path.GetFullPath(target);
            string current = Directory.Exists(fullTarget) || File.Exists(fullTarget)
                ? fullTarget
                : Path.GetDirectoryName(fullTarget);

            while (!string.IsNullOrEmpty(current)
                && (PathsEqual(current, fullAnchor) || IsStrictDescendant(fullAnchor, current)))
            {
                bool isAnchor = PathsEqual(current, fullAnchor);
                if ((includeAnchor || !isAnchor) && (Directory.Exists(current) || File.Exists(current)))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Refusing a build path that traverses a symbolic link or reparse point: '{current}'.");
                    }
                }

                if (isAnchor)
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            string normalized = TrimEndingDirectorySeparators(path);
            return normalized + Path.DirectorySeparatorChar;
        }

        private static string TrimEndingDirectorySeparators(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            int length = fullPath.Length;
            while (length > root.Length)
            {
                char value = fullPath[length - 1];
                if (value != Path.DirectorySeparatorChar && value != Path.AltDirectorySeparatorChar)
                {
                    break;
                }

                length--;
            }

            return length == fullPath.Length ? fullPath : fullPath.Substring(0, length);
        }

        private static bool ContainsTraversalSegment(string path)
        {
            string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    return true;
                }
            }

            return false;
        }

        private static void RejectWindowsDeviceNamespace(string path, string displayName)
        {
            string normalized = path.Replace('/', '\\');
            if (normalized.StartsWith("\\\\?\\", StringComparison.Ordinal)
                || normalized.StartsWith("\\\\.\\", StringComparison.Ordinal)
                || normalized.StartsWith("\\??\\", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"{displayName} uses a Windows extended/device namespace, which is not supported by " +
                    $"Unity build APIs: '{path}'. Configure a shorter regular filesystem path instead.");
            }
        }

        private static void ValidatePortableAbsolutePathSegments(string path, string displayName)
        {
            string fullPath = Path.GetFullPath(path);
            string volumeRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(volumeRoot) || fullPath.Length <= volumeRoot.Length)
            {
                throw new ArgumentException($"{displayName} must name a concrete artifact.", nameof(path));
            }

            if (Path.DirectorySeparatorChar == '/' && fullPath.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    $"{displayName} contains a backslash that is not a separator on this build host.",
                    nameof(path));
            }

            string relativePart = fullPath.Substring(volumeRoot.Length)
                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
            ValidatePortableProjectRelativePath(relativePart, displayName);
        }
    }
}
