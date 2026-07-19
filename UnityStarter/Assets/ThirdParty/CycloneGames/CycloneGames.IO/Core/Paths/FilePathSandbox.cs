using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CycloneGames.IO
{
    /// <summary>
    /// Resolves portable relative paths under one immutable trusted root.
    /// Existing links can be rejected, but lexical validation cannot close TOCTOU races.
    /// </summary>
    public sealed class FilePathSandbox
    {
        private static readonly char[] PortableInvalidCharacters =
        {
            '<', '>', ':', '"', '|', '?', '*'
        };

        public FilePathSandbox(
            string rootPath,
            FileLinkPolicy linkPolicy = FileLinkPolicy.RejectExistingLinks)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Root path cannot be null, empty, or whitespace.", nameof(rootPath));
            }

            if (!Enum.IsDefined(typeof(FileLinkPolicy), linkPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(linkPolicy));
            }

            RootPath = Path.GetFullPath(rootPath);
            LinkPolicy = linkPolicy;
        }

        public string RootPath { get; }

        public FileLinkPolicy LinkPolicy { get; }

        public string Resolve(string relativePath)
        {
            string normalizedRelativePath = NormalizeRelativePath(relativePath, true);
            string candidatePath = normalizedRelativePath.Length == 0
                ? RootPath
                : Path.Combine(RootPath, normalizedRelativePath);
            string normalizedCandidatePath = Path.GetFullPath(candidatePath);

            if (!ContainsAbsolutePath(normalizedCandidatePath))
            {
                throw new UnauthorizedAccessException("The path resolves outside the sandbox root.");
            }

            if (LinkPolicy == FileLinkPolicy.RejectExistingLinks)
            {
                EnsureNoExistingLinks(normalizedCandidatePath);
            }

            return normalizedCandidatePath;
        }

        public bool ContainsAbsolutePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            string normalizedPath = Path.GetFullPath(absolutePath);
            if (string.Equals(RootPath, normalizedPath, PathComparison))
            {
                return true;
            }

            return normalizedPath.StartsWith(AppendDirectorySeparator(RootPath), PathComparison);
        }

        public static string NormalizeRelativePath(string relativePath, bool allowEmpty = false)
        {
            if (relativePath == null)
            {
                throw new ArgumentNullException(nameof(relativePath));
            }

            if (relativePath.Length == 0)
            {
                if (allowEmpty)
                {
                    return string.Empty;
                }

                throw new ArgumentException("Relative path cannot be empty.", nameof(relativePath));
            }

            if (Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException("A relative path was required.", nameof(relativePath));
            }

            string[] segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                ValidateSegment(segments[i]);
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }

        private static StringComparison PathComparison =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static void ValidateSegment(string segment)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new ArgumentException("Relative paths cannot contain empty or dot segments.", "relativePath");
            }

            if (segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(" ", StringComparison.Ordinal))
            {
                throw new ArgumentException("Path segments cannot end with a dot or space.", "relativePath");
            }

            for (int i = 0; i < segment.Length; i++)
            {
                char character = segment[i];
                if (character < 32 || Array.IndexOf(PortableInvalidCharacters, character) >= 0)
                {
                    throw new ArgumentException("The relative path contains a non-portable character.", "relativePath");
                }
            }

            string baseName = segment;
            int extensionSeparator = baseName.IndexOf('.');
            if (extensionSeparator >= 0)
            {
                baseName = baseName.Substring(0, extensionSeparator);
            }

            if (IsReservedWindowsDeviceName(baseName))
            {
                throw new ArgumentException("The relative path contains a reserved device name.", "relativePath");
            }
        }

        private static bool IsReservedWindowsDeviceName(string baseName)
        {
            if (string.Equals(baseName, "CON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "PRN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "AUX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(baseName, "NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (baseName.Length != 4)
            {
                return false;
            }

            string prefix = baseName.Substring(0, 3);
            char suffix = baseName[3];
            return (string.Equals(prefix, "COM", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(prefix, "LPT", StringComparison.OrdinalIgnoreCase))
                && suffix >= '1'
                && suffix <= '9';
        }

        private void EnsureNoExistingLinks(string normalizedPath)
        {
            ThrowIfExistingLink(RootPath);
            if (string.Equals(RootPath, normalizedPath, PathComparison))
            {
                return;
            }

            string relativePath = Path.GetRelativePath(RootPath, normalizedPath);
            string[] segments = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string currentPath = RootPath;
            for (int i = 0; i < segments.Length; i++)
            {
                currentPath = Path.Combine(currentPath, segments[i]);
                ThrowIfExistingLink(currentPath);
            }
        }

        private static void ThrowIfExistingLink(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("The path traverses an existing filesystem link.");
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            char lastCharacter = path[path.Length - 1];
            if (lastCharacter == Path.DirectorySeparatorChar
                || lastCharacter == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
