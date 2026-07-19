using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AssetStorageVolumeUtility
    {
        internal static bool TryGetAvailableBytes(string path, out long availableBytes)
        {
            availableBytes = -1L;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            bool caseInsensitive = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            DriveInfo selected = null;
            int selectedRootLength = -1;
            DriveInfo[] drives = DriveInfo.GetDrives();
            for (int i = 0; i < drives.Length; i++)
            {
                DriveInfo drive = drives[i];
                string root = drive.RootDirectory.FullName;
                if (root.Length > selectedRootLength &&
                    IsPathWithinRoot(fullPath, root, caseInsensitive))
                {
                    selected = drive;
                    selectedRootLength = root.Length;
                }
            }

            if (selected == null || !selected.IsReady)
            {
                return false;
            }

            availableBytes = selected.AvailableFreeSpace;
            return availableBytes >= 0L;
        }

        internal static bool IsPathWithinRoot(
            string fullPath,
            string root,
            bool caseInsensitive)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            StringComparison comparison = caseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(fullPath, root, comparison))
            {
                return true;
            }

            if (!fullPath.StartsWith(root, comparison))
            {
                return false;
            }

            if (IsDirectorySeparator(root[root.Length - 1]))
            {
                return true;
            }

            return fullPath.Length > root.Length &&
                   IsDirectorySeparator(fullPath[root.Length]);
        }

        private static bool IsDirectorySeparator(char value)
        {
            return value == Path.DirectorySeparatorChar ||
                   value == Path.AltDirectorySeparatorChar ||
                   value == '/' ||
                   value == '\\';
        }
    }
}
