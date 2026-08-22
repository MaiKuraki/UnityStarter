using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Pure path-resolution helpers shared by the window creator's validators and writers. This class has no
    /// dependencies on either validator so the assembly and creation validators keep a one-way relationship
    /// instead of forming a static call cycle.
    /// </summary>
    internal static class UIWindowPathUtility
    {
        internal static bool TryValidateAssetFilePath(
            string assetPath,
            string requiredExtension,
            out string canonicalPath,
            out string error)
        {
            canonicalPath = string.Empty;
            if (!TryResolveAssetPath(assetPath, out canonicalPath, out _, out error))
            {
                return false;
            }

            if (string.IsNullOrEmpty(requiredExtension) ||
                !requiredExtension.StartsWith(".", StringComparison.Ordinal))
            {
                error = "A required file extension must begin with '.'.";
                return false;
            }

            if (!string.Equals(
                    Path.GetExtension(canonicalPath),
                    requiredExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = $"Asset path '{assetPath}' must use the '{requiredExtension}' extension.";
                return false;
            }

            return true;
        }

        internal static bool TryGetAbsoluteAssetPath(
            string assetPath,
            out string absolutePath,
            out string error)
        {
            return TryResolveAssetPath(assetPath, out _, out absolutePath, out error);
        }

        internal static bool TryEnsureOutputAvailable(
            string assetPath,
            string requiredExtension,
            out string absolutePath,
            out string error)
        {
            absolutePath = string.Empty;
            if (!TryValidateAssetFilePath(
                    assetPath,
                    requiredExtension,
                    out string canonicalPath,
                    out error) ||
                !TryGetAbsoluteAssetPath(canonicalPath, out absolutePath, out error))
            {
                return false;
            }

            bool assetExists = File.Exists(absolutePath);
            bool metaExists = File.Exists(absolutePath + ".meta");
            if (!assetExists && !metaExists)
            {
                return true;
            }

            string collision = assetExists && metaExists
                ? "asset and metadata files"
                : assetExists
                    ? "asset file"
                    : "orphan metadata file";
            error = $"Output '{canonicalPath}' already has an existing {collision}.";
            absolutePath = string.Empty;
            return false;
        }

        internal static bool TryNormalizeAssetFolderPath(
            string path,
            out string normalized,
            out string error)
        {
            normalized = string.Empty;
            error = string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                error = "A selected output folder has no project asset path.";
                return false;
            }

            string trimmed = path.EndsWith("/", StringComparison.Ordinal)
                ? path.Substring(0, path.Length - 1)
                : path;
            string probe = string.Equals(trimmed, "Assets", StringComparison.Ordinal)
                ? "Assets/__uiwindow_creator_folder_probe__.tmp"
                : trimmed + "/__uiwindow_creator_folder_probe__.tmp";
            if (!TryResolveAssetPath(probe, out string canonicalProbe, out _, out error))
            {
                return false;
            }

            int separator = canonicalProbe.LastIndexOf('/');
            normalized = canonicalProbe.Substring(0, separator + 1);
            return true;
        }

        internal static bool TryResolveAssetPath(
            string assetPath,
            out string canonicalPath,
            out string absolutePath,
            out string error)
        {
            const int maxAssetPathLength = 1024;
            canonicalPath = string.Empty;
            absolutePath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrEmpty(assetPath) || assetPath.Length > maxAssetPathLength)
            {
                error = $"Asset path is empty or exceeds {maxAssetPathLength} characters.";
                return false;
            }
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetPath.IndexOf('\\') >= 0 ||
                assetPath.EndsWith("/", StringComparison.Ordinal))
            {
                error = $"Asset path '{assetPath}' must be a canonical file path under Assets/.";
                return false;
            }

            for (int i = 0; i < assetPath.Length; i++)
            {
                if (char.IsControl(assetPath[i]))
                {
                    error = "Asset paths cannot contain control characters.";
                    return false;
                }
            }

            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                absolutePath = Path.GetFullPath(Path.Combine(
                    assetsRoot,
                    assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
                StringComparison pathComparison = Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string rootPrefix = assetsRoot + Path.DirectorySeparatorChar;
                if (!absolutePath.StartsWith(rootPrefix, pathComparison))
                {
                    error = $"Asset path '{assetPath}' escapes the project Assets root.";
                    absolutePath = string.Empty;
                    return false;
                }

                string relative = absolutePath.Substring(rootPrefix.Length)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                canonicalPath = "Assets/" + relative;
                if (!string.Equals(assetPath, canonicalPath, StringComparison.Ordinal))
                {
                    error = $"Asset path '{assetPath}' is not canonical. Expected '{canonicalPath}'.";
                    canonicalPath = string.Empty;
                    absolutePath = string.Empty;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = $"Asset path '{assetPath}' is invalid: {exception.Message}";
                canonicalPath = string.Empty;
                absolutePath = string.Empty;
                return false;
            }
        }
    }
}
