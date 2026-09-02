using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Build.Pipeline.Editor;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;
namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationFileOps
    {
        internal static void EnsureDirectoryPathAbsent(string path, string description)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }


        internal static void EnsureFilePathAbsent(string path, string description)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }


        internal static void CopyDirectorySafely(
            string projectRoot,
            string sourceDirectory,
            string destinationDirectory,
            string sourceApprovedRoot,
            string destinationApprovedRoot)
        {
            string source = Path.GetFullPath(sourceDirectory);
            string destination = Path.GetFullPath(destinationDirectory);
            if (!PublicationSafety.IsStrictDescendant(sourceApprovedRoot, source) ||
                !PublicationSafety.IsStrictDescendant(destinationApprovedRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Transactional copy escaped an approved root. Source: '{source}', destination: '{destination}'.");
            }

            PublicationSafety.ValidateNoPathRedirection(projectRoot, source);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, destination);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"Transactional copy source does not exist: '{source}'.");
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new InvalidOperationException($"Transactional copy destination already exists: '{destination}'.");
            }

            var pending = new Stack<CopyDirectoryEntry>();
            pending.Push(new CopyDirectoryEntry(source, destination, 0));
            int entryCount = 0;
            long copiedBytes = 0;
            while (pending.Count > 0)
            {
                CopyDirectoryEntry current = pending.Pop();
                if (current.Depth > MaximumCopyDepth)
                {
                    throw new InvalidOperationException(
                        $"Transactional copy exceeds the maximum directory depth of {MaximumCopyDepth}: '{current.Source}'.");
                }

                BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                    current.Destination,
                    "YooAsset transactional copy directory");
                Directory.CreateDirectory(current.Destination);
                foreach (string entry in Directory.EnumerateFileSystemEntries(current.Source, "*", SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaximumCopiedEntries)
                    {
                        throw new InvalidOperationException(
                            $"Transactional copy exceeds the entry limit of {MaximumCopiedEntries}: '{source}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException($"Transactional copy refuses a reparse-point entry: '{entry}'.");
                    }

                    string destinationEntry = Path.Combine(current.Destination, Path.GetFileName(entry));
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                            destinationEntry,
                            "YooAsset transactional copy directory");
                        pending.Push(new CopyDirectoryEntry(entry, destinationEntry, current.Depth + 1));
                        continue;
                    }

                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        destinationEntry,
                        "YooAsset transactional copy artifact");

                    long length = new FileInfo(entry).Length;
                    copiedBytes = checked(copiedBytes + length);
                    if (copiedBytes > MaximumCopiedBytes)
                    {
                        throw new InvalidOperationException(
                            $"Transactional copy exceeds the byte budget of {MaximumCopiedBytes}: '{source}'.");
                    }

                    File.Copy(entry, destinationEntry, false);
                }
            }
        }


        internal static void ValidateDirectoryMovePathBudgets(
            string sourceDirectory,
            string destinationDirectory,
            string displayName)
        {
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                destinationDirectory,
                displayName + " root");
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            var pending = new Stack<CopyDirectoryEntry>();
            pending.Push(new CopyDirectoryEntry(sourceDirectory, destinationDirectory, 0));
            int entryCount = 0;
            while (pending.Count > 0)
            {
                CopyDirectoryEntry current = pending.Pop();
                if (current.Depth > MaximumCopyDepth)
                {
                    throw new InvalidOperationException(
                        $"{displayName} exceeds the maximum directory depth of {MaximumCopyDepth}: '{sourceDirectory}'.");
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             current.Source,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaximumCopiedEntries)
                    {
                        throw new InvalidOperationException(
                            $"{displayName} exceeds the entry limit of {MaximumCopiedEntries}: '{sourceDirectory}'.");
                    }

                    string destination = Path.Combine(
                        current.Destination,
                        Path.GetFileName(entry));
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"{displayName} contains a reparse-point entry: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                            destination,
                            displayName);
                        pending.Push(new CopyDirectoryEntry(
                            entry,
                            destination,
                            current.Depth + 1));
                    }
                    else
                    {
                        BuildPathPolicy.EnsureWin32MaxPathBudget(
                            destination,
                            displayName);
                    }
                }
            }
        }


    }
}
