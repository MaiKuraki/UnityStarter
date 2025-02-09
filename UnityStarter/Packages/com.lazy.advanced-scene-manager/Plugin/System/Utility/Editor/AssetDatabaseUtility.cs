#if UNITY_EDITOR

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>Contains utility functions for working with the asset database.</summary>
    public static class AssetDatabaseUtility
    {

        const char BackSlash = '\\';
        const char ForwardSlash = '/';
        const string AssetsPath = "Assets/";

        /// <inheritdoc cref="CreateFolder(string, out string)"/>
        public static bool CreateFolder(string folder) =>
            CreateFolder(folder, out _);

        /// <summary>Creates the specified folder.</summary>
        /// <param name="path">The path to create folder at. Supports absolute paths (on same drive as project). Attempts to detect if path is file, and will then create containing folder.</param>
        /// <param name="createdFolder">The created folder.</param>
        /// <returns><see langword="true"/> if folder already exists, or if folder was created.</returns>
        public static bool CreateFolder(string path, [NotNullWhen(true)] out string createdFolder)
        {

            createdFolder = null;
            path = path?.ConvertToUnixPath()?.Trim();
            if (string.IsNullOrEmpty(path?.Trim('/')))
                return false;

            if (Path.IsPathRooted(path))
                path = Path.GetRelativePath(Application.dataPath, path).ConvertToUnixPath();

            if (path.StartsWith("../"))
                return false;

            var folder = Application.dataPath + ForwardSlash + path.Trim();
            folder = folder.Replace("/Assets/Assets", "/Assets/");

            createdFolder = Directory.CreateDirectory(folder).FullName.MakeRelative();
            createdFolder = createdFolder.TrimEnd(ForwardSlash) + ForwardSlash;
            AssetDatabase.ImportAsset(createdFolder);

            return AssetDatabase.IsValidFolder(createdFolder);

        }

        /// <summary>Converts the path separators to use forward slash.</summary>
        public static string ConvertToUnixPath(this string path) =>
              path?.Replace(BackSlash, ForwardSlash);

        /// <summary>Makes the path absolute. Converts path to unix style.</summary>
        /// <remarks>Only works for same disk as <see cref="Application.dataPath"/> is on.</remarks>
        public static string MakeRelative(this string path, bool includeAssetsFolder = true, bool prefixWithAssetsIfNecessary = true)
        {

            path = path.ConvertToUnixPath();

            if (path.StartsWith(Application.dataPath))
                path = path.Remove(0, Application.dataPath.Length);

            var assetsPath = AssetsPath.TrimEnd(ForwardSlash);
            var startsWithAssets = (path.StartsWith(AssetsPath) || path == assetsPath);

            if (!includeAssetsFolder && startsWithAssets)
                path = path.Remove(0, assetsPath.Length).TrimStart(ForwardSlash);

            else if (includeAssetsFolder && !startsWithAssets && prefixWithAssetsIfNecessary)
                path = AssetsPath + path.TrimStart(ForwardSlash);

            return path;

        }

        /// <summary>Finds all assets of type <typeparamref name="T"/>.</summary>
        public static IEnumerable<T> FindAssets<T>() where T : Object
        {
            return FindAssets<T>(System.Array.Empty<string>());
        }

        /// <summary>Finds all assets of type <typeparamref name="T"/>.</summary>
        public static IEnumerable<T> FindAssets<T>(params string[] searchInFolders) where T : Object
        {
            return FindAssetPaths<T>(searchInFolders).Select(AssetDatabase.LoadAssetAtPath<T>);
        }

        /// <summary>Finds all assets of type <typeparamref name="T"/>.</summary>
        public static IEnumerable<string> FindAssetPaths<T>(params string[] searchInFolders) where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).FullName}", searchInFolders).Select(AssetDatabase.GUIDToAssetPath);
        }

    }

}

#endif
