using System;
using System.IO;

using CycloneGames.IO.Runtime;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchProfilePathResolver
    {
        public static string ResolveRootDirectory(AssetPatchRootDirectorySource source, string pathOrRelativePath)
        {
            switch (source)
            {
                case AssetPatchRootDirectorySource.PersistentDataPath:
                    return ResolveUnderRoot(Application.persistentDataPath, pathOrRelativePath);
                case AssetPatchRootDirectorySource.TemporaryCachePath:
                    return ResolveUnderRoot(Application.temporaryCachePath, pathOrRelativePath);
                case AssetPatchRootDirectorySource.StreamingAssetsPath:
                    return ResolveUnderRoot(Application.streamingAssetsPath, pathOrRelativePath);
                default:
                    return ResolveExplicitPath(pathOrRelativePath);
            }
        }

        private static string ResolveUnderRoot(string rootDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new InvalidOperationException("Patch root base directory is not available on this platform.");
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return Path.GetFullPath(rootDirectory);
            }

            string combined = Path.Combine(rootDirectory, relativePath);
            return FilePathSecurity.EnsureWithinRoot(rootDirectory, combined);
        }

        private static string ResolveExplicitPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Explicit patch root path cannot be null or empty.", nameof(path));
            }

            return Path.GetFullPath(path);
        }
    }
}
