#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logger.Editor
{
    /// <summary>
    /// Handles hyperlink clicks in the Unity Console for CLogger messages.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggerHyperLinkHandler
    {
        static LoggerHyperLinkHandler()
        {
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
        }

        private static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            args.hyperLinkData.TryGetValue("path", out var assetPath);
            args.hyperLinkData.TryGetValue("href", out var hrefPath);
            args.hyperLinkData.TryGetValue("line", out var lineStr);
            string displayPath = NormalizePath(string.IsNullOrEmpty(assetPath) ? hrefPath : assetPath);
            int lineNumber = ParseLineNumber(displayPath, hrefPath, null, lineStr);
            StripLineSuffix(ref displayPath);
            if (!LoggerEditorLinkRegistry.TryGetFullPath(displayPath, lineNumber, out string registeredFullPath))
            {
                return;
            }

            string fullPath = NormalizePath(registeredFullPath);
            if (!IsAllowedLoggerSourcePath(fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, lineNumber);
        }

        private static bool IsAllowedLoggerSourcePath(string fullPath)
        {
            if (!IsAbsolutePath(fullPath))
            {
                return false;
            }

            string projectRoot = NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            if (IsSameOrChildPath(fullPath, projectRoot))
            {
                return true;
            }

            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                string resolvedPath = NormalizePath(packages[i].resolvedPath);
                if (IsSameOrChildPath(fullPath, resolvedPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseLoggerHref(string hrefPath, out string assetPath, out string fullPath, out string line)
        {
            assetPath = null;
            fullPath = null;
            line = null;

            const string Prefix = "clogger://open?";
            if (string.IsNullOrEmpty(hrefPath) || !hrefPath.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            int index = Prefix.Length;
            while (index < hrefPath.Length)
            {
                int equalsIndex = hrefPath.IndexOf('=', index);
                if (equalsIndex < 0) break;

                int valueEnd = hrefPath.IndexOf(';', equalsIndex + 1);
                if (valueEnd < 0) valueEnd = hrefPath.Length;

                string key = hrefPath.Substring(index, equalsIndex - index);
                string value = Uri.UnescapeDataString(hrefPath.Substring(equalsIndex + 1, valueEnd - equalsIndex - 1));

                switch (key)
                {
                    case "asset":
                        assetPath = value;
                        break;
                    case "path":
                        fullPath = value;
                        break;
                    case "line":
                        line = value;
                        break;
                }

                index = valueEnd + 1;
            }

            return !string.IsNullOrEmpty(assetPath) || !string.IsNullOrEmpty(fullPath);
        }

        private static bool TryOpenAssetPath(string assetPath, int lineNumber)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (!IsAssetPath(assetPath))
            {
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) return false;

            AssetDatabase.OpenAsset(asset, lineNumber);
            return true;
        }

        private static bool IsAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.StartsWith("Packages/", StringComparison.Ordinal);
        }

        private static bool TryResolvePackageAssetPath(string assetPath, string fullPath, out string packageAssetPath)
        {
            packageAssetPath = null;

            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            for (int i = 0; i < packages.Length; i++)
            {
                var package = packages[i];
                string packageAssetPathRoot = NormalizePath(package.assetPath);
                if (string.IsNullOrEmpty(packageAssetPathRoot))
                {
                    packageAssetPathRoot = "Packages/" + package.name;
                }

                string resolvedPath = NormalizePath(package.resolvedPath);
                if (!string.IsNullOrEmpty(resolvedPath) && IsAbsolutePath(fullPath))
                {
                    string resolvedRoot = TrimTrailingSlash(resolvedPath);
                    if (IsSameOrChildPath(fullPath, resolvedRoot))
                    {
                        packageAssetPath = packageAssetPathRoot + fullPath.Substring(resolvedRoot.Length);
                        return true;
                    }
                }

                if (TryGetPackageFolderName(resolvedPath, out var packageFolderName)
                    && TryCreatePackageAssetPathFromFolder(assetPath, packageFolderName, packageAssetPathRoot, out packageAssetPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreatePackageAssetPathFromFolder(string assetPath, string packageFolderName, string packageAssetPathRoot, out string packageAssetPath)
        {
            packageAssetPath = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            int index = FindPathSegment(assetPath, packageFolderName);
            if (index < 0) return false;

            int suffixStart = index + packageFolderName.Length;
            string suffix = suffixStart < assetPath.Length ? assetPath.Substring(suffixStart) : string.Empty;
            packageAssetPath = packageAssetPathRoot + suffix;
            return true;
        }

        private static int FindPathSegment(string path, string segment)
        {
            int searchIndex = 0;
            while (searchIndex < path.Length)
            {
                int index = path.IndexOf(segment, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return -1;

                bool startsAtSegment = index == 0 || path[index - 1] == '/';
                int end = index + segment.Length;
                bool endsAtSegment = end == path.Length || path[end] == '/';
                if (startsAtSegment && endsAtSegment)
                {
                    return index;
                }

                searchIndex = end;
            }

            return -1;
        }

        private static bool TryGetPackageFolderName(string resolvedPath, out string folderName)
        {
            folderName = null;
            if (string.IsNullOrEmpty(resolvedPath)) return false;

            string trimmed = TrimTrailingSlash(resolvedPath);
            int lastSlash = trimmed.LastIndexOf('/');
            folderName = lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
            return !string.IsNullOrEmpty(folderName);
        }

        private static bool TryOpenAbsolutePath(string filePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(filePath) || !IsAbsolutePath(filePath)) return false;
            if (!File.Exists(filePath)) return false;

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(filePath, lineNumber);
            return true;
        }

        private static bool TryOpenProjectRelativePath(string filePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(filePath) || IsAbsolutePath(filePath)) return false;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, filePath));
            if (!File.Exists(absolutePath)) return false;

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(absolutePath, lineNumber);
            return true;
        }

        private static int ParseLineNumber(string assetPath, string hrefPath, string fullPath, string lineStr)
        {
            if (int.TryParse(lineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber))
            {
                return lineNumber;
            }

            if (TryParseLineSuffix(assetPath, out lineNumber)) return lineNumber;
            if (TryParseLineSuffix(hrefPath, out lineNumber)) return lineNumber;
            if (TryParseLineSuffix(fullPath, out lineNumber)) return lineNumber;

            return lineNumber;
        }

        private static bool TryParseLineSuffix(string filePath, out int lineNumber)
        {
            lineNumber = 0;
            if (string.IsNullOrEmpty(filePath)) return false;

            int colonIndex = filePath.LastIndexOf(':');
            if (colonIndex < 0 || colonIndex + 1 >= filePath.Length) return false;

            return int.TryParse(
                filePath.Substring(colonIndex + 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out lineNumber);
        }

        private static void StripLineSuffix(ref string filePath)
        {
            if (!TryParseLineSuffix(filePath, out _)) return;

            int colonIndex = filePath.LastIndexOf(':');
            filePath = filePath.Substring(0, colonIndex);
        }

        private static bool IsSameOrChildPath(string filePath, string rootPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(rootPath)) return false;
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!filePath.StartsWith(rootPath, comparison)) return false;
            return filePath.Length == rootPath.Length || filePath[rootPath.Length] == '/';
        }

        private static bool IsAbsolutePath(string path)
        {
            return !string.IsNullOrEmpty(path) && Path.IsPathRooted(path);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            if (!IsAbsolutePath(path) && path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            return path;
        }

        private static string TrimTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int end = path.Length;
            while (end > 0 && path[end - 1] == '/')
            {
                end--;
            }

            return end == path.Length ? path : path.Substring(0, end);
        }
    }
}
#endif
