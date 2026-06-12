using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the Unity Console.
    /// Includes file path and line number in a format recognized by Unity for click-to-source.
    /// Designed to avoid extra allocations by formatting into a pooled StringBuilder.
    /// </summary>
    public sealed class UnityLogger : ILogger
    {
        public UnityLogger()
        {
            LoggerUpdater.EnsureInstance();
        }

        public void Log(LogMessage logMessage)
        {
            string unityMessage = FormatMessage(logMessage);
            LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage);
        }

        [UnityEngine.HideInCallstack]
        internal void LogImmediate(LogMessage logMessage)
        {
            string unityMessage = FormatMessage(logMessage);
            LoggerUpdater.LogToUnity(logMessage.Level, unityMessage, false);
        }

        internal static string FormatMessage(LogMessage logMessage)
        {
            if (logMessage == null) throw new ArgumentNullException(nameof(logMessage));

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append('[');
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }

                if (logMessage.MessageBuilder != null)
                {
                    var mb = logMessage.MessageBuilder;
                    for (int i = 0; i < mb.Length; i++)
                    {
                        sb.Append(mb[i]);
                    }
                }
                else if (logMessage.OriginalMessage != null)
                {
                    sb.Append(logMessage.OriginalMessage);
                }

                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    string sourcePath = logMessage.FilePath;
                    int assetsIndex = sourcePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIndex < 0)
                    {
                        assetsIndex = sourcePath.IndexOf("\\Assets\\", StringComparison.OrdinalIgnoreCase);
                    }

                    int startIndex = assetsIndex >= 0 ? assetsIndex + 1 : 0;

#if UNITY_EDITOR
                    string displayPath = LoggerEditorPathResolver.GetDisplayPath(sourcePath, startIndex);
                    LoggerEditorLinkRegistry.Register(displayPath, logMessage.LineNumber, GetUnityPath(sourcePath, 0));

                    sb.Append("\n\n<a href=\"");
                    sb.Append(displayPath);
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append("\">(at ");
                    sb.Append(displayPath);
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(")</a>");
#else
                    sb.Append("\n(at ");
                    AppendUnityPath(sb, sourcePath, startIndex);
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
#endif
                }

                // ToString() allocation is unavoidable here because Debug.LogFormat requires a string argument.
                return sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

#if UNITY_EDITOR
        private static string GetUnityPath(string sourcePath, int startIndex)
        {
            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                AppendUnityPath(sb, sourcePath, startIndex);
                return sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }
#endif

        private static void AppendUnityPath(StringBuilder sb, string sourcePath, int startIndex)
        {
            for (int i = startIndex; i < sourcePath.Length; i++)
            {
                sb.Append(sourcePath[i] == '\\' ? '/' : sourcePath[i]);
            }
        }

        public void Dispose() { }
    }

#if UNITY_EDITOR
    internal static class LoggerEditorLinkRegistry
    {
        private const int MaxEntries = 2048;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, string> FullPathByKey = new Dictionary<string, string>(MaxEntries, StringComparer.Ordinal);
        private static readonly string[] KeyRing = new string[MaxEntries];
        private static int _nextIndex;

        public static void Register(string assetPath, int lineNumber, string fullPath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(fullPath)) return;

            string key = MakeKey(assetPath, lineNumber);
            lock (SyncRoot)
            {
                if (FullPathByKey.ContainsKey(key))
                {
                    FullPathByKey[key] = fullPath;
                    return;
                }

                string oldKey = KeyRing[_nextIndex];
                if (!string.IsNullOrEmpty(oldKey))
                {
                    FullPathByKey.Remove(oldKey);
                }

                KeyRing[_nextIndex] = key;
                FullPathByKey[key] = fullPath;
                _nextIndex++;
                if (_nextIndex >= MaxEntries)
                {
                    _nextIndex = 0;
                }
            }
        }

        public static bool TryGetFullPath(string assetPath, int lineNumber, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string key = MakeKey(assetPath, lineNumber);
            lock (SyncRoot)
            {
                return FullPathByKey.TryGetValue(key, out fullPath) && !string.IsNullOrEmpty(fullPath);
            }
        }

        private static string MakeKey(string assetPath, int lineNumber)
        {
            return assetPath + ":" + lineNumber;
        }
    }

    internal static class LoggerEditorPathResolver
    {
        private const int MaxCacheEntries = 2048;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, string> DisplayPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] CacheKeys = new string[MaxCacheEntries];
        private static int _nextCacheIndex;

        public static string GetDisplayPath(string sourcePath, int fallbackStartIndex)
        {
            if (string.IsNullOrEmpty(sourcePath)) return sourcePath;

            string normalizedSourcePath = NormalizePath(sourcePath);
            lock (SyncRoot)
            {
                if (DisplayPathCache.TryGetValue(normalizedSourcePath, out var cachedPath))
                {
                    return cachedPath;
                }
            }

            string displayPath = ResolveDisplayPath(normalizedSourcePath, fallbackStartIndex);
            CacheDisplayPath(normalizedSourcePath, displayPath);
            return displayPath;
        }

        private static string ResolveDisplayPath(string normalizedSourcePath, int fallbackStartIndex)
        {
            string projectAssetsPath = NormalizePath(UnityEngine.Application.dataPath);
            if (IsSameOrChildPath(normalizedSourcePath, projectAssetsPath))
            {
                return "Assets" + normalizedSourcePath.Substring(projectAssetsPath.Length);
            }

            if (TryResolvePackagePath(normalizedSourcePath, out var packagePath))
            {
                return packagePath;
            }

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                AppendPath(sb, normalizedSourcePath, fallbackStartIndex);
                return sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        private static bool TryResolvePackagePath(string normalizedSourcePath, out string packagePath)
        {
            packagePath = null;

            string directory = Path.GetDirectoryName(normalizedSourcePath);
            directory = NormalizePath(directory);
            while (!string.IsNullOrEmpty(directory))
            {
                string packageJsonPath = directory + "/package.json";
                if (File.Exists(packageJsonPath) && TryReadPackageName(packageJsonPath, out var packageName))
                {
                    string root = TrimTrailingSlash(directory);
                    if (!IsSameOrChildPath(normalizedSourcePath, root)) return false;

                    packagePath = "Packages/" + packageName + normalizedSourcePath.Substring(root.Length);
                    return true;
                }

                string parent = NormalizePath(Path.GetDirectoryName(directory));
                if (string.IsNullOrEmpty(parent) || parent == directory) break;

                directory = parent;
            }

            return false;
        }

        private static bool TryReadPackageName(string packageJsonPath, out string packageName)
        {
            packageName = null;

            try
            {
                string content = File.ReadAllText(packageJsonPath);
                int nameIndex = content.IndexOf("\"name\"", StringComparison.Ordinal);
                if (nameIndex < 0) return false;

                int colonIndex = content.IndexOf(':', nameIndex + 6);
                if (colonIndex < 0) return false;

                int quoteStart = content.IndexOf('"', colonIndex + 1);
                if (quoteStart < 0) return false;

                int quoteEnd = content.IndexOf('"', quoteStart + 1);
                if (quoteEnd <= quoteStart + 1) return false;

                packageName = content.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                return !string.IsNullOrEmpty(packageName);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void CacheDisplayPath(string sourcePath, string displayPath)
        {
            lock (SyncRoot)
            {
                if (DisplayPathCache.ContainsKey(sourcePath)) return;

                string oldKey = CacheKeys[_nextCacheIndex];
                if (!string.IsNullOrEmpty(oldKey))
                {
                    DisplayPathCache.Remove(oldKey);
                }

                CacheKeys[_nextCacheIndex] = sourcePath;
                DisplayPathCache[sourcePath] = displayPath;
                _nextCacheIndex++;
                if (_nextCacheIndex >= MaxCacheEntries)
                {
                    _nextCacheIndex = 0;
                }
            }
        }

        private static bool IsSameOrChildPath(string filePath, string rootPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(rootPath)) return false;

            rootPath = TrimTrailingSlash(rootPath);
            if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) return false;
            return filePath.Length == rootPath.Length || filePath[rootPath.Length] == '/';
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

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        private static void AppendPath(StringBuilder sb, string sourcePath, int startIndex)
        {
            for (int i = startIndex; i < sourcePath.Length; i++)
            {
                sb.Append(sourcePath[i] == '\\' ? '/' : sourcePath[i]);
            }
        }
    }
#endif
}
