using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    internal static class YooAsset3BuildSafety
    {
        private const int MaxArtifactFileNameLength = 240;
        private const int MaxPortableFileNameUtf8ByteCount = 240;
        private const int MaxBundledCopyParamsLength = 4096;
        private const int MaxBundledCopyTagCount = 256;
        private const int MaxBundledCopyTagLength = 128;
        private const int MaxGuardedTreeEntryCount = 250000;
        private const string PortableInvalidFileNameCharacters = "<>:\"/\\|?*";

        private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static StringComparer FileSystemPathComparer => UsesWindowsPathSemantics
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public static StringComparer PortablePathSegmentComparer => StringComparer.OrdinalIgnoreCase;

        private static StringComparison FileSystemPathComparison => UsesWindowsPathSemantics
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // macOS volumes can be either case-sensitive or case-insensitive. Treating
        // non-Windows containment checks as case-sensitive is deliberately
        // conservative: a casing alias is rejected instead of being accepted on a
        // case-sensitive volume. Portable collision checks remain case-insensitive
        // so profiles cannot produce ambiguous cross-platform layouts.
        private static bool UsesWindowsPathSemantics => Path.DirectorySeparatorChar == '\\';

        public static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Unity project root is required.", nameof(projectRoot));
            }

            string resolved = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(resolved))
            {
                throw new DirectoryNotFoundException($"Unity project root does not exist: '{resolved}'.");
            }

            string assetsDirectory = Path.Combine(resolved, "Assets");
            if (!Directory.Exists(assetsDirectory))
            {
                throw new InvalidOperationException($"The requested project root is not a Unity project: '{resolved}'.");
            }

            return resolved;
        }

        public static string ResolveBuildOutputRoot(string projectRoot, string configuredPath)
        {
            return YooAssetBuildRootPolicy.ResolveBuildOutputRoot(projectRoot, configuredPath);
        }

        public static string ResolveBundledFileRoot(string projectRoot, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                string defaultRoot = Path.GetFullPath(
                    BundleBuilderHelper.GetStreamingAssetsRoot());
                return YooAssetBuildRootPolicy.ValidateBundledFileRoot(
                    projectRoot,
                    defaultRoot);
            }

            return YooAssetBuildRootPolicy.ResolveConfiguredBundledFileRoot(
                projectRoot,
                configuredPath);
        }

        public static void ValidateNoPathRedirection(string projectRoot, string targetPath)
        {
            EnsureNoReparsePointsInPath(projectRoot, targetPath);
        }

        public static void EnsureRootsDoNotOverlap(string buildOutputRoot, string bundledFileRoot)
        {
            YooAssetBuildRootPolicy.EnsureRootsDoNotOverlap(
                buildOutputRoot,
                bundledFileRoot);
        }

        public static void ValidateArtifactFileName(string value, string displayName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > MaxArtifactFileNameLength ||
                Encoding.UTF8.GetByteCount(value) > MaxPortableFileNameUtf8ByteCount ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value == "." ||
                value == ".." ||
                value.EndsWith(".", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{displayName} is not a portable artifact file name: '{value}'.");
            }

            foreach (char character in value)
            {
                if (char.IsControl(character) || PortableInvalidFileNameCharacters.IndexOf(character) >= 0)
                {
                    throw new InvalidOperationException($"{displayName} is not a portable artifact file name: '{value}'.");
                }
            }

            string baseName = value;
            int extensionSeparator = value.IndexOf('.');
            if (extensionSeparator >= 0)
            {
                baseName = value.Substring(0, extensionSeparator);
            }

            if (ReservedWindowsNames.Contains(baseName))
            {
                throw new InvalidOperationException($"{displayName} uses a reserved file-system name: '{value}'.");
            }
        }

        public static string NormalizeBundledCopyParams(YooAssetPackageProfile profile)
        {
            bool copyByTags = profile.bundledCopyOption == YooAssetBundledCopyOption.ClearAndCopyByTags ||
                              profile.bundledCopyOption == YooAssetBundledCopyOption.OnlyCopyByTags;
            if (!copyByTags)
            {
                return string.Empty;
            }

            string value = profile.bundledCopyTags ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"Package '{profile.packageName}' requires at least one bundled-copy tag.",
                    nameof(profile.bundledCopyTags));
            }

            if (value.Length > MaxBundledCopyParamsLength)
            {
                throw new ArgumentException(
                    $"Package '{profile.packageName}' bundled-copy tags exceed the {MaxBundledCopyParamsLength}-character limit.",
                    nameof(profile.bundledCopyTags));
            }

            string[] rawTags = value.Split(';');
            if (rawTags.Length > MaxBundledCopyTagCount)
            {
                throw new ArgumentException(
                    $"Package '{profile.packageName}' has more than {MaxBundledCopyTagCount} bundled-copy tags.",
                    nameof(profile.bundledCopyTags));
            }

            var normalizedTags = new List<string>(rawTags.Length);
            var seenTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawTag in rawTags)
            {
                string tag = rawTag.Trim();
                if (tag.Length == 0)
                {
                    throw new ArgumentException(
                        $"Package '{profile.packageName}' bundled-copy tags contain an empty entry.",
                        nameof(profile.bundledCopyTags));
                }

                if (tag.Length > MaxBundledCopyTagLength)
                {
                    throw new ArgumentException(
                        $"Package '{profile.packageName}' bundled-copy tag exceeds {MaxBundledCopyTagLength} characters: '{tag}'.",
                        nameof(profile.bundledCopyTags));
                }

                foreach (char character in tag)
                {
                    if (char.IsControl(character))
                    {
                        throw new ArgumentException(
                            $"Package '{profile.packageName}' bundled-copy tag contains a control character.",
                            nameof(profile.bundledCopyTags));
                    }
                }

                if (!seenTags.Add(tag))
                {
                    throw new ArgumentException(
                        $"Package '{profile.packageName}' bundled-copy tag is duplicated: '{tag}'.",
                        nameof(profile.bundledCopyTags));
                }

                normalizedTags.Add(tag);
            }

            return string.Join(";", normalizedTags);
        }

        public static void ValidatePackageOutputPath(
            string buildOutputRoot,
            YooAsset3PackageBuildPlan packagePlan)
        {
            string packageRoot = Path.GetFullPath(packagePlan.Parameters.GetPackageRootDirectory());
            string outputDirectory = Path.GetFullPath(packagePlan.OutputPackageDirectory);
            string parentDirectory = Path.GetDirectoryName(outputDirectory);

            if (!IsStrictDescendant(buildOutputRoot, packageRoot) ||
                string.IsNullOrEmpty(parentDirectory) ||
                !PathsEqual(packageRoot, parentDirectory))
            {
                throw new InvalidOperationException(
                    $"Unsafe YooAsset version output path for package '{packagePlan.PackageName}': '{outputDirectory}'.");
            }
        }

        public static void ValidateBundledPackagePath(
            string projectRoot,
            string bundledFileRoot,
            YooAsset3PackageBuildPlan packagePlan)
        {
            string bundledPackageDirectory = Path.GetFullPath(packagePlan.BundledPackageDirectory);
            string parentDirectory = Path.GetDirectoryName(bundledPackageDirectory);
            if (string.IsNullOrEmpty(parentDirectory) ||
                !PathsEqual(bundledFileRoot, parentDirectory) ||
                !IsStrictDescendant(bundledFileRoot, bundledPackageDirectory))
            {
                throw new InvalidOperationException(
                    $"Unsafe YooAsset bundled package path for package '{packagePlan.PackageName}': '{bundledPackageDirectory}'.");
            }

            // YooAsset can delete or overwrite this directory depending on the
            // explicit bundled-copy option. Refuse path redirection before its
            // task receives control.
            EnsureNoReparsePoints(projectRoot, bundledPackageDirectory);
        }

        public static void DeleteOwnedDirectory(
            string projectRoot,
            string approvedRoot,
            string targetDirectory)
        {
            string root = Path.GetFullPath(approvedRoot);
            string target = Path.GetFullPath(targetDirectory);
            if (!IsStrictDescendant(root, target))
            {
                throw new InvalidOperationException(
                    $"Owned directory must be a strict descendant of its approved root. Root: '{root}', target: '{target}'.");
            }

            EnsureNoReparsePoints(projectRoot, target);

            if (File.Exists(target))
            {
                throw new InvalidOperationException($"Owned directory resolves to an existing file: '{target}'.");
            }

            if (!Directory.Exists(target))
            {
                return;
            }

            Directory.Delete(target, true);
            if (Directory.Exists(target))
            {
                throw new IOException($"Failed to remove provider-owned directory: '{target}'.");
            }
        }

        public static void DeleteOwnedFile(
            string projectRoot,
            string approvedRoot,
            string targetFile)
        {
            string root = Path.GetFullPath(approvedRoot);
            string target = Path.GetFullPath(targetFile);
            if (!IsStrictDescendant(root, target))
            {
                throw new InvalidOperationException(
                    $"Owned file must be a strict descendant of its approved root. Root: '{root}', target: '{target}'.");
            }

            EnsureNoReparsePointsInPath(projectRoot, target);
            if (Directory.Exists(target))
            {
                throw new InvalidOperationException($"Owned file resolves to an existing directory: '{target}'.");
            }

            if (!File.Exists(target))
            {
                return;
            }

            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Refusing to delete a provider-owned reparse-point file: '{target}'.");
            }

            File.Delete(target);
            if (File.Exists(target))
            {
                throw new IOException($"Failed to remove provider-owned file: '{target}'.");
            }
        }

        public static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                FileSystemPathComparison);
        }

        public static bool IsStrictDescendant(string parentPath, string childPath)
        {
            string parent = TrimEndingDirectorySeparator(Path.GetFullPath(parentPath)) + Path.DirectorySeparatorChar;
            string child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, FileSystemPathComparison);
        }

        public static int ValidateArtifactTree(string rootDirectory, int maximumEntryCount)
        {
            if (maximumEntryCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
            }

            string root = Path.GetFullPath(rootDirectory);
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root);
            int entryCount = 0;
            int fileCount = 0;

            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > maximumEntryCount)
                    {
                        throw new InvalidOperationException(
                            $"Artifact tree entry count exceeds the configured safety limit of {maximumEntryCount}: '{root}'.");
                    }

                    string fullEntry = Path.GetFullPath(entry);
                    if (!IsStrictDescendant(root, fullEntry))
                    {
                        throw new InvalidOperationException($"Artifact enumeration escaped its output directory: '{fullEntry}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(fullEntry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException($"Artifact output contains a reparse point: '{fullEntry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(fullEntry);
                        continue;
                    }

                    fileCount++;
                }
            }

            return fileCount;
        }

        private static void EnsureNoReparsePoints(string approvedRoot, string target)
        {
            EnsureNoReparsePointsInPath(approvedRoot, target);

            if (!Directory.Exists(target))
            {
                return;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(target);
            int entryCount = 0;
            while (pendingDirectories.Count > 0)
            {
                string directory = pendingDirectories.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaxGuardedTreeEntryCount)
                    {
                        throw new InvalidOperationException(
                            $"Refusing to delete a version directory with more than {MaxGuardedTreeEntryCount} entries: '{target}'.");
                    }

                    string fullEntry = Path.GetFullPath(entry);
                    if (!IsStrictDescendant(target, fullEntry))
                    {
                        throw new InvalidOperationException($"Refusing to inspect an entry outside the exact version directory: '{fullEntry}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(fullEntry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException($"Refusing to delete a version directory containing a reparse point: '{fullEntry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(fullEntry);
                    }
                }
            }
        }

        private static void EnsureNoReparsePointsInPath(string approvedRoot, string target)
        {
            string root = Path.GetFullPath(approvedRoot);
            string fullTarget = Path.GetFullPath(target);
            if (!IsStrictDescendant(root, fullTarget))
            {
                throw new InvalidOperationException(
                    $"Path-redirection validation target must be inside its approved root. Root: '{root}', target: '{fullTarget}'.");
            }

            string current = root;
            string relativeTarget = fullTarget.Substring(
                TrimEndingDirectorySeparator(root).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (string segment in relativeTarget.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    break;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"Refusing to use a path through a reparse point: '{current}'.");
                }
            }
        }

        private static string TrimEndingDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
