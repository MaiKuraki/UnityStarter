#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Editor
{
    /// <summary>
    /// Handles hyperlink clicks in the Unity Console for LogPipeline messages.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggingHyperlinkHandler
    {
        private static readonly object PackageRootCacheLock = new object();
        private static string[] _registeredPackageRoots;
#if UNITY_INCLUDE_TESTS
        private static int _packageRootRefreshCount;

        internal static int PackageRootRefreshCountForTests
        {
            get
            {
                lock (PackageRootCacheLock)
                {
                    return _packageRootRefreshCount;
                }
            }
        }
#endif

        static LoggingHyperlinkHandler()
        {
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            InvalidatePackageRootCache();
        }

        private static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            args.hyperLinkData.TryGetValue("path", out var assetPath);
            args.hyperLinkData.TryGetValue("href", out var hrefPath);
            args.hyperLinkData.TryGetValue("line", out var lineStr);
            int lineNumber = ParseLineNumber(assetPath, hrefPath, null, lineStr);
            if (!TryResolveRegisteredPath(assetPath, lineNumber, out string registeredFullPath)
                && !TryResolveRegisteredPath(hrefPath, lineNumber, out registeredFullPath))
            {
                return;
            }

            string fullPath = NormalizePath(registeredFullPath);
            if (!IsAllowedLoggingSourcePath(fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, lineNumber);
        }

        internal static bool IsAllowedLoggingSourcePath(string fullPath)
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

            string[] packageRoots = GetRegisteredPackageRoots();
            for (int i = 0; i < packageRoots.Length; i++)
            {
                if (IsSameOrChildPath(fullPath, packageRoots[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void InvalidatePackageRootCache()
        {
            lock (PackageRootCacheLock)
            {
                _registeredPackageRoots = null;
            }
        }

        private static void OnRegisteredPackages(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            InvalidatePackageRootCache();
        }

        private static void OnBeforeAssemblyReload()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            InvalidatePackageRootCache();
        }

        private static bool TryResolveRegisteredPath(string candidate, int lineNumber, out string fullPath)
        {
            string linkPath = NormalizePath(candidate);
            StripLineSuffix(ref linkPath);
            return LoggingEditorLinkRegistry.TryGetFullPath(linkPath, lineNumber, out fullPath);
        }

        private static string[] GetRegisteredPackageRoots()
        {
            lock (PackageRootCacheLock)
            {
                if (_registeredPackageRoots != null)
                {
                    return _registeredPackageRoots;
                }

                UnityEditor.PackageManager.PackageInfo[] packages;
                try
                {
                    packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    packages = null;
                }

                if (packages == null || packages.Length == 0)
                {
                    _registeredPackageRoots = Array.Empty<string>();
                }
                else
                {
                    var roots = new string[packages.Length];
                    int rootCount = 0;
                    for (int i = 0; i < packages.Length; i++)
                    {
                        string root = NormalizeFullPath(packages[i]?.resolvedPath);
                        if (!string.IsNullOrEmpty(root))
                        {
                            roots[rootCount++] = root;
                        }
                    }

                    if (rootCount == 0)
                    {
                        _registeredPackageRoots = Array.Empty<string>();
                    }
                    else if (rootCount == roots.Length)
                    {
                        _registeredPackageRoots = roots;
                    }
                    else
                    {
                        var compactRoots = new string[rootCount];
                        Array.Copy(roots, compactRoots, rootCount);
                        _registeredPackageRoots = compactRoots;
                    }
                }

#if UNITY_INCLUDE_TESTS
                _packageRootRefreshCount++;
#endif
                return _registeredPackageRoots;
            }
        }

        private static string NormalizeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            try
            {
                return NormalizePath(Path.GetFullPath(path)).TrimEnd('/');
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return string.Empty;
            }
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

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            if (!IsAbsolutePath(path) && path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            return path;
        }

    }
}
#endif
