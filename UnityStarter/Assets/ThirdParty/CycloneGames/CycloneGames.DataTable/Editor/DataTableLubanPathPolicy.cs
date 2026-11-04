using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Single source of truth for the DataTable pipeline path policy in the Editor.
    /// The repository root is derived from the configured build_config.ini instead of a
    /// fixed project depth, and the tool project is accepted either inside that repository
    /// or inside the package that owns this Editor assembly.
    /// </summary>
    internal static class DataTableLubanPathPolicy
    {
        internal static StringComparison PathComparison =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        internal static string FindRepositoryRoot(string configurationPath)
        {
            string fullConfigurationPath = Path.GetFullPath(configurationPath);
            string configurationDirectory =
                Path.GetDirectoryName(fullConfigurationPath) ??
                throw new ArgumentException(
                    "The pipeline configuration path has no directory.",
                    nameof(configurationPath));
            string normalizedConfigurationDirectory = TrimEndingSeparator(configurationDirectory);

            var current = new DirectoryInfo(configurationDirectory);
            while (current != null)
            {
                string dataTableRoot = Path.Combine(current.FullName, "DataTable");
                if (Directory.Exists(dataTableRoot) &&
                    (string.Equals(
                         TrimEndingSeparator(Path.GetFullPath(dataTableRoot)),
                         normalizedConfigurationDirectory,
                         PathComparison) ||
                     IsStrictChild(dataTableRoot, configurationDirectory)))
                {
                    return Path.GetFullPath(current.FullName);
                }

                current = current.Parent;
            }

            throw new ArgumentException(
                "Could not discover the repository root that owns the DataTable directory for: " +
                fullConfigurationPath,
                nameof(configurationPath));
        }

        internal static bool IsContainedBy(string approvedRoot, string path)
        {
            string fullRoot = TrimEndingSeparator(Path.GetFullPath(approvedRoot));
            string fullPath = TrimEndingSeparator(Path.GetFullPath(path));
            return string.Equals(fullRoot, fullPath, PathComparison) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
        }

        internal static string ValidateContainedFile(
            string path,
            string approvedRoot,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", parameterName);
            }

            string fullPath = Path.GetFullPath(path);
            if (!IsContainedBy(approvedRoot, fullPath))
            {
                throw new ArgumentException(
                    "Path must be contained by " + approvedRoot + ": " + fullPath,
                    parameterName);
            }

            AssertNoReparsePoints(fullPath, approvedRoot, parameterName);
            return fullPath;
        }

        internal static bool IsOwnPackagePath(string path)
        {
            PackageManagerInfo package =
                PackageManagerInfo.FindForAssembly(typeof(DataTableLubanPathPolicy).Assembly);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                return false;
            }

            return IsStrictChild(package.resolvedPath, path);
        }

        private static bool IsStrictChild(string parent, string child)
        {
            string fullParent = TrimEndingSeparator(Path.GetFullPath(parent));
            string fullChild = TrimEndingSeparator(Path.GetFullPath(child));
            return fullChild.StartsWith(fullParent + Path.DirectorySeparatorChar, PathComparison);
        }

        private static void AssertNoReparsePoints(
            string fullPath,
            string approvedRoot,
            string parameterName)
        {
            string root = TrimEndingSeparator(Path.GetFullPath(approvedRoot));
            string probe = fullPath;
            while (!string.Equals(probe, root, PathComparison))
            {
                if ((File.Exists(probe) || Directory.Exists(probe)) &&
                    (File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException("Path traverses a reparse point: " + probe, parameterName);
                }

                probe = Path.GetDirectoryName(probe) ??
                        throw new ArgumentException("Path did not reach its approved root.", parameterName);
            }
        }

        private static string TrimEndingSeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
