using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Logging.Pipeline;
using CycloneGames.Logging.Pipeline.Internal;
using UnityEngine;

namespace CycloneGames.Logging.Unity
{
    /// <summary>
    /// Bounded adapter from the synchronous pipeline sink contract to the Unity main thread.
    /// </summary>
    public sealed class UnityConsoleLogSink : ILogSink, IFlushableLogSink, IIdempotentLogSinkDisposal
    {
        private readonly int _adapterGeneration;
        private int _disposed;

        public UnityConsoleLogSink()
            : this(null)
        {
        }

        public UnityConsoleLogSink(UnityConsoleLogSinkOptions options)
        {
            if (options != null)
            {
                LoggingRuntimeHost.Configure(options);
            }

            _adapterGeneration = LoggingRuntimeHost.RegisterAdapter();
            try
            {
                LoggingRuntimeHost.EnsureInstance();
            }
            catch
            {
                LoggingRuntimeHost.UnregisterAdapter(_adapterGeneration);
                throw;
            }
        }

        public static UnityConsoleLogSinkStatistics GetStatistics()
        {
            return LoggingRuntimeHost.GetStatistics();
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(UnityConsoleLogSink));
            }

            int sourcePathCharacters = logEvent.FilePath?.Length ?? 0;
            int estimate = UnityConsoleLogSinkOptions.EstimateRetainedCharacters(
                logEvent.MessageLength,
                logEvent.Category?.Length ?? 0,
                sourcePathCharacters);
            if (!LoggingRuntimeHost.TryReserve(logEvent.Severity, estimate, out LoggingRuntimeHost.Reservation reservation))
            {
                return;
            }

            string formatted = null;
            bool reservationOwned = true;
            try
            {
                formatted = FormatMessage(logEvent);
                reservationOwned = false;
#if UNITY_EDITOR
                LoggingRuntimeHost.Commit(logEvent.Severity, formatted, reservation, logEvent.FilePath, logEvent.LineNumber);
#else
                LoggingRuntimeHost.Commit(logEvent.Severity, formatted, reservation);
#endif
            }
            finally
            {
                if (reservationOwned)
                {
                    LoggingRuntimeHost.CancelReservation(reservation);
                }
            }
        }

        internal static string FormatMessage(LogEvent logEvent)
        {
            StringBuilder builder = StringBuilderPool.Get();
            try
            {
                if (!string.IsNullOrEmpty(logEvent.Category))
                {
                    builder.Append('[');
                    AppendSafePath(builder, logEvent.Category);
                    builder.Append("] ");
                }

                logEvent.AppendMessageTo(builder);
                AppendSourceLocation(builder, logEvent.FilePath, logEvent.LineNumber);
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
                LoggingRuntimeHost.UnregisterAdapter(_adapterGeneration);
            }
        }

        public bool TryFlush(LogFlushMode mode)
        {
            if (mode != LogFlushMode.Buffered && mode != LogFlushMode.Durable)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), "Unknown flush mode.");
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return true;
            }

            return LoggingRuntimeHost.TryFlushUnityQueue(20);
        }

        private static void AppendSourceLocation(StringBuilder builder, string sourcePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

#if UNITY_EDITOR
            string displayPath = LoggingEditorPathResolver.GetDisplayPath(sourcePath);
            string fullPath = NormalizeFullPath(sourcePath);
            string linkPath = displayPath;
            if (!string.IsNullOrEmpty(fullPath))
            {
                string registeredLinkPath = LoggingEditorLinkRegistry.Register(displayPath, lineNumber, fullPath);
                if (!string.IsNullOrEmpty(registeredLinkPath))
                {
                    linkPath = registeredLinkPath;
                }
            }

            builder.Append("\n\n<a href=\"");
            AppendSafePath(builder, linkPath);
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
            catch (Exception exception) when (!(exception is OutOfMemoryException))
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

    }

#if UNITY_EDITOR
    internal static class LoggingEditorLinkRegistry
    {
        private readonly struct LinkKey : IEquatable<LinkKey>
        {
            internal readonly string LinkPath;
            internal readonly int LineNumber;

            internal LinkKey(string linkPath, int lineNumber)
            {
                LinkPath = linkPath;
                LineNumber = lineNumber;
            }

            public bool Equals(LinkKey other)
            {
                return LineNumber == other.LineNumber
                    && string.Equals(LinkPath, other.LinkPath, StringComparison.Ordinal);
            }

            public override bool Equals(object value)
            {
                return value is LinkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int pathHash = LinkPath == null ? 0 : StringComparer.Ordinal.GetHashCode(LinkPath);
                    return (pathHash * 397) ^ LineNumber;
                }
            }
        }

        private readonly struct LinkIdentity : IEquatable<LinkIdentity>
        {
            internal readonly string FullPath;
            internal readonly int LineNumber;

            internal LinkIdentity(string fullPath, int lineNumber)
            {
                FullPath = fullPath;
                LineNumber = lineNumber;
            }

            public bool Equals(LinkIdentity other)
            {
                return LineNumber == other.LineNumber
                    && string.Equals(FullPath, other.FullPath, StringComparison.Ordinal);
            }

            public override bool Equals(object value)
            {
                return value is LinkIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int pathHash = FullPath == null ? 0 : StringComparer.Ordinal.GetHashCode(FullPath);
                    return (pathHash * 397) ^ LineNumber;
                }
            }
        }

        private readonly struct LinkRegistration
        {
            internal readonly LinkKey Key;
            internal readonly LinkIdentity Identity;

            internal LinkRegistration(LinkKey key, LinkIdentity identity)
            {
                Key = key;
                Identity = identity;
            }
        }

        private const int MaxEntries = 2048;
        private const string LinkPathPrefix = "cglog/";

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<LinkKey, string> FullPathByKey = new Dictionary<LinkKey, string>(MaxEntries);
        private static readonly Dictionary<LinkIdentity, string> LinkPathByIdentity = new Dictionary<LinkIdentity, string>(MaxEntries);
        private static readonly LinkRegistration[] RegistrationRing = new LinkRegistration[MaxEntries];
        private static string _generationToken = CreateGenerationToken();
        private static ulong _nextLinkId;
        private static int _nextIndex;

        internal static string Register(string displayPath, int lineNumber, string fullPath)
        {
            if (string.IsNullOrEmpty(displayPath) || string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

            var identity = new LinkIdentity(fullPath, lineNumber);
            lock (SyncRoot)
            {
                if (LinkPathByIdentity.TryGetValue(identity, out string existingLinkPath))
                {
                    return existingLinkPath;
                }

                _nextLinkId = unchecked(_nextLinkId + 1UL);
                string linkPath = LinkPathPrefix
                    + _generationToken
                    + "/"
                    + _nextLinkId.ToString("X16", CultureInfo.InvariantCulture);
                var key = new LinkKey(linkPath, lineNumber);

                LinkRegistration previous = RegistrationRing[_nextIndex];
                if (previous.Key.LinkPath != null)
                {
                    FullPathByKey.Remove(previous.Key);
                    LinkPathByIdentity.Remove(previous.Identity);
                }

                RegistrationRing[_nextIndex] = new LinkRegistration(key, identity);
                FullPathByKey[key] = fullPath;
                LinkPathByIdentity[identity] = linkPath;
                _nextIndex = (_nextIndex + 1) % MaxEntries;
                return linkPath;
            }
        }

        internal static bool TryGetFullPath(string linkPath, int lineNumber, out string fullPath)
        {
            if (string.IsNullOrEmpty(linkPath))
            {
                fullPath = null;
                return false;
            }

            lock (SyncRoot)
            {
                return FullPathByKey.TryGetValue(new LinkKey(linkPath, lineNumber), out fullPath)
                    && !string.IsNullOrEmpty(fullPath);
            }
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                FullPathByKey.Clear();
                LinkPathByIdentity.Clear();
                Array.Clear(RegistrationRing, 0, RegistrationRing.Length);
                _generationToken = CreateGenerationToken();
                _nextLinkId = 0UL;
                _nextIndex = 0;
            }
        }

        private static string CreateGenerationToken()
        {
            return Guid.NewGuid().ToString("N");
        }

    }

    internal static class LoggingEditorPathResolver
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
