using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Logger.Util;
using UnityEngine;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Bounded adapter from the synchronous core sink contract to the Unity main thread.
    /// </summary>
    public sealed class UnityLogger : ILogger, IFlushableLogger, IIdempotentLoggerSinkDisposal
    {
        private const int FormattingOverheadEstimate = LoggerProcessingOptions.UnityFormattingOverheadCharacters;
        private readonly int _adapterGeneration;
        private int _disposed;

        public UnityLogger()
        {
            _adapterGeneration = LoggerUpdater.RegisterAdapter();
            try
            {
                LoggerUpdater.EnsureInstance();
            }
            catch
            {
                LoggerUpdater.UnregisterAdapter(_adapterGeneration);
                throw;
            }
        }

        public static UnityLoggerStatistics GetStatistics()
        {
            return LoggerUpdater.GetStatistics();
        }

        public void Log(LogMessage logMessage)
        {
            if (logMessage == null)
            {
                throw new ArgumentNullException(nameof(logMessage));
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(UnityLogger));
            }

            int sourcePathCharacters = logMessage.FilePath?.Length ?? 0;
            int estimate = SaturatingAdd(
                logMessage.MessageLength,
                logMessage.Category?.Length ?? 0,
                sourcePathCharacters,
                sourcePathCharacters,
                FormattingOverheadEstimate);
#if UNITY_EDITOR
            estimate = SaturatingAdd(estimate, sourcePathCharacters);
#endif
            if (!LoggerUpdater.TryReserve(logMessage.Level, estimate, out LoggerUpdater.Reservation reservation))
            {
                return;
            }

            string formatted = null;
            bool reservationOwned = true;
            try
            {
                formatted = FormatMessage(logMessage);
                reservationOwned = false;
#if UNITY_EDITOR
                LoggerUpdater.Commit(logMessage.Level, formatted, reservation, logMessage.FilePath, logMessage.LineNumber);
#else
                LoggerUpdater.Commit(logMessage.Level, formatted, reservation);
#endif
            }
            finally
            {
                if (reservationOwned)
                {
                    LoggerUpdater.CancelReservation(reservation);
                }
            }
        }

        internal static string FormatMessage(LogMessage logMessage)
        {
            StringBuilder builder = StringBuilderPool.Get();
            try
            {
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    builder.Append('[');
                    AppendSafePath(builder, logMessage.Category);
                    builder.Append("] ");
                }

                logMessage.AppendMessageTo(builder);
                AppendSourceLocation(builder, logMessage.FilePath, logMessage.LineNumber);
                return builder.ToString();
            }
            finally
            {
                StringBuilderPool.Return(builder);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                LoggerUpdater.UnregisterAdapter(_adapterGeneration);
            }
        }

        public bool TryFlush(LogFlushMode mode)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return true;
            }

            return LoggerUpdater.TryFlushUnityQueue(20);
        }

        private static void AppendSourceLocation(StringBuilder builder, string sourcePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

#if UNITY_EDITOR
            string displayPath = LoggerEditorPathResolver.GetDisplayPath(sourcePath);
            string fullPath = NormalizeFullPath(sourcePath);
            if (!string.IsNullOrEmpty(fullPath))
            {
                LoggerEditorLinkRegistry.Register(displayPath, lineNumber, fullPath);
            }

            builder.Append("\n\n<a href=\"");
            AppendSafePath(builder, displayPath);
            builder.Append(':');
            InvariantText.AppendInt32(builder, lineNumber);
            builder.Append("\">(at ");
            AppendSafePath(builder, displayPath);
            builder.Append(':');
            InvariantText.AppendInt32(builder, lineNumber);
            builder.Append(")</a>");
#else
            builder.Append("\n(at ");
            AppendFileName(builder, sourcePath);
            builder.Append(':');
            InvariantText.AppendInt32(builder, lineNumber);
            builder.Append(')');
#endif
        }

        private static void AppendFileName(StringBuilder builder, string path)
        {
            int start = 0;
            for (int i = 0; i < path.Length; i++)
            {
                char value = path[i];
                if (value == '/' || value == '\\')
                {
                    start = i + 1;
                }
            }

            for (int i = start; i < path.Length; i++)
            {
                char value = path[i];
                builder.Append(char.IsControl(value) || value == '<' || value == '>' || value == '"' ? '_' : value);
            }
        }

#if UNITY_EDITOR
        private static string NormalizeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
#endif

        private static void AppendSafePath(StringBuilder builder, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character) || character == '<' || character == '>' || character == '"')
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(character == '\\' ? '/' : character);
                }
            }
        }

        private static int SaturatingAdd(int left, int right)
        {
            return left > int.MaxValue - right ? int.MaxValue : left + right;
        }

        private static int SaturatingAdd(int first, int second, int third)
        {
            return SaturatingAdd(SaturatingAdd(first, second), third);
        }

        private static int SaturatingAdd(int first, int second, int third, int fourth, int fifth)
        {
            return SaturatingAdd(SaturatingAdd(first, second, third), SaturatingAdd(fourth, fifth));
        }
    }

#if UNITY_EDITOR
    internal static class LoggerEditorLinkRegistry
    {
        private readonly struct LinkKey : IEquatable<LinkKey>
        {
            internal readonly string DisplayPath;
            internal readonly int LineNumber;

            internal LinkKey(string displayPath, int lineNumber)
            {
                DisplayPath = displayPath;
                LineNumber = lineNumber;
            }

            public bool Equals(LinkKey other)
            {
                return LineNumber == other.LineNumber
                    && string.Equals(DisplayPath, other.DisplayPath, StringComparison.Ordinal);
            }

            public override bool Equals(object value)
            {
                return value is LinkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int pathHash = DisplayPath == null ? 0 : StringComparer.Ordinal.GetHashCode(DisplayPath);
                    return (pathHash * 397) ^ LineNumber;
                }
            }
        }

        private const int MaxEntries = 2048;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<LinkKey, string> FullPathByKey = new Dictionary<LinkKey, string>(MaxEntries);
        private static readonly LinkKey[] KeyRing = new LinkKey[MaxEntries];
        private static int _nextIndex;

        internal static void Register(string displayPath, int lineNumber, string fullPath)
        {
            if (string.IsNullOrEmpty(displayPath) || string.IsNullOrEmpty(fullPath))
            {
                return;
            }

            var key = new LinkKey(displayPath, lineNumber);
            lock (SyncRoot)
            {
                if (FullPathByKey.ContainsKey(key))
                {
                    FullPathByKey[key] = fullPath;
                    return;
                }

                LinkKey previousKey = KeyRing[_nextIndex];
                if (previousKey.DisplayPath != null)
                {
                    FullPathByKey.Remove(previousKey);
                }

                KeyRing[_nextIndex] = key;
                FullPathByKey[key] = fullPath;
                _nextIndex = (_nextIndex + 1) % MaxEntries;
            }
        }

        internal static bool TryGetFullPath(string displayPath, int lineNumber, out string fullPath)
        {
            if (string.IsNullOrEmpty(displayPath))
            {
                fullPath = null;
                return false;
            }

            lock (SyncRoot)
            {
                return FullPathByKey.TryGetValue(new LinkKey(displayPath, lineNumber), out fullPath)
                    && !string.IsNullOrEmpty(fullPath);
            }
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                FullPathByKey.Clear();
                Array.Clear(KeyRing, 0, KeyRing.Length);
                _nextIndex = 0;
            }
        }

    }

    internal static class LoggerEditorPathResolver
    {
        private const int MaxEntries = 2048;

        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> DisplayPathBySource = new Dictionary<string, string>(MaxEntries, StringComparer.Ordinal);
        private static readonly string[] KeyRing = new string[MaxEntries];
        private static StringComparison _pathComparison = StringComparison.Ordinal;
        private static string _assetsPath = string.Empty;
        private static string _projectRoot = string.Empty;
        private static int _nextIndex;

        internal static void Configure(string applicationDataPath, bool ignoreCase)
        {
            string assetsPath = NormalizePath(applicationDataPath);
            string projectRoot = NormalizePath(Path.GetDirectoryName(applicationDataPath));
            var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            lock (SyncRoot)
            {
                _assetsPath = assetsPath;
                _projectRoot = projectRoot;
                _pathComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                DisplayPathBySource = new Dictionary<string, string>(MaxEntries, comparer);
                Array.Clear(KeyRing, 0, KeyRing.Length);
                _nextIndex = 0;
            }
        }

        internal static string GetDisplayPath(string sourcePath)
        {
            string normalized = NormalizePath(sourcePath);
            lock (SyncRoot)
            {
                if (DisplayPathBySource.TryGetValue(normalized, out string cached))
                {
                    return cached;
                }
            }

            string displayPath = ResolveDisplayPath(normalized);
            lock (SyncRoot)
            {
                string previousKey = KeyRing[_nextIndex];
                if (!string.IsNullOrEmpty(previousKey))
                {
                    DisplayPathBySource.Remove(previousKey);
                }

                KeyRing[_nextIndex] = normalized;
                DisplayPathBySource[normalized] = displayPath;
                _nextIndex = (_nextIndex + 1) % MaxEntries;
            }

            return displayPath;
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                DisplayPathBySource.Clear();
                Array.Clear(KeyRing, 0, KeyRing.Length);
                _assetsPath = string.Empty;
                _projectRoot = string.Empty;
                _pathComparison = StringComparison.Ordinal;
                _nextIndex = 0;
            }
        }

        private static string ResolveDisplayPath(string normalizedSourcePath)
        {
            string assetsPath;
            string projectRoot;
            StringComparison pathComparison;
            lock (SyncRoot)
            {
                assetsPath = _assetsPath;
                projectRoot = _projectRoot;
                pathComparison = _pathComparison;
            }

            if (!string.IsNullOrEmpty(assetsPath)
                && IsSameOrChildPath(normalizedSourcePath, assetsPath, pathComparison))
            {
                return "Assets" + normalizedSourcePath.Substring(assetsPath.Length);
            }

            if (!string.IsNullOrEmpty(projectRoot)
                && IsSameOrChildPath(normalizedSourcePath, projectRoot, pathComparison))
            {
                string relative = normalizedSourcePath.Substring(projectRoot.Length).TrimStart('/');
                if (relative.StartsWith("Packages/", StringComparison.Ordinal)
                    || relative.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    return relative;
                }
            }

            return GetFileName(normalizedSourcePath);
        }

        private static bool IsSameOrChildPath(string candidate, string parent, StringComparison comparison)
        {
            if (string.Equals(candidate, parent, comparison))
            {
                return true;
            }

            return candidate.Length > parent.Length
                && candidate[parent.Length] == '/'
                && candidate.StartsWith(parent, comparison);
        }

        private static string GetFileName(string path)
        {
            int start = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/')
                {
                    start = i + 1;
                }
            }

            return start < path.Length ? path.Substring(start) : "source";
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }
    }
#endif
}
